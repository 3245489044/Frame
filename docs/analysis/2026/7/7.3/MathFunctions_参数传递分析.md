# MathFunctions.json 多个 UI 控件之间的参数传递机制分析

## 概述

本文分析 `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json` 中多个 UI 控件之间的参数传递是如何实现的。整个机制分为 **4 层架构**：JSON 配置声明层 → 配置模型反序列化层 → 事件绑定层 → 函数执行层。

---

## 一、JSON 配置结构

`MathFunctions.json` 定义了一个数学函数测试表单，包含以下 UI 控件：

| 控件名 | 类型 | 角色 |
|--------|------|------|
| `valueATextBox` | textbox | 输入值 A |
| `valueBTextBox` | textbox | 输入值 B |
| `addButton` / `subtractButton` / `multiplyButton` / `divideButton` | button (action-button 模板) | 运算触发按钮 |
| `functionComboBox` | combobox | 函数选择下拉框 |
| `resultTextBox` | textbox (readOnly) | 结果显示 |

### 1.1 输入控件声明（valueATextBox）

```json
{
  "type": "textbox",
  "name": "valueATextBox",
  "events": {
    "textchanged": "SetValue",
    "bindings": {
      "inputs": ["valueATextBox"],
      "output": "resultTextBox"
    }
  }
}
```

### 1.2 按钮控件声明（addButton）

```json
{
  "template": "action-button",
  "name": "addButton",
  "functionName": "Add",
  "events": {
    "click": "Add",
    "bindings": {
      "inputs": ["valueATextBox", "valueBTextBox"],
      "output": "resultTextBox"
    }
  }
}
```

### 1.3 关键字段说明

| 字段 | 含义 |
|------|------|
| `events.click` / `events.textchanged` | 事件类型 → 绑定的函数名 |
| `events.bindings.inputs` | 输入参数来源（控件名列表，字符串引用） |
| `events.bindings.output` | 输出目标控件名 |

---

## 二、涉及的核心文件清单

### 2.1 JSON 配置文件

| 文件路径 | 作用 |
|----------|------|
| `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json` | 定义 UI 控件布局、事件绑定和参数传递关系 |

### 2.2 配置模型层

| 文件路径 | 作用 |
|----------|------|
| `CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs` | `EventConfig` 类（事件配置模型）、`EventBinding` 类（绑定信息模型）、`EventConfigConverter`（JSON 反序列化器） |

### 2.3 事件绑定解析层

| 文件路径 | 作用 |
|----------|------|
| `CsharpPrj/Share/EventBindingOrchestrator/Services/EventBindingResolver.cs` | `EventBindingResolver` — 从已加载的 UIConfiguration 中查找控件的事件绑定信息 |
| `CsharpPrj/Share/EventBindingOrchestrator/ContainerBinderPipeline.cs` | `ContainerBinderPipeline` — 容器绑定管线，协调配置加载、控件注册、事件绑定等阶段 |
| `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/PipelineHelper.cs` | `PipelineHelper` — 管线公共辅助方法 |

### 2.4 事件绑定执行层

| 文件路径 | 作用 |
|----------|------|
| `FileDriven/FileDriven.Execution/EventBinding/WinFormsEventBinder.cs` | `WinFormsEventBinder` — 通过反射将事件绑定到 WinForms 原生控件事件 |
| `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` | `FrameworkInitializer` — 框架初始化器，创建 EventBinderTransformer 并绑定事件 |

### 2.5 管线上下文与命令执行层

| 文件路径 | 作用 |
|----------|------|
| `FileDriven/FileDriven.Execution/Context/FileDrivenPipelineContext.cs` | `FileDrivenPipelineContext` — 管线统一上下文，存储 `BoundInputs`、`BoundOutputTarget`、`ExtractedValues` 等 |
| `FileDriven/FileDriven.Execution/Pipeline/Stages/ExecuteCommandStage.cs` | `ExecuteCommandStage` — 命令执行阶段，根据绑定信息分发执行 |
| `FileDriven/FileDriven.Business/Registrations/UIToolbox.cs` | `UIToolbox` — UI 工具箱，包含 `ExtractValuesSkeleton`、`ExecuteFunctionSkeleton` 等骨架方法 |

---

## 三、逐层详解

### 第 1 层：JSON 配置声明（声明式绑定）

在 JSON 中，每个控件的 `events` 字段声明了事件绑定关系。参数传递通过控件名（字符串）引用实现：

- **inputs**：声明该事件触发时需要从哪些控件读取值
- **output**：声明该事件执行结果写入哪个控件

这种设计实现了 **声明式参数传递** — 参数传递关系完全在 JSON 中定义，无需编写任何代码。

### 第 2 层：配置模型反序列化层

JSON 通过 `EventConfigConverter`（`EventConfig.cs`）反序列化为强类型模型：

