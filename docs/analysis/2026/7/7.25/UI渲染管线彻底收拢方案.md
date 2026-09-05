# UI 渲染管线彻底收拢方案

> 基于 IoC 注入的渲染管线重构设计文档
> 2026-07-24

---

## 一、现状分析

### 1.1 当前管线架构

```
ExecutePipeline()
├── EcsCapabilityApplier.Update()      ← 直接操作 WinForms（创建控件、添加父子关系）
├── LayoutSystem.Update()              ← 纯 ECS（比例→像素，只写 Component）✅
├── SplitContainerSystem.Update()      ← 直接操作 WinForms（订阅 SplitterMoved 事件）
├── StyleSystem.Update()               ← 纯 ECS（样式合并，只写 Component）✅
├── EventBindingSystem.Update()        ← 直接操作 WinForms（绑定事件处理器）
├── CsgEntryPoint.Collect()            ← 混合（读 Component + 读控件实际状态）
└── WinFormsOutputPort.Apply()         ← 直接操作 WinForms（应用属性到控件）
```

### 1.2 绕过收拢点的具体问题

| 问题 | 位置 | 表现 |
|------|------|------|
| **控件创建绕过** | `EcsCapabilityApplier.Update()` | 直接调用 `_adapter.AddControl()` 和 `_controlPool.Register()`，未经过 WinFormsOutputPort |
| **事件订阅绕过** | `SplitContainerSystem.Update()` | 直接调用 `_adapter.SubscribeSplitterMoved()`，未经过统一的 EventBinding 机制 |
| **状态源混合** | `CsgEntryPoint.CollectAllInOnePass()` | 布局/样式从 Component 读取，但 Text/Enabled/Visible 从控件读取，ECS World 不是唯一真实来源 |
| **残留代码** | `StyleSystem.ApplyStyle()` | 方法存在但未被调用，重构不彻底的痕迹 |
| **硬编码依赖** | 所有 System | 构造函数直接依赖 `IWinFormsControlAdapter`，System 知道 WinForms 的存在 |

### 1.3 核心矛盾

**"三收拢点"的设计意图**是：
```
EcsInputPort (写ECS) → ECS Systems (纯计算) → CsgEntryPoint (读ECS→CSG) → WinFormsOutputPort (写控件)
```

但实际实现中，**ECS Systems 阶段混入了直接操作 WinForms 的代码**，绕过了管线末尾的输出端口。

---

## 二、重构目标

### 2.1 核心原则

1. **ECS Systems 零 WinForms 依赖** — System 不知道 WinForms 的存在，只操作 Component
2. **所有 WinForms 操作收拢到 WinFormsOutputPort** — 控件创建、事件订阅、属性应用全部通过输出端口
3. **ECS World 是唯一真实来源** — CsgEntryPoint 只从 Component 读取，不从控件读取
4. **IoC 注入解耦** — System 通过注入的抽象接口与输出端口通信，不直接依赖具体实现

### 2.2 目标架构

```
ExecutePipeline()
├── EcsCapabilityApplier.Update()      ← 只写 PendingCreationComponent ✅
├── LayoutSystem.Update()              ← 纯 ECS（比例→像素）✅
├── SplitContainerSystem.Update()      ← 只写 PendingEventSubscriptionComponent ✅
├── StyleSystem.Update()               ← 纯 ECS（样式合并）✅
├── EventBindingSystem.Update()        ← 只写 PendingEventBindingComponent ✅
├── CsgEntryPoint.Collect()            ← 只读 Component ✅
└── WinFormsOutputPort.Apply()         ← 唯一操作 WinForms 的出口 ✅
     ├── 控件创建（从 PendingCreationComponent 读取）
     ├── 事件订阅（从 PendingEventSubscriptionComponent 读取）
     ├── 属性应用（从 CanonicalForm 读取）
     └── 事件绑定（从 EventBindingsComponent 读取）
```

---

## 三、详细设计方案

### 3.1 定义输出通道接口

在 `Ports.cs` 或独立文件中定义一组细粒度接口：

