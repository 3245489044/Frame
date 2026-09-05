# UI 数据管线与工作流整合方案

> **目标**：将 UI 框架的 `bindings.inputs/output` 数据传递机制改造为默认使用工作流的 `ParameterKeys/ResultKeys`，使 UI 成为工作流的可视化前端层。

---

## 1. 现状分析

### 1.1 当前 UI 数据管线

```
用户点击按钮
    ↓
EventBinderTransformer.CreateEventHandler()
    ↓
BuildParametersFromBindings()
    ├── 从 Control.Tag 读取 inputs = ["valueATextBox", "valueBTextBox"]
    ├── FindByName("valueATextBox") → 从 WinForms 控件树查找
    ├── GetValue(TextBox) → "10" (string)
    └── 返回 ["10", "20"]
    ↓
EventExecutionService.ExecuteFunctionAsync("Add", ["10", "20"])
    ↓
WriteOutputToControl()
    ├── 从 Tag 读取 output = "resultTextBox"
    ├── FindByName("resultTextBox") → 从 WinForms 控件树查找
    └── SetValue(TextBox, "30")
```

**问题**：数据源是 WinForms 控件树，与工作流上下文隔离，无法复用工作流的参数映射引擎。

### 1.2 当前工作流数据管线

```
工作流引擎执行节点
    ↓
KeyProcessor.ProcessConnectionKeyMapping()
    ├── 读取 fromNode.ResultKeys = ["MERGE1_Result"]
    ├── 读取 toNode.ParameterKeys = ["DataProcess", "MerageNode.Inputs_"]
    ├── ProcessKeyMappings(sourceKeys, targetKeys)
    └── 更新 toNode.ParameterKeys
    ↓
节点执行函数
    ↓
结果写回 RuntimeContext
```

**问题**：工作流数据无法直接在 UI 上展示，需要额外的桥接层。

---

## 2. 整合方案

### 2.1 核心设计：三层数据模型

```
┌─────────────────────────────────────────────────────────┐
│                     UI 视图层                            │
│  (TextBox/Button/Label 等 WinForms 控件)                │
│  控件名 ↔ ParameterKey 双向绑定                          │
├─────────────────────────────────────────────────────────┤
│                    绑定层                                │
│  BindingMetadataTransformer                             │
│  将 bindings.inputs/output 解析为 ParameterKey 引用      │
├─────────────────────────────────────────────────────────┤
│                    数据层                                │
│  WorkflowRuntimeContext                                 │
│  ParameterKeys → 参数值                                  │
│  ResultKeys → 结果值                                     │
└─────────────────────────────────────────────────────────┘
```

### 2.2 核心原则：复用工作流语法，不创造新语法

**`inputs` 直接使用工作流的 `ParameterKeys` 语法**，即纯字符串键名。

区分"这是控件名还是 ParameterKey"的方式：通过 `dataBinding` 配置节声明控件与 ParameterKey 的映射关系。

### 2.3 JSON 配置语法

#### 当前语法（向后兼容 — 无 dataBinding 时）
```json
{
  "name": "addButton",
  "events": {
    "click": ["Add"],
    "bindings": {
      "inputs": ["valueATextBox", "valueBTextBox"],
      "output": "resultTextBox"
    }
  }
}
```
> 无 `dataBinding` 时，`inputs/output` 中的值视为 WinForms 控件名，走旧路径。

#### 新语法（有 dataBinding 时，inputs 直接使用 ParameterKey）
```json
{
  "name": "addButton",
  "events": {
    "click": ["Add"],
    "bindings": {
      "inputs": ["MathInputA", "MathInputB"],
      "output": "MathResult"
    }
  }
}
```
> 当控件自身声明了 `dataBinding.parameterKey` 时，`inputs` 中的值优先按 ParameterKey 解析。

