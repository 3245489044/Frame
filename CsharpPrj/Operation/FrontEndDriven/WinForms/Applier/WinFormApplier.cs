
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ECS.Bridge;
using ECS.Core;
using FrontEndDriven.Abstractions;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS;
using FrontEndDriven.Protocol;
using FrontEndDriven.Shared;
using FrontEndDriven.Shared.Utils;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.WinForms.Applier.Projection;
using TConfig.ECS;
using TConfig.ECS.Components;

namespace FrontEndDriven.WinForms.Applier;

public sealed class WinFormApplier
{
    private readonly ControlPool _controlPool;
    private readonly IControlAdapter _adapter;
    private readonly SplitterPanelRegistry _splitterPanelRegistry;
    private readonly EntityIdCache _entityIdCache;
    private readonly EntityManager? _entityManager;

    // R1·快照驱动投影（唯一差异路径）：差异比较在数据层完成（AppliedSnapshot 纯快照对比），
    // 旧物理差异路径（GetCoreChanged 读 control.Left/Top）已随双轨开关一并移除（R6 收敛完成）。
    private readonly AppliedSnapshotRegistry _appliedSnapshots;

    // R3·投影策略注册表：豁免从 if 链 → 策略（DockProjector/PlatformConstrainedProjector/默认 PixelProjector）。
    private readonly ControlProjectionStrategyRegistry _projectionStrategies;

    public WinFormApplier(
        ControlPool controlPool,
        IControlAdapter adapter,
        SplitterPanelRegistry? splitterPanelRegistry = null,
        EntityIdCache? entityIdCache = null,
        AppliedSnapshotRegistry? appliedSnapshotRegistry = null,
        EntityManager? entityManager = null,
        ControlProjectionStrategyRegistry? projectionStrategies = null)
    {
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _splitterPanelRegistry = splitterPanelRegistry ?? new SplitterPanelRegistry();
        _entityIdCache = entityIdCache ?? EntityIdCache.Instance;
        _entityManager = entityManager;

        _appliedSnapshots = appliedSnapshotRegistry ?? new AppliedSnapshotRegistry();
        _projectionStrategies = projectionStrategies ?? ControlProjectionStrategyRegistry.CreateDefault();
    }

    public ApplyResult Apply(WinFormSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        if (snapshot.Controls.Count == 0)
        {
            FrontEndDrivenLog.Log("WinFormApplier_Apply_skipped_empty");
            return new ApplyResult();
        }

        ClearEntityIdCache();

        // R2·两阶段投影（渲染框架架构级重构方案 §四 R2）：消灭「先全量错误、后差异修正」的双重 SetBounds。
        // ① ApplyStructure：创建/更新控件引用 + ApplyRelations（父链唯一权威）——不应用任何属性（坐标/Text/Font 均不在此设置）；
        // ② ApplyProperties：父链已完整 → ToParentRelative 基准确定 → 快照差异应用（新控件全量、已有控件差异）→ 一次 SetBounds 到位 → 回写 AppliedSnapshot。
        var applyResult = ApplyStructure(snapshot.Controls, snapshot.Relations);

        ApplyProperties(snapshot.Controls, applyResult);

        // Tab 容器语义：挂载与属性投影完成后显式注入选中态（选中页子树保证可见）。
        // 不依赖 WinForms 隐式 auto-select（时序脆弱），由配置 selectedIndex（缺省 0）确定性裁决。
        ApplyTabSelection(snapshot.Controls);

        // P7：挂载统计单一回传——上层（RenderRunResult/审计）以此判定「控件是否已挂载可见」，
        // 消除「validate 存在性通过但 UI 空」的假阳性。
        FrontEndDrivenLog.Log("WinFormApplier_ApplyResult_summary",
            new
            {
                summary = applyResult.ToSummaryText(),
                allMounted = applyResult.AllMounted
            });

        return applyResult;
    }

    private bool TryResolveEntity(ControlDescriptor desc, out Entity entity)
    {
        if (!TryParseEntityId(desc.Id, out entity))
        {
            FrontEndDrivenLog.Log("WinFormApplier_FindControl_ParseFailed",
                new { nodeId = desc.Id, name = desc.Name });
            return false;
        }

        return true;
    }