```csharp
// ============================================================
// IOutputPort.cs — 输出通道接口定义
// ============================================================

/// <summary>
/// 控件创建输出通道
/// </summary>
public interface IControlCreationOutput
{
    /// <summary>为 Entity 创建 WinForms 控件实例</summary>
    void CreateControl(Entity entity, string controlType);

    /// <summary>将子控件添加到父控件</summary>
    void AddToParent(Entity childEntity, Entity parentEntity);

    /// <summary>注册 Entity↔Control 映射</summary>
    void RegisterMapping(Entity entity, string controlName, object control);
}

/// <summary>
/// 事件订阅输出通道
/// </summary>
public interface IEventSubscriptionOutput
{
    /// <summary>订阅 SplitterMoved 事件</summary>
    void SubscribeSplitterMoved(object control, Entity entity);

    /// <summary>取消订阅 SplitterMoved 事件</summary>
    void UnsubscribeSplitterMoved(object control);
}

/// <summary>
/// 属性应用输出通道
/// </summary>
public interface IPropertyApplyOutput
{
    /// <summary>应用属性集合到控件</summary>
    void ApplyProperties(Entity entity, Dictionary<string, object?> properties);

    /// <summary>应用单个值变更到控件</summary>
    void ApplyValueChange(object control, object? value);
}

/// <summary>
/// 事件绑定输出通道
/// </summary>
public interface IEventBindingOutput
{
    /// <summary>绑定事件处理器</summary>
    void BindEvent(object control, string eventName, string functionName, object bindingData);
}
```

### 3.2 WinFormsOutputPort 实现所有输出通道

```csharp
// WinFormsOutputPort 实现所有输出通道接口
public sealed class WinFormsOutputPort
    : IControlCreationOutput, IEventSubscriptionOutput,
      IPropertyApplyOutput, IEventBindingOutput
{
    // 原有依赖保持不变
    private readonly WinFormsUIAdapter _winFormsAdapter;
    private readonly UIReconciler _reconciler;
    private readonly CsgFacade _csg;
    private readonly IControlValueAccessor _valueAccessor;
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly IWinFormsControlAdapter _adapter;

    // ========== IControlCreationOutput ==========

    public void CreateControl(Entity entity, string controlType)
    {
        var control = ControlStrategyRegistry.Create(controlType);
        if (control == null) return;

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);
        var controlName = typeComp.Name ?? entity.Id.ToString();
        _controlPool.Register(entity, controlName, (Control)control);
    }

    public void AddToParent(Entity childEntity, Entity parentEntity)
    {
        var childControl = _controlPool.GetControl(childEntity);
        var parentControl = _controlPool.GetControl(parentEntity);
        if (childControl == null || parentControl == null) return;

        // SplitterPanel 特殊处理
        if (_entityManager.HasComponent<SplitterPanelComponent>(parentEntity))
        {
            var panel = _entityManager.GetComponent<SplitterPanelComponent>(parentEntity);
            var targetPanel = panel.PanelSide == "Panel1"
                ? _adapter.GetSplitterPanel(parentControl, "Panel1")
                : _adapter.GetSplitterPanel(parentControl, "Panel2");
            if (targetPanel != null)
                _adapter.AddControl(targetPanel, childControl);
        }
        else
        {
            _adapter.AddControl(parentControl, childControl);
        }
    }

    public void RegisterMapping(Entity entity, string controlName, object control)
    {
        _controlPool.Register(entity, controlName, (Control)control);
    }

    // ========== IEventSubscriptionOutput ==========

    public void SubscribeSplitterMoved(object control, Entity entity)
    {
        _adapter.SubscribeSplitterMoved(control,
            (sender, e) => OnSplitterMoved(sender, e, entity));
    }

    public void UnsubscribeSplitterMoved(object control)
    {
        // 通过 ConditionalWeakTable 管理
    }

    // ========== IPropertyApplyOutput ==========

    public void ApplyProperties(Entity entity, Dictionary<string, object?> properties)
    {
        var entry = _winFormsAdapter.FindByEntityId(entity);
        if (entry == null) return;
        _winFormsAdapter.ApplyPropertyChangesPublic(entry.Control, properties);
    }

    public void ApplyValueChange(object control, object? value)
    {
        _valueAccessor.SetValue(control, value);
    }

    // ========== IEventBindingOutput ==========

    public void BindEvent(object control, string eventName, string functionName, object bindingData)
    {
        _eventBindHandler?.Invoke(control, eventName, functionName, bindingData);
    }

    // ========== 原有 Apply 方法保持不变 ==========

    public void Apply(CanonicalForm targetForm) { /* 原有逻辑 */ }
}
```

### 3.3 System 改造：只依赖抽象接口

#### EcsCapabilityApplier