#### 完整示例（含 dataBinding 声明）
```json
{
  "views": [
    {
      "name": "MathView",
      "controls": [
        {
          "name": "valueATextBox",
          "type": "TextBox",
          "dataBinding": {
            "parameterKey": "MathInputA",
            "mode": "TwoWay"
          }
        },
        {
          "name": "valueBTextBox",
          "type": "TextBox",
          "dataBinding": {
            "parameterKey": "MathInputB",
            "mode": "TwoWay"
          }
        },
        {
          "name": "resultTextBox",
          "type": "TextBox",
          "dataBinding": {
            "resultKey": "MathResult",
            "mode": "OneWay"
          }
        },
        {
          "name": "addButton",
          "type": "Button",
          "events": {
            "click": ["Add"],
            "bindings": {
              "inputs": ["MathInputA", "MathInputB"],
              "output": "MathResult"
            }
          }
        }
      ]
    }
  ]
}
```

### 2.4 解析规则

| 条件 | inputs 中的值解析为 | output 中的值解析为 |
|------|-------------------|-------------------|
| 无 `dataBinding` | WinForms 控件名（向后兼容） | WinForms 控件名（向后兼容） |
| 有 `dataBinding`，且值匹配某控件的 `parameterKey` | 工作流 ParameterKey → `RuntimeContext.GetParameter(key)` | — |
| 有 `dataBinding`，且值匹配某控件的 `resultKey` | — | 工作流 ResultKey → `RuntimeContext.SetResult(key, value)` |
| 有 `dataBinding`，但值不匹配任何声明 | 回退为 WinForms 控件名 | 回退为 WinForms 控件名 |

---

## 3. 改造点

### 3.1 `BindingMetadataTransformer` — 元数据解析

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

**改动**：在 `SaveControlParameters` 中，额外保存控件的 `dataBinding` 信息到 Tag。

```csharp
// 当前：只保存 inputs/output
var inputs = bindings.Inputs is { Count: > 0 } ? new List<string>(bindings.Inputs) : null;
SaveControlParameters(control, output, inputs);

// 改造后：额外保存 dataBinding 信息
var inputs = bindings.Inputs is { Count: > 0 } ? new List<string>(bindings.Inputs) : null;
SaveControlParameters(control, output, inputs, config.DataBinding);
```

### 3.2 `BuildParametersFromBindings` — 参数提取（核心改造）

```csharp
private static object?[]? BuildParametersFromBindings(Control ctrl, IControlValueAccessor? valueAccessor)
{
    if (ctrl.Tag is Dictionary<string, object> tag &&
        tag.TryGetValue("inputs", out var inputsObj) &&
        inputsObj is List<string> inputNames && inputNames.Count > 0)
    {
        var parameters = new object?[inputNames.Count];

        for (int i = 0; i < inputNames.Count; i++)
        {
            var inputName = inputNames[i];
            parameters[i] = ResolveInputValue(inputName, ctrl, tag, valueAccessor);
        }

        return parameters;
    }

    // 回退：没有 inputs 绑定信息时，传触发控件自身的值
    var value = GetControlValueStatic(ctrl, valueAccessor);
    return new object?[] { value };
}

/// <summary>
/// 解析输入值：
/// 1. 优先按 ParameterKey 从工作流上下文读取
/// 2. 回退为 WinForms 控件名从控件树读取
/// </summary>
private static object? ResolveInputValue(string inputName, Control ctrl, 
    Dictionary<string, object> tag, IControlValueAccessor? valueAccessor)
{
    // 从 Tag 中读取 dataBinding 映射表
    // dataBindingMap: { parameterKey → controlName, ... }
    if (tag.TryGetValue("dataBindingMap", out var mapObj) &&
        mapObj is Dictionary<string, string> dataBindingMap)
    {
        // 检查 inputName 是否匹配某个控件的 parameterKey
        if (dataBindingMap.TryGetValue(inputName, out var _))
        {
            // ★ 从工作流上下文读取 ParameterKey 的值
            var context = WorkflowRuntimeContext.Current;
            if (context != null)
            {
                var value = context.GetParameter(inputName);
                EventTriggerLog.Log("BuildParameters_FromWorkflow", 
                    new { parameterKey = inputName, value });
                return value?.ToString() ?? string.Empty;
            }
        }
    }

    // 回退：按 WinForms 控件名从控件树读取
    return GetControlValueByName(inputName, ctrl, valueAccessor);
}
```

### 3.3 `WriteOutputToControl` — 结果回写（核心改造）