    /// <summary>
    /// R2·结构投影（StructureProjection）：创建/更新控件引用 + 建立父链（ApplyRelations 唯一权威）。
    /// 不应用任何属性（坐标/Text/Font 等均不在本阶段设置）——属性由 ApplyProperties 在父链完整后快照差异应用，
    /// 消除「父链未就位时坐标被 SetBounds 错误值、随后被修正」的双重 SetBounds 与 OrderByParentDepth 排序补丁。
    /// </summary>
    private ApplyResult ApplyStructure(List<ControlDescriptor> controls, List<ChildRelation> relations)
    {
        var result = new ApplyResult { ControlsProcessed = controls.Count };

        foreach (var desc in controls)
        {
            if (!TryResolveEntity(desc, out var entity))
            {
                result.ControlsSkipped++;
                result.SkipReasons.Add($"ParseFailed nodeId={desc.Id}");
                continue;
            }

            var existingControl = _controlPool.GetControl(entity);
            if (existingControl != null)
            {
                // R2：已有控件仅维护结构身份（SplitContainer 面板注册），属性投影统一在 ApplyProperties 执行。
                if (existingControl is SplitContainer existingSc)
                {
                    EnsureSplitterPanelIdentity(existingSc, desc.Name, entity);
                }
            }
            else
            {
                var control = ControlStrategyRegistry.Create<Control>(desc.Type);
                if (control == null)
                {
                    FrontEndDrivenLog.Log("WinFormApplier_Skip_CreateFailed",
                        new { nodeId = desc.Id, type = desc.Type });
                    result.ControlsSkipped++;
                    result.SkipReasons.Add($"CreateFailed nodeId={desc.Id} type={desc.Type}");
                    continue;
                }

                // R2：创建阶段不应用属性（新控件属性由 ApplyProperties 全量应用，一次 SetBounds 到位）
                if (control is SplitContainer newSc)
                {
                    EnsureSplitterPanelIdentity(newSc, desc.Name, entity);
                }

                _controlPool.Register(entity, desc.Name, control);
                result.CreatedCount++;
            }
        }

        // 父链唯一权威：仍在结构阶段内（父链先于属性投影建立，ToParentRelative 基准确定）。
        ApplyRelations(relations, result);

        return result;
    }

    private void EnsureSplitterPanelIdentity(
        SplitContainer sc,
        string descName,
        Entity splitContainerEntity)
    {
        if (sc == null || string.IsNullOrEmpty(descName) || !splitContainerEntity.IsValid)
            return;

        if (string.IsNullOrEmpty(sc.Panel1.Name))
            sc.Panel1.Name = $"{descName}_Panel1";
        if (string.IsNullOrEmpty(sc.Panel2.Name))
            sc.Panel2.Name = $"{descName}_Panel2";

        _splitterPanelRegistry.Register(splitContainerEntity, UIProtocol.SplitterPanel1, sc.Panel1);
        _splitterPanelRegistry.Register(splitContainerEntity, UIProtocol.SplitterPanel2, sc.Panel2);
    }

    /// <summary>
    /// R2·属性投影（PropertyProjection）：父链已完整（ApplyStructure 阶段 ApplyRelations 唯一权威建立）→
    /// ToParentRelative 换算基准确定 → 快照差异应用（新控件/首渲全量、已有控件差异）→ 一次 SetBounds 到位 → 回写 AppliedSnapshot。
    /// 差异判定（唯一路径）：AppliedSnapshot 纯数据对比（零物理读取零转换，UpdatedCount=快照差异数）。
    /// 父先子后排序（物理父链深度）——ToParentRelative 换算基准 = 父控件当前物理位置，父 Bounds/SplitterDistance
    /// 未就位时子 SetBounds 基准错乱。物理父链 = ApplyStructure/ApplyRelations 后已建立的权威结构。
    /// </summary>
    private void ApplyProperties(List<ControlDescriptor> controls, ApplyResult result)
    {
        if (controls == null)
            return;

        // 单次遍历解析实体/控件（复用解析结果，避免排序与处理阶段重复解析）
        var work = new List<(ControlDescriptor Desc, Entity Entity, Control Control)>();
        foreach (var desc in controls)
        {
            if (!TryResolveEntity(desc, out var entity))
                continue;

            var control = _controlPool.GetControl(entity);
            if (control == null || control.IsDisposed)
                continue;

            work.Add((desc, entity, control));
        }

        // 父先子后：父控件 Bounds/SplitterDistance 先就位，子控件 ToParentRelative 换算基准才正确
        work.Sort((a, b) => LayoutQuery.PhysicalDepth(a.Control).CompareTo(LayoutQuery.PhysicalDepth(b.Control)));

        foreach (var (desc, entity, control) in work)
        {
            var props = BuildPropertyDict(desc);

            // 快照差异判定（唯一路径）：目标快照 vs 上次 AppliedSnapshot，无快照（新控件/首渲）→ 全量。
            var changed = GetSnapshotChanged(entity, desc, control, props);

            if (changed.Count == 0)
                continue;

            // R3·布局真源收敛（SingleImplementation）：ECS 布局托管实体（proportional / dock·anchor 原生意图）
            // 的像素 Bounds 不再由内容 Apply 写入——物理 Bounds 唯一由 LayoutProjector（纯 ECS 父原点）在挂载后投影。
            // 消除「内容 Apply 在 root 未挂宿主时经物理父链 ToParentRelative 换算 → 把 ECS 全局坐标当父相对 → 多重偏移」缺陷。
            // dock/anchor 由原生布局引擎接管（Dock/Anchor 属性仍由属性投影设置），两者均不依赖本处像素 Bounds。
            if (_entityManager != null && IsEcsLayoutManaged(entity))
                StripBoundsKeys(changed);

            // E3·SplitterRatio 双写消除：含 SplitterRatioComponent 的 splitcontainer，其 SplitterDistance 物理属性
            // 唯一由 LayoutProjector 按比例投影（SplitterMath 单一权威），内容 Apply 不再写该键，
            // 否则同一物理属性存在两个写入源 → 快照差异判定（AppliedSnapshot）与实际物理值漂移。
            if (_entityManager != null && _entityManager.HasComponent<SplitterRatioComponent>(entity))
                changed.Remove(UIProtocol.Prop_SplitterDistance);

            ApplyPropertiesFromDict(control, changed, entity);
            result.UpdatedCount++;

            // R1：应用成功后回写目标快照（归一化产物，ECS form 相对基准）——只写实际应用过的属性集。
            _appliedSnapshots.Set(entity, NormalizeSnapshot(desc, control, props));
        }
    }

