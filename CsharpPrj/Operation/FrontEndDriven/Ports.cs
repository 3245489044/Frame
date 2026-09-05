#nullable enable

using System;
using ECS.Core;
using ECS.Bridge;
using ECS.Components;
using TConfig;
using FrontEndDriven.ECS.Components;
using System.Collections.Generic;
using System.Linq;
using CSG.Unified;
using FrontEndDriven.Abstractions;
using FrontEndDriven.ControlLookup;
using FrontEndDriven.Protocol;
using FrontEndDriven.WinForms.Adapter;
using FrontEndDriven.WinForms.Applier;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ECS;
using FrontEndDriven.ECS.Creation;
using FrontEndDriven.ControlRender.Layout;
using FrontEndDriven.Shared;
using FrontEndDriven.Shared.Utils;
using TConfig.ECS.Components;
using System.Windows.Forms;
using FrameworkAbstractions.ComponentRegistry;

namespace Ports;


public sealed class CsgEntryPoint
{
    private readonly EntityManager _entityManager;
    private readonly EntityQueryService _entityQuery;

    public CsgEntryPoint(EntityManager entityManager, EntityQueryService entityQuery)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _entityQuery = entityQuery ?? throw new ArgumentNullException(nameof(entityQuery));
    }


    private sealed record ControlData
    {
        public string Id { get; init; } = string.Empty;
        public string EntityIdStr { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string Text { get; init; } = string.Empty;
        public bool Enabled { get; init; }
        public bool Visible { get; init; }

        public bool HasLayout { get; init; }
        public int Left { get; init; }
        public int Top { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }

        public bool HasDock { get; init; }
        public string? Dock { get; init; }
        public string? Anchor { get; init; }

        public bool HasStyle { get; init; }
        public string? FontFamily { get; init; }
        public float FontSize { get; init; }
        public bool FontBold { get; init; }
        public bool FontItalic { get; init; }
        public bool FontUnderline { get; init; }
        public string? ForeColor { get; init; }
        public string? BackColor { get; init; }
        public int BorderWidth { get; init; }
        public string? BorderColor { get; init; }

        public int SplitterDistance { get; init; }

        public int? SelectedIndex { get; init; }

        public List<string>? Items { get; init; }

        public List<EventBindingInfo>? EventBindings { get; init; }

        public string? ExecutionResult { get; init; }
        public bool? ExecutionSuccess { get; init; }
        public string? ExecutionError { get; init; }
    }

    private sealed record EventBindingInfo(string EventName, string FunctionName);


    public CanonicalForm Collect()
    {
        FrontEndDrivenLog.Log("CsgEntryPoint_Collect_start");

        var builder = new CanonicalFormBuilder()
            .WithMetadata(UIProtocol.Meta_PipelineStage, "render")
            .WithMetadata(UIProtocol.Meta_Timestamp, DateTime.UtcNow.ToString("O"));

        CollectAllInOnePass(builder);

        var result = builder.Build();

        FrontEndDrivenLog.Log("CsgEntryPoint_Collect_complete",
            new { entityCount = result.Nodes?.Count ?? 0 });

        return result;
    }

    /// <summary>
    /// 收集快照（作用域感知，S3）。
    /// scope 非空时仅收集该作用域实体（经 ScopeComponent 过滤），消除「Side 渲染却收集 MainLayout 全量」的快照语义失真。
    /// scope 为 null/空 时保持原全量语义（兼容旧调用）。
    /// </summary>
    public WinFormSnapshot CollectSnapshot(string? scope = null)
    {
        var controls = new List<ControlDescriptor>();
        var relations = new List<ChildRelation>();

        // R1：共享收集骨架（实体枚举 + SplitterPanel 过滤 + CollectComponentData 单一数据源）
        foreach (var (entity, data) in EnumerateControls(scope))
        {
            controls.Add(ToControlDescriptor(data));

            var childRelation = BuildChildRelation(entity);
            if (childRelation != null)
                relations.Add(childRelation);
        }

        var result = new WinFormSnapshot
        {
            Controls = controls,
            Relations = relations
        };

        return result;
    }
    /// <summary>
    /// 收集骨架通用化（R1）：实体枚举 + SplitterPanel 过滤 + 共享 CollectComponentData 唯一数据源。
    /// Collect() 与 CollectSnapshot() 均经此枚举，消除逐行重复；新增第三输出形态仅需在此上追加转换。
    /// 无参版保持全量语义（兼容 CanonicalForm 收集等旧调用路径）。
    /// </summary>
    private IEnumerable<(Entity Entity, ControlData Data)> EnumerateControls()
        => EnumerateControls(null);

    /// <summary>
    /// 作用域感知枚举（S3）：scope 非空时仅返回 ScopeComponent.Scope 匹配的实体；
    /// scope 为 null/空 时返回全部实体（全量收集兼容路径）。
    /// D2 收敛：实体枚举（排除 SplitterPanel + scope 过滤）统一经 EntityQueryService.QueryControls，
    /// 消除本处手写过滤与查询服务重复（基集一致：ControlTypeComponent + 非 SplitterPanel + scope）。
    /// </summary>
    private IEnumerable<(Entity Entity, ControlData Data)> EnumerateControls(string? scope)
    {
        foreach (var entity in _entityQuery.QueryControls(scope))
        {
            yield return (entity, CollectComponentData(entity));
        }
    }


    private void CollectAllInOnePass(CanonicalFormBuilder builder)
    {
        // R1：共享收集骨架（实体枚举 + SplitterPanel 过滤 + CollectComponentData 单一数据源）
        foreach (var (_, data) in EnumerateControls())
        {
            var props = ToCanonicalProps(data);

            builder.AddNode(
                id: data.Id,
                label: data.Name ?? data.Id,
                type: CanonicalNodeType.Control,
                properties: props);
        }
    }

    private ControlData CollectComponentData(Entity entity)
    {
        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);

        bool hasInfoComp = _entityManager.TryGetComponent<ControlInfoComponent>(entity, out var infoComp);
        if (!hasInfoComp)
        {
            FrontEndDrivenLog.Log("CsgEntryPoint_CollectComponentData_missing_ControlInfoComponent",
                new { entity = entity.ToString(), type = typeComp.Type ?? "unknown" });
        }

        bool hasLayout = _entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var layout);
        bool hasLayoutIntent = _entityManager.TryGetComponent<LayoutIntentComponent>(entity, out var layoutIntent);
        bool hasStyle = _entityManager.TryGetComponent<MergedStyleComponent>(entity, out var style);
        bool hasBindings = _entityManager.TryGetComponent<EventBindingsComponent>(entity, out var bindComp);
        var execResult = GetLatestExecutionResult(entity.Id);

        List<EventBindingInfo>? eventBindings = null;
        if (hasBindings && bindComp.Bindings is { Count: > 0 })
        {
            eventBindings = bindComp.Bindings
                .Select(b => new EventBindingInfo(
                    b.EventName ?? string.Empty,
                    b.FunctionName ?? string.Empty))
                .ToList();
        }

        var data = new ControlData
        {
            Id = entity.ToString(),
            EntityIdStr = entity.Id.ToString(),
            Type = typeComp.Type ?? "unknown",
            Name = hasInfoComp ? infoComp.Name : null,
            Text = hasInfoComp ? infoComp.Text ?? string.Empty : string.Empty,
            Enabled = hasInfoComp && infoComp.Enabled,
            Visible = !hasInfoComp || infoComp.Visible,

            HasLayout = hasLayout,
            Left = hasLayout ? layout.Left : 0,
            Top = hasLayout ? layout.Top : 0,
            Width = hasLayout ? layout.Width : 0,
            Height = hasLayout ? layout.Height : 0,

            // M2·布局意图化：dock/anchor 由 RenderPort 原生接管（不写像素 Bounds）
            HasDock = hasLayoutIntent && string.Equals(layoutIntent.Intent, "dock", StringComparison.OrdinalIgnoreCase),
            Dock = hasLayoutIntent && string.Equals(layoutIntent.Intent, "dock", StringComparison.OrdinalIgnoreCase)
                ? layoutIntent.Dock
                : null,
            Anchor = hasLayoutIntent && string.Equals(layoutIntent.Intent, "anchor", StringComparison.OrdinalIgnoreCase)
                ? layoutIntent.Anchor
                : null,

            HasStyle = hasStyle,
            FontFamily = hasStyle ? style.FontFamily : null,
            FontSize = hasStyle ? style.FontSize : 0,
            FontBold = hasStyle && style.Bold,
            FontItalic = hasStyle && style.Italic,
            FontUnderline = hasStyle && style.Underline,
            ForeColor = hasStyle && !string.IsNullOrWhiteSpace(style.ForeColor) ? style.ForeColor : null,
            BackColor = hasStyle && !string.IsNullOrWhiteSpace(style.BackColor) ? style.BackColor : null,
            BorderWidth = hasStyle ? style.BorderWidth : 0,
            BorderColor = hasStyle && !string.IsNullOrWhiteSpace(style.BorderColor) ? style.BorderColor : null,

            SplitterDistance = CalculateSplitterDistance(entity),

            SelectedIndex = _entityManager.TryGetComponent<TabContainerComponent>(entity, out var tabContainer)
                ? tabContainer.SelectedIndex
                : (_entityManager.TryGetComponent<ControlStaticDataComponent>(entity, out var staticData)
                    ? staticData.SelectedIndex
                    : null),

            Items = _entityManager.TryGetComponent<ControlStaticDataComponent>(entity, out var staticDataItems)
                ? staticDataItems.Items
                : null,

            EventBindings = eventBindings,

            ExecutionResult = execResult?.Result?.ToString(),
            ExecutionSuccess = execResult?.Success,
            ExecutionError = execResult?.ErrorMessage
        };

        return data;
    }


    private static Dictionary<string, object?> ToCanonicalProps(ControlData data)
    {
        var props = new Dictionary<string, object?>
        {
            [UIProtocol.Prop_ControlType] = data.Type,
            [UIProtocol.Meta_EntityId] = data.EntityIdStr,
            [UIProtocol.Prop_Text] = data.Text,
            [UIProtocol.Prop_Enabled] = data.Enabled,
            [UIProtocol.Prop_Visible] = data.Visible
        };

        if (data.Name != null)
            props[UIProtocol.Prop_Name] = data.Name;

        if (data.HasLayout)
        {
            props[UIProtocol.Prop_Left] = data.Left;
            props[UIProtocol.Prop_Top] = data.Top;
            props[UIProtocol.Prop_Width] = data.Width;
            props[UIProtocol.Prop_Height] = data.Height;
        }

        if (data.HasStyle)
        {
            props[UIProtocol.Prop_FontFamily] = data.FontFamily ?? FrontEndDriven.FrontEndDrivenConstants.DefaultFontFamily;
            props[UIProtocol.Prop_FontSize] = data.FontSize;
            props[UIProtocol.Prop_FontBold] = data.FontBold;
            props[UIProtocol.Prop_FontItalic] = data.FontItalic;
            props[UIProtocol.Prop_FontUnderline] = data.FontUnderline;
            if (data.ForeColor != null)
                props[UIProtocol.Prop_ForeColor] = data.ForeColor;
            if (data.BackColor != null)
                props[UIProtocol.Prop_BackColor] = data.BackColor;
            props[UIProtocol.Prop_BorderWidth] = data.BorderWidth;
            if (data.BorderColor != null)
                props[UIProtocol.Prop_BorderColor] = data.BorderColor;
        }

        if (data.SplitterDistance > 0)
            props[UIProtocol.Prop_SplitterDistance] = data.SplitterDistance;

        if (data.EventBindings is { Count: > 0 })
        {
            props[UIProtocol.Prop_EventBindings] = string.Join(",",
                data.EventBindings.Select(b => b.EventName));
            props[UIProtocol.Prop_FunctionNames] = string.Join(",",
                data.EventBindings.Select(b => b.FunctionName));
        }

        return props;
    }


    private static ControlDescriptor ToControlDescriptor(ControlData data)
    {
        var desc = new ControlDescriptor
        {
            Id = data.Id,
            Name = data.Name ?? data.Id,
            Type = data.Type,
            Text = data.Text,
            Enabled = data.Enabled,
            Visible = data.Visible
        };

        if (data.HasLayout)
        {
            desc.Left = data.Left;
            desc.Top = data.Top;
            desc.Width = data.Width;
            desc.Height = data.Height;
        }

        if (data.HasStyle)
        {
            desc.FontFamily = data.FontFamily;
            desc.FontSize = data.FontSize;
            desc.FontBold = data.FontBold;
            desc.FontItalic = data.FontItalic;
            desc.FontUnderline = data.FontUnderline;
            desc.ForeColor = data.ForeColor;
            desc.BackColor = data.BackColor;
            desc.BorderWidth = data.BorderWidth;
            desc.BorderColor = data.BorderColor;
        }

        desc.SplitterDistance = data.SplitterDistance;

        desc.SelectedIndex = data.SelectedIndex;

        desc.Items = data.Items;

        // M2·布局意图化：原生 Dock/Anchor 意图透传（dock/anchor 实体不写像素 Bounds）
        desc.HasDock = data.HasDock;
        desc.Dock = data.Dock;
        desc.Anchor = data.Anchor;

        if (data.EventBindings is { Count: > 0 })
        {
            desc.EventBindings = data.EventBindings
                .Select(b => new EventBindingDescriptor
                {
                    EventName = b.EventName,
                    FunctionName = b.FunctionName
                })
                .ToList();
        }

        desc.ExecutionResult = data.ExecutionResult;
        desc.ExecutionSuccess = data.ExecutionSuccess ?? false;
        desc.ExecutionError = data.ExecutionError;

        return desc;
    }


    private ExecutionResultComponent? GetLatestExecutionResult(int sourceEntityId)
    {
        // D10：单次扫描取最大 CompletedTimestamp（免全量排序）——语义与原 OrderByDescending.FirstOrDefault 等价。
        ExecutionResultComponent? latest = null;
        foreach (var e in _entityManager.GetEntities<ExecutionResultComponent>())
        {
            var r = _entityManager.GetComponent<ExecutionResultComponent>(e);
            if (r.SourceEntityId != sourceEntityId)
                continue;
            if (latest == null)
            {
                latest = r;
                continue;
            }
            var current = latest.GetValueOrDefault();
            if (r.CompletedTimestamp > current.CompletedTimestamp)
                latest = r;
        }
        return latest;
    }

    private int CalculateSplitterDistance(Entity entity)
    {
        if (_entityManager.TryGetComponent<SplitterRatioComponent>(entity, out var ratio)
            && _entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var compLayout))
        {
            // E1 收敛：方向感知基准（SplitterMath 单一权威），与 LayoutProjector 反应式布局一致，
            // 消除残留 Math.Max 对横向分割（比高还宽）取错基准维度的漂移。
            return SplitterMath.Distance(ratio.Ratio, ResolveOrientation(entity), compLayout.Width, compLayout.Height);
        }
        return 0;
    }

    /// <summary>
    /// 解析 SplitContainer 实体方向（E1）：经 AppServiceProvider 取物理控件（ControlPool 已注册），
    /// 读取实际 Orientation；取不到物理控件时回退 Vertical（与主路径/布局结算默认一致）。
    /// </summary>
    private static Orientation ResolveOrientation(Entity entity)
    {
        var pool = FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Resolve<ControlPool>();
        return pool?.GetControl(entity) is SplitContainer sc && !sc.IsDisposed
            ? sc.Orientation
            : Orientation.Vertical;
    }

    private ChildRelation? BuildChildRelation(Entity entity)
    {
        if (!_entityManager.TryGetComponent<ParentEntityComponent>(entity, out var parentComp)
            || !parentComp.ParentEntity.IsValid)
            return null;

        SplitterPanelHelper.ResolveSplitterParent(
            _entityManager, parentComp.ParentEntity,
            out var effectiveParent, out var targetPanel);

        // 兄弟顺序：读取声明下标（SiblingOrderComponent），供挂载按 (ParentId, OrderIndex) 稳定排序。
        int orderIndex = 0;
        if (_entityManager.TryGetComponent<SiblingOrderComponent>(entity, out var sibling))
            orderIndex = sibling.OrderIndex;

        return new ChildRelation
        {
            ParentId = effectiveParent.ToString(),
            ChildId = entity.ToString(),
            TargetPanel = targetPanel,
            OrderIndex = orderIndex
        };
    }
}