```csharp
private static void WriteOutputToControl(Control ctrl, object? resultValue, 
    IControlValueAccessor? valueAccessor)
{
    if (ctrl.Tag is Dictionary<string, object> tag &&
        tag.TryGetValue("output", out var outputObj) &&
        outputObj is string outputName && !string.IsNullOrWhiteSpace(outputName))
    {
        var resultText = resultValue?.ToString() ?? string.Empty;

        // 从 Tag 中读取 dataBinding 映射表
        if (tag.TryGetValue("dataBindingMap", out var mapObj) &&
            mapObj is Dictionary<string, string> dataBindingMap)
        {
            // 检查 outputName 是否匹配某个控件的 resultKey
            if (dataBindingMap.TryGetValue(outputName, out var _))
            {
                // ★ 写回工作流上下文
                var context = WorkflowRuntimeContext.Current;
                if (context != null)
                {
                    context.SetResult(outputName, resultText);
                    EventTriggerLog.Log("WriteOutput_ToWorkflow", 
                        new { resultKey = outputName, value = resultText });
                }
                return;
            }
        }

        // 回退：按 WinForms 控件名写回控件树
        SetControlValue(outputName, resultText, ctrl, valueAccessor);
    }
}
```

### 3.4 `SaveControlParameters` — 保存 dataBinding 映射

```csharp
protected virtual void SaveControlParameters(object control, string? output, 
    List<string>? inputs, DataBindingConfig? dataBinding = null)
{
    if (control is not Control winFormsControl)
        return;

    var tag = winFormsControl.Tag as Dictionary<string, object>;
    if (tag == null)
    {
        tag = new Dictionary<string, object>();
        winFormsControl.Tag = tag;
    }

    if (!string.IsNullOrWhiteSpace(output))
        tag["output"] = output;

    if (inputs is { Count: > 0 })
        tag["inputs"] = inputs;

    // ★ 新增：保存 dataBinding 映射表
    // dataBindingMap: { parameterKey → controlName, resultKey → controlName }
    if (dataBinding != null)
    {
        var bindingMap = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(dataBinding.ParameterKey))
            bindingMap[dataBinding.ParameterKey] = (control as Control)?.Name ?? "";
        if (!string.IsNullOrEmpty(dataBinding.ResultKey))
            bindingMap[dataBinding.ResultKey] = (control as Control)?.Name ?? "";
        if (bindingMap.Count > 0)
            tag["dataBindingMap"] = bindingMap;
    }
}
```

### 3.5 `WorkflowRuntimeContext` — 上下文集成

```csharp
// 在 FrameworkInitializer 或 GuiModeRunner 中初始化
public class GuiModeRunner
{
    public async Task RunAsync()
    {
        // 初始化工作流运行时上下文
        var context = new WorkflowRuntimeContext();
        WorkflowRuntimeContext.Current = context;
        
        // ... 现有初始化逻辑
    }
}
```

---

## 4. 数据流图（整合后）

