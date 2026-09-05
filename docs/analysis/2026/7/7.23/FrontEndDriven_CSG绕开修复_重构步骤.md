# FrontEndDriven CSG/Ports 绕开修复 — 重构步骤

> **基于**：`FrontEndDriven_CSG绕开分析_详细报告.md`  
> **基于**：`FrontEndDriven_ECS_WinForms_三收拢点重构方案.md`  
> **分析日期**：2026-07-23  
> **目标**：将 5 处绕开点逐一修复，纳入三收拢点架构  
> **状态**：📋 方案设计完成，待实施

---

## 架构现状

三收拢点已就位（`Ports.cs`），管线末尾已串联：
```
ExecutePipeline():
  → ECS System 执行（只写 Component）
  → CsgEntryPoint.Collect() → CanonicalForm
  → WinFormsOutputPort.Apply() → 差异更新控件
```

**但仍有 5 处绕开点**未纳入此架构。

---

## 修复优先级

| 优先级 | # | 模块 | 绕开类型 | 工作量 |
|--------|---|------|---------|--------|
| **P0** | ① | WinFormsControlValueWriter | 值同步直接操作控件 | ⭐⭐⭐ |
| **P0** | ② | UIEventBinder | 事件绑定直接注册 WinForms | ⭐⭐ |
| **P1** | ③ | CommandHandler | 直接持有 TextBox 引用 | ⭐ |
| **P1** | ④ | EventSubscriptionManager | 直接订阅全局静态事件 | ⭐ |
| **P1** | ⑤ | CompositeValueWriter | 委托链末端直接操作控件 | ⭐（随①） |

---

## 阶段 1：修复 WinFormsControlValueWriter（P0）

### 问题

`WinForms/ValueSync/WinFormsControlValueWriter.cs` 实现 `IControlValueAccessor`，直接操作 17+ 种 WinForms 控件的原生属性，完全绕过 CSG 和 WinFormsOutputPort。

### 修复方案

**方案：将值读写改为通过 WinFormsOutputPort 执行**

```
当前路径：
  ControlInteraction/ValueSync/ 中的调用方
    → WinFormsControlValueWriter.GetValue/SetValue
    → 直接操作控件属性

目标路径：
  ControlInteraction/ValueSync/ 中的调用方
    → WinFormsControlValueWriter（保留接口兼容）
    → 内部委托给 WinFormsOutputPort
    → WinFormsOutputPort → WinFormsUIAdapter → 控件操作
```

#### 具体步骤

##### 步骤 1.1：WinFormsControlValueWriter 注入 WinFormsOutputPort

**文件**：`WinForms/ValueSync/WinFormsControlValueWriter.cs`

**修改**：
1. 构造函数新增 `WinFormsOutputPort? outputPort` 可选参数
2. 新增 `_useOutputPort` 标志位
3. `GetValue()` 和 `SetValue()` 中：
   - 当 `_useOutputPort == true` 时，通过 `WinFormsOutputPort` 执行
   - 当 `_useOutputPort == false` 时，保持现有行为（向后兼容）
4. 通过 `WinFormsOutputPort` 执行时：
   - `SetValue(control, value)` → 构建增量 CanonicalForm → `_outputPort.Apply(form)`
   - `GetValue(control)` → 通过 `IWinFormsControlAdapter` 读取（已在 WinFormsOutputPort 内部）

```csharp
// 新增构造函数
public WinFormsControlValueWriter(WinFormsOutputPort? outputPort = null) : this()
{
    _outputPort = outputPort;
}

public void SetValue(object control, object? value)
{
    if (_outputPort != null)
    {
        // 通过 WinFormsOutputPort 执行
        var form = BuildValueChangeForm(control, value);
        _outputPort.Apply(form);
        return;
    }
    // 旧路径（向后兼容）
    var success = WriteValue(control, value);
    // ...
}
```

##### 步骤 1.2：更新 EcsRenderPipeline 注入

**文件**：`RenderPipeline/EcsRenderPipeline.cs`

**修改**：在 `InitializeCsgLayer()` 中，将 `_winFormsOutputPort` 注入到 `WinFormsControlValueWriter`

##### 步骤 1.3：标记旧路径为 Obsolete

**文件**：`WinForms/ValueSync/WinFormsControlValueWriter.cs`

**修改**：在无参构造函数上添加 `[Obsolete("请使用带 WinFormsOutputPort 的构造函数")]`

---

## 阶段 2：修复 UIEventBinder（P0）

### 问题

`WinForms/EventBinding/UIEventBinder.cs` 通过 `IWinFormsEventRegistrar` 直接注册 WinForms 原生事件，绕过 ECS 的 `EventBindingSystem`。

