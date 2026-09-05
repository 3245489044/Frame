# EventTrigger → WorkFlowMethod 参数传递错误分析

## 错误现象

日志文件 `docs\Document\log` 第 160-172 行记录了两个关键错误：

```
[DEBUG] [EventTrigger] [eventtrigger:EventBinder_TriggerEvent] {"eventName":"textchanged","functionName":"SetValue","controlName":"valueATextBox"}
[DEBUG] [EventTrigger] [eventtrigger:EventBinder_ExecuteFailed] {"controlName":"valueATextBox","eventName":"textchanged","functionName":"SetValue","error":"Object of type 'WorkFlowMethod.Orchestration.Context.Models.WorkflowContext' cannot be converted to type 'System.String'."}

[DEBUG] [EventTrigger] [eventtrigger:EventBinder_TriggerEvent] {"eventName":"click","functionName":"Add","controlName":"addButton"}
[DEBUG] [EventTrigger] [eventtrigger:EventBinder_ExecuteFailed] {"controlName":"addButton","eventName":"click","functionName":"Add","error":"Parameter count mismatch."}
```

---

## 错误原因分析

### 错误1：类型转换错误（SetValue）

**错误信息**：`WorkflowContext` 无法转换为 `System.String`

**根本原因**：基础函数被错误地当作工作流函数处理

**调用链**：

```
1. EventBindingTransformers.cs:429
   BuildParametersFromBindings(ctrl, valueAccessor)
   → 返回: Dictionary<string, object> { "valueATextBox": "用户输入值" }

2. WorkflowMethodExecutionService.cs:59-70
   ExecuteFunctionAsync(functionName, namedParameters)
   → 创建 WfContext(parameters: namedParameters)
   → 调用 WorkFlowMethodUsage.CallBridgedFunctionAsync(functionName, wfContext)

3. WorkFlowMethodUsage.cs:323-327
   CallBridgedFunctionAsync(name, context)
   → 检查 _workflowRegistry.HasFunction(name) → 返回 true
   → 调用 _workflowRegistry.CallAsync(name, context)
   → 将 WfContext 对象作为唯一参数传入

4. DefaultWorkflowRegistry.cs:54-65
   CallAsync(name, params object?[] args)
   → args = [WfContext]
   → config.Invoke([WfContext])

5. UIToolbox.cs:110-115
   SetValue 注册为 Func<string, string>
   → 期望: string 参数
   → 实际: WfContext 对象
   → 类型转换失败！
```

### 错误2：参数数量不匹配（Add）

**错误信息**：`Parameter count mismatch`

**根本原因**：参数名称不匹配，基础函数路径无法正确解析参数

**调用链**：

```
1. EventBindingTransformers.cs:429
   BuildParametersFromBindings(ctrl, valueAccessor)
   → 返回: Dictionary<string, object> { "valueATextBox": "1", "valueBTextBox": "2" }

2. WorkflowMethodExecutionService.cs:59-70
   → 创建 WfContext(parameters: namedParameters)

3. WorkFlowMethodUsage.cs:323-329
   CallBridgedFunctionAsync(name, context)
   → HasFunction 返回 true（工作流注册表也能找到 Add）
   → 走工作流函数路径，传入 WfContext

4. ResolveParameters.cs:346-358
   → 尝试从 WfContext.Parameters 提取参数
   → 参数名是 "valueATextBox", "valueBTextBox"
   → 函数参数名是 "a", "b"
   → 名称不匹配，解析失败
   → 参数数量为 0，与期望的 2 个参数不匹配！
```

---

## 核心问题定位

### 问题1：注册表混淆

基础函数（`Add`, `SetValue` 等）和工作流函数共用同一注册表：

```csharp
// FunctionStorageLifecycle.cs:367-370
WorkFlowMethodUsage.DefaultBasicRegistryProvider = () => _registry;
```

当 `WorkFlowMethodUsage.EnsureInitialized()` 被调用时，工作流注册表使用了与 FunctionStorage 相同的底层注册表实例。这导致：

- `HasFunction("Add")` 返回 true（因为 Add 注册在 FunctionStorage）
- `CallBridgedFunctionAsync` 错误地将基础函数当作工作流函数处理

### 问题2：参数格式不匹配