```
┌─────────────────────────────────────────────────────────────────────┐
│ MathFunctions.json                                                   │
│  valueATextBox.dataBinding.parameterKey = "MathInputA"               │
│  valueBTextBox.dataBinding.parameterKey = "MathInputB"               │
│  resultTextBox.dataBinding.resultKey = "MathResult"                  │
│  addButton.bindings.inputs = ["MathInputA", "MathInputB"]            │
│  addButton.bindings.output = "MathResult"                            │
└──────────────────────┬──────────────────────────────────────────────┘
                       │ 阶段1: 配置加载
                       ▼
┌──────────────────────────────────────────────┐
│ BindingMetadataTransformer                    │
│ 保存 inputs=["MathInputA","MathInputB"]       │
│ 保存 output="MathResult"                      │
│ 保存 dataBindingMap={                         │
│   "MathInputA"→"valueATextBox",               │
│   "MathInputB"→"valueBTextBox",               │
│   "MathResult"→"resultTextBox"                │
│ }                                             │
└──────────────────────┬───────────────────────┘
                       │ 阶段2: 事件绑定
                       ▼
┌──────────────────────────────────────────────┐
│ EventBinderTransformer                       │
│ addButton.Click += handler (async void)      │
└──────────────────────┬───────────────────────┘
                       │ 阶段3: 用户点击按钮
                       ▼
┌──────────────────────────────────────────────┐
│ BuildParametersFromBindings                  │
│ 1. 从 Tag 读取 inputs=["MathInputA",...]     │
│ 2. dataBindingMap 包含 "MathInputA"          │
│ 3. context.GetParameter("MathInputA") → "10" │
│ 4. dataBindingMap 包含 "MathInputB"          │
│ 5. context.GetParameter("MathInputB") → "20" │
│ 6. 返回 ["10", "20"]                         │
└──────────────────────┬───────────────────────┘
                       │ 阶段4: 函数执行
                       ▼
┌──────────────────────────────────────────────┐
│ EventExecutionService.ExecuteFunctionAsync    │
│ 1. 获取 Add(double, double) 委托             │
│ 2. TypeConverter: "10"→10.0, "20"→20.0      │
│ 3. DynamicInvoke(10.0, 20.0)                 │
│ 4. 返回 30.0                                 │
└──────────────────────┬───────────────────────┘
                       │ 阶段5: 结果回写
                       ▼
┌──────────────────────────────────────────────┐
│ WriteOutputToControl                         │
│ 1. 从 Tag 读取 output="MathResult"           │
│ 2. dataBindingMap 包含 "MathResult"          │
│ 3. context.SetResult("MathResult", "30")     │
│ 4. 同时更新 UI: resultTextBox.Text = "30"    │
└──────────────────────┬───────────────────────┘
                       │ 阶段6: UI 同步（双向绑定）
                       ▼
┌──────────────────────────────────────────────┐
│ ControlDataBindingService                    │
│ 当用户修改 valueATextBox 的值时：             │
│ 1. TextChanged 事件触发                       │
│ 2. 查找 dataBinding: parameterKey="MathInputA"│
│ 3. context.SetParameter("MathInputA", newVal) │
└──────────────────────────────────────────────┘
```

---

## 5. 控件 ↔ ParameterKey 双向绑定

### 5.1 `dataBinding` 配置节

```json
{
  "name": "valueATextBox",
  "type": "TextBox",
  "dataBinding": {
    "parameterKey": "MathInputA",
    "mode": "TwoWay"
  }
}
```

### 5.2 绑定模式

| 模式 | 说明 | 方向 |
|------|------|------|
| `TwoWay` | 控件值变化 ↔ 工作流参数同步更新 | 双向 |
| `OneWayToControl` | 工作流参数变化 → 更新控件值 | 单向（控件只读） |
| `OneWayToWorkflow` | 控件值变化 → 更新工作流参数 | 单向（控件只写） |
| `OneTime` | 初始化时从工作流读取一次 | 一次性 |

### 5.3 运行时同步机制

```csharp
/// <summary>
/// 控件数据绑定服务 — 维护控件与工作流参数的同步
/// </summary>
public class ControlDataBindingService : IDisposable
{
    private readonly Dictionary<string, ControlBinding> _bindings = new();
    private readonly WorkflowRuntimeContext _context;
    private readonly IControlValueAccessor _valueAccessor;

    /// <summary>
    /// 注册控件的数据绑定
    /// </summary>
    public void RegisterBinding(string controlName, Control control, DataBindingConfig binding)
    {
        var controlBinding = new ControlBinding
        {
            Control = control,
            Config = binding,
            ChangeHandler = binding.Mode is BindingMode.TwoWay or BindingMode.OneWayToWorkflow
                ? CreateChangeHandler(control, binding)
                : null
        };

        _bindings[controlName] = controlBinding;

        // 初始化：从工作流读取值更新控件
        if (binding.Mode is BindingMode.TwoWay or BindingMode.OneWayToControl or BindingMode.OneTime)
        {
            SyncControlFromContext(control, binding);
        }
    }

    /// <summary>
    /// 当工作流参数变化时，同步更新所有绑定的控件
    /// </summary>
    public void OnParameterChanged(string parameterKey)
    {
        foreach (var (controlName, binding) in _bindings)
        {
            if (binding.Config.ParameterKey == parameterKey &&
                binding.Config.Mode is BindingMode.TwoWay or BindingMode.OneWayToControl)
            {
                SyncControlFromContext(binding.Control, binding.Config);
            }
        }
    }

    private void SyncControlFromContext(Control control, DataBindingConfig binding)
    {
        if (!string.IsNullOrEmpty(binding.ParameterKey))
        {
            var value = _context.GetParameter(binding.ParameterKey);
            _valueAccessor.SetValue(control, value?.ToString() ?? string.Empty);
        }
        if (!string.IsNullOrEmpty(binding.ResultKey))
        {
            var value = _context.GetResult(binding.ResultKey);
            _valueAccessor.SetValue(control, value?.ToString() ?? string.Empty);
        }
    }

    /// <summary>
    /// 创建控件值变化事件处理器
    /// </summary>
    private EventHandler? CreateChangeHandler(Control control, DataBindingConfig binding)
    {
        if (control is TextBox textBox)
        {
            EventHandler handler = null!;
            handler = (sender, args) =>
            {
                if (!string.IsNullOrEmpty(binding.ParameterKey))
                {
                    _context.SetParameter(binding.ParameterKey, textBox.Text);
                }
            };
            textBox.TextChanged += handler;
            return handler;
        }
        // 可扩展其他控件类型...
        return null;
    }
}
```