```
JSON "click": "Add" 或 "click": ["Func1","Func2"]
  → EventConfigConverter.ReadStringOrArray()
  → EventConfig.Click = List<string>?  // 支持单函数和多函数绑定

JSON "bindings": { "inputs": [...], "output": "..." }
  → EventConfig.Bindings = EventBinding
  → EventBinding.Inputs = List<string>?  // 输入控件名列表
  → EventBinding.Output = string?        // 输出控件名
```

**关键设计**：`EventConfigConverter` 支持向后兼容 — `"click": "FunctionName"` 和 `"click": ["Func1","Func2"]` 两种 JSON 写法都合法。

### 第 3 层：事件绑定层（运行时绑定）

#### 3.1 渲染管线绑定阶段

当配置加载到 `ConfigContainer` 后，`ContainerBinderPipeline.ExecuteAsync()` 执行以下阶段：

```
阶段 1b: 控件注册
  → RegisterControlsAsync()
  → ControlTreeWalker.CollectControlNames(config) 收集所有控件名
  → ControlRegistry.Register(name, actualControl) 注册到运行时控件注册表

阶段 2b: 渲染管线
  → InvokeRenderPipelineAsync()
  → renderPipeline.RenderSessionOnly(container, config) 创建渲染会话
  → renderPipeline.BindEventsAsync(container) 绑定事件
```

#### 3.2 WinFormsEventBinder.BindControlEvent 核心逻辑

`WinFormsEventBinder` 负责将配置中的事件绑定到 WinForms 原生控件事件：

```csharp
// WinFormsEventBinder.cs
public void BindControlEvent(object control, string eventName, Delegate handler,
    string? output = null, string? inputs = null)
{
    // 1. 保存 output/inputs 到 Control.Tag（运行时存储）
    SaveControlParameters(control, output, inputs);
    
    // 2. 反射查找事件（如 "click" → "Click" EventInfo）
    var eventInfo = GetOrAddEventInfo(control.GetType(), eventName);
    
    // 3. 包装 handler 并添加到事件
    EventHandler wrappedHandler = (sender, args) => { handler.DynamicInvoke(sender, args); };
    eventInfo.AddEventHandler(control, wrappedHandler);
}
```

**SaveControlParameters** 将 `inputs` 和 `output` 存储到控件的 `Control.Tag`（`Dictionary<string, object>`）中，供运行时使用。

### 第 4 层：函数执行层（参数提取与执行）

#### 4.1 EventBindingResolver 查找绑定

当事件触发时，`EventBindingResolver.FindEventBinding()` 从配置模型中查找绑定信息：

```csharp
// EventBindingResolver.cs
private IEventBindingInfo? GetBindingInfo(ControlConfig control, string eventType)
{
    var functionNames = GetEventFunctions(control, eventType);  // 获取函数名列表
    var inputs = ExtractInputs(control);    // 从 control.Events.Bindings.Inputs 提取
    var output = ExtractOutput(control);    // 从 control.Events.Bindings.Output 提取
    return new EventBindingInfoImpl { FunctionNames = functionNames, Inputs = inputs, Output = output };
}
```

#### 4.2 ExecuteCommandStage 分发执行

管线 `ExecuteCommandStage.Process()` 根据绑定信息执行：

```csharp
// ExecuteCommandStage.cs
var (functionName, inputs, output) = _findBindingFunc(target, action);
// inputs = ["valueATextBox", "valueBTextBox"]
// output = "resultTextBox"
ctx.BoundInputs = inputs;
ctx.BoundOutputTarget = output;

// 通过 Container 的 IExecutionCell 执行
var request = FileDrivenExecutionEngine.CreateExecutionRequest(ctx.CurrentCommand, ctx);
var containerResult = await _executionCell.ExecuteAsync(request);
```

#### 4.3 参数值提取与函数执行

执行引擎内部经过以下步骤：

```
BoundInputs = ["valueATextBox", "valueBTextBox"]
    ↓ ExtractValuesSkeleton 从 UI 控件读取实际值
ExtractedValues = {"valueATextBox": "10", "valueBTextBox": "20"}
    ↓ ExecuteFunctionSkeleton 按 BoundInputs 顺序取值
实际参数 = ["10", "20"]
    ↓ 调用 MathLibrary.Add(10, 20)
结果 = 30
```

#### 4.4 结果回写

执行完成后，结果通过 `BoundOutputTarget` 回写到目标控件：

```
BoundOutputTarget = "resultTextBox"
    ↓ UICommandManager.SetControlValue("resultTextBox", "30")
resultTextBox.Text = "30"
```

---

## 四、两种参数传递路径

### 路径 A：按钮点击（click 事件）

