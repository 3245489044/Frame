#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ECS.Core;
using ECS.Bridge;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS;
using FrontEndDriven.ECS.Systems;
using FrontEndDriven.ECS.Components;
using FrontEndDriven.Container;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.Shared.Framework;
using FrontEndDriven.WinForms;
using FrontEndDriven.WinForms.Applier;
using Modules.LayoutAnalysis.UIInspector;
using Modules.Pipeline;
using Ports;
using TConfig;
using TConfig.ECS.Components;

namespace FrontEndDriven.Pipeline;

/// <summary>
/// 渲染管线共享上下文（stage 间传递 + IPipelineContext 组合转发）。
/// IPipelineContext 由内嵌 PipelineContext 承载（组合转发），业务强类型输入字段独立承载三聚拢点输入。
/// 输出收敛为 RenderRunResult 单一回传对象（RD4：消除 ReportCollectedCount/ReportExecuted 双回调碎片化）。
/// </summary>
public sealed class RenderContext : IPipelineContext
{
    // ── 输入字段（RenderStageRunner 执行输入） ──
    public UIConfiguration? Config { get; set; }

    public string Scope { get; set; } = "MainLayout";

    public Entity RootEntity { get; set; }

    public WinFormSnapshot? Snapshot { get; set; }

    /// <summary>P7：最近一次渲染区间的挂载结果（RenderApplyStage 回填），供 BuildResult 组装挂载统计。</summary>
    public ApplyResult? ApplyResult { get; set; }

    /// <summary>
    /// R5：宿主真实子控件数——由 RenderMountStage 在宿主物理挂载完成后回填的唯一真值（替代 BuildResult 挂载前预计算 + EcsGate 挂载后二次覆盖的双算）。
    /// null=无宿主或挂载阶段未回填（非状态管理场景），此时 BuildResult 回退 0。
    /// </summary>
    public int? HostActualChildCount { get; set; }

    /// <summary>
    /// S1·布局应用时相信号（单一权威）：RenderApplyStage 成功执行 ApplySnapshot 后置位。
    /// 可见性检查（RenderVisibilityCheckStage）gate 于此信号——布局未应用前不做 inHost/尺寸断言，
    /// 消除「布局未就位 → inHost=false」假阴性（StateSnapshot：可见性检查读取的物理状态在确定时相）。
    /// </summary>
    public bool LayoutApplied { get; set; }

    // ── 区间控制（ExecuteCore=全量；ExecuteCreationStage=创建；ExecuteRenderStage=渲染） ──
    public bool PartLoad { get; set; }

    public bool PartCreation { get; set; }

    public bool PartRender { get; set; }

    // ── IPipelineContext 组合转发 ──
    private readonly PipelineContext _inner = new() { Mode = "RenderPipeline" };

    public string Mode
    {
        get => _inner.Mode;
        set => _inner.Mode = value;
    }

    public IReadOnlyDictionary<string, bool> StageSuccess => _inner.StageSuccess;

    public IReadOnlyDictionary<string, string?> StageErrors => _inner.StageErrors;

    public IDictionary<string, object?> SharedData => _inner.SharedData;

    public bool HasError => _inner.HasError;

    public string? FirstError => _inner.FirstError;

    public void SetStageResult(string stageName, bool success, string? error = null)
        => _inner.SetStageResult(stageName, success, error);

    public bool GetStageSuccess(string stageName) => _inner.GetStageSuccess(stageName);

    public string? GetStageError(string stageName) => _inner.GetStageError(stageName);

    public void Reset()
    {
        _inner.Reset();
        Config = null;
        Snapshot = null;
        ApplyResult = null;
        RootEntity = default;
        Scope = "MainLayout";
        PartLoad = false;
        PartCreation = false;
        PartRender = false;
        HostActualChildCount = null;
        LayoutApplied = false;
    }
}