---

## 6. 向后兼容策略

### 6.1 兼容性规则

| 条件 | 行为 | 兼容性 |
|------|------|--------|
| 无 `dataBinding` | `inputs/output` 中的值视为 WinForms 控件名 | ✅ 完全兼容 |
| 有 `dataBinding`，值匹配 `parameterKey` | 从工作流上下文读取 | ✅ 新功能 |
| 有 `dataBinding`，值匹配 `resultKey` | 写回工作流上下文 | ✅ 新功能 |
| 有 `dataBinding`，值不匹配任何声明 | 回退为 WinForms 控件名 | ✅ 自动降级 |
| 无 `WorkflowRuntimeContext` | 回退为 WinForms 控件名 | ✅ 自动降级 |

### 6.2 检测逻辑

```csharp
/// <summary>
/// 判断值是否为工作流 ParameterKey（在 dataBindingMap 中有对应条目）
/// </summary>
private static bool IsWorkflowParameterKey(string value, Dictionary<string, object> tag)
{
    return tag.TryGetValue("dataBindingMap", out var mapObj) &&
           mapObj is Dictionary<string, string> dataBindingMap &&
           dataBindingMap.ContainsKey(value);
}
```

---

## 7. 改造清单

| 文件 | 改动内容 | 优先级 |
|------|---------|--------|
| `EventBindingTransformers.cs` | 新增 `ResolveInputValue()`、`GetControlValueByName()`、`SetControlValue()` | P0 |
| `EventBindingTransformers.cs` | 改造 `BuildParametersFromBindings()` 调用 `ResolveInputValue()` | P0 |
| `EventBindingTransformers.cs` | 改造 `WriteOutputToControl()` 支持 dataBinding 检测 | P0 |
| `EventBindingTransformers.cs` | 改造 `SaveControlParameters()` 保存 dataBindingMap | P0 |
| `EventBindingTransformers.cs` | 新增 `ControlDataBindingService` 类 | P1 |
| `ContainerBinderPipeline.cs` | 新增阶段：控件 dataBinding 注册 | P1 |
| `GuiModeRunner.cs` | 初始化 `WorkflowRuntimeContext` | P1 |
| 文档 | 更新 JSON 配置示例 | P2 |

---

## 8. 预期效果

1. **UI 成为工作流的前端视图层**：按钮点击直接读写工作流上下文
2. **消除参数映射重复**：UI 管线复用工作流的 `KeyProcessor` 和 `TypeConverter`
3. **工作流可视化调试**：通过 UI 手动输入参数、触发函数、查看结果
4. **渐进式迁移**：旧配置无需修改，新功能使用 `dataBinding` 声明
5. **统一数据流**：所有数据传递经过 `WorkflowRuntimeContext`，便于追踪和审计
6. **零新语法**：`inputs` 直接使用工作流的 `ParameterKeys` 语法，不创造新的包装格式