```csharp
public class EcsCapabilityApplier
{
    private readonly EntityManager _entityManager;
    private readonly IControlCreationOutput _creationOutput;  // ← 注入接口

    public EcsCapabilityApplier(
        EntityManager entityManager,
        IControlCreationOutput creationOutput)  // ← 不再依赖 IWinFormsControlAdapter
    {
        _entityManager = entityManager;
        _creationOutput = creationOutput;
    }

    public void Update()
    {
        var entities = _entityManager.GetEntities<PendingCapabilityComponent>();
        foreach (var entity in entities)
        {
            if (_entityManager.HasComponent<SplitterPanelComponent>(entity))
            {
                _entityManager.RemoveComponent<PendingCapabilityComponent>(entity);
                continue;
            }

            if (!_entityManager.TryGetComponent<ControlTypeComponent>(entity, out var typeComp))
            {
                _entityManager.RemoveComponent<PendingCapabilityComponent>(entity);
                continue;
            }

            // 委托给输出通道创建控件
            _creationOutput.CreateControl(entity, typeComp.Type);

            // 委托给输出通道建立父子关系
            if (_entityManager.TryGetComponent<ParentEntityComponent>(entity, out var parentComp))
            {
                _creationOutput.AddToParent(entity, parentComp.ParentEntity);
            }

            _entityManager.RemoveComponent<PendingCapabilityComponent>(entity);
        }
    }
}
```

#### SplitContainerSystem

```csharp
public class SplitContainerSystem
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly IEventSubscriptionOutput _subscriptionOutput;  // ← 注入接口

    public SplitContainerSystem(
        EntityManager entityManager,
        ControlPool controlPool,
        IEventSubscriptionOutput subscriptionOutput)  // ← 不再依赖 IWinFormsControlAdapter
    {
        _entityManager = entityManager;
        _controlPool = controlPool;
        _subscriptionOutput = subscriptionOutput;
    }

    public void Update()
    {
        // 只查询有 PendingSplitterSubscriptionComponent 的 Entity
        var entities = _entityManager.GetEntities<
            ControlTypeComponent,
            PendingSplitterSubscriptionComponent>();

        foreach (var entity in entities)
        {
            var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);
            if (!string.Equals(typeComp.Type, "splitcontainer", StringComparison.OrdinalIgnoreCase))
                continue;

            var control = _controlPool.GetControl(entity);
            if (control == null) continue;

            // 委托给输出通道订阅事件
            _subscriptionOutput.SubscribeSplitterMoved(control, entity);

            _entityManager.RemoveComponent<PendingSplitterSubscriptionComponent>(entity);
        }
    }
}
```

#### CsgEntryPoint

```csharp
public sealed class CsgEntryPoint
{
    private readonly EntityManager _entityManager;

    // 不再依赖 IWinFormsControlAdapter
    // 所有属性从 Component 读取

    private void CollectAllInOnePass(CanonicalFormBuilder builder)
    {
        var entities = _entityManager.GetEntities<ControlTypeComponent>();

        foreach (var entity in entities)
        {
            var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);

            var mergedProps = new Dictionary<string, object?>
            {
                [UIProtocol.Prop_ControlType] = typeComp.Type ?? "unknown",
                [UIProtocol.Meta_EntityId] = entity.Id.ToString()
            };

            // ── 控件基本信息（从 Component 读取，不再从控件读取）──
            if (_entityManager.TryGetComponent<ControlInfoComponent>(entity, out var info))
            {
                mergedProps[UIProtocol.Prop_Name] = info.Name;
                mergedProps[UIProtocol.Prop_Text] = info.Text ?? string.Empty;
                mergedProps[UIProtocol.Prop_Enabled] = info.Enabled;
                mergedProps[UIProtocol.Prop_Visible] = info.Visible;
            }

            // ── 布局信息（从 Component 读取）──
            if (_entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var layout))
            {
                mergedProps[UIProtocol.Prop_Left] = layout.Left;
                mergedProps[UIProtocol.Prop_Top] = layout.Top;
                mergedProps[UIProtocol.Prop_Width] = layout.Width;
                mergedProps[UIProtocol.Prop_Height] = layout.Height;
            }

            // ── 样式信息（从 Component 读取）──
            if (_entityManager.TryGetComponent<MergedStyleComponent>(entity, out var style))
            {
                // ... 样式属性
            }

            // ── SplitContainer 信息（从 Component 读取）──
            if (_entityManager.TryGetComponent<SplitterRatioComponent>(entity, out var ratio)
                && _entityManager.TryGetComponent<ComputedLayoutComponent>(entity, out var compLayout))
            {
                int dimension = Math.Max(compLayout.Width, compLayout.Height);
                mergedProps[UIProtocol.Prop_SplitterDistance] = dimension > 0
                    ? (int)(ratio.Ratio * dimension) : 0;
            }

            builder.AddNode(
                id: entity.Id.ToString(),
                label: info?.Name ?? entity.Id.ToString(),
                type: CanonicalNodeType.Control,
                properties: mergedProps);
        }
    }
}
```

### 3.4 新增 Component 类型

