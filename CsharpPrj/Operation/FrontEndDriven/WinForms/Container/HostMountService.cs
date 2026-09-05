using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using ECS.Core.Lifecycle;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.ContainerAbstractions;
using FrontEndDriven.Diagnostics;
using Share.StateManagement;

namespace FrontEndDriven.Container;

/// <summary>
/// 宿主聚拢服务（HostMountService）——容器宿主唯一语义据点（SingleImplementation）。
/// 收敛 5 项宿主语义为单一权威，全库只经本服务解耦宿主，杜绝「解析↔挂载↔计数↔断言」实例分裂：
/// ① 解析宿主：委托唯一权威链路（ContainerRegistry.ResolveByScopeOrHostKey）；
/// ② 物理挂载：把渲染根实体控件 Add 进宿主子控件（幂等，防重复 Add / 防自挂载）；
///    当根控件暂未入池时入"待挂队列"，由渲染管线在 Apply 完成后自愈补挂，解耦挂载时相与创建时相；
/// ③ 宿主真实子控件计数：供结果契约 / 断言 / 审计 / 状态回填共用同数；
/// ④ 挂载状态变迁：MarkMounted / MarkUnmounted 唯一写入口。
/// 2026-08-06 重构（阶段4）：直接实现 IContainerLifecycle（StateCenter 容器生命周期 Provider），
/// 消除 ContainerLifecycleAdapter 中间包装层——寻址/挂载/计数/状态/注册收敛为单一实现。
/// </summary>
public sealed class HostMountService : IEntityLifecycleSubscriber, IContainerLifecycle
{
    private readonly ContainerRegistry _registry;
    private readonly ControlPool _controlPool;

    // 待挂队列：root 控件尚未入池时挂起，渲染管线（RenderMountStage）在 Apply 后自愈补挂。
    private readonly ConcurrentDictionary<string, Entity> _pendingAttach = new(StringComparer.OrdinalIgnoreCase);

    // WinForms Panel 默认尺寸（new Panel() 未设 Size 时的物理初值）。root 处于该尺寸 = ECS 布局未投影到物理
    // （如 root 实体尚无 ComputedLayoutComponent），需 Dock=Fill 兜底填满宿主（见 AttachRootToHost 可见性守卫）。
    private const int DefaultRootPanelWidth = 200;
    private const int DefaultRootPanelHeight = 100;

    public HostMountService(ContainerRegistry registry, ControlPool controlPool)
    {
        _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        _controlPool = controlPool ?? throw new System.ArgumentNullException(nameof(controlPool));

        // 阶段2：订阅 Entity 生命周期总线（scope 失效 → 清理待挂队列中的旧 root 实体引用，
        // 取代 DestroyScopeEntities 的手写联动调用，纳入注册-广播-自愈治理）。
        AppServiceProvider.Resolve<EntityLifecycleBus>()?.Register(this);
    }

    /// <summary>待挂队列中等待补挂的作用域数量（诊断/断言用）。</summary>
    public int PendingAttachCount => _pendingAttach.Count;

    /// <summary>
    /// 作用域销毁联动：移除该作用域的待挂队列项。
    /// 作用域实体已销毁（DestroyScopeEntities）后，旧 root 实体引用不再补挂——待挂队列不再持有已销毁实体，
    /// 消除 TryAttachPending 在二次加载场景对旧 root 实体的无效补挂。
    /// </summary>
    public void RemovePendingAttach(string scopeOrHostKey)
    {
        if (string.IsNullOrWhiteSpace(scopeOrHostKey)) return;
        _pendingAttach.TryRemove(scopeOrHostKey, out _);
    }

    // ── IEntityLifecycleSubscriber（阶段2：注册-广播-自愈治理） ──

    /// <summary>单实体失效：待挂队列若持有该实体（root）则移除，杜绝补挂已销毁实体。</summary>
    public void OnEntityInvalidated(Entity entity)
    {
        foreach (var (scope, rootEntity) in _pendingAttach.ToArray())
        {
            if (rootEntity == entity)
                _pendingAttach.TryRemove(scope, out _);
        }
    }