### 修复方案

**方案：UIEventBinder 的事件注册信息改为通过 ECS EventBindingSystem 管理**

```
当前路径：
  UIEventBinder.BindAll()
    → _eventRegistrar.RegisterClick(...) 直接注册 WinForms 事件
    → _commandTextBox.KeyPress += handler 直接 +=

目标路径：
  UIEventBinder.BindAll()
    → 将事件绑定信息写入 ECS Component（EventBindingsComponent）
    → 触发管线重执行
    → EventBindingSystem 读取 Component → CanonicalForm
    → WinFormsOutputPort → WinFormsEventAdapter 执行绑定
```

#### 具体步骤

##### 步骤 2.1：UIEventBinder 注入 EntityManager

**文件**：`WinForms/EventBinding/UIEventBinder.cs`

**修改**：
1. 构造函数新增 `EntityManager entityManager` 和 `ControlPool controlPool` 参数
2. `BindAll()` 中：
   - 不再直接调用 `_eventRegistrar.RegisterClick(...)` 等
   - 改为查找对应控件的 Entity，添加 `EventBindingsComponent`
   - 添加 `PendingEventBindingComponent` 触发 EventBindingSystem 处理
3. 保留 `BindCommandEvents()` 中的 `_commandTextBox.KeyPress`（这是命令输入，属于特殊处理）

```csharp
private void BindConfigSelectorEvents()
{
    // 1. searchTextBox.TextChanged → 查找 Entity，添加事件绑定
    var searchEntity = FindEntity("searchTextBox");
    if (searchEntity.IsValid)
    {
        AddEventBinding(searchEntity, "TextChanged", "RefreshConfigList");
    }
    // 2. configListView.SelectedIndexChanged → ...
    var listViewEntity = FindEntity("configListView");
    if (listViewEntity.IsValid)
    {
        AddEventBinding(listViewEntity, "SelectedIndexChanged", "OnConfigSelected");
    }
    // ...
}

private void AddEventBinding(Entity entity, string eventName, string functionName)
{
    var bindings = _entityManager.HasComponent<EventBindingsComponent>(entity)
        ? _entityManager.GetComponent<EventBindingsComponent>(entity)
        : new EventBindingsComponent();

    bindings.Bindings.Add(new EventBindingComponent
    {
        EventName = eventName,
        FunctionName = functionName
    });

    _entityManager.SetComponent(entity, bindings);
    _entityManager.AddComponent(entity, new PendingEventBindingComponent());
}
```

##### 步骤 2.2：更新 EcsRenderPipeline 注入

**文件**：`RenderPipeline/EcsRenderPipeline.cs`

**修改**：在 `InitializeCsgLayer()` 中，将 `_entityManager` 和 `_controlPool` 注入到 `UIEventBinder`

---

## 阶段 3：修复 CommandHandler（P1）

### 问题

`WinForms/CommandHandler.cs` 直接持有 `TextBox` 引用并调用 `.Clear()` / `.AppendText()`。

### 修复方案

**方案：通过 IWinFormsControlAdapter 操作控件**

#### 具体步骤

##### 步骤 3.1：CommandHandler 注入 IWinFormsControlAdapter

**文件**：`WinForms/CommandHandler.cs`

**修改**：
1. 构造函数新增 `IWinFormsControlAdapter adapter` 参数
2. `ClearOutput()` 改为通过 `_adapter` 操作：
   ```csharp
   private void ClearOutput()
   {
       _adapter.SetText(_outputTextBox, string.Empty);
   }
   ```
3. `LogMessage()` 改为通过 `_adapter` 操作：
   ```csharp
   private void LogMessage(string message)
   {
       var currentText = _adapter.GetText(_outputTextBox) ?? string.Empty;
       _adapter.SetText(_outputTextBox, currentText + message + Environment.NewLine);
   }
   ```
4. `_commandTextBox.Clear()` 改为 `_adapter.SetText(_commandTextBox, string.Empty)`

**注意**：CommandHandler 仍然直接持有 `TextBox` 引用（用于 `InvokeRequired` 判断），但控件操作改为通过 `IWinFormsControlAdapter`。

---

## 阶段 4：修复 EventSubscriptionManager（P1）

### 问题

`ControlInteraction/EventBinding/Implementation/EventSubscriptionManager.cs` 直接订阅 `LogRepository.LogWritten` 和 `FrameworkErrors.ErrorOccurred` 全局静态事件。

### 修复方案

**方案：通过 EcsInputPort 将全局事件转换为 ECS Component**