/// <summary>
/// 管线单次执行唯一回传对象（RD4）。由 RenderStageRunner 于 ExecuteCore/ExecuteRenderStage 完成后组装。
/// P7：扩展挂载统计（结果契约闭合）——MountedCount/OrphanCount/SkipReasons 供上层判定「控件已挂载可见」。
/// </summary>
public sealed record RenderRunResult
{
    public string Scope { get; init; } = "MainLayout";

    public Entity RootEntity { get; init; }

    public int CollectedCount { get; init; }

    public int AppliedCount { get; init; }

    public long ElapsedMs { get; init; }

    /// <summary>最近一次执行是否包含渲染区间（PartRender）。false=仅 Load/Creation，渲染计数不适用。</summary>
    public bool RenderExecuted { get; init; }

    // ── P7：挂载统计（来自 WinFormApplier.ApplyResult） ──

    /// <summary>成功挂载到父容器的关系数（= 控件可见挂载下界）。</summary>
    public int MountedCount { get; init; }

    /// <summary>
    /// M3 内容层物理核验挂载数（挂载判据物理闭环）。= 经 ApplyResult.RelationsApplied（物理核验后）的挂载关系数。
    /// 与 HostActualChildCount（root 是否入宿主）区分：前者管内容控件是否真实入宿主，后者管 root 是否挂上宿主。
    /// 用于 ScopeLifecycleAdapter 双条件判据——仅 HostActualChildCount>0 不构成渲染成功（内容层可能物理 orphan）。
    /// </summary>
    public int ContentMounted { get; init; }

    /// <summary>关系挂载跳过数（孤儿控件：父/子未找到、目标面板无效、AddControl 失败、物理核验未入宿主）。</summary>
    public int OrphanCount { get; init; }

    /// <summary>控件级跳过数（Entity 解析失败 / 创建失败）。</summary>
    public int ControlsSkippedCount { get; init; }

    /// <summary>跳过明细（分类：ParseFailed / CreateFailed / ParentNotFound / ChildNotFound / InvalidTargetPanel）。</summary>
    public IReadOnlyList<string>? SkipReasons { get; init; }

    /// <summary>
    /// 宿主真实收到子控件数（宿主物理挂载判据）。显式宿主（RootContainer != null）时 = 宿主 Controls.Count；
    /// 无宿主（MainLayout 全量）时为 0。
    /// 「渲染执行 ≠ 挂载可见」由本字段暴露宿主空——S1 堵 D4 假绿（详见宿主物理挂载断链修复）。
    /// </summary>
    public int HostActualChildCount { get; init; }

    /// <summary>
    /// 结果契约判据（P7）：渲染区间实际执行且无任何挂载/控件跳过。
    /// false ≠ 抛异常，而是「渲染执行了但 UI 未挂载可见」——消除「日志全绿但 UI 空」的假阳性。
    /// 注：Succeeded 仅表「渲染管线成功」；宿主是否真实收到子控件由 HostActualChildCount 判断（上层真判据）。
    /// </summary>
    public bool Succeeded => RenderExecuted && OrphanCount == 0 && ControlsSkippedCount == 0;

    /// <summary>
    /// 渲染异常标记（F4.1·fail-stop）：渲染期间抛异常时 EcsGate 落 HasError=true 快照
    /// （作用域 Failed 由 ScopeLifecycleStateMachine 捕获落定），供审计/GetLastRenderStats 可见"渲染失败"。
    /// </summary>
    public bool HasError { get; init; }
}

/// <summary>
/// 输入聚拢 S1：EcsInputPort.LoadConfig → 注册根容器（三聚拢点输入①）。
/// </summary>
public sealed class RenderLoadStage : PipelineStageBase<RenderContext>
{
    private readonly EcsInputPort _inputPort;

    public RenderLoadStage(EcsInputPort inputPort)
        : base("RenderLoadStage", "EcsInputPort.LoadConfig（输入聚拢点①）")
    {
        _inputPort = inputPort ?? throw new ArgumentNullException(nameof(inputPort));
    }

    public override bool CanExecute(RenderContext context) => context.PartLoad;