public sealed class WinFormsOutputPort
    : IControlCreationOutput, IEventSubscriptionOutput
{
    private readonly WinFormsUIAdapter _winFormsAdapter;
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly ContainerBasisResolver _basisResolver;
    private readonly IControlAdapter _adapter;
    private readonly WinFormApplier _winFormApplier;
    private readonly FrontEndDriven.ControlLookup.SplitterPanelRegistry _splitterPanelRegistry;

    public FrontEndDriven.ControlLookup.SplitterPanelRegistry SplitterPanelRegistry => _splitterPanelRegistry;

    public bool IsFirstApply => _isFirstApplySnapshot;
    private bool _isFirstApplySnapshot = true;

    public int LastAppliedNodeCount { get; private set; }

    private readonly Dictionary<object, Action> _splitterUnsubscribeActions = new();

    private bool _suppressProjectionEvents;

    public WinFormsOutputPort(
        WinFormsUIAdapter winFormsAdapter,
        EntityManager entityManager,
        ControlPool controlPool,
        IControlAdapter adapter,
        FrontEndDriven.ControlLookup.SplitterPanelRegistry? splitterPanelRegistry = null,
        FrontEndDriven.Shared.Utils.EntityIdCache? entityIdCache = null)
    {
        _winFormsAdapter = winFormsAdapter ?? throw new ArgumentNullException(nameof(winFormsAdapter));
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _basisResolver = new ContainerBasisResolver(entityManager, controlPool);
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _splitterPanelRegistry = splitterPanelRegistry ?? new FrontEndDriven.ControlLookup.SplitterPanelRegistry();

        // R1：快照驱动投影（唯一路径）——带 EntityManager 的 AppliedSnapshotRegistry 随实体销毁自动清理
        // （EntityDestroying 订阅，杜绝残留快照 → 虚假差异）；旧物理差异路径（GetCoreChanged）已随双轨开关移除。
        _winFormApplier = new WinFormApplier(controlPool, adapter, _splitterPanelRegistry, entityIdCache,
            new FrontEndDriven.WinForms.Applier.AppliedSnapshotRegistry(entityManager), entityManager);
    }

    public ApplyResult ApplySnapshot(WinFormSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        FrontEndDrivenLog.Log("WinFormsOutputPort_ApplySnapshot_start",
            new { controlCount = snapshot.Controls.Count, relationCount = snapshot.Relations.Count });

        _suppressProjectionEvents = true;
        ApplyResult applyResult;
        try
        {
            applyResult = _winFormApplier.Apply(snapshot);
        }
        finally
        {
            _suppressProjectionEvents = false;
        }

        LastAppliedNodeCount = snapshot.Controls.Count;

        // S3：ECS_MISMATCH 审计已移至挂载后（RenderStageRunner 在管线含挂载+ECS 重排后调用 RunMismatchAudit）。
        // 布局真源在挂载后（ReflowNow 重算 + 投影）才收敛；在 ApplySnapshot 内审计将先于物理 Bounds 写入而误报。

        _isFirstApplySnapshot = false;

        // P7：挂载结果回传（AllMounted=false → 上层审计/断言可判定「控件未挂载可见」）
        return applyResult;
    }

    /// <summary>
    /// S3·挂载后 ECS_MISMATCH 只读审计（每次渲染管线完成、含挂载 + 挂载后 ECS 重排后调用）。
    /// 布局真源在挂载后（ReflowNow 重算 + 投影）才收敛——物理≠ECS 须于此后比对，否则首渲误报。
    /// 返回缺陷数（ComboBox DropDownList 平台豁免不计）。
    /// </summary>
    public int RunMismatchAudit(string? scope = null)
    {
        try
        {
            // 布局真源在挂载后（ReflowNow 重算 + 投影）收敛，TabPage 基准由同步父区推导、
            // TabControl.Resize 触发 reflow 自愈——无需审计前再补捕获/重排。

            return SyncActualContainerSizes();
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("SyncActualContainerSizes_failed",
                new { context = "RunMismatchAudit", error = ex.Message });
            return 0;
        }
    }


    public void CreateControl(Entity entity, string controlType)
    {
        var control = ControlStrategyRegistry.Create<Control>(controlType);
        if (control == null) return;

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);
        var controlName = typeComp.Name ?? entity.ToString();
        _controlPool.Register(entity, controlName, control);

        // 行为属性应用（multiline/readOnly/scrollBars 创建期应用）；AutoSize 统一由布局应用内核
        // （PropertyApplier.SetBounds 分支）保证，此处不再创建期补丁（S3·单一权威）。
        ApplyControlBehavior(entity, control);

        // 静态数据应用（items/selectedIndex/checkedItems/columns/view/checked/dropDownStyle 创建期应用，
        // 经 ControlStaticDataApplier 类型驱动；与 ApplyControlBehavior 同构，单点应用，不做快照差异重复应用）。
        ApplyStaticData(entity, control);

        // M3·交互采集：物理控件事件 → ECS 事件链（命令 click 与 GUI 人手点击同链；幂等挂接）
        var capturePort = AppServiceProvider.Resolve<FrontEndDriven.ControlInteraction.EventBinding.InputCapturePort>();
        capturePort?.Subscribe(control, entity);
    }

    /// <summary>
    /// 行为属性应用：ControlBehaviorComponent（ControlFactory 从 ControlConfig 写入）→ 物理控件。
    /// AutoSize=false 使布局高度由 ECS 驱动（禁原生单行自动回缩）；Multiline/ReadOnly/ScrollBars 按配置生效。
    /// </summary>
    private void ApplyControlBehavior(Entity entity, Control control)
    {
        if (control is not TextBoxBase textBoxBase)
            return;

        // S3：AutoSize 统一由布局应用内核（PropertyApplier.SetBounds 分支）在应用前强制关闭——
        // 布局高度由 ECS 驱动，行为适配下沉到单一权威，此处不再创建期补丁。
        if (!_entityManager.TryGetComponent<ControlBehaviorComponent>(entity, out var behavior))
            return;

        if (behavior.Multiline)
        {
            textBoxBase.Multiline = true;

            if (textBoxBase is TextBox textBox && !string.IsNullOrWhiteSpace(behavior.ScrollBars))
                textBox.ScrollBars = ParseScrollBars(behavior.ScrollBars);
        }

        if (behavior.ReadOnly)
            textBoxBase.ReadOnly = true;
    }

    /// <summary>
    /// 静态数据应用：ControlStaticDataComponent（ControlFactory 从 ControlConfig 写入）→ 物理控件。
    /// 创建期单点应用（与 ApplyControlBehavior 同构），不做快照差异重复应用，避免双写。
    /// 覆盖但未登记应用器的类型 → 显式告警（声明即覆盖守卫：有静态数据却被丢弃，不再静默）。
    /// </summary>
    private void ApplyStaticData(Entity entity, Control control)
    {
        if (!_entityManager.TryGetComponent<ControlStaticDataComponent>(entity, out var data))
            return;

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);
        var type = typeComp.Type;

        // 物理控件 Name 在创建期尚未由属性应用（PropertyApplier）设置 → 用实体侧 ControlInfoComponent 名，
        // 保证日志可定位到具体控件（多个同型控件不混淆）。
        string controlName = _entityManager.TryGetComponent<ControlInfoComponent>(entity, out var info)
            ? info.Name
            : control.Name;

        if (!ControlStaticDataApplier.HasApplier(type))
        {
            FrontEndDrivenLog.Log("ControlStaticData_dropped_no_applier",
                new { controlName, controlType = type });
            return;
        }

        ControlStaticDataApplier.Apply(type, control, data);

        FrontEndDrivenLog.Log("ControlStaticData_applied",
            new { controlName, controlType = type, itemCount = data.Items?.Count ?? 0 });
    }

    private static ScrollBars ParseScrollBars(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "both" => ScrollBars.Both,
            "horizontal" => ScrollBars.Horizontal,
            "vertical" => ScrollBars.Vertical,
            _ => ScrollBars.None
        };
    }

    public void AddToParent(Entity childEntity, Entity parentEntity)
    {
        // 父子挂载唯一权威阶段 = WinFormApplier.ApplyRelations（Create 阶段仅尽力而为，不静默丢边）。
        // 此处任一控件解析失败只记录诊断日志，父链最终由 Apply 阶段建立；不做入队补挂（避免双父链）。
        var childControl = _controlPool.GetControl(childEntity);
        if (childControl == null)
        {
            FrontEndDrivenLog.Log("WinFormsOutputPort_Skip_ChildNotReady",
                new { childEntityId = childEntity.Id.ToString(), parentEntityId = parentEntity.Id.ToString(), note = "子控件未入池，父链由 WinFormApplier.ApplyRelations 建立" });
            return;
        }

        SplitterPanelHelper.ResolveSplitterParent(
            _entityManager, parentEntity,
            out var resolvedParent, out var targetPanel);
        parentEntity = resolvedParent;

        var parentControl = _controlPool.GetControl(parentEntity);
        if (parentControl == null)
        {
            FrontEndDrivenLog.Log("WinFormsOutputPort_Skip_ParentNotReady",
                new { childEntityId = childEntity.Id.ToString(), parentEntityId = parentEntity.Id.ToString(), note = "父控件未入池，父链由 WinFormApplier.ApplyRelations 建立" });
            return;
        }

        if (!SplitterPanelHelper.TryAddControl(_adapter, parentControl, targetPanel, childControl))
        {
            FrontEndDrivenLog.Log("WinFormsOutputPort_Skip_InvalidTargetPanel",
                new { childEntityId = childEntity.Id.ToString(), parentEntityId = parentEntity.Id.ToString(), targetPanel });
        }
    }

    public void RegisterMapping(Entity entity, string controlName, object control)
    {
        _controlPool.Register(entity, controlName, (Control)control);
    }


    public void SubscribeSplitterMoved(object control, Entity entity)
    {
        UnsubscribeSplitterMoved(control);

        var unsubscribe = _adapter.SubscribeSplitterMoved(control,
            (sender, e) => OnSplitterMoved(sender, e, entity));
        _splitterUnsubscribeActions[control] = unsubscribe;
    }

    public void UnsubscribeSplitterMoved(object control)
    {
        if (_splitterUnsubscribeActions.TryGetValue(control, out var unsubscribe))
        {
            unsubscribe();
            _splitterUnsubscribeActions.Remove(control);
        }
    }

    public void UnsubscribeSplitterMoved(Entity entity)
    {
        var control = _controlPool.GetControl(entity);
        if (control != null)
            UnsubscribeSplitterMoved(control);
    }

    private void OnSplitterMoved(object? sender, EventArgs e, Entity entity)
    {
        if (sender == null || !entity.IsValid) return;

        // 投影期间（ApplySnapshot）抑制：不读回自投影产生的 SplitterDistance，避免「投影 → 事件 → 再投影」自循环。
        if (_suppressProjectionEvents)
            return;

        try
        {
            if (!_entityManager.TryGetComponent<SplitterRatioComponent>(entity, out var ecsRatio))
                return;

            var (width, height) = _winFormsAdapter.GetSize(sender);
            var orientation = _adapter.GetOrientation(sender);
            int dimension = orientation == Orientation.Vertical ? width : height;
            if (dimension <= 0) return;

            double physicalRatio = Math.Clamp(
                (double)_adapter.GetSplitterDistance(sender) / dimension, 0.0, 1.0);

            // Phase 4·Splitter 真源闭环：物理拖拽比例回写 ECS（SplitterRatioComponent 为唯一真源），
            // 使后续布局重算（宿主 Resize 触发的 RecomputeAndProject / splitter 所属 scope 的 ForceReflow）使用新比例，
            // 不再用旧比例覆盖物理拖拽结果——这是对 M1「拖拽被回滚」缺陷的正确收敛（M1 靠放弃回灌规避，现以真源回灌 + scope 感知重算根治）。
            // 0.01 容差：比例未实质变化不写、不触发重排，兼作循环兜底（程序化 SetSplitterDistance 再次触发本事件的幂等短路）。
            if (Math.Abs(physicalRatio - ecsRatio.Ratio) > 0.01)
            {
                _entityManager.AddOrSetComponent(entity, new SplitterRatioComponent { Ratio = physicalRatio });

                FrontEndDrivenLog.Log("OnSplitterMoved_truth_update",
                    new { entityId = entity.Id.ToString(), ratio = physicalRatio });

                // R3·统一调度：splitter 拖拽经 LayoutSubsystem.RequestReflow 汇入 LayoutScheduler，
                // 与宿主 Resize 重排共用同一 latest-wins 调度、UI 线程单消费者串行（消除并发踩踏/重复投影）。
                if (_entityManager.TryGetComponent<ScopeComponent>(entity, out var scopeComp)
                    && !string.IsNullOrWhiteSpace(scopeComp.Scope))
                {
                    AppServiceProvider.Resolve<FrontEndDriven.ControlRender.Layout.LayoutSubsystem>()
                        ?.RequestReflow(scopeComp.Scope);
                }
            }
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("OnSplitterMoved_audit_failed",
                new { entityId = entity.Id.ToString(), error = ex.Message });
        }
    }


    private int SyncActualContainerSizes()
    {
        // M1·真源单向化：本方法为只读一致性审计（不回写 ECS）。
        // ECS 布局为唯一真源；WinForms 实际尺寸仅与 ECS ContainerSizeComponent 比对，
        // 不一致即 ECS_MISMATCH 缺陷（S3：从 Debug 常态记录升 Warning 缺陷信号，计数计入 ApplyResult.MismatchCount）。
        var containerEntities = _entityManager.GetEntities<ContainerSizeComponent>();
        int mismatchCount = 0;

        foreach (var entity in containerEntities)
        {
            var control = _controlPool.GetControl(entity);
            if (control == null) continue;

            try
            {
                // R3：复用尺寸读取辅助（GetSize 组合入口）
                (int actualWidth, int actualHeight) = _winFormsAdapter.GetSize(control);

                if (actualWidth > 0 && actualHeight > 0)
                {
                    var current = _entityManager.GetComponent<ContainerSizeComponent>(entity);

                    var expectedWidth = current.Width;
                    var expectedHeight = current.Height;
                    if (_entityManager.TryGetComponent<HostBasisComponent>(entity, out var basis) && basis.IsValid)
                    {
                        // M1·布局基准意图化：root 实体以 HostBasisComponent 为布局基准（ContainerSizeComponent 仅配置初始值），
                        // 审计预期取 HostBasisComponent；子容器仍以 ContainerSizeComponent 为准。
                        expectedWidth = basis.Width;
                        expectedHeight = basis.Height;
                    }
                    else if (control is TabPage
                             && _entityManager.TryGetComponent<ParentEntityComponent>(entity, out var parentComp)
                             && parentComp.ParentEntity.IsValid
                             && _basisResolver.TryResolveChildArea(parentComp.ParentEntity, out _, out _, out var tabW, out var tabH))
                    {
                        // TabPage 为 TabControl 托管子容器：可达成显示区 = 父 TabControl 同步父区扣页签条+边框 inset，
                        // 由 ContainerBasisResolver 同步推导（父 ContainerSize 减 DisplayRectangle 偏移），
                        // 替代陈旧 ContainerSizeComponent——消除「TabPage 尺寸不一致」审计误报（与布局真源一致）。
                        expectedWidth = tabW;
                        expectedHeight = tabH;
                    }

                    if (Math.Abs(expectedWidth - actualWidth) > 2
                        || Math.Abs(expectedHeight - actualHeight) > 2)
                    {
                        // 重构前 Tab-managed 容器（TabPage）需要托管几何特殊预期；现在 TabPage 的
                        // ContainerSizeComponent 已是「同步父区扣 inset」的可达成值 = 物理显示区，审计直接匹配。
                        if (control is ComboBox comboBox && comboBox.DropDownStyle == ComboBoxStyle.DropDownList)
                        {
                            // S3·ComboBox 平台豁免：DropDownList 实际高度由字体约束（SetBounds 高度不生效），
                            // 属平台固有行为，记 Debug 豁免声明、不计缺陷。
                            FrontEndDrivenLog.Log("SyncActualContainerSizes_combo_exempted",
                                new
                                {
                                    entityId = entity.Id.ToString(),
                                    controlName = control?.Name,
                                    ecsWidth = expectedWidth,
                                    ecsHeight = expectedHeight,
                                    actualWidth,
                                    actualHeight
                                });
                            continue;
                        }

                        mismatchCount++;
                        FrontEndDrivenLog.Log("SyncActualContainerSizes_mismatch",
                            new
                            {
                                entityId = entity.Id.ToString(),
                                controlName = control?.Name,
                                controlType = control?.GetType().Name,
                                ecsWidth = expectedWidth,
                                ecsHeight = expectedHeight,
                                actualWidth,
                                actualHeight,
                                note = "ExternalConsistency: ECS 布局为唯一真源，WinForms 实际尺寸仅审计不回写"
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                FrontEndDrivenLog.Log("SyncActualContainerSizes_failed",
                    new { entityId = entity.Id.ToString(), controlName = control?.Name, error = ex.Message });
            }
        }

        return mismatchCount;
    }

    internal WinFormsUIAdapter WinFormsAdapter => _winFormsAdapter;
}

public sealed class EcsInputPort
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly ConfigDrivenControlFactory _configDrivenFactory;

    public event Action? DataChanged;

    public EcsInputPort(
        EntityManager entityManager,
        ControlPool controlPool,
        ConfigDrivenControlFactory configDrivenFactory)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        _configDrivenFactory = configDrivenFactory ?? throw new ArgumentNullException(nameof(configDrivenFactory));
    }

    public Entity LoadConfig(UIConfiguration config, string scope = "MainLayout")
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // FrontEndDriven 亦解析 events.bindings.workflows：把配置树中声明的显式工作流定义
        // 注册进事件执行服务（WorkflowMethodExecutionService → FunctionHub），使事件绑定可将
        // click 指向工作流名执行多节点编排（而非裸函数自动包装），参数经事件命名参数传递。
        RegisterDeclaredWorkflows(config);

        var rootEntity = _configDrivenFactory.Convert(config, scope);

        FrontEndDrivenLog.Log("EcsInputPort_LoadConfig_complete",
            new { rootEntityId = rootEntity.Id.ToString(), scope });

        return rootEntity;
    }

    /// <summary>
    /// FrontEndDriven 配置侧工作流注册（对齐 BackEndDriven BindingMetadataTransformer → RegisterWorkflows 语义）：
    /// 递归扫描 UIConfiguration 控件树，收集各控件 events.bindings.workflows 的显式工作流定义，
    /// 合并后一次性注册进 IEventExecutionService（WorkflowMethodExecutionService → FunctionHub），
    /// 使事件绑定可将 click 指向工作流名、参数经工作流命名参数 ${...} 传递、输出经节点 outputTarget 回写。
    /// </summary>
    private static void RegisterDeclaredWorkflows(UIConfiguration config)
    {
        if (config?.Views is not { Count: > 0 })
            return;

        Dictionary<string, TConfig.WorkflowDefinitionEntry>? collected = null;

        void Collect(TConfig.ControlConfig? control)
        {
            if (control == null) return;

            if (control.Events?.Bindings?.Workflows is { Count: > 0 })
            {
                collected ??= new Dictionary<string, TConfig.WorkflowDefinitionEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in control.Events.Bindings.Workflows)
                    collected[kvp.Key] = kvp.Value;
            }

            if (control.Controls is { Count: > 0 })
                foreach (var c in control.Controls) Collect(c);
            if (control.Panel1Controls is { Count: > 0 })
                foreach (var c in control.Panel1Controls) Collect(c);
            if (control.Panel2Controls is { Count: > 0 })
                foreach (var c in control.Panel2Controls) Collect(c);
        }

        foreach (var view in config.Views)
        {
            if (view?.Controls is { Count: > 0 })
                foreach (var c in view.Controls) Collect(c);
        }

        if (collected is { Count: > 0 })
        {
            var exec = FrameworkAbstractions.ComponentRegistry.AppServiceProvider
                .Resolve<TConfig.IEventExecutionService>();
            if (exec != null)
            {
                exec.RegisterWorkflows(collected);
                FrontEndDrivenLog.Log("EcsInputPort_RegisterWorkflows",
                    new { workflowCount = collected.Count, names = string.Join(",", collected.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) });
            }
        }
    }

    public void ApplyDataChange<T>(Entity entity, T newValue, Func<T?, T>? updateAction = null)
        where T : struct, IComponent
    {
        if (!entity.IsValid)
            throw new ArgumentException("Entity 无效", nameof(entity));

        FrontEndDrivenLog.Log("EcsInputPort_ApplyDataChange_start",
            new { entityId = entity.Id.ToString(), componentType = typeof(T).Name });

        try
        {
            if (updateAction != null)
            {
                var oldValue = _entityManager.HasComponent<T>(entity)
                    ? _entityManager.GetComponent<T>(entity)
                    : default(T?);
                var updated = updateAction(oldValue);
                _entityManager.AddOrSetComponent(entity, updated);
            }
            else
            {
                _entityManager.AddOrSetComponent(entity, newValue);
            }
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("EcsInputPort_ApplyDataChange_failed",
                new { entityId = entity.Id.ToString(), componentType = typeof(T).Name, error = ex.Message });
            throw;
        }

        DataChanged?.Invoke();
    }


}