#### 具体步骤

##### 步骤 4.1：EventSubscriptionManager 注入 EcsInputPort

**文件**：`ControlInteraction/EventBinding/Implementation/EventSubscriptionManager.cs`

**修改**：
1. 构造函数新增 `EcsInputPort? ecsInputPort` 可选参数
2. `SubscribeLogWritten()` 中：
   - 当 `_ecsInputPort != null` 时，通过 `EcsInputPort.ApplyDataChange()` 处理
   - 当 `_ecsInputPort == null` 时，保持现有行为（向后兼容）
3. `SubscribeFrameworkError()` 同理

```csharp
public void SubscribeLogWritten(Action<LogEntry> handler)
{
    if (_ecsInputPort != null)
    {
        // 通过 EcsInputPort 处理
        LogRepository.LogWritten += entry =>
        {
            _ecsInputPort.ApplyDataChange(someEntity, entry, ...);
        };
        return;
    }
    // 旧路径（向后兼容）
    // ...
}
```

##### 步骤 4.2：标记旧路径为 Obsolete

在无 `EcsInputPort` 的构造函数上添加 `[Obsolete]`。

---

## 阶段 5：修复 CompositeValueWriter（P1，随阶段 1）

### 问题

`ControlInteraction/ValueSync/Implementation/CompositeValueWriter.cs` 作为值访问的组合入口，末端委托链落到 `WinFormsControlValueWriter` 直接操作控件。

### 修复方案

**方案：CompositeValueWriter 本身无需修改，阶段 1 修复 WinFormsControlValueWriter 后，委托链末端自然纳入三收拢点**

```
修复前：
  CompositeValueWriter → WinFormsControlValueWriter → 直接操作控件

修复后（阶段 1 完成后）：
  CompositeValueWriter → WinFormsControlValueWriter → WinFormsOutputPort → 控件
```

**无需单独修改 CompositeValueWriter**，只需确保阶段 1 中 `WinFormsControlValueWriter` 正确注入 `WinFormsOutputPort`。

---

## 阶段 6：清理与验证

### 步骤 6.1：编译验证

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln"
```

### 步骤 6.2：回归测试

验证以下功能正常：
- [ ] UI 配置加载和渲染
- [ ] 布局计算和控件定位
- [ ] 样式合并和应用
- [ ] 事件绑定（配置选择器事件）
- [ ] 命令输入和执行
- [ ] 值同步（控件值读写）
- [ ] SplitContainer 分割管理

### 步骤 6.3：架构验证

- [ ] WinFormsControlValueWriter 不再直接操作控件（通过 WinFormsOutputPort）
- [ ] UIEventBinder 的事件注册通过 ECS Component 管理
- [ ] CommandHandler 通过 IWinFormsControlAdapter 操作控件
- [ ] EventSubscriptionManager 的全局事件通过 EcsInputPort 处理
- [ ] CompositeValueWriter 的委托链末端通过 WinFormsOutputPort

---

## 实施顺序依赖图

```
阶段 1（WinFormsControlValueWriter）
  └── 依赖：WinFormsOutputPort 已就位（Ports.cs）

阶段 2（UIEventBinder）
  └── 依赖：EntityManager、ControlPool 已就位

阶段 3（CommandHandler）
  └── 依赖：IWinFormsControlAdapter 已就位

阶段 4（EventSubscriptionManager）
  └── 依赖：EcsInputPort 已就位（Ports.cs）

阶段 5（CompositeValueWriter）
  └── 依赖：阶段 1 完成（随阶段 1 自动修复）

阶段 6（清理与验证）
  └── 依赖：所有前置阶段完成
```

**阶段 1-4 可并行实施**，因为它们修改的是不同的文件，依赖的都是已就位的基础设施（Ports.cs 中的三收拢点）。

---

## 文件变更清单

| 阶段 | 文件 | 操作 | 风险 |
|------|------|------|------|
| 1 | `WinForms/ValueSync/WinFormsControlValueWriter.cs` | 修改 | 中 |
| 1 | `RenderPipeline/EcsRenderPipeline.cs` | 修改 | 低 |
| 2 | `WinForms/EventBinding/UIEventBinder.cs` | 修改 | 中 |
| 2 | `RenderPipeline/EcsRenderPipeline.cs` | 修改 | 低 |
| 3 | `WinForms/CommandHandler.cs` | 修改 | 低 |
| 4 | `ControlInteraction/EventBinding/Implementation/EventSubscriptionManager.cs` | 修改 | 低 |
| 5 | 无（随阶段 1 自动修复） | - | - |
| 6 | 无（验证） | - | - |