    /// <summary>ECS 布局托管判定：实体是否由 ECS 布局（LayoutProjector 投影）或原生布局引擎接管，其像素 Bounds 不应由内容 Apply 写入。</summary>
    private bool IsEcsLayoutManaged(Entity entity)
        => _entityManager!.HasComponent<ProportionalLayoutComponent>(entity)
           || LayoutQuery.IsNativeIntent(_entityManager!, entity);

    /// <summary>移除像素 Bounds 键（Left/Top/Width/Height），使内容 Apply 不写布局托管实体的位置/尺寸。</summary>
    private static void StripBoundsKeys(Dictionary<string, object?> dict)
    {
        dict.Remove(UIProtocol.Prop_Left);
        dict.Remove(UIProtocol.Prop_Top);
        dict.Remove(UIProtocol.Prop_Width);
        dict.Remove(UIProtocol.Prop_Height);
    }

    /// <summary>快照驱动差异判定（唯一路径）：目标快照 vs 上次 AppliedSnapshot，无快照（新控件/首渲）→ 全量。</summary>
    private Dictionary<string, object?> GetSnapshotChanged(
        Entity entity,
        ControlDescriptor desc,
        Control control,
        Dictionary<string, object?> target)
    {
        var normalized = NormalizeSnapshot(desc, control, target);

        if (_appliedSnapshots.TryGet(entity, out var last))
            return PropertyComparer.GetChangedProperties(last, normalized);

        return normalized;
    }

    /// <summary>
    /// R3·快照归一：按投影策略注册表（DockProjector/PlatformConstrainedProjector/默认 PixelProjector）解析策略并归一目标属性集。
    /// 豁免规则从 if 链内聚为策略——新增控件类型/平台差异 = 注册新策略，不改投影核心。
    /// </summary>
    private Dictionary<string, object?> NormalizeSnapshot(
        ControlDescriptor desc,
        Control control,
        Dictionary<string, object?> target)
        => _projectionStrategies.Resolve(desc, control).Normalize(target, control);