    protected override Task ExecuteCoreAsync(RenderContext ctx, CancellationToken cancellationToken)
    {
        var rootEntity = _inputPort.LoadConfig(ctx.Config!, ctx.Scope);

        // 宿主物理挂载已收敛为渲染管线内的框架阶段 RenderMountStage（Apply→Mount→Bind 时相，统一经 HostMountService）。
        // 不再在此用「宿主名映射 root 实体」（历史 D2 残留）——那会以宿主名覆盖 root 实体的名字键，
        // 使挂载解析可能命中宿主自身而短路，且与 RenderMountStage 物理挂载语义重复。
        ctx.RootEntity = rootEntity;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 创建 S2：Capability/Layout/SplitContainer/Style 四系统更新（等价原 ExecuteCreationStage）。
/// </summary>
public sealed class RenderCreationStage : PipelineStageBase<RenderContext>
{
    private readonly EcsCapabilityApplier _capabilityApplier;
    private readonly LayoutSystem _layoutSystem;
    private readonly SplitContainerSystem _splitContainerSystem;
    private readonly StyleSystem _styleSystem;

    public RenderCreationStage(
        EcsCapabilityApplier capabilityApplier,
        LayoutSystem layoutSystem,
        SplitContainerSystem splitContainerSystem,
        StyleSystem styleSystem)
        : base("RenderCreationStage", "Capability/Layout/SplitContainer/Style 四系统创建")
    {
        _capabilityApplier = capabilityApplier ?? throw new ArgumentNullException(nameof(capabilityApplier));
        _layoutSystem = layoutSystem ?? throw new ArgumentNullException(nameof(layoutSystem));
        _splitContainerSystem = splitContainerSystem ?? throw new ArgumentNullException(nameof(splitContainerSystem));
        _styleSystem = styleSystem ?? throw new ArgumentNullException(nameof(styleSystem));
    }

    public override bool CanExecute(RenderContext context) => context.PartCreation;

    protected override Task ExecuteCoreAsync(RenderContext ctx, CancellationToken cancellationToken)
    {
        _capabilityApplier.Update();
        _layoutSystem.Update();
        _splitContainerSystem.Update();
        _styleSystem.Update();
        return Task.CompletedTask;
    }
}

/// <summary>
/// 收集 S3：CsgEntryPoint.CollectSnapshot（三聚拢点执行②）。
/// collectedCount=0 时为显式降级日志（消除原静默，对齐方案 §七 collectedCount=0 断言）。
/// </summary>
public sealed class RenderCollectStage : PipelineStageBase<RenderContext>
{
    private readonly CsgEntryPoint _collector;

    public RenderCollectStage(CsgEntryPoint collector)
        : base("RenderCollectStage", "CsgEntryPoint.CollectSnapshot（执行聚拢点②）")
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
    }

    public override bool CanExecute(RenderContext context) => context.PartRender;

    protected override Task ExecuteCoreAsync(RenderContext ctx, CancellationToken cancellationToken)
    {
        // S3：收集器作用域感知——传 ctx.Scope，消除「Side 渲染却收集 MainLayout 全量」的缺陷
        var snapshot = _collector.CollectSnapshot(ctx.Scope);
        ctx.Snapshot = snapshot;

        FrontEndDrivenLog.Log("CsgCollectSnapshot_summary",
            new { scope = ctx.Scope, controlCount = snapshot.Controls.Count, relationCount = snapshot.Relations.Count });

        if (snapshot.Controls.Count == 0)
        {
            FrontEndDrivenLog.Log("RenderCollectStage_zero_count_degraded",
                new { scope = ctx.Scope, note = "collectedCount=0 显式降级：渲染结果为空，进入审计降级路径" });
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 应用 S4：WinFormsOutputPort.ApplySnapshot（三聚拢点输出③）。
/// ECS_MISMATCH 审计（SyncActualContainerSizes）已移至管线（含挂载+挂载后 ECS 重排）完成后，
/// 由 RenderStageRunner 调用 RunMismatchAudit 统一执行（见 RenderStageRunner.ExecuteRenderStage），不在此重复触发。
/// </summary>
public sealed class RenderApplyStage : PipelineStageBase<RenderContext>
{
    private readonly WinFormsOutputPort _outputPort;

    public RenderApplyStage(WinFormsOutputPort outputPort)
        : base("RenderApplyStage", "WinFormsOutputPort.ApplySnapshot（输出聚拢点③）")
    {
        _outputPort = outputPort ?? throw new ArgumentNullException(nameof(outputPort));
    }

    public override bool CanExecute(RenderContext context)
        => context.PartRender && context.Snapshot != null;

    protected override Task ExecuteCoreAsync(RenderContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.Snapshot == null)
            throw new InvalidOperationException("RenderCollectStage 未产出 Snapshot，无法执行 Apply");

        // P7：挂载结果回填 ctx（ApplyResult → BuildResult 组装 RenderRunResult 挂载统计）
        ctx.ApplyResult = _outputPort.ApplySnapshot(ctx.Snapshot);

        // S1·布局应用时相信号：Apply 阶段成功执行后置位，可见性检查 gate 于此——
        // 布局未应用前不进行 inHost/尺寸断言，消除「布局未就位 → inHost=false」假阴性。
        ctx.LayoutApplied = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 挂载 S4.5（宿主物理挂载·框架自动阶段）——在 Apply 之后、Bind 之前执行。
/// 把渲染根控件（root:{scope}）物理 Add 进宿主，保证事件绑定（RenderBindStage）时 root 已在宿主控件树内（EB1 绑定时相）。
/// root 控件由 Apply 阶段（WinFormApplier 创建 root 实体控件 + Register）产生，故挂载必须在其后（M4 挂载前移验证失败：
/// root 未注册时 AttachRootToHost 入待挂队列且无后续补挂，宿主子控件计数为 0 → MarkMounted 不触发 → Side 物理断言 FAIL）。
/// 宿主/根控件寻址依赖 HostMountService（经 ContainerRegistry 解析宿主 + ControlPool 按 root:{scope} 解析根），
/// 根控件暂未入池时由 HostMountService 入待挂队列，本阶段再 TryAttachPending 自愈补挂。
/// 依赖惰性解析：HostMountService 由 StateServiceCollection 在管线装配后注册进 AppServiceProvider。
/// </summary>
public sealed class RenderMountStage : PipelineStageBase<RenderContext>
{
    public RenderMountStage()
        : base("RenderMountStage", "宿主物理挂载（AttachRootToHost + TryAttachPending）")
    {
    }

    public override bool CanExecute(RenderContext context) => context.PartRender;

    protected override Task ExecuteCoreAsync(RenderContext ctx, CancellationToken cancellationToken)
    {
        var hostMount = AppServiceProvider.Resolve<HostMountService>();
        if (hostMount == null)
            return Task.CompletedTask; // 宿主服务未装配（非状态管理场景），跳过挂载阶段

        // 仅凭 scope 驱动：AttachRootToHost 内部经 Registry 解析宿主（无宿主绑定则安全返回）、
        // 经 ControlPool 按 root:{scope} 解析根控件并物理挂载到宿主。
        if (!string.IsNullOrWhiteSpace(ctx.Scope))
        {
            hostMount.AttachRootToHost(ctx.Scope);
        }

        // 自愈：补挂此前因根控件尚未入池而排队的作用域。
        hostMount.TryAttachPending();

        // Phase 1·反应式布局子系统：挂载后注册宿主 Resize 源——宿主容器拉伸 → 独立布局重算 + 差异投影。
        // 惰性解析 LayoutSubsystem（composition 装配后注册）；宿主为 Form 时 RegisterScope 内部跳过订阅。
        var layoutSubsystem = AppServiceProvider.Resolve<LayoutSubsystem>();
        var host = hostMount.ResolveHost(ctx.Scope);
        if (host != null)
        {
            layoutSubsystem?.RegisterScope(ctx.Scope, host);
        }

        // R3·首渲挂载后同步 ECS 布局重排（ResolveBasis → RecalculateScope → Project）：
        // root 已挂宿主、宿主真实客户区就绪 → 由 ECS 布局真源写入正确 Bounds。
        // 首次加载即走与宿主 Resize 相同的权威路径，消除「内容 Apply 先于挂载用物理父链换算→多重偏移」缺陷。
        // TabPage 基准由 ContainerBasisResolver 在 ReflowNow 内同步推导（父区扣 inset），无需预先捕获几何缓存。
        layoutSubsystem?.ReflowNow(ctx.Scope);

        // R5：宿主物理挂载完成后回填真实子控件数（唯一真值），替代 BuildResult 挂载前预计算（恒为 0）
        // 与 EcsGate 挂载后二次覆盖的双算。无宿主时为 0。
        ctx.HostActualChildCount = hostMount.GetDirectChildCount(ctx.Scope);

        return Task.CompletedTask;
    }
}

/// <summary>
/// 绑定 S5：EventBindingSystem.Update（事件绑定三聚拢点 ①输入→②执行→③输出 Gate）。
/// </summary>
public sealed class RenderBindStage : PipelineStageBase<RenderContext>
{
    private readonly EventBindingSystem _eventBindingSystem;

    public RenderBindStage(EventBindingSystem eventBindingSystem)
        : base("RenderBindStage", "EventBindingSystem.Update（事件绑定三聚拢点）")
    {
        _eventBindingSystem = eventBindingSystem ?? throw new ArgumentNullException(nameof(eventBindingSystem));
    }

    public override bool CanExecute(RenderContext context) => context.PartRender;

    protected override Task ExecuteCoreAsync(RenderContext ctx, CancellationToken cancellationToken)
    {
        _eventBindingSystem.Update();
        return Task.CompletedTask;
    }
}

/// <summary>
/// 可见性契约断言（M5·结果契约）：渲染后断言物理控件「尺寸>0 && Visible && 与宿主客户区相交」。
/// 失败显式 Warning（Info 常开）——「控件在树但屏幕不可见」的物理侧判据（ECS 侧由 LayoutConvergenceReport 对照）。
/// 2026-08-06 重构（E5→M1）：本阶段仅断言 + 显式告警 + **自愈重渲染经 RenderRequestQueue 入队一次**——
/// 违规时入队（reason=visibility-self-heal），渲染由队列唯一入口（阶段2）接管，不在 stage 内递归调用 ExecuteRenderStage；
/// 时间窗（2s）内同 scope 仅入队一次，防止同步 Drain 期间嵌套渲染递归。
/// </summary>
public sealed class RenderVisibilityCheckStage : PipelineStageBase<RenderContext>
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;

    /// <summary>
    /// 自愈请求节流门（集中管理散落的刷新节流）：每 scope 在 2s 窗口内最多自愈入队一次（防重入重渲染风暴）。
    /// 收敛手写 <c>ConcurrentDictionary&lt;string,long&gt;</c> 节流到通用 <see cref="ThrottleGate"/>（统一窗口/上限/重置）。
    /// </summary>
    private readonly ThrottleGate _selfHealGate = new(1, TimeSpan.FromSeconds(2));

    public RenderVisibilityCheckStage(EntityManager entityManager, ControlPool controlPool)
        : base("RenderVisibilityCheckStage", "可见性契约断言（Bounds ⊆ 宿主客户区 && 尺寸>0 && Visible）")
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
    }

    public override bool CanExecute(RenderContext context) => context.PartRender;

    protected override Task ExecuteCoreAsync(RenderContext ctx, CancellationToken cancellationToken)
    {
        // S1·布局应用时相 gate：布局未应用（RenderApplyStage 未执行/未完成）→ 跳过检查并记 Debug，
        // 明确「检查延后」而非误报（消除 7 条 inHost=false 假阴性的时相根因）。
        if (!ctx.LayoutApplied)
        {
            FrontEndDrivenLog.Log("RenderVisibilityCheckStage_skipped_not_applied",
                new { scope = ctx.Scope, reason = "LayoutApplied=false（RenderApplyStage 未执行或未完成）" });
            return Task.CompletedTask;
        }

        var hostMount = AppServiceProvider.Resolve<HostMountService>();
        if (hostMount == null || string.IsNullOrWhiteSpace(ctx.Scope))
            return Task.CompletedTask; // 无宿主服务或非 scope 渲染：跳过（MainLayout 字面宿主等场景）

        var host = hostMount.ResolveHost(ctx.Scope);
        if (host == null)
            return Task.CompletedTask;

        // 检查时机守卫：宿主无句柄时控件树尚未就位，跳过可见性断言
        if (!host.IsHandleCreated)
            return Task.CompletedTask;
        var ownerForm = host.FindForm();
        if (ownerForm == null || !ownerForm.Visible)
            return Task.CompletedTask;

        // P0·宿主就绪门控：宿主客户区 0×0（宿主与父面板均不可用，基准不可建立）→ 布局未就位，
        // 审计延后记 Debug（不计违规、不自愈），消除「宿主未就位 → inHost=false」假阴性风暴。
        // 与 LayoutConvergencePolicy.ClassifyHost 就绪口径一致（clientReady ∨ parentClientReady）。
        // 宿主一旦就位（窗口显示/尺寸化），host.ClientSize 恢复非零 → 门控放行 → 真实违规照常暴露。
        bool hostReady = (host.ClientSize.Width > 0 && host.ClientSize.Height > 0)
            || (host.Parent is { ClientSize.Width: > 0, ClientSize.Height: > 0 });
        if (!hostReady)
        {
            FrontEndDrivenLog.Log("RenderVisibilityCheckStage_deferred_host_zero_client",
                new { scope = ctx.Scope, host = host.Name ?? "<unnamed>", reason = "host_and_parent_zero_client" });
            return Task.CompletedTask;
        }

        // S2·坐标换算单一出口：统一经 LayoutCoordinateResolver.ToFormRelative（父链 .Location 累加，不含窗体）——
        // 与重叠检测/位置报告同基准（form 相对），不依赖窗体屏幕定位/控件句柄。
        var hostRect = LayoutCoordinateResolver.ToFormRelative(host);
        int checkedCount = 0;
        int violated = 0;
        int healViolated = 0;     // 瞬态类「应可见却不可见」（ExpectVisible 违规，父链合法但布局/时序未就位）——可经重渲染自愈
        int structuralViolated = 0;  // 结构类「应可见却不可见」（父链非法/孤立）——重渲染永不可修，升 Error 提示人工修配置

        foreach (var entity in _entityManager.GetEntities<ControlInfoComponent>())
        {
            // scope 过滤
            if (!_entityManager.TryGetComponent<ScopeComponent>(entity, out var scopeComp)
                || scopeComp.Scope != ctx.Scope)
            {
                continue;
            }

            var control = _controlPool.GetControl(entity);
            if (control == null || control.IsDisposed || ReferenceEquals(control, host))
                continue;

            checkedCount++;

            var controlRect = LayoutCoordinateResolver.ToFormRelative(control);
            bool inHost = hostRect.IntersectsWith(controlRect);
            bool sizeOk = control.Width > 0 && control.Height > 0;
            bool visibleOk = control.Visible;

            // 可见性语义分类校验（替代逐类硬编码豁免）：
            //   ExpectVisible → 要求 Visible=true 且尺寸>0 且 inHost（真违规 → 触发自愈）
            //   SemanticHidden → 合法隐藏（Tab 非选中页），仅断言尺寸/位置，不触发自愈
            //   DeferredHidden → 生命周期空载态（空容器 0x0 合法），仅断言未溢出，不触发自愈
            var intent = ResolveVisibilityIntent(entity, control);
            bool pass = intent switch
            {
                VisibilityIntent.ExpectVisible => sizeOk && visibleOk && inHost,
                VisibilityIntent.SemanticHidden => sizeOk && inHost,
                VisibilityIntent.DeferredHidden => inHost || (control.Width == 0 && control.Height == 0),
                _ => false
            };

            if (intent != VisibilityIntent.ExpectVisible)
            {
                FrontEndDrivenLog.Log("VisibilityContract_semantic_exempt",
                    new { scope = ctx.Scope, controlName = control.Name, intent = intent.ToString() });
            }

            if (pass)
                continue;

            violated++;
            bool isStructural = false;
            if (intent == VisibilityIntent.ExpectVisible)
            {
                // M5 根因门：区分「瞬态（可自愈）」与「结构（不可自愈，需人工修配置）」。
                // 结构类 = 父链非法/孤立（Parent==null 或 TabPage 无 TabControl 宿主）——重渲染无法修复。
                isStructural = IsStructuralViolation(control);
                if (isStructural)
                    structuralViolated++;
                else
                    healViolated++;
            }

            string ecsLayout = "?";
            if (_entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var cl))
                ecsLayout = $"({cl.Left},{cl.Top},{cl.Width}x{cl.Height})";

            FrontEndDrivenLog.Log("VisibilityContract_violated",
                new
                {
                    scope = ctx.Scope,
                    controlName = control.Name,
                    intent = intent.ToString(),
                    size = $"{control.Width}x{control.Height}",
                    visible = control.Visible,
                    inHost,
                    // M5 根因门：结构类（父链非法/孤立，不可自愈）标记，供日志甄别
                    isStructural = isStructural,
                    parent = control.Parent?.Name,
                    // S1 诊断：补打实际 Left/Top + host 尺寸，使「布局未应用 vs 真违规」可判定
                    actualLeft = control.Left,
                    actualTop = control.Top,
                    hostSize = $"{hostRect.Width}x{hostRect.Height}",
                    hostRect = hostRect.ToString(),
                    controlRect = controlRect.ToString(),
                    ecsLayout
                });
        }

        if (violated > 0)
        {
            FrontEndDrivenLog.Log("VisibilityContract_summary_violated",
                new { scope = ctx.Scope, checkedCount, violated, healViolated, structuralViolated });

            // M5 根因门：结构类违规（父链非法/孤立，如 tabpage 无 TabControl 宿主）——
            // 重渲染永不可修，**不自愈**，升 Error 标 config-error，提示人工修配置。
            // 移除「healViolated 计数即代表已修复」的语义——结构类不再计入自愈请求。
            if (structuralViolated > 0)
            {
                FrontEndDrivenLog.Log("VisibilityContract_summary_structural",
                    new
                    {
                        scope = ctx.Scope,
                        checkedCount,
                        structuralViolated,
                        note = "结构类违规不自愈：父链非法或控件孤立（如 tabpage 无 TabControl 宿主），请检查配置结构（config-error）"
                    });
            }

            // M1·可见性自愈接通：仅「瞬态类」（ExpectVisible 违规且父链合法，布局/时序未就位）才请求自愈——
            // 语义隐藏（SemanticHidden）/ 生命周期态（DeferredHidden）属合法状态，不触发多余重渲染；
            // 结构类（IsStructuralViolation）不入队自愈（重渲染无法修复）。
            // 时间窗防重入：2s 内同 scope 仅入队一次（E5 承诺项，由 TryRequestSelfHeal 拦截）。
            if (healViolated > 0 && TryRequestSelfHeal(ctx.Scope))
            {
                var renderQueue = AppServiceProvider.Resolve<RenderRequestQueue>();
                if (renderQueue != null)
                {
                    renderQueue.Enqueue(ctx.Scope, "visibility-self-heal");
                    FrontEndDrivenLog.Log("VisibilityContract_self_heal_requested",
                        new { scope = ctx.Scope, healViolated });
                }
            }
        }
        else if (checkedCount > 0)
        {
            FrontEndDrivenLog.Log("VisibilityContract_ok",
                new { scope = ctx.Scope, checkedCount });
        }

        return Task.CompletedTask;
    }

    /// <summary>自愈请求节流：时间窗（2s）内同 scope 仅首次违规请求自愈，后续违规不再重复入队。</summary>
    private bool TryRequestSelfHeal(string scope)
        => _selfHealGate.TryEnter(scope);

    /// <summary>
    /// 可见性语义判定（替代逐一硬编码豁免）：把「物理不可见」分类为语义意图，校验器按意图走不同断言分支。
    /// 优先级：Tab 运行时结构语义 &gt; 生命周期态(空容器) &gt; 配置声明(VisibilitySemanticsComponent) &gt; 默认 ExpectVisible。
    /// </summary>
    private VisibilityIntent ResolveVisibilityIntent(Entity entity, Control control)
    {
        // 1) Tab 运行时结构语义：位于 TabControl 非选中 TabPage（含子树）→ SemanticHidden；
        //    选中页由 TabControl.SelectedTab 逐帧判定，不依赖配置时点。
        if (IsInNonSelectedTabPage(control))
            return VisibilityIntent.SemanticHidden;

        // 1b) 选中页但宿主尚未 realize（TabControl 未建句柄）→ 瞬态：物理 Visible 尚未被选中语义生效，
        //     判 SemanticHidden 而非 ExpectVisible 违规，避免「选中页但未显示完成」被自愈误报。
        if (control is TabPage selectedPage
            && selectedPage.Parent is TabControl selectedTab
            && ReferenceEquals(selectedPage, selectedTab.SelectedTab)
            && !selectedTab.IsHandleCreated)
        {
            return VisibilityIntent.SemanticHidden;
        }

        // 2) 生命周期态（InitialStateNullity 消费）：EcsConfigContainer 空载时 0x0 为合法初始态 → DeferredHidden。
        if (control is EcsConfigContainer container && !container.IsLoaded)
            return VisibilityIntent.DeferredHidden;

        // 3) 配置声明语义（扩展点，Phase ② 由 config hiddenSemantics 填充）：组件存在则以其为准。
        if (_entityManager.TryGetComponent<VisibilitySemanticsComponent>(entity, out var sem))
            return sem.Intent;

        // 4) 默认
        return VisibilityIntent.ExpectVisible;
    }

    /// <summary>
    /// 位于 TabControl 非选中 TabPage（含其后代）判定：回溯父链，命中未选中 TabPage 即 true。
    /// </summary>
    private static bool IsInNonSelectedTabPage(Control control)
    {
        var child = control;
        var node = control.Parent;
        while (node != null)
        {
            if (node is TabControl tabControl && child is TabPage page)
                return !ReferenceEquals(page, tabControl.SelectedTab);
            child = node;
            node = node.Parent;
        }
        return false;
    }

    /// <summary>
    /// M5 根因门·结构类违规判定：物理树中父链非法或孤立，重渲染永不可修（需人工修配置）。
    /// 判定准则：控件 Parent==null（物理孤立，无宿主）；或 TabPage 的父链向上找不到任何 TabControl 宿主
    /// （配置误嵌套，如 tabpage 被错挂到 Panel/根面板）。区别于瞬态类（父链合法但布局/时序未就位）。
    /// </summary>
    private static bool IsStructuralViolation(Control control)
    {
        if (control == null || control.IsDisposed)
            return false;

        // 物理孤立：无父宿主（非 root——root 父为宿主控件；此处只对内容控件判定，调用方已排除 host 自身）
        if (control.Parent == null)
            return true;

        // TabPage 必须位于某 TabControl.TabPages；父链无 TabControl 宿主 → 结构违规
        if (control is TabPage)
        {
            for (var node = control.Parent; node != null; node = node.Parent)
            {
                if (node is TabControl)
                    return false;
            }
            return true;
        }

        return false;
    }
}