    /// <summary>scope 整体失效：移除该作用域待挂队列项（由 EntityLifecycleBus 广播驱动）。</summary>
    public void OnScopeInvalidated(string scopeName, IReadOnlyList<Entity> scopeEntities)
    {
        RemovePendingAttach(scopeName);
    }

    /// <summary>
    /// ① 解析宿主控件（scope 或 hostKey 均可）——唯一权威链路，转发 ContainerRegistry.ResolveByScopeOrHostKey。
    /// </summary>
    public System.Windows.Forms.Control? ResolveHost(string scopeOrHostKey)
        => _registry.ResolveByScopeOrHostKey(scopeOrHostKey)?.Control;

    /// <summary>① 解析宿主身份（scope 优先，hostKey 兜底反查）——复用 ContainerRegistry 单一寻址权威。</summary>
    public ContainerId? ResolveHostId(string scopeOrHostKey)
    {
        if (string.IsNullOrWhiteSpace(scopeOrHostKey)) return null;

        // 统一走 ContainerRegistry.ResolveByScopeOrHostKey（scope 优先 + hostKey 反查的唯一实现），
        // 消除本处与 Registry 内部手写反查算法的重复漂移（原实现为 FindIdByScope → ResolveScopeByHostKey → FindIdByScope）。
        return _registry.ResolveByScopeOrHostKey(scopeOrHostKey)?.Id;
    }