```
用户点击 "Add (+)" 按钮
  → WinForms Click 事件触发
  → wrappedHandler.DynamicInvoke()
  → IEventExecutionService.ExecuteAsync("Add", inputs=["valueATextBox", "valueBTextBox"])
  → EventBindingResolver.FindEventBinding("addButton", "click")
      → 返回 FunctionName="Add", Inputs=["valueATextBox","valueBTextBox"], Output="resultTextBox"
  → ExtractValuesSkeleton 从 valueATextBox/valueBTextBox 读取值
  → ExecuteFunctionSkeleton 调用 MathLibrary.Add(a, b)
  → 结果写入 resultTextBox
```

### 路径 B：文本框输入（textchanged 事件）

```
用户在 valueATextBox 中输入
  → WinForms TextChanged 事件触发
  → wrappedHandler.DynamicInvoke()
  → IEventExecutionService.ExecuteAsync("SetValue", inputs=["valueATextBox"])
  → EventBindingResolver.FindEventBinding("valueATextBox", "textchanged")
      → 返回 FunctionName="SetValue", Inputs=["valueATextBox"], Output="resultTextBox"
  → ExtractValuesSkeleton 从 valueATextBox 读取值
  → ExecuteFunctionSkeleton 调用 UIToolbox.SetValue(value)
      → SetValue 是占位函数，实际值设置由管线 Skeleton 完成
  → 结果写入 resultTextBox
```

---

## 五、关键设计要点

### 5.1 声明式绑定

参数传递关系完全在 JSON 中声明，无需编写代码。控件之间的数据流通过 `inputs` 和 `output` 字段定义。

### 5.2 控件名引用解耦

`inputs` 和 `output` 通过控件名（字符串）引用，实现控件之间的解耦。运行时通过 `ControlRegistry` 或 `ControlFinder` 委托将控件名解析为实际 WinForms 控件。

### 5.3 Control.Tag 运行时存储

运行时将 `inputs`/`output` 存储在 WinForms 控件的 `Tag` 属性中，确保事件触发时能快速获取绑定信息。

### 5.4 EventBindingResolver 延迟缓存

`EventBindingResolver` 使用延迟构建的 `ConcurrentDictionary` 缓存：
- 首次查找时遍历配置树构建缓存
- 后续查找 O(1) 命中
- 线程安全（double-check locking）

### 5.5 多函数绑定支持

`EventConfig` 的所有事件属性从 `string?` 改为 `List<string>?`，支持一个事件绑定多个函数。通过 `SingleOrArrayConverter` 支持 JSON 中单字符串和数组两种写法。

### 5.6 向后兼容

`EventConfigConverter` 确保旧版 JSON（单字符串写法）和新版 JSON（数组写法）都能正确反序列化。

---

## 六、完整数据流总结

```
JSON 配置 (MathFunctions.json)
  ↓ JsonSerializer.Deserialize<UIConfiguration>
  ↓ EventConfigConverter 自定义反序列化
ControlConfig (含 EventConfig + EventBinding)
  ↓ ContainerBinderPipeline.RegisterControlsAsync
ControlRegistry (控件名 → 运行时控件)
  ↓ ContainerBinderPipeline.InvokeRenderPipelineAsync
  ↓ renderPipeline.BindEventsAsync()
WinFormsEventBinder.BindControlEvent (反射绑定到原生事件)
  ↓ SaveControlParameters (inputs/output → Control.Tag)
运行时事件触发 (用户点击/输入)
  ↓ wrappedHandler.DynamicInvoke()
  ↓ IEventExecutionService.ExecuteAsync()
EventBindingResolver.FindEventBinding (从配置模型查找)
  ↓ 返回 FunctionName, Inputs, Output
ExtractValuesSkeleton (从 inputs 控件名读取实际值)
  ↓ {"valueATextBox": "10", "valueBTextBox": "20"}
ExecuteFunctionSkeleton (按 BoundInputs 顺序取值并调用函数)
  ↓ MathLibrary.Add(10, 20) = 30
函数执行结果
  ↓ UICommandManager.SetControlValue("resultTextBox", "30")
UI 控件更新 (resultTextBox.Text = "30")
```

---

## 七、架构层次总览

| 层次 | 核心文件 | 职责 |
|------|----------|------|
| JSON 配置层 | `MathFunctions.json` | 声明 UI 布局、事件绑定、参数传递关系 |
| 配置模型层 | `EventConfig.cs` | 反序列化 JSON 为强类型模型 |
| 事件绑定解析层 | `EventBindingResolver.cs`、`ContainerBinderPipeline.cs` | 从配置模型查找绑定信息，协调绑定流程 |
| 事件绑定执行层 | `WinFormsEventBinder.cs`、`FrameworkInitializer.cs` | 反射绑定到 WinForms 原生事件 |
| 管线上下文层 | `FileDrivenPipelineContext.cs` | 存储 BoundInputs、BoundOutputTarget 等运行时状态 |
| 命令执行层 | `ExecuteCommandStage.cs` | 根据绑定信息分发执行 |
| 函数执行层 | `UIToolbox.cs` | 提取参数值、调用函数、回写结果 |