为了支持"ECS World 是唯一真实来源"，需要新增 Component 来承载之前从控件读取的状态：

```csharp
/// <summary>
/// 控件基本信息 Component
/// <para>替代从 WinForms 控件读取 Text/Enabled/Visible 的方式。</para>
/// <para>由 WinFormsOutputPort 在应用属性时同步更新。</para>
/// </summary>
public struct ControlInfoComponent : IComponent
{
    public string Name { get; set; }
    public string Text { get; set; }
    public bool Enabled { get; set; }
    public bool Visible { get; set; }
}

/// <summary>
/// 待处理 Splitter 订阅标记
/// <para>由 EcsCapabilityApplier 在控件创建完成后添加，</para>
/// <para>由 SplitContainerSystem 消费后移除。</para>
/// </summary>
public struct PendingSplitterSubscriptionComponent : IComponent
{
}
```

### 3.5 EcsRenderPipeline 初始化改造

```csharp
public void Initialize()
{
    // ... 原有 ECS World、ControlPool 等初始化 ...

    // 创建 WinFormsOutputPort（实现所有输出通道接口）
    _winFormsOutputPort = new WinFormsOutputPort(
        _winFormsAdapter, _uiReconciler, _csg, valueAccessor,
        _entityManager, _controlPool, _adapter, _eventBindHandler);

    // System 只依赖抽象接口，不依赖 IWinFormsControlAdapter
    _capabilityApplier = new EcsCapabilityApplier(
        _entityManager, _winFormsOutputPort);  // ← 注入接口
    _layoutSystem = new LayoutSystem(
        _entityManager, _controlPool, _adapter);  // ← LayoutSystem 仍需 adapter 读取父容器尺寸
    _splitContainerSystem = new SplitContainerSystem(
        _entityManager, _controlPool, _winFormsOutputPort);  // ← 注入接口
    _styleSystem = new StyleSystem(
        _entityManager, _controlPool, _adapter);  // ← StyleSystem 仍需 adapter（待进一步收拢）
    _eventBindingSystem = new EventBindingSystem(
        _entityManager, _controlPool, _winFormsOutputPort);  // ← 注入接口

    // CsgEntryPoint 不再需要 IWinFormsControlAdapter
    _csgEntryPoint = new CsgEntryPoint(_entityManager);
}
```

### 3.6 管线执行流程改造

```csharp
public void ExecutePipeline()
{
    // ── ECS System 执行（全部只写 Component）──
    _capabilityApplier?.Update();       // 写 PendingCreationComponent → 输出通道创建控件
    _layoutSystem?.Update();            // 纯 ECS 计算
    _splitContainerSystem?.Update();    // 写 PendingSplitterSubscriptionComponent → 输出通道订阅事件
    _styleSystem?.Update();             // 纯 ECS 计算
    _eventBindingSystem?.Update();      // 写 PendingEventBindingComponent → 输出通道绑定事件

    // ── CSG 收集 + 输出 ──
    if (shouldExecuteCsg)
    {
        var form = _csgEntryPoint.Collect();  // 只读 Component
        _winFormsOutputPort.Apply(form);       // 唯一操作 WinForms 的出口
    }
}
```

---

## 四、改造清单

### 4.1 新增文件

| 文件 | 内容 |
|------|------|
| `Ports/IOutputPort.cs` | 定义 `IControlCreationOutput`、`IEventSubscriptionOutput`、`IPropertyApplyOutput`、`IEventBindingOutput` 接口 |
| `ECS/Components/ControlInfoComponent.cs` | 新增 `ControlInfoComponent`（承载 Text/Enabled/Visible） |
| `ECS/Components/PendingSplitterSubscriptionComponent.cs` | 新增 `PendingSplitterSubscriptionComponent`（Splitter 订阅标记） |

### 4.2 修改文件

| 文件 | 修改内容 |
|------|----------|
| `Ports.cs` — `WinFormsOutputPort` | 实现 `IControlCreationOutput`、`IEventSubscriptionOutput`、`IPropertyApplyOutput`、`IEventBindingOutput` |
| `Ports.cs` — `CsgEntryPoint` | 移除 `IWinFormsControlAdapter` 依赖，Text/Enabled/Visible 从 `ControlInfoComponent` 读取 |
| `ControlRender/Creation/Systems/ControlCreateSystem.cs` — `EcsCapabilityApplier` | 依赖从 `IWinFormsControlAdapter` 改为 `IControlCreationOutput` |
| `ControlRender/Systems/SplitContainerSystem.cs` | 依赖从 `IWinFormsControlAdapter` 改为 `IEventSubscriptionOutput` |
| `ControlInteraction/EventBinding/Systems/EventBindingSystem.cs` | 依赖从 `EventBindingDelegate` 改为 `IEventBindingOutput` |
| `ControlRender/Style/Systems/StyleSystem.cs` | 删除残留的 `ApplyStyle()` 方法 |
| `RenderPipeline/EcsRenderPipeline.cs` | 初始化时注入接口，System 不再直接传 `_adapter` |