    /// <summary>
    /// ② 物理挂载：把渲染根控件 Add 进宿主子控件。
    /// 幂等：宿主已包含该根 / 宿主与根同对象 / 根已被挂到该宿主 → 跳过，防重复 Add 与重放竞态。
    /// 根实体可为无效（empty/new()）：渲染管线 ExecuteRenderStage 阶段 ctx.RootEntity 为 default，
    /// 此时退化按作用域根控件名（root:{scope}）解析——挂载只依赖「root 面板已渲染入池」，不依赖 Load 阶段实体时相。
    /// C1（方案C）：root 控件不可解析 / root==宿主 / 宿主未解析 均显式落日志，把「root 未挂 Form」的无声断链
    ///              变为可诊断失败，不再静默 return。
    /// </summary>
    public void AttachRootToHost(string scopeOrHostKey, Entity? rootEntity = null)
    {
        var host = ResolveHost(scopeOrHostKey);
        if (host == null)
        {
            FrontEndDrivenLog.Log("HostMountService_NotResolved",
                new { scope = scopeOrHostKey, note = "宿主未解析，root 未挂载" });
            return;
        }

        // 根实体有效先按实体解析；实体无效（渲染当时相）或实体未命中时，退化为按作用域根控件名（root:{scope}）解析。
        var rootControl = rootEntity is { IsValid: true } ? _controlPool.GetControl(rootEntity.Value) : null;

        if (rootControl == null)
        {
            var rootName = $"root:{scopeOrHostKey}";
            rootControl = _controlPool.GetControl(rootName);

            // 按名解析是常态兜底路径（fallbackHit=true 属设计内成功，走 Debug 降噪）；
            // 仅未命中（fallbackHit=false）升 Warning 暴露断链前兆，避免「满屏 WARNING 中找不到新告警」。
            if (rootControl != null)
            {
                FrontEndDrivenLog.Log("HostMountService_RootControlFallbackByName_debug",
                    new { scope = scopeOrHostKey, rootName, fallbackHit = true });
            }
            else
            {
                FrontEndDrivenLog.Log("HostMountService_RootControlFallbackByName",
                    new { scope = scopeOrHostKey, rootName, fallbackHit = false });
            }
        }

        if (rootControl == null)
        {
            // C1：root 控件不可解析 = root 未挂 Form 的前兆，显式日志供诊断（不再静默断链）。
            // 自愈：进入待挂队列，由渲染管线在 Apply 完成后 TryAttachPending 补挂，解耦挂载时相与创建时相。
            if (rootEntity is { IsValid: true })
            {
                _pendingAttach[scopeOrHostKey] = rootEntity.Value;
            }
            FrontEndDrivenLog.Log("HostMountService_RootControlNotFound",
                new { scope = scopeOrHostKey, rootEntity = rootEntity?.ToString() ?? "<by-name>", pending = true });
            return;
        }

        if (ReferenceEquals(rootControl, host))
        {
            // C1：root 控件与宿主同对象（早期宿主名覆盖残留）——显式记录，防被误判为「正常已挂载」。
            FrontEndDrivenLog.Log("HostMountService_RootIsHost",
                new { scope = scopeOrHostKey, note = "root 控件 == 宿主，跳过物理挂载（疑似残留名字映射）" });
            return;
        }

        if (ReferenceEquals(rootControl.Parent, host))
        {
            _pendingAttach.TryRemove(scopeOrHostKey, out _);
            return; // 已挂到宿主，幂等
        }

        host.Controls.Add(rootControl);

        // 可见性守卫：root 以无效尺寸 Add 进宿主=「日志全绿但 UI 空」直接来源。
        // root 尚无有效尺寸——0x0，或仍为 WinForms Panel 默认 200x100（ECS 布局未投影到物理，修复 A 的兜底防线）——
        // 时 Dock=Fill 使其填满宿主，避免误覆盖已按 ECS 布局驱动尺寸的 root。
        if ((rootControl.Width == 0 && rootControl.Height == 0)
            || (rootControl.Dock == System.Windows.Forms.DockStyle.None
                && rootControl.Width <= DefaultRootPanelWidth
                && rootControl.Height <= DefaultRootPanelHeight))
        {
            rootControl.Dock = System.Windows.Forms.DockStyle.Fill;
        }

        // 宿主自愈：root 挂进宿主，但宿主自身客户区为 0x0（典型：SplitContainer.Panel 子容器，如
        // sideContainer ），SplitterPanel 尺寸由父 SplitContainer.SplitterDistance 在布局时相分配，
        // 挂载时刻通常尚未生效 → 触发宿主及其父链重布局，使面板获得真实尺寸后再判定可见性。
        // 仍未恢复的 0x0 场景由 EmitRootMountedVisual 显式上报（根因=布局基准 0，需查 LayoutSystem）。
        if (host.ClientSize.Width == 0 && host.ClientSize.Height == 0)
        {
            TrySelfHealHostLayout(host);
        }

        FrontEndDrivenLog.Log("HostMountService_AttachedRoot",
            new { scope = scopeOrHostKey, rootName = rootControl.Name, hostName = host.Name });

        // 「挂载成功 ≠ 可见」永久诊断：跨层暴露 root 的真实 尺寸/Dock/Anchor/可见性/宿主客户区，
        // 供「窗口空白」在 A(0尺寸/未Dock) 与 B(有尺寸被塌掉) 之间一次性定位（教训：静态分析无法定位同名 root 时序覆盖）。
        EmitRootMountedVisual(scopeOrHostKey, rootControl, host);

        _pendingAttach.TryRemove(scopeOrHostKey, out _);
    }