`BuildParametersFromBindings` 返回的字典键是**控件名称**：

```csharp
// EventBindingTransformers.cs:489-491
for (int i = 0; i < inputNames.Count; i++)
{
    var inputName = inputNames[i];  // "valueATextBox", "valueBTextBox"
    parameters[inputName] = inputValue;
}
```

而 `Add` 函数的参数名是**形参名称**：

```csharp
// MathLibrary.cs:33
public static double Add(double a, double b) => a + b;
```

当 `ParameterBuilder` 尝试按名称匹配时，`"valueATextBox"` ≠ `"a"`，匹配失败。

---

## 涉及文件清单

| 文件 | 角色 | 关键代码行 |
|------|------|-----------|
| [EventBindingTransformers.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs) | 参数构建与事件绑定 | 第 429-438 行：调用 BuildParametersFromBindings 和 ExecuteFunctionAsync |
| [WorkflowMethodExecutionService.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs) | IEventExecutionService 实现 | 第 59-70 行：创建 WfContext 并调用 CallBridgedFunctionAsync |
| [WorkFlowMethodUsage.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs) | 工作流方法统一调用入口 | 第 323-344 行：CallBridgedFunctionAsync 逻辑 |
| [DefaultWorkflowRegistry.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/WorkFlowMethod/FunctionExecutionFramework/DefaultWorkflowRegistry.cs) | 工作流注册表 | 第 54-65 行：CallAsync 方法 |
| [FunctionStorageRegistry.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs) | 函数注册中心 | 第 367-370 行：设置 DefaultBasicRegistryProvider |
| [MathLibrary.cs](file:///d:/Project/Program/WorkFlowPrj/FileDriven/FileDriven.Business/Registrations/MathLibrary.cs) | 数学函数注册 | 第 25-28 行：注册 Add/Subtract/Multiply/Divide |
| [UIToolbox.cs](file:///d:/Project/Program/WorkFlowPrj/FileDriven/FileDriven.Business/Registrations/UIToolbox.cs) | UI 工具函数注册 | 第 110-115 行：注册 SetValue |
| [ArgumentBuilder.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/BackEndDriven/Invoke/ArgumentBuilder.cs) | 参数构建器 | 第 137-161 行：BuildFromDelegate 方法 |
| [ParameterBuilder.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Share/ParameterResolver/Builder/ParameterBuilder.cs) | 参数匹配器 | 第 64-105 行：MatchParameters 方法 |
| [MathFunctions.json](file:///d:/Project/Program/WorkFlowPrj/FileDriven/DrivenConfig/JsonConfig/MathFunctions.json) | UI 配置文件 | 第 36-61, 76-124 行：事件绑定配置 |

---

## 数据流图

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        EventBindingTransformers.cs                         │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ BuildParametersFromBindings(ctrl, valueAccessor)                     │ │
│  │  - 从 ctrl.Tag 读取 inputs 列表                                     │ │
│  │  - 遍历 inputNames，从对应控件读取值                                │ │
│  │  - 返回: Dictionary<string, object>                                │ │
│  │      { "valueATextBox": "1", "valueBTextBox": "2" }               │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
│                              │                                            │
│                              ▼                                            │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ executionService.ExecuteFunctionAsync(functionName, parameters)     │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                    WorkflowMethodExecutionService.cs                       │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ ExecuteFunctionAsync(functionName, namedParameters)                 │ │
│  │  - WfContext(parameters: namedParameters)                          │ │
│  │  - WorkFlowMethodUsage.CallBridgedFunctionAsync(name, wfContext)   │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        WorkFlowMethodUsage.cs                              │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ CallBridgedFunctionAsync(name, context)                             │ │
│  │  - if (_workflowRegistry.HasFunction(name))                         │ │
│  │      → _workflowRegistry.CallAsync(name, context)  ← 错误路径！     │ │
│  │  - else                                                             │ │
│  │      → _functionExecutor.ExecuteAsync(name, parameters, execContext) │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼ (错误路径)
┌─────────────────────────────────────────────────────────────────────────────┐
│                     DefaultWorkflowRegistry.cs                             │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ CallAsync(name, params object?[] args)                             │ │
│  │  - args = [WfContext]                                              │ │
│  │  - config.Invoke([WfContext])                                      │ │
│  │  - 但函数期望: Add(double a, double b) 或 SetValue(string value)   │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                     ┌─────────────────┐
                     │ 类型转换失败！   │
                     │ 参数数量不匹配！ │
                     └─────────────────┘
```

---

## 问题根因总结

### 架构层面问题

1. **注册表共享导致函数类型混淆**：FunctionStorage 的基础函数和 WorkFlowMethod 的工作流函数共用同一注册表实例，`HasFunction()` 无法区分两者。

2. **参数格式不兼容**：UI 事件绑定使用控件名作为参数键（如 `"valueATextBox"`），而基础函数使用形参名（如 `"a"`），导致名称匹配失败。

3. **执行路径选择错误**：`CallBridgedFunctionAsync` 在 `HasFunction()` 返回 true 时，无条件走工作流函数路径，传入 `WfContext` 对象，而非按函数签名传递实际参数值。

### 代码层面问题

```csharp
// WorkFlowMethodUsage.cs:323-327 - 问题代码
if (_workflowRegistry!.HasFunction(name))
    return await _workflowRegistry.CallAsync(name, context);  // ← 错误：传入 WfContext

// 正确逻辑应该是：
// 1. 检查函数是否为工作流函数（接受 WfContext 参数）
// 2. 如果是基础函数，提取 WfContext.Parameters 中的值按位置传递
// 3. 如果是工作流函数，传递 WfContext
```

---

## 已完成的修复（2026-07-04）

### 修复1：`WorkflowMethodExecutionService.cs` — 直接使用 FunctionExecutor

**修改内容**：
- 移除 `WfContext` 创建和 `CallBridgedFunctionAsync` 调用
- 改为直接使用 `WorkFlowMethodUsage.Executor.ExecuteAsync(functionName, parameters, execContext)`
- 参数数组版本直接传递 `object?[]?` 参数
- 参数字典版本按位置索引排序后转换为 `object?[]` 数组

**修改前**：
```csharp
var wfContext = new WfContext(parameters: namedParameters);
var result = await WorkFlowMethodUsage.CallBridgedFunctionAsync(functionName, wfContext);
```

**修改后**：
```csharp
var parameters = namedParameters
    .OrderBy(kvp => int.TryParse(kvp.Key, out var idx) ? idx : int.MaxValue)
    .Select(kvp => kvp.Value)
    .ToArray();
var execContext = new BmExecutionContext { FunctionName = functionName };
var execResult = await WorkFlowMethodUsage.Executor.ExecuteAsync(functionName, parameters, execContext);
return execResult.IsSuccess
    ? ExecutionResult.Ok(execResult.Value)
    : ExecutionResult.Fail(execResult.ErrorMessage ?? "Unknown error");
```

### 修复2：`WorkflowMethodUseage.cs` — 移除 HasFunction 分支

**修改内容**：
- `CallBridgedFunction` 和 `CallBridgedFunctionAsync` 中移除 `HasFunction` 分支判断
- 始终从 `WfContext.Parameters` 提取参数值通过 `FunctionExecutor` 执行
- 异步版本增加按位置索引排序逻辑

**修改前**：
```csharp
if (_workflowRegistry!.HasFunction(name))
    return await _workflowRegistry.CallAsync(name, context);
// else: 走 FunctionExecutor 路径
```

**修改后**：
```csharp
// 始终走 FunctionExecutor 路径
var parameters = context.Parameters
    .OrderBy(kvp => int.TryParse(kvp.Key, out var idx) ? idx : int.MaxValue)
    .Select(kvp => kvp.Value)
    .ToArray();
var result = await _functionExecutor!.ExecuteAsync(name, parameters, execContext);
```

### 修复原理

| 问题 | 修复前 | 修复后 |
|------|--------|--------|
| 注册表混淆 | `HasFunction` 返回 true，走错路径 | 移除 `HasFunction` 分支，统一走 `FunctionExecutor` |
| 参数名不匹配 | 传入 `WfContext` 对象 | 传入参数值数组，`ArgumentBuilder` 按位置匹配 |
| 类型转换 | `WfContext` → `string` 失败 | 参数值已经是正确类型，`TypeConverter` 处理转换 |

### 编译验证

```
dotnet build WorkFlowMethod.csproj → 成功（0 警告，0 错误）
```