### 4.3 无需修改的文件

| 文件 | 原因 |
|------|------|
| `ControlRender/Systems/LayoutSystem.cs` | 已实现"只写 Component"，但 `FindScreenRoot` 仍需 `IWinFormsControlAdapter` 读取父控件尺寸 — **这是合理的**，因为父容器尺寸是 WinForms 运行时属性，无法从 Component 获取 |
| `WinForms/Adapter/WinFormsUIAdapter.cs` | 作为底层适配器保持不变，WinFormsOutputPort 通过它操作控件 |
| `RenderPipeline/UIReconciler.cs` | 差异比较逻辑不变 |

---

## 五、边界情况与注意事项

### 5.1 LayoutSystem 仍需 IWinFormsControlAdapter

`LayoutSystem.FindScreenRoot()` 需要沿 WinForms 控件树向上查找父控件的 `ClientSize`。这是合理的，因为：
- 父容器尺寸是 WinForms 运行时的物理属性
- 无法从 ECS Component 获取（父容器可能由外部代码创建）
- 这是"读"操作而非"写"操作，不违反收拢原则

**改进方案**：将 `FindScreenRoot` 的逻辑也委托给输出通道，定义 `ILayoutQueryOutput` 接口：

```csharp
public interface ILayoutQueryOutput
{
    /// <summary>获取父容器的 ClientSize</summary>
    Size GetParentClientSize(Entity entity);

    /// <summary>获取 ScreenRoot 尺寸</summary>
    ScreenRootComponent? GetScreenRoot(Entity entity);
}
```

### 5.2 ControlInfoComponent 的同步问题

新增 `ControlInfoComponent` 后，需要确保：
1. **创建时初始化**：`EcsCapabilityApplier` 创建控件后，WinFormsOutputPort 将默认值写回 Component
2. **用户交互后更新**：用户修改 Text/Enabled/Visible 后，通过事件处理器更新 Component
3. **管线执行时同步**：WinFormsOutputPort 在应用属性后，将最新值写回 Component

### 5.3 向后兼容

- 所有接口都有默认实现（通过 WinFormsOutputPort）
- 现有外部调用代码无需修改
- 管线执行流程不变（仍然是 7 个阶段）

---

## 六、收益评估

| 维度 | 改造前 | 改造后 |
|------|--------|--------|
| **System 的 WinForms 依赖** | 5/5 个 System 依赖 | 2/5 个 System 依赖（LayoutSystem 仍需读父容器尺寸） |
| **WinForms 操作出口** | 分散在 4 个位置 | 统一在 WinFormsOutputPort |
| **ECS World 真实性** | 部分属性从控件读取 | 全部从 Component 读取 |
| **System 可测试性** | 需要 Mock WinForms | 只需 Mock 轻量接口 |
| **控件平台切换成本** | 高（每个 System 都要改） | 低（只需替换 WinFormsOutputPort 实现） |

---

## 七、实施路线

### 第一阶段：接口定义 + WinFormsOutputPort 扩展（无功能变更）

1. 定义 `IOutputPort.cs` 中的四个接口
2. WinFormsOutputPort 实现这些接口
3. 新增 `ControlInfoComponent` 和 `PendingSplitterSubscriptionComponent`

### 第二阶段：System 改造（逐个替换）

1. `EcsCapabilityApplier`：依赖从 `IWinFormsControlAdapter` 改为 `IControlCreationOutput`
2. `SplitContainerSystem`：依赖从 `IWinFormsControlAdapter` 改为 `IEventSubscriptionOutput`
3. `EventBindingSystem`：依赖从 `EventBindingDelegate` 改为 `IEventBindingOutput`
4. `StyleSystem`：删除残留的 `ApplyStyle()` 方法

### 第三阶段：CsgEntryPoint 改造

1. 移除 `IWinFormsControlAdapter` 依赖
2. Text/Enabled/Visible 改为从 `ControlInfoComponent` 读取
3. 确保 WinFormsOutputPort 在应用属性后同步更新 `ControlInfoComponent`

### 第四阶段：EcsRenderPipeline 初始化调整

1. System 构造函数不再传递 `_adapter`
2. WinFormsOutputPort 作为接口实现注入
3. 验证管线执行正确性