    /// <summary>
    /// 宿主 0x0 自愈：向上冒泡触发父链布局，直到宿主获得非零客户区或触顶。
    /// 对 SplitterPanel 子容器（如 sideContainer）尤其必需——容器自身 Dock=None 且 size 由 ECS 布局
    /// 未填充时保持 0x0，父 SplitterPanel 有尺寸也无处可见。自愈把宿主 Dock=Fill 填满其父面板后重布局。
    /// </summary>
    private static void TrySelfHealHostLayout(Control host)
    {
        // 宿主 0x0 且直接父为「已有非零客户区的 SplitterPanel/容器」→ 宿主 Dock=Fill 填满父面板，
        // 覆盖「ECS 布局未给容器尺寸」的断链（诊断曾见 sideContainer dock=None 且 0x0，而父 Panel2 296x760）。
        if (host.ClientSize.Width == 0 && host.ClientSize.Height == 0 && host.Parent != null
            && host.Parent.ClientSize.Width > 0 && host.Parent.ClientSize.Height > 0)
        {
            host.Dock = System.Windows.Forms.DockStyle.Fill;
        }

        var node = host;
        int guard = 0;
        while (node != null && guard++ < 64)
        {
            if (node is System.Windows.Forms.SplitContainer sc)
            {
                // 重设 SplitterDistance（触发 Panel1/Panel2 尺寸重排）；值非法时由 PerformLayout 兜底。
                try { sc.PerformLayout(); } catch { /* 不阻断 */ }
                if (sc.SplitterDistance <= 0 && sc.Width > 0)
                {
                    sc.SplitterDistance = Math.Max(1, sc.Width / 2);
                }
                sc.PerformLayout();
            }
            else
            {
                node.PerformLayout();
            }

            if (node.ClientSize.Width > 0 && node.ClientSize.Height > 0)
            {
                // 目标宿主已获得尺寸，向上完成一次整体 PerformLayout 后返回。
                host.PerformLayout();
                return;
            }
            node = node.Parent;
        }
    }

    private void EmitRootMountedVisual(string scopeOrHostKey, Control rootControl, Control host)
    {
        FrontEndDrivenLog.Log("HostMountService_RootMountedVisual",
            new
            {
                scope = scopeOrHostKey,
                rootName = rootControl.Name,
                rootSize = $"{rootControl.Width}x{rootControl.Height}",
                rootDock = rootControl.Dock.ToString(),
                rootAnchor = rootControl.Anchor.ToString(),
                rootVisible = rootControl.Visible,
                rootParent = rootControl.Parent?.Name ?? "<none>",
                hostName = host.Name,
                hostClient = $"{host.ClientSize.Width}x{host.ClientSize.Height}",
                hostChildCount = host.Controls.Count,
                hostParentChain = DescribeHostParentChain(host),
                note = rootControl.Width == 0 && rootControl.Height == 0
                    ? "root 尺寸为 0x0，已兜底 Dock=Fill（若仍空白则属布局基准=0 场景，需查 LayoutSystem）"
                    : "root 尺寸非 0"
            });
    }

    /// <summary>
    /// 宿主父链快照：逐级向上打印 父控件名/Dock/尺寸，遇到 SplitContainer 时额外打印
    /// SplitterDistance 与 Panel1/Panel2 的客户区——用于精确定位「SplitterPanel 子容器 0x0」的父 splitter 尺寸根因。
    /// </summary>
    private static string DescribeHostParentChain(Control host)
    {
        var parts = new List<string>();
        System.Windows.Forms.Control? node = host;
        int guard = 0;
        while (node != null && guard++ < 16)
        {
            var label = $"{node.GetType().Name}:{node.Name ?? "<unnamed>"}";
            var size = $"{node.Width}x{node.Height}";
            if (node is System.Windows.Forms.SplitContainer sc)
            {
                parts.Add($"{label}(Split:{sc.SplitterDistance},P1:{sc.Panel1?.Width}x{sc.Panel1?.Height},P2:{sc.Panel2?.Width}x{sc.Panel2?.Height},dock:{sc.Dock})");
            }
            else
            {
                parts.Add($"{label}({size},dock:{node.Dock})");
            }
            node = node.Parent;
        }
        return string.Join(" -> ", parts);
    }

    /// <summary>
    /// 自愈补挂：遍历待挂队列，根控件已入池的作用域立即物理挂载并出队。
    /// 由渲染管线（RenderMountStage）在每次 Apply 完成后调用，解除「挂载时相 vs 创建时相」耦合。
    /// </summary>
    public void TryAttachPending()
    {
        if (_pendingAttach.IsEmpty) return;

        // 快照遍历，避免迭代中修改字典
        foreach (var (scope, rootEntity) in _pendingAttach.ToArray())
        {
            // 已挂（宿主已收到子控件）→ 出队，避免重放重复 Add。
            if (GetDirectChildCount(scope) > 0)
            {
                _pendingAttach.TryRemove(scope, out _);
                continue;
            }
            AttachRootToHost(scope, rootEntity);
        }
    }

