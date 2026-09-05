# 参数契约实施计划 — 基于 CSG P 维度解决 MathFunctions 参数传递问题

> **关联文档**：
> - `docs/analysis/参数传递路径分析与CSG参数契约设计.md` — CSG 参数契约标准（P 维度）
> - `docs/analysis/2026/7.4/MathFunctions_DataFlow_Analysis.md` — MathFunctions 数据流分析
> - `CsharpPrj/Protocol/CSG/CSG.Unified/Contracts/IParameterContract.cs` — 参数契约数据模型
> - `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventExecutionService.cs` — 事件执行服务接口
> - `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` — IEventExecutionService 实现
> - `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs` — 事件绑定转换器
> - `CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs` — EventConfig / EventBinding 数据模型
> - `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json` — MathFunctions UI 配置
>
> **日期**：2026-07-04 | **状态**：✅ 已实施 | **版本**：v1.1
>
> **实施记录**：
> - 2026-07-04 完成 4 步重构，整个解决方案构建通过，零错误
> - 详见下方 [6. 实施记录](#6-实施记录)

---

## 目录

1. [问题回顾](#1-问题回顾)
2. [解决方案：CSG 参数契约（P 维度）](#2-解决方案csg-参数契约p-维度)
3. [实施步骤](#3-实施步骤)
4. [涉及文件清单](#4-涉及文件清单)
5. [风险与注意事项](#5-风险与注意事项)
6. [实施记录](#6-实施记录)

---

## 1. 问题回顾

### 1.1 当前参数传递的三个核心问题

#### 问题 1：控件名 ≠ 形参名，参数匹配靠"双键 Hack"

`BuildParametersFromBindings()`（EventBindingTransformers.cs:476-508）返回的 Dictionary 同时包含两个键：

```csharp
parameters[inputName] = inputVal;       // 按控件名（如 "valueATextBox"）
parameters[i.ToString()] = inputVal;    // 按位置索引（如 "0"）
```

**根源**：JSON 配置中 `bindings.inputs` 存的是**控件名**（`"valueATextBox"`），而 C# 函数 `Add(double a, double b)` 的形参名是 `a`、`b`。两者不一致，所以用位置索引 `"0"`、`"1"` 作为"桥梁"来匹配。

**风险**：位置索引是脆弱的——如果 inputs 顺序改变或函数形参顺序改变，静默传错值。

#### 问题 2：参数绑定信息存储在 Control.Tag 中，缺乏结构化

```csharp
tag["output"] = output;    // "resultTextBox"
tag["inputs"] = inputs;    // ["valueATextBox", "valueBTextBox"]
```

**问题**：Tag 是 `Dictionary<string, object>`，没有类型约束，没有校验，运行时才能发现错误。

#### 问题 3：外壳层与内核层的参数格式不统一

| 层 | 参数格式 | 说明 |
|----|---------|------|
| 外壳层（EventBinderTransformer） | `Dictionary<string, object>` | 键=控件名/位置索引 |
| 桥接层（IEventExecutionService） | `Dictionary<string, object>` | 同上 |
| 内核层（FunctionExecutor） | `object?[]` 位置参数 | 按形参位置传参 |

**问题**：Dictionary 到 object[] 的转换依赖 `ArgumentBuilder` 的位置索引匹配，参数名信息在转换中丢失。

### 1.2 当前 JSON 配置格式

```json
{
  "type": "button",
  "name": "addButton",
  "events": {
    "click": "Add",
    "bindings": {
      "inputs": ["valueATextBox", "valueBTextBox"],
      "output": "resultTextBox"
    }
  }
}
```

`bindings.inputs` 只存了控件名列表，没有声明"控件名 → 形参名"的映射关系。

---

## 2. 解决方案：CSG 参数契约（P 维度）

### 2.1 核心思想

引入 CSG 第四维度 **P（Parameter）**，定义三层数据模型：

```
ParameterDeclaration  — 编译时：函数形参定义（函数期望接收什么）
ParameterBinding      — 配置时：参数值来源声明（数据如何流入函数）
ParameterValue        — 运行时：具体的参数值（实际传递的数据）
```

### 2.2 数据模型（已实现）

`CsharpPrj/Protocol/CSG/CSG.Unified/Contracts/IParameterContract.cs` 中已定义：

```csharp
public record ParameterDeclaration
{
    public required string Name { get; init; }     // 形参名
    public string Type { get; init; } = "any";     // 参数类型
    public bool Required { get; init; } = true;    // 是否必需
    public object? Default { get; init; }           // 默认值
    public string Description { get; init; } = "";  // 参数说明
}

public record ParameterBinding
{
    public required string Target { get; init; }    // 目标形参名
    public required string Source { get; init; }    // 源标识（控件名/上下文键名/字面值）
    public string SourceType { get; init; } = "direct";  // control/context/direct/literal
    public object? Default { get; init; }           // 回退值
    public bool Required { get; init; } = true;     // 是否必需
}

public record ParameterValue
{
    public required string Name { get; init; }      // 形参名
    public object? Value { get; init; }              // 参数值
    public string Type { get; init; } = "any";       // 值类型
}
```

### 2.3 解决方案总览

```
当前架构：
  JSON inputs=["valueATextBox","valueBTextBox"]
    → Tag["inputs"] = ["valueATextBox","valueBTextBox"]
    → BuildParametersFromBindings() → Dictionary{"valueATextBox":"3", "0":"3", ...}
    → IEventExecutionService.ExecuteFunctionAsync("Add", dict)
    → 内核层按位置索引 "0"/"1" 匹配形参

引入参数契约后：
  JSON parameters=[{target:"a", source:"valueATextBox", sourceType:"control"},
                   {target:"b", source:"valueBTextBox", sourceType:"control"}]
    → ParameterBinding[]（结构化绑定声明）
    → ParameterResolver.Resolve() → ParameterValue[]{ {name:"a", value:3}, {name:"b", value:5} }
    → IEventExecutionService.ExecuteFunctionAsync("Add", parameterValues)
    → 内核层按形参名精确匹配
```

---

## 3. 实施步骤

### 阶段一：JSON 配置升级（MathFunctions.json）

#### 3.1.1 改动内容

将 `bindings.inputs` 数组改为 `bindings.parameters` 数组（`ParameterBinding[]` 格式）：

**当前**：
```json
"bindings": {
  "inputs": ["valueATextBox", "valueBTextBox"],
  "output": "resultTextBox"
}
```

**改为**：
```json
"bindings": {
  "parameters": [
    { "target": "a", "source": "valueATextBox", "sourceType": "control" },
    { "target": "b", "source": "valueBTextBox", "sourceType": "control" }
  ],
  "output": "resultTextBox"
}
```

#### 3.1.2 涉及控件

| 控件 | 当前 inputs | 改为 parameters |
|------|------------|----------------|
| addButton | `["valueATextBox", "valueBTextBox"]` | `[{target:"a", source:"valueATextBox"}, {target:"b", source:"valueBTextBox"}]` |
| subtractButton | `["valueATextBox", "valueBTextBox"]` | `[{target:"a", source:"valueATextBox"}, {target:"b", source:"valueBTextBox"}]` |
| multiplyButton | `["valueATextBox", "valueBTextBox"]` | `[{target:"a", source:"valueATextBox"}, {target:"b", source:"valueBTextBox"}]` |
| divideButton | `["valueATextBox", "valueBTextBox"]` | `[{target:"a", source:"valueATextBox"}, {target:"b", source:"valueBTextBox"}]` |
| valueATextBox | `["valueATextBox"]` | `[{target:"value", source:"valueATextBox"}]` |
| valueBTextBox | `["valueBTextBox"]` | `[{target:"value", source:"valueBTextBox"}]` |

> **注意**：valueATextBox/valueBTextBox 的 textchanged 事件绑定的是 `SetValue` 函数，该函数只有一个参数，形参名可能是 `value`。需要确认 `SetValue` 的形参名。

#### 3.1.3 文件

- `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json`

---

### 阶段二：EventBinding 数据模型升级

#### 3.2.1 EventBinding 类增加 Parameters 属性

**文件**：`CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs`

在 `EventBinding` 类中增加 `Parameters` 属性：

```csharp
public class EventBinding
{
    // 保留旧属性（阶段五移除）
    public List<string>? Inputs { get; set; }

    // ★ 2026-07-04 新增：参数契约绑定声明
    public List<ParameterBinding>? Parameters { get; set; }

    public string? Output { get; set; }
    public Dictionary<string, string>? ParameterMapping { get; set; }
    public Dictionary<string, string>? OutputMapping { get; set; }
}
```

> **注意**：`ParameterBinding` 是 `CSG.Unified` 命名空间中的 record，需要在 `EventConfig.cs` 中引用 `CSG.Unified` 或使用 JSON 反序列化的方式处理。

#### 3.2.2 EventConfigConverter 增加 parameters 解析

在 `EventConfigConverter.Read()` 的 switch 中增加 `"parameters"` 分支：

```csharp
case "parameters":
    config.Bindings ??= new EventBinding();
    config.Bindings.Parameters = JsonSerializer.Deserialize<List<ParameterBinding>>(ref reader, options);
    break;
```

---

### 阶段三：外壳层改造（EventBindingTransformers.cs）

#### 3.3.1 BindingMetadataTransformer — 存储 ParameterBinding[]

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

**改动 1**：`BindingMetadataTransformer.Transform()` 中，从 `bindings.Parameters` 读取绑定信息，存储到 Tag：

```csharp
// 当前（第128行）：
var inputs = bindings.Inputs is { Count: > 0 } ? new List<string>(bindings.Inputs) : null;
SaveControlParameters(control, output, inputs);

// 改为：
var parameterBindings = bindings.Parameters is { Count: > 0 } ? new List<ParameterBinding>(bindings.Parameters) : null;
SaveControlParameters(control, output, parameterBindings);
```

**改动 2**：`SaveControlParameters()` 签名和实现：

```csharp
// 当前：
protected virtual void SaveControlParameters(object control, string? output, List<string>? inputs)

// 改为：
protected virtual void SaveControlParameters(object control, string? output, List<ParameterBinding>? parameterBindings)
{
    // ...
    if (parameterBindings is { Count: > 0 })
        tag["parameterBindings"] = parameterBindings;
}
```

#### 3.3.2 BuildParametersFromBindings — 使用 ParameterBinding 解析

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

**改动**：从 Tag 读取 `parameterBindings` 而非 `inputs`，按 `ParameterBinding.Target` 作为参数名：

```csharp
private static Dictionary<string, object> BuildParametersFromBindings(Control ctrl, IControlValueAccessor? valueAccessor)
{
    var parameters = new Dictionary<string, object>();

    // 从 Tag 中读取 parameterBindings 绑定信息
    if (ctrl.Tag is Dictionary<string, object> tag &&
        tag.TryGetValue("parameterBindings", out var bindingsObj) &&
        bindingsObj is List<ParameterBinding> parameterBindings &&
        parameterBindings.Count > 0)
    {
        var form = ctrl.FindForm();

        for (int i = 0; i < parameterBindings.Count; i++)
        {
            var binding = parameterBindings[i];
            object? inputValue = null;

            if (binding.SourceType == "control" && form != null)
            {
                var inputControl = WinFormsControlService.Instance.FindByName(form, binding.Source);
                if (inputControl != null)
                {
                    inputValue = valueAccessor != null
                        ? valueAccessor.GetValue(inputControl)
                        : (inputControl is Control ic ? ic.Text : null);
                }
            }

            var inputVal = inputValue?.ToString() ?? string.Empty;
            parameters[binding.Target] = inputVal;  // 按形参名（如 "a", "b"）
        }

        return parameters;
    }

    // 回退：没有 parameterBindings 时，尝试旧格式 inputs（向后兼容）
    // ★ 阶段五移除
    // ...（保留旧逻辑）
}
```

> **关键变化**：`parameters[binding.Target]` 使用形参名（如 `"a"`、`"b"`）作为键，不再使用控件名或位置索引。

---

### 阶段四：桥接层改造（IEventExecutionService + WorkflowMethodExecutionService）

#### 3.4.1 IEventExecutionService 增加 ParameterValue[] 重载

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventExecutionService.cs`

```csharp
/// <summary>
/// 执行指定函数（参数契约版本）
/// <para>★ 2026-07-04 新增：支持 ParameterValue[] 命名参数，</para>
/// <para>   与 CSG 参数契约（P 维度）兼容。</para>
/// </summary>
Task<ExecutionResult> ExecuteFunctionAsync(string functionName, ParameterValue[] parameters);
```

#### 3.4.2 WorkflowMethodExecutionService 实现 ParameterValue[] 重载

**文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs`

```csharp
public async Task<ExecutionResult> ExecuteFunctionAsync(string functionName, ParameterValue[] parameters)
{
    try
    {
        WorkFlowMethodUsage.EnsureInitialized();

        var execContext = new BmExecutionContext
        {
            FunctionName = functionName
        };

        // ParameterValue[] → Dictionary<string, object>
        var namedParameters = new Dictionary<string, object>();
        foreach (var p in parameters)
        {
            namedParameters[p.Name] = p.Value!;
        }

        var result = await WorkFlowMethodUsage.Executor.ExecuteAsync(functionName, namedParameters, execContext);

        return result.IsSuccess
            ? ExecutionResult.Ok(result.Value)
            : ExecutionResult.Fail(result.ErrorMessage ?? "Unknown error");
    }
    catch (Exception ex)
    {
        return ExecutionResult.Fail(ex.Message);
    }
}
```

#### 3.4.3 CreateEventHandler 使用 ParameterValue[] 重载

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

在 `CreateEventHandler` 中，将 `BuildParametersFromBindings` 返回的 Dictionary 转换为 `ParameterValue[]`，调用新的重载：

```csharp
// 当前（第442行）：
var execResult = await executionService.ExecuteFunctionAsync(functionName, parameters);

// 改为：
var parameterValues = parameters.Select(kvp => new ParameterValue
{
    Name = kvp.Key,
    Value = kvp.Value
}).ToArray();
var execResult = await executionService.ExecuteFunctionAsync(functionName, parameterValues);
```

---

### 阶段五：清理（移除双键 Hack 和旧格式兼容代码）

#### 3.5.1 移除 BuildParametersFromBindings 中的双键逻辑

```csharp
// 移除这两行：
parameters[inputName] = inputVal;       // 按控件名
parameters[i.ToString()] = inputVal;    // 按位置索引
```

#### 3.5.2 移除旧格式 inputs 的回退逻辑

当确认所有 JSON 配置都已升级到 `parameters` 格式后，移除 `BuildParametersFromBindings` 中读取 `inputs` 的回退分支。

#### 3.5.3 移除 EventBinding.Inputs 属性（可选）

如果不再需要向后兼容，可以从 `EventBinding` 类中移除 `Inputs` 属性。

---

## 4. 涉及文件清单

### 4.1 需修改的文件

| 文件 | 阶段 | 改动内容 |
|------|------|---------|
| `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json` | 一 | `bindings.inputs` → `bindings.parameters` |
| `CsharpPrj/Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs` | 二 | EventBinding 增加 `Parameters` 属性；EventConfigConverter 增加解析 |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | 三 | BindingMetadataTransformer 存储 ParameterBinding[]；BuildParametersFromBindings 按 Target 匹配 |
| `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventExecutionService.cs` | 四 | 增加 `ExecuteFunctionAsync(string, ParameterValue[])` 重载 |
| `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 四 | 实现 ParameterValue[] 重载 |

### 4.2 需引用的命名空间

| 文件 | 需添加的 using |
|------|---------------|
| `EventConfig.cs` | `using CSG.Unified;` |
| `EventBindingTransformers.cs` | `using CSG.Unified;` |
| `IEventExecutionService.cs` | `using CSG.Unified;` |
| `WorkflowMethodExecutionService.cs` | `using CSG.Unified;` |

### 4.3 项目依赖关系

```
CSG.Unified（定义 ParameterDeclaration / ParameterBinding / ParameterValue）
  ↑ 被引用
EventBindingOrchestrator.Abstractions（IEventExecutionService）
  ↑ 被引用
EventTriggerFramework（EventBindingTransformers）
  ↑ 被引用
WorkFlowMethod（WorkflowMethodExecutionService）
```

> **注意**：`CSG.Unified` 是协议层项目，不依赖任何 Operation/Share 项目。其他项目通过 ProjectReference 引用 `CSG.Unified`。

---

## 5. 风险与注意事项

### 5.1 向后兼容

- **阶段三**中保留旧格式 `inputs` 的回退逻辑，确保未升级的 JSON 配置仍能工作
- **阶段五**在所有配置升级完成后，再移除旧格式兼容代码

### 5.2 函数形参名确认

需要确认以下函数的形参名，确保 `ParameterBinding.Target` 与 C# 形参名一致：

| 函数名 | 形参签名 | 确认状态 |
|--------|---------|---------|
| `Add` | `(double a, double b)` | ✅ `a`, `b` |
| `Subtract` | `(double a, double b)` | ✅ `a`, `b` |
| `Multiply` | `(double a, double b)` | ✅ `a`, `b` |
| `Divide` | `(double a, double b)` | ✅ `a`, `b` |
| `SetValue` | `(double value)` | ⚠️ 需确认形参名 |

### 5.3 其他 JSON 配置文件

如果还有其他 JSON 配置文件使用了 `bindings.inputs` 格式，需要一并升级。搜索命令：

```bash
grep -r '"inputs"' FileDriven/DrivenConfig/ --include="*.json"
```

### 5.4 测试验证

实施后需要验证的场景：

1. **正常流程**：输入数值 → 点击按钮 → 正确计算结果并显示
2. **参数名匹配**：验证 `ParameterBinding.Target` 与函数形参名一致
3. **错误处理**：参数缺失时给出明确错误信息
4. **旧格式兼容**：未升级的 JSON 配置仍能工作（阶段五前）

---

## 6. 实施记录

### 6.1 实际实施步骤（2026-07-04）

本次实施聚焦于**内核层**的 4 步重构，解决了参数名信息在注册→执行链路中丢失的核心问题。

#### 步骤 1：FunctionDescriptor 新增 FromContract 工厂方法

**文件**：`CsharpPrj/Plate/TConfig/TrConfig/IFunctionDescriptor.cs`

**改动**：新增两个 `FromContract` 重载：
- `FromContract(Delegate, IReadOnlyList<ParameterDeclaration>, string?)` — 从 CSG 参数契约创建
- `FromContract(Delegate, IReadOnlyList<IParamConfig>, string?)` — 从 IParamConfig 列表创建

**原本代码路径**：`FromDelegate` 从 `Delegate.Method.GetParameters()` 提取参数名。对于 `Func<string, string>` 这样的包装委托，`GetParameters()` 返回的是 `(string arg)`，丢失了原始形参名（如 `a`、`b`）。

**新代码路径**：`FromContract` 使用传入的 `IParamConfig[]` 中的 `Name` 作为参数名，覆盖从 `MethodInfo` 提取的参数名。

```csharp
// 关键代码（IFunctionDescriptor.cs:248-277）：
public static FunctionDescriptor FromContract(
    Delegate function,
    IReadOnlyList<IParamConfig> parameters,
    string? description = null)
{
    var paramDescriptors = parameters
        .Select(p => new ParameterDescriptor
        {
            Name = p.Name,       // ← 使用传入的真实参数名
            Type = p.Type,
            IsOptional = p.IsOptional,
            DefaultValue = p.DefaultValue
        })
        .ToArray();

    return new FunctionDescriptor
    {
        Parameters = paramDescriptors,  // ← 参数名存入配置
        Delegate = function
    };
}
```

#### 步骤 2：TrConfigRegistry / FunctionRegistry 新增参数名感知的注册重载

**文件**：
- `CsharpPrj/Plate/TConfig/TrConfig/ITrConfig.cs` — 接口新增重载
- `CsharpPrj/Plate/TConfig/TrConfig/TrConfigRegistry.cs` — 实现
- `CsharpPrj/Modules/Registry/06_ConcreteRegistries.cs` — FunctionRegistry 实现

**改动**：新增 `Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)` 重载。

**原本代码路径**：`Register(name, function, description)` 内部调用 `TrConfig.FromDelegate(function)`，参数名来自 `MethodInfo.GetParameters()`。

**新代码路径**：`Register(name, function, parameters, description)` 内部调用 `FunctionDescriptor.FromContract(function, parameters)`，参数名来自传入的 `IParamConfig[]`。

```csharp
// TrConfigRegistry.cs:71-85
public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
{
    var config = FunctionDescriptor.FromContract(function, parameters, description);
    config.Name = name;
    _configs[name] = config;
    _delegates[name] = function;
}
```

#### 步骤 3：AutoRegisterEngine 传递参数名信息

**文件**：`CsharpPrj/Modules/Registry/AutoRegister/AutoRegisterEngine.cs`

**改动**：`RegisterResultToRegistry` 方法中，当 `item.Parameters` 包含参数名信息时，调用带参数名的注册重载。

**原本代码路径**：始终调用 `registry.Register(name, wrappedDelegate, description)`，参数名信息丢失。

**新代码路径**：检查 `item.Parameters`，有值时转换为 `IParamConfig[]` 并传入注册重载。

```csharp
// AutoRegisterEngine.cs:157-174
if (item.Parameters is { Length: > 0 })
{
    var paramConfigs = item.Parameters.Select(p => new ParamConfig
    {
        Name = p.Name,       // ← 保留原始参数名
        Type = p.Type,
        IsOptional = p.IsOptional,
        DefaultValue = p.DefaultValue
    }).ToArray();

    registry.Register(item.RegisteredName, wrappedDelegate, paramConfigs, item.Description);
}
else
{
    registry.Register(item.RegisteredName, wrappedDelegate, item.Description);
}
```

**参数名来源**：`item.Parameters` 来自 `AutoRegisterService` 的 `ExtractedMethodInfo`，它通过反射从原始 C# 方法的 `MethodInfo.GetParameters()` 提取参数名。即使经过 `MathWrapper` 包装为 `Func<string, string>`，参数名信息在 `ExtractedMethodInfo` 阶段已经提取并缓存，不会丢失。

#### 步骤 4：ParameterTransformer.FillFromDict 增加位置索引回退

**文件**：`CsharpPrj/Modules/ExecutionFramework/Engine/FunctionExecutor.cs`

**改动**：`FillFromDict` 方法增加按参数名匹配的逻辑，失败时按位置索引回退。

**原本代码路径**：`FillFromDict` 只按位置索引 `"0"`、`"1"` 匹配，参数名信息被忽略。

**新代码路径**：优先按参数名匹配，失败时按位置索引回退。

```csharp
// FunctionExecutor.cs:308-329
private object[] FillFromDict((object? parameters, object[] args) tuple)
{
    var dict = (Dictionary<string, object>)tuple.parameters!;
    for (int i = 0; i < _count; i++)
    {
        // ★ 优先按参数名匹配
        if (dict.TryGetValue(_paramNames[i], out var value) && value != null)
        {
            tuple.args[i] = _convert(i, value);
        }
        // ★ 失败时按位置索引回退
        else if (dict.TryGetValue(i.ToString(), out var fallbackValue) && fallbackValue != null)
        {
            tuple.args[i] = _convert(i, fallbackValue);
        }
        else
        {
            tuple.args[i] = _getDefault(i);
        }
    }
    return tuple.args;
}
```

### 6.2 完整调用链路

```
注册阶段（参数名信息如何存储）：
  MathFunctionRegistrar.RegisterAll()
    → AutoRegisterEngine.ProcessConfig()
      → AutoRegisterService.ExtractMethods()
        → 从 MethodInfo.GetParameters() 提取参数名 → 存入 ExtractedMethodInfo
      → AutoRegisterEngine.RegisterResultToRegistry()
        → 检查 item.Parameters 是否有值
          → 有值：registry.Register(name, delegate, paramConfigs, desc)
            → FunctionDescriptor.FromContract(delegate, paramConfigs)
              → Parameters = [{Name:"a", Type:double}, {Name:"b", Type:double}]
          → 无值：registry.Register(name, delegate, desc)（旧路径，参数名来自 MethodInfo）

执行阶段（参数名信息如何匹配）：
  FunctionExecutor.ExecuteAsync("Add", Dictionary{"a":"3", "b":"5"})
    → ExecuteWithTimeoutAsync()
      → _parameterProcessor.ProcessAsync()  // Dictionary 类型被跳过，原样传递
      → executeFunc(processedParams)
        → ParameterTransformer.ForConfig(config).Transform(dict)
          → BuildDispatcher() → FillFromDict()
            → dict.TryGetValue("a") → "3"  ✓ 按参数名匹配
            → dict.TryGetValue("b") → "5"  ✓ 按参数名匹配
      → InvokeDelegateAsync(config, ["3", "5"])
        → config.Invoke(["3", "5"]) → Add(3, 5) → 8
```

### 6.3 额外修复

| 文件 | 修复内容 |
|------|---------|
| `Modules/Registry/Registry.csproj` | 添加 `CSG.Unified` 项目引用 |
| `WorkFlowMethod/FunctionExecutionFramework/DefaultWorkflowRegistry.cs` | 实现新增的 `Register(..., IReadOnlyList<IParamConfig>, ...)` 接口方法 |
| `FunctionStorage/Framework/FunctionStorageAdapter.cs` | 实现新增的接口方法，转发到 `FunctionStorageRegistry` |
| `FunctionStorage/Framework/FunctionStorageRegistry.cs` | 新增 `Register(..., IReadOnlyList<IParamConfig>, ...)` 重载 |

### 6.4 构建验证

```
dotnet build MainProj.sln → 成功，零错误（52 个项目全部通过）
```

### 6.5 后续待实施（阶段一~五）

以下阶段涉及 UI 层和桥接层改造，需在后续迭代中完成：

| 阶段 | 内容 | 文件 |
|------|------|------|
| 一 | JSON 配置升级 | `MathFunctions.json` |
| 二 | EventBinding 数据模型升级 | `EventConfig.cs` |
| 三 | 外壳层改造 | `EventBindingTransformers.cs` |
| 四 | 桥接层改造 | `IEventExecutionService.cs`, `WorkflowMethodExecutionService.cs` |
| 五 | 清理旧格式兼容代码 | 多文件 |

---

## 附录：实施顺序建议

```
步骤 1（阶段二）→ EventBinding 增加 Parameters 属性
步骤 2（阶段一）→ MathFunctions.json 升级
步骤 3（阶段三）→ BindingMetadataTransformer 改造
步骤 4（阶段三）→ BuildParametersFromBindings 改造
步骤 5（阶段四）→ IEventExecutionService 增加重载
步骤 6（阶段四）→ WorkflowMethodExecutionService 实现重载
步骤 7（阶段四）→ CreateEventHandler 使用新重载
步骤 8（阶段五）→ 清理旧格式兼容代码
```

> **建议**：按步骤顺序实施，每完成一步后编译验证，确保不破坏现有功能。