    private void ApplyRelations(List<ChildRelation> relations, ApplyResult result)
    {
        result.RelationsProcessed = relations.Count;

        // 兄弟顺序收敛：按 (ParentId, OrderIndex) 稳定排序后挂载。顺序敏感容器（TabControl.TabPages / SplitterPanel
        // 子控件 / 普通 Controls 的 z-order）由此与声明序一致——ECS 枚举序与声明无关且非稳定（组件增删、边遍历边移出），
        // 不能作为挂载序。同一父下按 OrderIndex 递增；ParentId + ChildId 作稳定平局锚点，保证重复渲染次序可复现。
        var ordered = relations
            .OrderBy(r => r.ParentId, StringComparer.Ordinal)
            .ThenBy(r => r.OrderIndex)
            .ThenBy(r => r.ChildId, StringComparer.Ordinal)
            .ToList();

        foreach (var rel in ordered)
        {
            if (!TryParseEntityId(rel.ParentId, out var parentEntity))
            {
                // P7：挂载失败显式化——ParseFailed 进入跳过统计（不再仅 Debug 日志静默丢失）
                result.RelationsSkipped++;
                result.SkipReasons.Add($"ParseFailed parent={rel.ParentId}");
                FrontEndDrivenLog.Log("WinFormApplier_FindControl_ParseFailed",
                    new { nodeId = rel.ParentId, name = "(relation parent)" });
                continue;
            }

            if (!TryParseEntityId(rel.ChildId, out var childEntity))
            {
                result.RelationsSkipped++;
                result.SkipReasons.Add($"ParseFailed child={rel.ChildId}");
                FrontEndDrivenLog.Log("WinFormApplier_FindControl_ParseFailed",
                    new { nodeId = rel.ChildId, name = "(relation child)" });
                continue;
            }

            var parentControl = _controlPool.GetControl(parentEntity);
            var childControl = _controlPool.GetControl(childEntity);

            if (parentControl == null || childControl == null)
            {
                // P7：孤儿控件（对象入池但未挂载）是「UI 空但日志绿」的直接证据——升 Warning 并汇总统计
                result.RelationsSkipped++;
                result.SkipReasons.Add($"RelationControlNotFound parent={rel.ParentId} child={rel.ChildId}");
                FrontEndDrivenLog.Log("WinFormApplier_Skip_RelationControlNotFound",
                    new { parentId = rel.ParentId, childId = rel.ChildId });
                continue;
            }

            // M2 挂载成败透传：TryAddControl 已返回 adapter.AddControl 真实结果（SplitterPanelHelper M2 透传）。
            // false 覆盖：父/子未就绪、targetPanel 无效、AddControl 失败（如 TabPage 无 TabControl 宿主）。
            if (!SplitterPanelHelper.TryAddControl(_adapter, parentControl, rel.TargetPanel, childControl))
            {
                result.RelationsSkipped++;
                result.SkipReasons.Add($"MountFailed parent={rel.ParentId} child={rel.ChildId} target={rel.TargetPanel}");
                FrontEndDrivenLog.Log("WinFormApplier_Skip_MountFailed",
                    new { parentId = rel.ParentId, childId = rel.ChildId, targetPanel = rel.TargetPanel });
                continue;
            }

            // M2 物理核验（挂载判据物理闭环）：AddControl 返回 true 后再确认子控件确已进入宿主目标子集合
            // （TabPages / Controls / SplitterPanel），杜绝「调用即成功」——挂载失败不得在统计层被当成成功。
            if (!IsPhysicallyMounted(parentControl, rel.TargetPanel, childControl))
            {
                result.RelationsSkipped++;
                result.SkipReasons.Add($"PhysicallyNotMounted parent={rel.ParentId} child={rel.ChildId}");
                FrontEndDrivenLog.Log("WinFormApplier_Skip_PhysicallyNotMounted",
                    new { parentId = rel.ParentId, childId = rel.ChildId });
                continue;
            }

            result.RelationsApplied++;

            // 架构收口（方案3 + M6）：MountState 写入从本处移除——挂载事实不再由「配置关系边」驱动。
            // 真实挂载机制是「宿主注入」（ScopeLifecycleAdapter.RenderAsync ⑥ = MarkMounted 唯一权威点，
            // 经 EcsGate.LoadConfigAndRenderAsync(host)），Orphaned 在宿主销毁时（状态机 DestroyAsync →
            // ScopeDestroyed）落定。
            // ApplyRelations 仅负责结构挂载（AddControl），不再读/写容器挂载状态，消除
            // 「关系边 ≠ 宿主注入」两条挂载机制对同一容器口径分裂（sideContainer 可见树已挂载但 MountState=Orphaned）。
        }
    }

    /// <summary>
    /// M2 物理核验（挂载判据物理闭环）：判定 child 是否已实际进入挂载目标宿主。
    /// 经 IControlAdapter.Contains 平台适配宿主集合差异（TabControl.TabPages / 普通 Controls）。
    /// 有 targetPanel 时宿主为目标 SplitterPanel，否则为 parentControl 本身。
    /// </summary>
    private bool IsPhysicallyMounted(object parentControl, string? targetPanel, object childControl)
    {
        object host = parentControl;

        if (!string.IsNullOrEmpty(targetPanel))
        {
            var panel = _adapter.GetSplitterPanel(parentControl, targetPanel);
            if (panel == null)
                return false;
            host = panel;
        }

        return _adapter.Contains(host, childControl);
    }