    /// <summary>③ 宿主真实子控件数（scope 或 hostKey 均可；无宿主返回 0）。</summary>
    public int GetDirectChildCount(string scopeOrHostKey)
        => ResolveHost(scopeOrHostKey)?.Controls.Count ?? 0;

    /// <summary>④ 置挂载状态（MarkMounted / MarkUnmounted 唯一写入口）。</summary>
    public void SetMounted(string scopeOrHostKey, bool mounted)
    {
        var id = ResolveHostId(scopeOrHostKey);
        var hostKey = id?.HostKey ?? scopeOrHostKey;
        if (mounted)
            _registry.MarkMounted(hostKey);
        else
            _registry.MarkUnmounted(hostKey);
    }

    /// <summary>代理：按 scope 解析（供端口/状态层同源复用）。</summary>
    public ContainerHandle? ResolveByScope(string scope) => _registry.ResolveByScope(scope);

    /// <summary>代理：按 hostKey 精确解析（供端口在持完整 ContainerId 时优先命中，避免只用一半标识）。</summary>
    public ContainerHandle? ResolveByHostKey(string hostKey) => _registry.ResolveByHostKey(hostKey);

    /// <summary>统一宿主寻址（scope 优先 + hostKey 反查），返回 ContainerHandle——单一实现转发，供端口/断言同源复用。</summary>
    public ContainerHandle? ResolveByScopeOrHostKey(string scopeOrHostKey)
        => _registry.ResolveByScopeOrHostKey(scopeOrHostKey);

    /// <summary>按宿主标识反查作用域（供端口/反查统一）。</summary>
    public string? ResolveScopeByHostKey(string hostKey) => _registry.ResolveScopeByHostKey(hostKey);

    /// <summary>挂载事实判定（对齐 ContainerRegistry.MountState）。</summary>
    public bool IsMounted(ContainerId id) => _registry.GetMountState(id) == MountState.Mounted;

    // ── IContainerLifecycle 实现（阶段4：StateCenter 容器生命周期 Provider 收敛到本服务，删 ContainerLifecycleAdapter）──

    /// <summary>注册 scope↔hostKey 绑定（幂等；重复注册更新来源标记）。</summary>
    public void Register(string scope, string hostKey, string source = "code-registered")
        => _registry.Register(new ContainerRegistration { Scope = scope, HostKey = hostKey }, source);

    /// <summary>按作用域注销绑定。</summary>
    public void Unregister(string scope)
        => _registry.UnregisterScope(scope);

    /// <summary>唯一寻址：scope 优先，hostKey 反查兜底（治 L5）。</summary>
    public string? ResolveHostKey(string scopeOrHostKey)
        => ResolveHostId(scopeOrHostKey)?.HostKey;

    /// <summary>hostKey → scope 反查。</summary>
    public string? ResolveScope(string hostKey)
        => ResolveScopeByHostKey(hostKey);

    /// <summary>scope 是否已绑定宿主。</summary>
    public bool IsBound(string scope)
        => ResolveByScope(scope) != null;

    /// <summary>挂载状态唯一写入点（R5）——SetMounted 接口别名。</summary>
    public void MarkMounted(string scopeOrHostKey, bool mounted)
        => SetMounted(scopeOrHostKey, mounted);

    /// <summary>挂载状态查询（Registered/Mounted/Orphaned 三态契约）。</summary>
    public ContainerMountState GetMountState(string scopeOrHostKey)
    {
        var id = ResolveHostId(scopeOrHostKey);
        if (id == null) return ContainerMountState.Orphaned;

        return IsMounted(id)
            ? ContainerMountState.Mounted
            : ContainerMountState.Registered;
    }
}
