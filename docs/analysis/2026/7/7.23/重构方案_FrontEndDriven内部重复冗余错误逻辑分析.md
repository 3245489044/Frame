# 重构方案：FrontEndDriven 内部重复/冗余/错误逻辑分析

> **版本**：v1.1  
> **日期**：2026-07-22  
> **范围**：`CsharpPrj/Operation/FrontEndDriven/`（模块内部）  
> **状态**：部分实施（DUP-2/DUP-3/DUP-4 已完成）

---

## 目录

1. [背景与目标](#1-背景与目标)
2. [问题总览](#2-问题总览)
3. [详细分析](#3-详细分析)
   - [3.1 重复逻辑](#31-重复逻辑)
   - [3.2 冗余逻辑](#32-冗余逻辑)
   - [3.3 错误逻辑](#33-错误逻辑)
4. [重构方案](#4-重构方案)
5. [实施路线图](#5-实施路线图)
6. [风险与注意事项](#6-风险与注意事项)

---

## 1. 背景与目标

### 1.1 背景

`FrontEndDriven`（位于 `CsharpPrj/Operation/FrontEndDriven/`）是从 `FileDriven.UI` 迁移和重构后的新一代前端驱动模块，采用 ECS 架构。在快速迭代过程中，模块内部积累了一些代码质量问题：

- **重复逻辑**：接口职责重叠、适配器功能重叠、方法模式重复
- **冗余逻辑**：废弃接口未清理、死代码方法、过度设计
- **错误逻辑**：值同步逻辑错误、ControlPool 一对多引用、异常处理不当

### 1.2 目标

1. **消除重复**：合并职责重叠的接口和适配器，消除重复的方法模式
2. **消除冗余**：清理废弃接口和死代码，简化过度设计
3. **修复错误**：修复值同步逻辑错误、ControlPool 引用错误、异常处理问题
4. **统一适配层**：明确 IWinFormsControlAdapter 与 IControlValueAccessor 的职责边界

---

## 2. 问题总览

| 编号 | 类别 | 严重程度 | 文件 | 摘要 |
|------|------|----------|------|------|
| DUP-1 | 重复 | 中 | `IControlStrategy.cs` / `IControlCreationStrategy.cs` | 两个创建接口职责重叠 |
| DUP-2 | 重复 | 中 | `ControlCreationServiceAdapter.cs` | 纯委托包装，无额外逻辑 |
| DUP-3 | 重复 | 中 | `WinFormsControlValueWriter.cs` | `ReadValue/SupportsType` 与 `GetValue/IsSupported` 完全重复 |
| DUP-4 | 重复 | 低 | `ControlValueSyncSystem.cs` | `SyncProperty` 与 `SyncValue` 模式重复 |
| DUP-5 | 重复 | 中 | `WinFormsControlAdapter.cs` / `WinFormsControlValueWriter.cs` | 两个适配器功能重叠 |
| DUP-6 | 重复 | 中 | `ConfigDrivenControlFactory.cs` | `ConvertView` 与 `ConvertControl` 模式重复 |
| DUP-7 | 重复 | 低 | `LayoutSystem.cs` / `StyleSystem.cs` | `Update` 结构重复 |
| DUP-8 | 重复 | 低 | `WinFormsControlValueWriter.cs` | 三个 UI 线程调度包装方法结构重复 |
| R1 | 冗余 | 低 | `Systems/IControlCreationStrategy.cs` | 废弃接口未删除 |
| R2 | 冗余 | 低 | `ControlSyncSystem.cs` | 注释掉的空分支代码 |
| R3 | 冗余 | 中 | `EcsConfigContainer.cs` | `LoadConfigAsync(string)` 直接抛异常的死代码 |
| R4 | 冗余 | 低 | `ConfigSelectorController.cs` | 注释说"已迁移"但代码保留 |
| R5 | 冗余 | 低 | `ControlStrategyRegistry.cs` | 两个字典可合并 |
| R6 | 冗余 | 低 | `WinFormsControlValueWriter.cs` | `WriteValue` 的冗余回退逻辑 |
| R7 | 冗余 | 低 | `SplitContainerSystem.cs` | `_initialized` 与 `_subscribedSplitters` 双重跟踪 |
| R8 | 冗余 | 低 | `EcsConfigContainer.cs` | `WduiEventManager` 纯委托属性 |
| **B1** | **错误** | **严重** | `ControlValueSyncSystem.cs` | **SyncEnabledVisible 通过 GetValue 获取 Enabled/Visible 完全错误** |
| **B2** | **错误** | **严重** | `ConfigDrivenControlFactory.cs` | **ControlPool 中一个 Control 被多个 Entity 引用** |
| B3 | 错误 | 低 | `SplitContainerSystem.cs` | `Dictionary<object, bool>` 引用相等性问题 |
| B4 | 错误 | 中 | `EcsConfigContainer.cs` | `Task.Run` + `GetAwaiter().GetResult()` 潜在死锁 |
| B5 | 错误 | 低 | `StyleSystem.cs` | 颜色解析失败违反零容错原则 |
| B6 | 错误 | 中 | `EcsConfigContainer.cs` / `ControlStrategyRegistry.cs` | 无参构造 Obsolete 与回退路径矛盾 |
| **B7** | **错误** | **严重** | `EventBindingSystem.cs` | **ContinueWith 参数可能不正确** |
| B8 | 错误 | 低 | `ConfigDrivenControlFactory.cs` | SplitterPanel Entity 验证缺失 |
| B9 | 错误 | 中 | `EcsConfigContainer.cs` | `catch when (!strictMode)` 捕获所有异常 |
| B10 | 错误 | 低 | `CompositeValueWriter.cs` | `RegisterAdapter` 广播语义可能导致意外行为 |

---

## 3. 详细分析

### 3.1 重复逻辑

#### DUP-1. IControlStrategy 与 IControlCreationStrategy 接口职责重叠

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/IControlStrategy.cs`
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/Systems/IControlCreationStrategy.cs`

**现状**：
两个接口都定义了"根据控件类型创建控件实例"的方法：

```csharp
// IControlStrategy — 单控件类型工厂
public interface IControlStrategy
{
    object Create();
}

// IControlCreationStrategy — 聚合分发策略
public interface IControlCreationStrategy
{
    object? CreateControl(string controlType);
}
```

`IControlCreationStrategy` 的注释说"从委托提升为正式策略接口"，但 `IControlStrategy` 的注释说"退化为纯创建工厂接口"。实际上 `ConfigDrivenControlFactory` 已完全替代了这两个接口的功能，它们都处于"半废弃"状态。

**影响**：
- 两个接口的存在增加了理解成本
- 新开发者需要判断应该实现哪个接口
- 维护时需要同步修改两个接口

**方案**：删除 `IControlCreationStrategy`，将 `IControlStrategy` 标记为 `[Obsolete]` 并逐步迁移到 `ControlStrategyRegistry` 的直接调用。

---

#### DUP-2. ControlCreationServiceAdapter 是纯委托包装

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ControlCreationServiceAdapter.cs`
- `CsharpPrj/Operation/FrontEndDriven/Shared/Abstractions/IControlCreationService.cs`

**现状**：
`ControlCreationServiceAdapter` 的 4 个方法全部是 `ControlStrategyRegistry` 的纯委托转发，无任何额外逻辑：

```csharp
public object? Create(string controlType)
    => ControlStrategyRegistry.Create(controlType);
public void Register(string typeName, Func<object> factory)
    => ControlStrategyRegistry.Register(typeName, factory);
public bool Unregister(string typeName)
    => ControlStrategyRegistry.Unregister(typeName);
public string[] GetRegisteredTypes()
    => ControlStrategyRegistry.GetRegisteredTypes();
```

**影响**：
- 增加一层间接调用
- 调用方多一次方法调用开销
- 增加代码体积

**方案**：如果 `IControlCreationService` 接口的唯一实现就是此适配器，且调用方可以直接依赖 `ControlStrategyRegistry`，则应删除此适配器和接口，减少间接层。

---

#### DUP-3. WinFormsControlValueWriter 中 ReadValue/SupportsType 与 GetValue/IsSupported 重复

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/WinForms/ValueSync/WinFormsControlValueWriter.cs`

**现状**：
两组方法实现完全相同的逻辑：

| 方法组 A | 方法组 B | 差异 |
|----------|----------|------|
| `GetValue(control)` → `FindAdapter` → `Reader` | `ReadValue(control)` → `FindAdapter` → `Reader` | 完全相同 |
| `IsSupported(control)` → `FindAdapter != null` | `SupportsType(control)` → `FindAdapter != null` | 完全相同 |

**影响**：
- 任何修改需要同步两组方法
- 调用方可能混淆应该使用哪组方法
- 增加代码体积

**方案**：删除 `ReadValue` 和 `SupportsType`，统一使用 `GetValue` 和 `IsSupported`。

---

#### DUP-4. ControlValueSyncSystem 中 SyncProperty 与 SyncValue 模式重复

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/ValueSync/Systems/ControlValueSyncSystem.cs`

**现状**：
`SyncProperty`（第 135-154 行）和 `SyncValue`（第 83-109 行）都实现了相同的模式：

```
读取 Component → 比较 → 更新时间戳 → 写回 Component → 写入控件
```

`SyncProperty` 是 `SyncValue` 的特化版本（处理 bool 属性而非 string 值），但两者可以合并。

**影响**：
- 两个方法维护两套相似的逻辑
- 修复一个时容易忘记同步另一个

**方案**：将 `SyncProperty` 的逻辑合并到 `SyncValue` 中，通过参数化消除重复。

---

#### DUP-5. WinFormsControlAdapter 与 WinFormsControlValueWriter 功能重叠

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsControlAdapter.cs`
- `CsharpPrj/Operation/FrontEndDriven/WinForms/ValueSync/WinFormsControlValueWriter.cs`

**现状**：
两个类都直接操作 WinForms 控件属性：

| 操作 | WinFormsControlAdapter | WinFormsControlValueWriter |
|------|----------------------|---------------------------|
| Text 读写 | `GetText`/`SetText` | `RegisterTextControlAdapter` |
| Enabled 读写 | `GetEnabled`/`SetEnabled` | `WriteProperty`/`ReadProperty`（反射） |
| Visible 读写 | `GetVisible`/`SetVisible` | `WriteProperty`/`ReadProperty`（反射） |

`WinFormsControlAdapter` 通过 `IWinFormsControlAdapter` 接口提供属性操作，`WinFormsControlValueWriter` 通过 `IControlValueAccessor` 接口也提供属性操作。两者职责边界模糊。

**影响**：
- 新开发者难以判断应该使用哪个适配器
- 属性操作有两套实现，行为可能不一致
- 维护成本翻倍

**方案**：明确职责划分：
- `WinFormsControlAdapter` → 控件树操作和布局/样式（AddControl、SetBounds、SetFont 等）
- `WinFormsControlValueWriter` → 控件值读写（Text、Checked、SelectedItem 等）
- 将 Enabled/Visible 属性操作统一到 `WinFormsControlAdapter` 中

---

#### DUP-6. ConfigDrivenControlFactory 中 ConvertView 与 ConvertControl 模式重复

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs`

**现状**：
`ConvertView`（第 135-166 行）和 `ConvertControl`（第 171-217 行）都遵循完全相同的模式：

1. 创建 Entity（能力卡片）
2. 创建 WinForms 控件实例
3. 注册到 ControlPool
4. 添加到父控件
5. 递归处理子控件

`ConvertView` 可以委托给 `ConvertControl`，只需额外处理 View 的默认类型（"panel"）。

**影响**：
- 两个方法维护两套相似的逻辑
- 修改控件创建流程时需要同步修改两处

**方案**：消除 `ConvertView`，将 View 视为 Type="view" 的特殊 Control，统一由 `ConvertControl` 处理。

---

#### DUP-7. LayoutSystem 与 StyleSystem 的 Update 结构重复

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/LayoutSystem.cs`
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Style/Systems/StyleSystem.cs`

**现状**：
两个 System 的 `Update()` 方法结构几乎完全相同：

```
GetEntities → foreach → 检查 state.IsXxxApplied → 处理 → 添加/更新 Component → 同步到控件 → 更新 state
```

**影响**：
- 新增类似 System 时需要复制相同的骨架代码
- 修改遍历逻辑时需要同步修改多处

**方案**：提取模板方法模式，将"遍历→检查状态→处理→同步→更新状态"的骨架提取到基类或辅助方法中。

---

#### DUP-8. WinFormsControlValueWriter 中三个 UI 线程调度包装方法结构重复

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/WinForms/ValueSync/WinFormsControlValueWriter.cs`

**现状**：
`WrapWithUiThreadDispatch`、`WrapActionWithUiThreadDispatch`、`WrapFuncWithUiThreadDispatch` 三个方法的结构几乎相同，都是"检查类型 → 检查是否为 Control → UI 线程调度 → 执行"。

**影响**：
- 新增类似包装方法时需要复制相同的调度逻辑
- 修改调度策略时需要同步修改三处

**方案**：合并为一个统一的 `WrapWithUiThreadDispatch<TControl, TArgs, TResult>` 方法。

---

### 3.2 冗余逻辑

#### R1. IControlCreationStrategy 接口已废弃但保留

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/Systems/IControlCreationStrategy.cs`

**现状**：
该接口的注释说"从委托提升为正式策略接口"，但 `ConfigDrivenControlFactory` 已完全替代其功能。接口本身没有任何已知的实现类（或仅用于测试），属于"死接口"。

**方案**：删除该接口文件。

---

#### R2. ControlSyncSystem 中 ControlStateComponent 分支被移除

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/ValueSync/Systems/ControlSyncSystem.cs`

**现状**：
注释说"消除 R2：移除 ControlStateComponent 的空同步分支。SyncState 方法为空（仅注释），无实际同步逻辑。"但分支代码仍然保留为注释/空块。

**方案**：彻底清理注释掉的代码。

---

#### R3. EcsConfigContainer.LoadConfigAsync(string) 直接抛异常

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs`

**现状**：
该方法直接抛出 `InvalidOperationException`，属于"死代码"。调用方如果误调用会直接崩溃。

```csharp
public async Task LoadConfigAsync(string configPath)
{
    throw new InvalidOperationException("请使用 LoadConfigAsync(UIConfiguration) 重载。");
}
```

**方案**：标记为 `[Obsolete]` 并添加更明确的错误信息，或直接删除。

---

#### R4. ConfigSelectorController 中注释提到"已迁移"但代码保留

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/WinForms/ConfigSelectorController.cs`

**现状**：
注释说"文件扫描逻辑已迁移至 FileDriven.UI.Json.Loading.ConfigSelectorScanner"和"状态文本生成已迁移至...ConfigSelectorScanner"，但 `RefreshConfigList` 和 `UpdateConfigStatus` 方法仍然保留完整实现。

**方案**：如果确实已迁移，应删除这些方法并委托给新的 Scanner 类。

---

#### R5. ControlStrategyRegistry 同时维护两个字典

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ControlStrategyRegistry.cs`

**现状**：
`_strategyMap`（`IControlStrategy`）和 `_factoryMap`（`Func<object>`）两个字典。但 `IControlStrategy` 接口只有一个 `Create()` 方法，本质上等同于 `Func<object>`。两个字典可以合并。

**方案**：将 `IControlStrategy` 的注册也包装为 `Func<object>`，统一使用一个字典。

---

#### R6. WinFormsControlValueWriter 中 WriteValue 的冗余回退逻辑

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/WinForms/ValueSync/WinFormsControlValueWriter.cs`

**现状**：
注释说"★ 2026-07-22 修复 B1：非 string 类型通过 ToString() 写入"，但 `WriteValueInternal` 内部已经处理了 string 类型。`WriteValue` 中的 `value is string str` 分支和 `value.ToString()` 分支最终都调用 `WriteValueInternal`，没有本质区别。

**方案**：简化 `WriteValue`，统一通过 `value?.ToString()` 传入 `WriteValueInternal`。

---

#### R7. SplitContainerSystem 的 _initialized 标志与 _subscribedSplitters 字典双重跟踪

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/SplitContainerSystem.cs`

**现状**：
`_initialized` 标志保证 `Update()` 只执行一次，而 `_subscribedSplitters` 字典也跟踪已订阅的 SplitContainer。两者功能重叠 — `_initialized` 已经确保不会重复执行，`_subscribedSplitters` 的防重复功能是多余的。

**方案**：移除 `_subscribedSplitters` 字典，仅使用 `_initialized` 标志。

---

#### R8. EcsConfigContainer 的 WduiEventManager 属性

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs`

**现状**：
`WduiEventManager` 属性只是返回基类的 `EventManager`，是一个纯委托属性。注释说"移除 WdUI 依赖"，但属性名仍然保留 "Wdui" 前缀。

**方案**：删除此属性，调用方直接使用基类的 `EventManager`。

---

### 3.3 错误逻辑

#### B1（严重）：SyncEnabledVisible 通过 _valueWriter.GetValue 获取 Enabled/Visible 完全错误

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/ValueSync/Systems/ControlValueSyncSystem.cs` — 第 168-186 行

**现状**：
```csharp
SyncProperty(ref typeComp, control, entity, _entityManager,
    getCompValue: (c) => c.Enabled,
    getControlValue: (ctrl) =>
    {
        var val = _valueWriter.GetValue(ctrl);  // ← 错误！
        return val?.ToString() == bool.TrueString;
    },
    // ...
```

**问题**：
`_valueWriter.GetValue(control)` 返回的是控件的**主值**（如 TextBox 的 Text、CheckBox 的 Checked 状态），而非 `Enabled` 或 `Visible` 属性。例如：
- 对一个 `TextBox` 调用 `GetValue` 返回的是 `TextBox.Text`
- 对一个 `Button` 调用 `GetValue` 返回的是 `Button.Text`

这意味着 `Enabled` 同步会错误地比较控件的 Text 值是否为 `"True"`，导致：
1. 如果 Text 为 `"True"`，即使控件实际是 `Enabled=false`，也会认为"已同步"
2. 如果 Text 不为 `"True"`，每次 Update 都会尝试写入（即使 Enabled 状态没有变化）

**修复方案**：
应使用 `_adapter.GetEnabled(control)` 和 `_adapter.GetVisible(control)`（通过 `IWinFormsControlAdapter`），而非 `_valueWriter.GetValue(control)`。

```csharp
// 修复后
SyncProperty(ref typeComp, control, entity, _entityManager,
    getCompValue: (c) => c.Enabled,
    getControlValue: (ctrl) => _adapter.GetEnabled(ctrl),
    setTimestamp: (c, t) => c.EnabledLastUpdated = t,
    setControlValue: (ctrl, v) => _adapter.SetEnabled(ctrl, v));
```

---

#### B2（严重）：ControlPool 中一个 Control 被多个 Entity 引用

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs` — 第 225-251 行

**现状**：
`CreateSplitterPanelEntity` 将父 `SplitContainer` 的 Control 引用注册为 `splitterPanel` Entity 的 Control：

```csharp
private Entity CreateSplitterPanelEntity(Entity parentEntity, string name, string panelSide)
{
    // ...
    var parentControl = _controlPool.GetControl(parentEntity);
    if (parentControl != null && !_adapter.IsDisposed(parentControl))
    {
        _controlPool.Register(panelEntity, name, parentControl);  // ← 将 SplitContainer 注册为 splitterPanel 的 Control
    }
    return panelEntity;
}
```

**问题**：
1. `ControlPool` 中一个 Control（SplitContainer）被两个 Entity（SplitContainer Entity + SplitterPanel Entity）引用
2. 当通过 `ControlPool.GetEntity(controlName)` 反向查找时，只能找到最后注册的 Entity（splitterPanel），找不到原始的 SplitContainer Entity
3. `AddToParentControl` 中通过 `_controlPool.GetControl(parentEntity)` 获取的是 SplitContainer 而非 splitterPanel 的独立控件

**修复方案**：
SplitterPanel 不应注册到 ControlPool，而应在 `AddToParentControl` 中通过父 Entity 查找 SplitContainer，再根据 `SplitterPanelComponent.PanelSide` 获取 Panel1/Panel2。

```csharp
private Entity CreateSplitterPanelEntity(Entity parentEntity, string name, string panelSide)
{
    var panelEntity = _controlFactory.CreateFromConfig(new ControlConfig
    {
        Type = "splitterpanel",
        Name = name,
        Text = name
    });

    _entityManager.AddComponent(panelEntity, new SplitterPanelComponent
    {
        PanelSide = panelSide
    });

    // ★ 修复：不再注册到 ControlPool
    // SplitterPanel 没有对应的独立 WinForms Control，
    // AddToParentControl 中通过父 Entity 查找 SplitContainer，
    // 再根据 SplitterPanelComponent.PanelSide 获取 Panel1/Panel2。
    return panelEntity;
}
```

---

#### B3. SplitContainerSystem 的 Dictionary<object, bool> 引用相等性问题

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Layout/Systems/SplitContainerSystem.cs` — 第 40 行、第 123 行

**现状**：
```csharp
private readonly Dictionary<object, bool> _subscribedSplitters = new();
// ...
if (_subscribedSplitters.ContainsKey(splitter))
    return;
```

**问题**：
`Dictionary<object, bool>` 默认使用引用相等性（ReferenceEquals）。如果 SplitContainer 被释放后重新创建（新对象），旧条目仍然在字典中，新的 SplitContainer 对象不会被订阅事件。

此外，`_initialized` 标志已经确保 `Update()` 只执行一次，`_subscribedSplitters` 的防重复功能是多余的（见 R7）。

**修复方案**：
移除 `_subscribedSplitters` 字典，仅使用 `_initialized` 标志。

---

#### B4. EcsConfigContainer.LoadFromFile 的 Task.Run + GetAwaiter().GetResult() 潜在死锁

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs` — 第 244 行

**现状**：
```csharp
var config = Task.Run(() => LoadFromFileAsync(configPath, strictMode)).GetAwaiter().GetResult();
```

**问题**：
虽然在非 UI 线程调用时风险较低，但如果在 UI 线程调用此方法，`Task.Run` 将任务调度到线程池，`GetAwaiter().GetResult()` 阻塞当前线程。如果 `LoadFromFileAsync` 内部有回到 UI 线程的操作（如 `UiThreadHelper.RunOnUiThread`），就会发生死锁。

**修复方案**：
将同步版本标记为 `[Obsolete]`，强制调用方使用异步版本 `LoadFromFileAsync`。

---

#### B5. StyleSystem 颜色解析失败时违反零容错原则

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Style/Systems/StyleSystem.cs` — 第 101-110 行
- `CsharpPrj/Operation/FrontEndDriven/WinForms/Adapter/WinFormsControlAdapter.cs` — 第 112 行

**现状**：
`WinFormsControlAdapter.SetForeColor` 内部颜色解析失败时只记录 `Debug.WriteLine`，没有抛出异常或记录结构化日志。

**问题**：
违反 `.clinerules/05-zero-tolerance.md` 的"不允许静默吞没异常"原则。

**修复方案**：
使用 `FrontEndDrivenLog.Log` 记录结构化日志，或根据严重程度决定是否抛出异常。

---

#### B6. EcsConfigContainer 无参构造的 Obsolete 警告与 ControlStrategyRegistry 的使用矛盾

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs` — 第 151-157 行
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ControlStrategyRegistry.cs` — 第 45-52 行

**现状**：
`EcsConfigContainer` 的无参构造标记为 `[Obsolete]` 并警告"不可用"，但 `ControlStrategyRegistry` 的静态构造函数中注册的 `"container"` 工厂在 `IECSStorage`/`IStorageRegistry` 解析失败时会回退到 `new EcsConfigContainer()`（无参构造）：

```csharp
Register("container", () =>
{
    var ecsStorage = AppServiceProvider.Resolve<IECSStorage>();
    var storageRegistry = AppServiceProvider.Resolve<IStorageRegistry>();
    if (ecsStorage != null && storageRegistry != null)
        return new EcsConfigContainer(ecsStorage, storageRegistry);
    return new EcsConfigContainer();  // ← 使用 Obsolete 构造
});
```

**问题**：
在依赖注入不可用的情况下，会创建一个"半初始化"的容器实例，后续调用 `RenderConfigAsync` 会抛出异常。

**修复方案**：
在回退路径中也应抛出异常，而非创建不可用的实例。

---

#### B7（严重）：EventBindingSystem 的 ContinueWith 未正确指定 TaskScheduler

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/Systems/EventBindingSystem.cs` — 第 100-108 行

**现状**：
```csharp
_bindingDelegate(control, binding.EventName, binding.FunctionName, binding)
    .ContinueWith(t =>
    {
        if (t.IsFaulted)
        {
            FrontEndDrivenLog.Log("EventBindingSystem_BindEvent_failed",
                new { eventName = binding.EventName, error = t.Exception?.Message });
        }
    }, TaskScheduler.Default);
```

**问题**：
注释说"★ 2026-07-22 修复 B5：显式指定 TaskScheduler.Default"，但 `ContinueWith` 的第二个参数是 `CancellationToken` 而非 `TaskScheduler`。`TaskScheduler.Default` 被隐式转换为 `CancellationToken` — 这实际上不会按预期工作。

`ContinueWith` 的签名重载：
- `ContinueWith(Action<Task>, TaskScheduler)` — 正确用法
- `ContinueWith(Action<Task>, CancellationToken)` — 当前被错误匹配的重载

`TaskScheduler.Default` 被当作 `CancellationToken` 使用，延续任务仍然在原始任务的 TaskScheduler 上执行，而非线程池。

**修复方案**：
使用 `await` 替代 `ContinueWith`，或显式指定 `TaskScheduler.Default` 作为正确的参数位置：

```csharp
// 方案 A：使用 await
try
{
    await _bindingDelegate(control, binding.EventName, binding.FunctionName, binding);
}
catch (Exception ex)
{
    FrontEndDrivenLog.Log("EventBindingSystem_BindEvent_failed",
        new { eventName = binding.EventName, error = ex.Message });
}

// 方案 B：正确指定 TaskScheduler
_bindingDelegate(control, binding.EventName, binding.FunctionName, binding)
    .ContinueWith(t =>
    {
        if (t.IsFaulted)
        {
            FrontEndDrivenLog.Log("EventBindingSystem_BindEvent_failed",
                new { eventName = binding.EventName, error = t.Exception?.Message });
        }
    }, default, TaskContinuationOptions.None, TaskScheduler.Default);
```

---

#### B8. ConfigDrivenControlFactory 中 SplitterPanel 的 Entity 验证缺失

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/ConfigDrivenControlFactory.cs` — 第 119-120 行

**现状**：
```csharp
if (!rootEntity.IsValid)
    throw new ArgumentException("rootEntity 无效，请确保已通过 CreateRootEntity 创建。", nameof(rootEntity));
```

**问题**：
`ConvertChildren` 只验证了 `rootEntity` 的有效性，但没有验证 `rootEntity` 是否已注册到 `ControlPool`。如果调用方忘记调用 `_controlPool.Register(rootEntity, ...)`，后续 `AddToParentControl` 中通过 `_controlPool.GetControl(parentEntity)` 会返回 null，抛出异常。

**修复方案**：
在 `ConvertChildren` 入口处添加 `_controlPool.HasControl(rootEntity)` 检查，提供更明确的错误信息。

---

#### B9. LoadFromFile 的 catch when (!strictMode) 捕获所有异常

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Container/EcsConfigContainer.cs` — 第 247-250 行、第 281-284 行

**现状**：
```csharp
catch when (!strictMode)
{
    return false;
}
// 和
catch (Exception) when (!strictMode)
{
    return null;
}
```

**问题**：
`catch when (!strictMode)` 会捕获所有异常类型，包括 `ThreadAbortException`、`OutOfMemoryException`、`StackOverflowException` 等不应被捕获的异常。违反零容错原则。

**修复方案**：
只捕获预期的异常类型（如 `JsonException`、`IOException`、`InvalidOperationException`）。

---

#### B10. CompositeValueWriter.RegisterAdapter 的广播语义可能导致意外行为

**涉及文件**：
- `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/ValueSync/Implementation/CompositeValueWriter.cs` — 第 117-124 行

**现状**：
```csharp
public void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter)
{
    foreach (var w in _writers)
    {
        w.RegisterAdapter(controlType, getter, setter);
    }
}
```

**问题**：
`RegisterAdapter` 将适配器注册到所有子 writer。如果某个子 writer 已经注册了相同控件类型的适配器，会被覆盖。而且 `ClearAdapters` 也会清空所有子 writer 的适配器，可能导致其他依赖这些适配器的组件出现问题。

**修复方案**：
`RegisterAdapter` 和 `ClearAdapters` 应只操作第一个匹配的 writer，或提供更精确的控制。

---

## 4. 重构方案

### 4.1 方案优先级

| 优先级 | 编号 | 方案 | 工作量 | 风险 |
|--------|------|------|--------|------|
| **P0** | **B1** | **修复 SyncEnabledVisible 值同步逻辑** | **1h** | **高 — 影响 Enabled/Visible 同步** |
| **P0** | **B7** | **修复 ContinueWith 参数** | **0.5h** | **高 — 影响事件绑定异常处理** |
| **P0** | **B2** | **修复 ControlPool 一对多引用** | **1h** | **高 — 影响 SplitContainer 控件树** |
| P1 | DUP-3/R6 | 删除 ReadValue/SupportsType 重复方法 | 0.5h | 低 |
| P1 | R1 | 删除 IControlCreationStrategy 废弃接口 | 0.5h | 低 |
| P1 | R3 | 删除 LoadConfigAsync(string) 死代码 | 0.5h | 低 |
| P1 | R7 | 移除 _subscribedSplitters 冗余字典 | 0.5h | 低 |
| P2 | DUP-1 | 合并 IControlStrategy 与 IControlCreationStrategy | 1h | 中 |
| P2 | DUP-5 | 明确适配器职责边界 | 2h | 中 |
| P2 | B4 | 标记同步方法为 Obsolete | 0.5h | 低 |
| P2 | B9 | 限制 catch 异常类型 | 0.5h | 低 |
| P3 | DUP-2 | 删除 ControlCreationServiceAdapter | 1h | 中 |
| P3 | DUP-4 | 合并 SyncProperty 到 SyncValue | 1h | 低 |
| P3 | DUP-6 | 消除 ConvertView | 1h | 低 |
| P3 | DUP-7 | 提取 System 基类 | 2h | 中 |
| P3 | DUP-8 | 合并 UI 线程调度方法 | 1h | 低 |
| P4 | R2 | 清理注释代码 | 0.5h | 低 |
| P4 | R4 | 清理已迁移方法 | 1h | 低 |
| P4 | R5 | 合并两个字典 | 1h | 低 |
| P4 | R8 | 删除 WduiEventManager 属性 | 0.5h | 低 |
| P4 | B5 | 修复颜色解析日志 | 0.5h | 低 |
| P4 | B6 | 修复回退路径 | 1h | 中 |
| P4 | B8 | 添加 Entity 验证 | 0.5h | 低 |
| P4 | B10 | 修复 RegisterAdapter 广播语义 | 1h | 低 |

### 4.2 详细重构步骤

#### P0：修复核心错误

**步骤 1：修复 SyncEnabledVisible 值同步逻辑（B1）**

```csharp
// 在 ControlValueSyncSystem 中注入 IWinFormsControlAdapter
private readonly IWinFormsControlAdapter _adapter;

// 构造函数增加 adapter 参数
public ControlValueSyncSystem(EntityManager entityManager, ControlPool controlPool,
    TConfig.Controls.IControlValueAccessor? valueWriter = null,
    IListSelectionAdapter? listSelectionAdapter = null,
    IWinFormsControlAdapter? adapter = null)
{
    // ...
    _adapter = adapter ?? new WinForms.Adapter.WinFormsControlAdapter();
}

// 修复 SyncEnabledVisible 中的 getControlValue 和 setControlValue
private void SyncEnabledVisible()
{
    var entities = _entityManager.GetEntities<TConfigControlType>();
    foreach (var entity in entities)
    {
        var typeComp = _entityManager.GetComponent<TConfigControlType>(entity);
        var control = _controlPool.GetControl(entity);
        if (control == null) continue;

        // ★ 修复 B1：使用 _adapter.GetEnabled/SetEnabled 替代 _valueWriter.GetValue/SetValue
        SyncProperty(ref typeComp, control, entity, _entityManager,
            getCompValue: (c) => c.Enabled,
            getControlValue: (ctrl) => _adapter.GetEnabled(ctrl),
            setTimestamp: (c, t) => c.EnabledLastUpdated = t,
            setControlValue: (ctrl, v) => _adapter.SetEnabled(ctrl, v));

        SyncProperty(ref typeComp, control, entity, _entityManager,
            getCompValue: (c) => c.Visible,
            getControlValue: (ctrl) => _adapter.GetVisible(ctrl),
            setTimestamp: (c, t) => c.VisibleLastUpdated = t,
            setControlValue: (ctrl, v) => _adapter.SetVisible(ctrl, v));
    }
}
```

**步骤 2：修复 ContinueWith 参数（B7）**

```csharp
// 方案 A（推荐）：使用 await 替代 ContinueWith
private async void BindEvent(object control, EventBindingComponent binding)
{
    if (_bindingDelegate == null)
        return;

    try
    {
        await _bindingDelegate(control, binding.EventName, binding.FunctionName, binding);
    }
    catch (Exception ex)
    {
        FrontEndDrivenLog.Log("EventBindingSystem_BindEvent_failed",
            new { eventName = binding.EventName, error = ex.Message });
    }
}
```

**步骤 3：修复 ControlPool 一对多引用（B2）**

```csharp
// CreateSplitterPanelEntity 不再注册到 ControlPool
private Entity CreateSplitterPanelEntity(Entity parentEntity, string name, string panelSide)
{
    var panelEntity = _controlFactory.CreateFromConfig(new ControlConfig
    {
        Type = "splitterpanel",
        Name = name,
        Text = name
    });

    _entityManager.AddComponent(panelEntity, new SplitterPanelComponent
    {
        PanelSide = panelSide
    });

    // ★ 修复：不再注册到 ControlPool
    // SplitterPanel 没有对应的独立 WinForms Control，
    // AddToParentControl 中通过父 Entity 查找 SplitContainer，
    // 再根据 SplitterPanelComponent.PanelSide 获取 Panel1/Panel2。
    return panelEntity;
}
```

#### P1：低风险清理

**步骤 4：删除 ReadValue/SupportsType 重复方法（DUP-3/R6）**

删除 `WinFormsControlValueWriter` 中的 `ReadValue` 和 `SupportsType` 方法，所有调用方统一使用 `GetValue` 和 `IsSupported`。

**步骤 5：删除 IControlCreationStrategy 废弃接口（R1）**

删除 `Systems/IControlCreationStrategy.cs` 文件。

**步骤 6：删除 LoadConfigAsync(string) 死代码（R3）**

删除 `EcsConfigContainer.LoadConfigAsync(string)` 方法，或标记为 `[Obsolete]`。

**步骤 7：移除 _subscribedSplitters 冗余字典（R7/B3）**

删除 `SplitContainerSystem` 中的 `_subscribedSplitters` 字典，仅使用 `_initialized` 标志。

#### P2：中等风险清理

**步骤 8：合并 IControlStrategy 与 IControlCreationStrategy（DUP-1）**

删除 `IControlCreationStrategy`，将 `IControlStrategy` 标记为 `[Obsolete]`。

**步骤 9：明确适配器职责边界（DUP-5）**

- `WinFormsControlAdapter` → 控件树操作和布局/样式
- `WinFormsControlValueWriter` → 控件值读写
- 将 Enabled/Visible 属性操作统一到 `WinFormsControlAdapter`

**步骤 10：限制 catch 异常类型（B9）**

```csharp
catch (JsonException ex) when (!strictMode)
{
    FrontEndDrivenLog.Log("EcsConfigContainer_LoadFromFileAsync_json_error",
        new { path = configPath, error = ex.Message });
    return null;
}
catch (IOException ex) when (!strictMode)
{
    FrontEndDrivenLog.Log("EcsConfigContainer_LoadFromFileAsync_io_error",
        new { path = configPath, error = ex.Message });
    return null;
}
```

#### P3：架构优化

**步骤 11：删除 ControlCreationServiceAdapter（DUP-2）**

删除适配器和 `IControlCreationService` 接口，调用方直接使用 `ControlStrategyRegistry`。

**步骤 12：合并 SyncProperty 到 SyncValue（DUP-4）**

将 `SyncProperty` 的逻辑参数化后合并到 `SyncValue` 泛型方法中。

**步骤 13：消除 ConvertView（DUP-6）**

将 View 视为 Type="view" 的特殊 Control，统一由 `ConvertControl` 处理。

**步骤 14：提取 System 基类（DUP-7）**

```csharp
public abstract class EcsSystemBase
{
    protected abstract Type[] RequiredComponents { get; }
    protected abstract bool ShouldProcess(Entity entity);
    protected abstract void ProcessEntity(Entity entity);
    
    public void Update(EntityManager entityManager)
    {
        var entities = entityManager.GetEntities(RequiredComponents);
        foreach (var entity in entities)
        {
            if (!ShouldProcess(entity)) continue;
            ProcessEntity(entity);
        }
    }
}
```

**步骤 15：合并 UI 线程调度方法（DUP-8）**

```csharp
private T WrapWithUiThreadDispatch<TControl, T>(TControl control, Func<TControl, T> func)
    where TControl : class
{
    if (control is Control c && c.InvokeRequired)
        return (T)c.Invoke(func, control);
    return func(control);
}
```

#### P4：低优先级

| 步骤 | 编号 | 内容 |
|------|------|------|
| 16 | R2 | 清理 ControlSyncSystem 中的注释代码 |
| 17 | R4 | 清理 ConfigSelectorController 中已迁移的方法 |
| 18 | R5 | 合并 ControlStrategyRegistry 的两个字典 |
| 19 | R8 | 删除 EcsConfigContainer 的 WduiEventManager 属性 |
| 20 | B5 | 修复颜色解析日志，使用 FrontEndDrivenLog |
| 21 | B6 | 修复 ControlStrategyRegistry 的回退路径 |
| 22 | B8 | 添加 Entity 注册验证 |
| 23 | B10 | 修复 RegisterAdapter 广播语义 |

---

## 5. 实施路线图

### 阶段 1：紧急修复（P0）— 1 天

```
Day 1:
  ├── 上午: 修复 SyncEnabledVisible 值同步逻辑（B1）
  │   ├── 在 ControlValueSyncSystem 中注入 IWinFormsControlAdapter
  │   ├── 替换 _valueWriter.GetValue/SetValue 为 _adapter.GetEnabled/SetEnabled
  │   └── 编写单元测试验证 Enabled/Visible 同步
  ├── 中午: 修复 ContinueWith 参数（B7）
  │   ├── 使用 await 替代 ContinueWith
  │   └── 验证异常日志输出
  └── 下午: 修复 ControlPool 一对多引用（B2）
      ├── CreateSplitterPanelEntity 不再注册到 ControlPool
      ├── 验证 AddToParentControl 的 SplitterPanel 分支
      └── 集成测试 SplitContainer 控件树
```

### 阶段 2：低风险清理（P1）— 0.5 天

```
Day 2:
  ├── 删除 ReadValue/SupportsType 重复方法（DUP-3/R6）
  ├── 删除 IControlCreationStrategy 废弃接口（R1）
  ├── 删除 LoadConfigAsync(string) 死代码（R3）
  └── 移除 _subscribedSplitters 冗余字典（R7/B3）
```

### 阶段 3：中等风险清理（P2）— 1 天

```
Day 3:
  ├── 合并 IControlStrategy 与 IControlCreationStrategy（DUP-1）
  ├── 明确适配器职责边界（DUP-5）
  ├── 标记同步方法为 Obsolete（B4）
  └── 限制 catch 异常类型（B9）
```

### 阶段 4：架构优化（P3）— 1.5 天

```
Day 4-5:
  ├── 删除 ControlCreationServiceAdapter（DUP-2）
  ├── 合并 SyncProperty 到 SyncValue（DUP-4）
  ├── 消除 ConvertView（DUP-6）
  ├── 提取 System 基类（DUP-7）
  └── 合并 UI 线程调度方法（DUP-8）
```

### 阶段 5：低优先级（P4）— 1 天

```
Day 6:
  ├── 清理注释代码和已迁移方法（R2, R4）
  ├── 合并字典和删除委托属性（R5, R8）
  ├── 修复日志和验证（B5, B6, B8）
  └── 修复广播语义（B10）
```

### 总工作量估计

| 阶段 | 工作量 | 风险 |
|------|--------|------|
| 阶段 1：紧急修复 | 1 天 | 高 — 影响核心同步和控件树 |
| 阶段 2：低风险清理 | 0.5 天 | 低 |
| 阶段 3：中等风险清理 | 1 天 | 中 |
| 阶段 4：架构优化 | 1.5 天 | 中 |
| 阶段 5：低优先级 | 1 天 | 低 |
| **合计** | **5 天** | |

---

## 6. 风险与注意事项

### 6.1 高风险项

1. **SyncEnabledVisible 修复（B1）**
   - 影响 Enabled/Visible 同步的核心逻辑
   - 需要注入 `IWinFormsControlAdapter`，可能影响现有构造函数调用
   - 建议先编写单元测试验证修复前后的行为差异

2. **ControlPool 一对多引用修复（B2）**
   - 影响 SplitContainer 的控件树构建
   - `CreateSplitterPanelEntity` 不再注册到 ControlPool 后，`AddToParentControl` 的 SplitterPanel 分支需要依赖 `SplitterPanelComponent` 查找父 SplitContainer
   - 需要确保所有调用路径都正确处理

3. **ContinueWith 参数修复（B7）**
   - 使用 `await` 替代 `ContinueWith` 后，`BindEvent` 方法变为 `async void`
   - `async void` 的异常无法被外部捕获，需要确保内部 try-catch 完整
   - 建议保留 `ContinueWith` 方案但正确指定参数

### 6.2 注意事项

1. **删除文件前确认无调用方**
   - 使用 `find_references` 或 `search_code` 确认
   - 特别注意间接引用（反射、动态加载）

2. **适配器职责划分的兼容性**
   - `WinFormsControlValueWriter` 中可能已有调用方依赖 `ReadValue`/`SupportsType`
   - 删除前先标记为 `[Obsolete]` 一个版本周期

3. **System 基类提取的影响范围**
   - `LayoutSystem` 和 `StyleSystem` 可能有各自特殊的处理逻辑
   - 基类提取后需要确保子类可以灵活覆盖默认行为

4. **向后兼容**
   - 删除的接口如果被外部项目引用，需要保留 `[Obsolete]` 转发类
   - 建议在删除前先标记为 `[Obsolete]` 一个版本周期

---

## 附录 A：文件清单

### A.1 待删除文件

| 文件 | 原因 | 替代 |
|------|------|------|
| `ControlRender/Creation/Systems/IControlCreationStrategy.cs` | R1 — 废弃接口 | 无（功能已由 ConfigDrivenControlFactory 替代） |
| `ControlRender/Creation/ControlCreationServiceAdapter.cs` | DUP-2 — 纯委托包装 | `ControlStrategyRegistry` 直接调用 |
| `Shared/Abstractions/IControlCreationService.cs` | DUP-2 — 与适配器一起删除 | `ControlStrategyRegistry` 直接调用 |

### A.2 待修改文件

| 文件 | 修改内容 |
|------|----------|
| `ControlInteraction/ValueSync/Systems/ControlValueSyncSystem.cs` | B1 — 修复 SyncEnabledVisible；DUP-4 — 合并 SyncProperty |
| `ControlRender/Creation/ConfigDrivenControlFactory.cs` | B2 — 修复 ControlPool 引用；B8 — 添加验证；DUP-6 — 消除 ConvertView |
| `ControlInteraction/EventBinding/Systems/EventBindingSystem.cs` | B7 — 修复 ContinueWith 参数 |
| `WinForms/ValueSync/WinFormsControlValueWriter.cs` | DUP-3/R6 — 删除重复方法；DUP-8 — 合并调度方法 |
| `ControlRender/Layout/Systems/SplitContainerSystem.cs` | R7/B3 — 移除冗余字典 |
| `ControlLookup/Container/EcsConfigContainer.cs` | R3 — 删除死代码；B4 — 标记 Obsolete；B9 — 限制 catch |
| `ControlRender/Style/Systems/StyleSystem.cs` | B5 — 修复颜色解析日志 |
| `ControlRender/Creation/ControlStrategyRegistry.cs` | B6 — 修复回退路径；R5 — 合并字典 |
| `ControlInteraction/ValueSync/Implementation/CompositeValueWriter.cs` | B10 — 修复广播语义 |
| `ControlRender/Creation/IControlStrategy.cs` | DUP-1 — 标记 Obsolete |
| `ControlRender/Layout/Systems/LayoutSystem.cs` | DUP-7 — 提取基类 |
| `ControlRender/Style/Systems/StyleSystem.cs` | DUP-7 — 提取基类 |
| `WinForms/ConfigSelectorController.cs` | R4 — 清理已迁移方法 |
| `ControlInteraction/ValueSync/Systems/ControlSyncSystem.cs` | R2 — 清理注释代码 |
| `ControlLookup/Container/EcsConfigContainer.cs` | R8 — 删除 WduiEventManager 属性 |

---

## 附录 B：调用关系图

```
ControlValueSyncSystem (B1, DUP-4)
  ├── SyncText() → SyncValue<TextComponent>
  ├── SyncEnabledVisible() → SyncProperty (B1: 应使用 _adapter 而非 _valueWriter)
  ├── SyncSelection() → SyncValue<SelectionComponent>
  ├── SyncCheckState() → SyncValue<CheckStateComponent>
  ├── SyncNumericValue() → SyncValue<NumericValueComponent>
  ├── SyncDateTimeValue() → SyncValue<DateTimeValueComponent>
  └── SyncListSelection() → SyncListSelectionCore

ConfigDrivenControlFactory (B2, B8, DUP-6)
  ├── Convert() → CreateRootEntity + ConvertChildren
  ├── ConvertChildren() → ConvertView + ConvertControl
  ├── ConvertView() (DUP-6: 可委托给 ConvertControl)
  ├── ConvertControl() → CreateSplitterPanelEntity + AddToParentControl
  ├── CreateSplitterPanelEntity() (B2: 不应注册到 ControlPool)
  └── AddToParentControl() → SplitterPanel 分支 / 普通分支

EventBindingSystem (B7)
  └── BindEvent() → ContinueWith (B7: 参数错误)

SplitContainerSystem (R7/B3)
  └── Update() → _initialized + _subscribedSplitters (R7: 双重跟踪)

EcsConfigContainer (R3, B4, B9, R8)
  ├── LoadConfigAsync(string) (R3: 死代码)
  ├── LoadFromFile() (B4: 死锁风险)
  ├── LoadFromFileAsync() (B9: catch 范围过大)
  └── WduiEventManager (R8: 纯委托属性)

WinFormsControlValueWriter (DUP-3/R6, DUP-8)
  ├── GetValue / ReadValue (DUP-3: 重复)
  ├── IsSupported / SupportsType (DUP-3: 重复)
  └── WrapWithUiThreadDispatch × 3 (DUP-8: 结构重复)

ControlStrategyRegistry (R5, B6)
  ├── _strategyMap + _factoryMap (R5: 可合并)
  └── container 工厂回退路径 (B6: 创建半初始化对象)

CompositeValueWriter (B10)
  └── RegisterAdapter() → 广播到所有 writer (B10: 可能覆盖)
```

---

## 附录 C：与跨项目重构文档的关系

本文档与 `重构方案_FileDriven.UI与FrontEndDriven重复冗余错误逻辑分析.md` 互补：

| 维度 | 跨项目文档 | 本文档（模块内部） |
|------|-----------|-------------------|
| **范围** | `FileDriven.UI` ↔ `FrontEndDriven` | `FrontEndDriven` 内部 |
| **重点** | 跨项目委托转发层、日志系统合并 | 模块内部接口/适配器/System 质量问题 |
| **P0 问题** | EcsConfigContainer 加载链、参数传递 | SyncEnabledVisible 错误、ControlPool 引用、ContinueWith 参数 |
| **P1 问题** | 删除纯委托转发类 | 删除重复方法、废弃接口、死代码 |
| **实施顺序** | 建议先实施跨项目文档的 P0 | 建议先实施本文档的 P0，再实施跨项目文档 |

**建议实施顺序**：
1. 本文档 P0（紧急修复 FrontEndDriven 内部错误）
2. 跨项目文档 P0（修复加载链和参数传递）
3. 本文档 P1-P2 + 跨项目文档 P1（清理重复和冗余）
4. 本文档 P3-P4 + 跨项目文档 P2-P4（架构优化）