    private static Dictionary<string, object?> BuildPropertyDict(ControlDescriptor desc)
    {
        var dict = new Dictionary<string, object?>
        {
            [UIProtocol.Prop_Left] = desc.Left,
            [UIProtocol.Prop_Top] = desc.Top,
            [UIProtocol.Prop_Width] = desc.Width,
            [UIProtocol.Prop_Height] = desc.Height,
            [UIProtocol.Prop_Name] = desc.Name,
            [UIProtocol.Prop_Text] = desc.Text,
            [UIProtocol.Prop_Enabled] = desc.Enabled,
            [UIProtocol.Prop_Visible] = desc.Visible,
        };

        if (!string.IsNullOrEmpty(desc.FontFamily))
            dict[UIProtocol.Prop_FontFamily] = desc.FontFamily;
        if (desc.FontSize > 0)
            dict[UIProtocol.Prop_FontSize] = desc.FontSize;
        if (desc.FontBold)
            dict[UIProtocol.Prop_FontBold] = desc.FontBold;
        if (desc.FontItalic)
            dict[UIProtocol.Prop_FontItalic] = desc.FontItalic;
        if (desc.FontUnderline)
            dict[UIProtocol.Prop_FontUnderline] = desc.FontUnderline;
        if (!string.IsNullOrEmpty(desc.ForeColor))
            dict[UIProtocol.Prop_ForeColor] = desc.ForeColor;
        if (!string.IsNullOrEmpty(desc.BackColor))
            dict[UIProtocol.Prop_BackColor] = desc.BackColor;

        dict[UIProtocol.Prop_BorderWidth] = desc.BorderWidth;
        if (!string.IsNullOrEmpty(desc.BorderColor))
            dict[UIProtocol.Prop_BorderColor] = desc.BorderColor;

        // M2·布局意图化：原生 Dock/Anchor 接管（dock/anchor 实体不写像素 Bounds，由原生引擎排布）
        if (desc.HasDock && !string.IsNullOrEmpty(desc.Dock))
            dict[UIProtocol.Prop_Dock] = desc.Dock;
        if (!string.IsNullOrEmpty(desc.Anchor))
            dict[UIProtocol.Prop_Anchor] = desc.Anchor;

        if (desc.SplitterDistance.HasValue)
            dict[UIProtocol.Prop_SplitterDistance] = desc.SplitterDistance.Value;

        return dict;
    }

    internal void ApplyPropertiesFromDict(Control control, Dictionary<string, object?> properties, Entity? entity = null)
    {
        // F5·写路径 ECS 单一权威：有 ECS 上下文时，父原点取自 EcsParentOrigin.Resolve（纯 ECS），
        // 由 PropertyApplier 用「全局 form 坐标 − ECS 父原点」写父相对 Bounds，替代物理父链 ToParentRelative。
        // 无 ECS 上下文（测试态/装配期）→ ecsParentOrigin=null → PropertyApplier 回退物理父链换算。
        Point? ecsParentOrigin = null;
        if (_entityManager != null && entity.HasValue && entity.Value.IsValid)
            ecsParentOrigin = EcsParentOrigin.Resolve(_entityManager, entity.Value);

        PropertyApplier.ApplyProperties(control, properties, _adapter, ecsParentOrigin);
    }

    /// <summary>
    /// Tab 容器选中态注入：对每个 tabcontrol 实体按 ControlDescriptor.SelectedIndex（缺省 0，越界回退 0）设置物理 SelectedIndex。
    /// 全部 TabPage 已挂载到 TabPages 集合后调用，使选中页（含子树）确定性可见，消除「无选中页→内容区空白」。
    /// </summary>
    private void ApplyTabSelection(List<ControlDescriptor> controls)
    {
        if (controls == null)
            return;

        foreach (var desc in controls)
        {
            if (desc == null
                || !string.Equals(desc.Type, "tabcontrol", StringComparison.OrdinalIgnoreCase)
                || !TryResolveEntity(desc, out var entity))
            {
                continue;
            }

            var control = _controlPool.GetControl(entity);
            if (control is not TabControl tabControl || tabControl.TabPages.Count == 0)
                continue;

            int idx = desc.SelectedIndex ?? 0;
            if (idx < 0 || idx >= tabControl.TabPages.Count)
                idx = 0;

            if (tabControl.SelectedIndex != idx)
                tabControl.SelectedIndex = idx;
        }
    }

    private bool TryParseEntityId(string str, out Entity entity)
    {
        return _entityIdCache.TryParse(str, out entity);
    }

    internal void ClearEntityIdCache()
    {
        _entityIdCache.Clear();
    }
}