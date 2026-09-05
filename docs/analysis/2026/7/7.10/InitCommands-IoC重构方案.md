# InitCommands IoC 重构方案

> **问题：** `AgentConfigUI.json` 的工作流列表（`funcListView`）加载不出来。
> **根因：** `ExecuteInitCommandsAsFunctionsAsync` 执行 `GetWorkflowNames` 后，返回值被丢弃，`FillWorkflowListView` 拿不到数据。
> **方案：** 基于控制反转（IoC）重构，让 WorkFlowMethod 管线全权处理参数传递。

---

## 一、问题分析

### 1.1 当前调用链

```
AgentConfigUI.json
  initCommandMode: "function"
  initCommands: ["GetWorkflowNames"]   ← 只配置了 GetWorkflowNames

ContainerBinderPipeline.ExecuteInitCommandsAsync()
  → ExecuteInitCommandsAsFunctionsAsync(commands, functionExecutor)
    → functionExecutor("GetWorkflowNames")
      → eventExecService.ExecuteAsync("GetWorkflowNames", null, null)
        → WorkflowMethodExecutionService.ExecuteFunctionAsync("GetWorkflowNames", null)
          → IFunctionRegistry.TryGetConfig → 找到函数配置
          → ParameterBuilder.Build → 无参数
          → NodeExecutor.ExecuteNodeAsync → 执行
          → 返回 ExecutionResult.Success(["workflow1", "workflow2", ...])
    → result.Success == true → executedCount++
    → result.Data 被丢弃！ ← 问题在这里
```

### 1.2 根因

1. **`FunctionExecutor` 委托签名限制：** `Func<string, Task<OperationResult<object>>>` 只接受函数名，不支持参数传递
2. **返回值被丢弃：** `ExecuteInitCommandsAsFunctionsAsync` 只检查 `result.Success`，不保存 `result.Data`
3. **配置不完整：** `initCommands` 只配置了 `GetWorkflowNames`，没有配置 `FillWorkflowListView`
4. **即使配置了 `FillWorkflowListView`，当前架构也无法传递参数**

### 1.3 底层能力已具备

`IEventExecutionService`（继承自 `IFunctionExecutor`）的 `ExecuteAsync` 签名：

```csharp
Task<ExecutionResult> ExecuteAsync(
    string functionName,
    object?[]? parameters,    // ← 支持参数传递
    ExecutionContext? context = null);
```

`WorkflowMethodExecutionService.ExecuteFunctionAsync` 内部已实现：
- `ParameterBuilder.Build` — 参数匹配和类型转换
- `NodeExecutor.ExecuteNodeAsync` — 完整节点执行管线
- `TryWriteWorkflowResult` — 结果自动回写 UI 控件

**中间层（`FunctionExecutor` 委托）把参数能力丢掉了。**

---

## 二、重构方案：IoC 改造

### 2.1 核心思路

| 当前（紧耦合） | 重构后（IoC） |
|---|---|
| `ContainerBinderPipeline` → `FunctionExecutor` 委托（无参数） → `IEventExecutionService` | `ContainerBinderPipeline` → `IFunctionExecutor`（完整参数能力） → `WorkflowMethodExecutionService` |
| 参数传递由管线层手动编排 | 参数传递由 WorkFlowMethod 管线全权处理 |
| 返回值由管线层手动保存和传递 | 返回值由 `IFunctionExecutor` 返回，管线层只负责按序调用 |

### 2.2 架构变化

```
┌─────────────────────────────────────────────────────────────┐
│                     AgentConfigUI.json                       │
│  initCommands: ["GetWorkflowNames",                          │
│    "{\"function\":\"FillWorkflowListView\",\"args\":[...]}"] │
└──────────────────────┬──────────────────────────────────────┘
                       │ 加载
                       ▼
┌─────────────────────────────────────────────────────────────┐
│              ContainerBinderPipeline                         │
│  ExecuteInitCommandsAsFunctionsAsync(commands, executor)     │
│    → 解析命令格式（纯字符串 / JSON 对象）                     │
│    → 解析 @引用（前一个函数的返回值）                          │
│    → executor.ExecuteAsync(name, args, null)                 │
│    → 保存结果到 results 字典                                  │
└──────────────────────┬──────────────────────────────────────┘
                       │ IFunctionExecutor
                       ▼
┌─────────────────────────────────────────────────────────────┐
│           WorkflowMethodExecutionService                     │
│  ExecuteFunctionAsync(functionName, parameters)              │
│    → ParameterBuilder.Build → 参数匹配 + 类型转换             │
│    → NodeExecutor.ExecuteNodeAsync → 执行                    │
│    → TryWriteWorkflowResult → 结果回写 UI                    │
│    → 返回 ExecutionResult                                    │
└─────────────────────────────────────────────────────────────┘
```

---

## 三、需要修改的文件

### 文件 1：`IContainerBinderContext.cs`

**路径：** `CsharpPrj/Share/EventBindingOrchestrator/Contexts/IContainerBinderContext.cs`

**改动：** `FunctionExecutor` 从委托改为 `IFunctionExecutor` 接口

```csharp
// 删除（第 116-120 行）：
/// <summary>
/// 函数执行委托（用于 InitCommands 的 function 模式）
/// <para>★ 2026-07-09 新增：P3 重构，InitCommandMode="function" 时通过此委托直接调用函数，</para>
/// <para>   跳过命令解析管线。</para>
/// <para>签名与 CommandExecutor 一致：接收函数名，返回 OperationResult<object>。</para>
/// </summary>
Func<string, Task<OperationResult<object>>>? FunctionExecutor { get; set; }

// 改为：
/// <summary>
/// 函数执行器（IoC 注入）
/// <para>★ 2026-07-09 重构：从委托改为 IFunctionExecutor 接口，</para>
/// <para>   复用 WorkFlowMethod 的完整参数管线。</para>
/// <para>通过 DI 容器注入，由 FrameworkInitializer 在初始化时注册。</para>
/// <para>调用方通过 executor.ExecuteAsync(name, args, null) 执行函数，</para>
/// <para>参数传递和类型转换由 WorkFlowMethod 的 ParameterBuilder 全权处理。</para>
/// </summary>
IFunctionExecutor? FunctionExecutor { get; set; }
```

### 文件 2：`ContainerBinderContext.cs`

**路径：** `CsharpPrj/Share/EventBindingOrchestrator/Contexts/ContainerBinderContext.cs`

**改动：** 同步更新 `FunctionExecutor` 属性的类型

```csharp
// 删除：
public Func<string, Task<OperationResult<object>>>? FunctionExecutor
{
    get => GetData<Func<string, Task<OperationResult<object>>>>(nameof(FunctionExecutor));
    set => SetData(value!, nameof(FunctionExecutor));
}

// 改为：
public IFunctionExecutor? FunctionExecutor
{
    get => GetData<IFunctionExecutor>(nameof(FunctionExecutor));
    set => SetData(value!, nameof(FunctionExecutor));
}
```

### 文件 3：`CreateAndConfigureBlock.cs`

**路径：** `FileDriven/FileDriven.Execution/Gui/Blocks/CreateAndConfigureBlock.cs`

**改动：** 直接注入 `IFunctionExecutor`，移除手动委托包装

```csharp
// 当前（第 149-179 行）：
// ★ 2026-07-09 新增：注入 FunctionExecutor（用于 InitCommandMode="function" 模式）
//   独立于 CommandExecutor，通过 IEventExecutionService 直接调用函数
//   避免反向引用 Operation 层的 WorkFlowMethod/FunctionStorageRegistry
var eventExecService = AppServiceProvider.Resolve<IEventExecutionService>();
if (eventExecService != null)
{
    mainForm.FunctionExecutor = async functionName =>
    {
        try
        {
            // 通过 IEventExecutionService 直接调用函数，跳过命令解析管线
            var result = await eventExecService.ExecuteAsync(functionName, null, null);
            return OperationResult<object>.SuccessResult(
                result.Value,
                $"函数执行成功: {functionName}");
        }
        catch (Exception ex)
        {
            return OperationResult<object>.FailureResult(
                $"函数执行失败: {ex.Message}",
                ErrorCode.UnknownError,
                ex);
        }
    };
}
else
{
    ExecutionLog.Log(
        "CreateAndConfigureBlock_functionExecutor_null",
        new { message = "IEventExecutionService 未注册，FunctionExecutor 未注入" });
}

// 改为：
// ★ 2026-07-09 重构：直接注入 IFunctionExecutor，移除手动委托包装
//   IFunctionExecutor 由 DI 容器管理，WorkflowMethodExecutionService 已实现此接口
var functionExecutor = AppServiceProvider.Resolve<IFunctionExecutor>();
if (functionExecutor != null)
{
    mainForm.FunctionExecutor = functionExecutor;
    ExecutionLog.Log(
        "CreateAndConfigureBlock_functionExecutor_injected",
        new { message = "IFunctionExecutor 已注入到 MainForm" });
}
else
{
    ExecutionLog.Log(
        "CreateAndConfigureBlock_functionExecutor_null",
        new { message = "IFunctionExecutor 未注册，FunctionExecutor 未注入" });
}
```

### 文件 4：`ContainerBinderPipeline.cs`

**路径：** `CsharpPrj/Share/EventBindingOrchestrator/ContainerBinderPipeline.cs`

**改动 4a：** `ExecuteInitCommandsAsFunctionsAsync` 签名改为 `IFunctionExecutor`

```csharp
// 当前（第 435-437 行）：
private static async Task<bool> ExecuteInitCommandsAsFunctionsAsync(
    List<string> commands,
    Func<string, Task<OperationResult<object>>> functionExecutor)

// 改为：
private static async Task<bool> ExecuteInitCommandsAsFunctionsAsync(
    List<string> commands,
    IFunctionExecutor functionExecutor)
```

**改动 4b：** `ExecuteInitCommandsAsFunctionsAsync` 内部实现

```csharp
private static async Task<bool> ExecuteInitCommandsAsFunctionsAsync(
    List<string> commands,
    IFunctionExecutor functionExecutor)
{
    var executedCount = 0;
    // ★ 新增：存储每个函数的执行结果，供后续函数通过 @引用访问
    var results = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    foreach (var cmd in commands)
    {
        if (string.IsNullOrWhiteSpace(cmd)) continue;

        try
        {
            // ★ 新增：解析命令格式（纯字符串 / JSON 对象）
            var (functionName, rawArgs) = ParseInitCommand(cmd);

            // ★ 新增：解析参数中的 @引用
            var resolvedArgs = ResolveArgReferences(rawArgs, results);

            // ★ 使用 IFunctionExecutor.ExecuteAsync，支持参数传递
            var executionResult = await functionExecutor.ExecuteAsync(
                functionName, resolvedArgs, null);

            if (executionResult.Success)
            {
                executedCount++;
                // ★ 新增：保存返回值供后续函数引用
                results[functionName] = executionResult.Value;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ContainerBinderPipeline] 函数执行失败: {functionName}, 错误: {executionResult.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ContainerBinderPipeline] 函数执行异常: {cmd}, 错误: {ex.Message}");
        }
    }

    return executedCount > 0;
}
```

**改动 4c：** 新增 `ParseInitCommand` 辅助方法

```csharp
/// <summary>
/// 解析初始化命令条目
/// <para>★ 2026-07-09 新增：支持两种格式：</para>
/// <para>  1. 纯字符串 "FunctionName" — 无参数</para>
/// <para>  2. JSON 对象 "{\"function\":\"Fn\",\"args\":[\"@prev\"]}" — 带参数</para>
/// </summary>
private static (string functionName, object?[]? args) ParseInitCommand(string cmd)
{
    cmd = cmd.Trim();
    
    if (cmd.StartsWith('{') && cmd.EndsWith('}'))
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(cmd);
            var root = doc.RootElement;
            
            var funcName = root.TryGetProperty("function", out var funcProp)
                ? funcProp.GetString() ?? ""
                : "";
            
            if (string.IsNullOrWhiteSpace(funcName))
                return (cmd, null);  // 解析失败，回退到纯字符串模式
            
            object?[]? args = null;
            if (root.TryGetProperty("args", out var argsProp))
            {
                var argList = new List<object?>();
                foreach (var arg in argsProp.EnumerateArray())
                {
                    argList.Add(arg.ValueKind == System.Text.Json.JsonValueKind.String
                        ? arg.GetString()
                        : arg.GetRawText());
                }
                args = argList.ToArray();
            }
            
            return (funcName, args);
        }
        catch
        {
            // JSON 解析失败，回退到纯字符串模式
            return (cmd, null);
        }
    }
    
    // 纯字符串格式
    return (cmd, null);
}
```

**改动 4d：** 新增 `ResolveArgReferences` 辅助方法

```csharp
/// <summary>
/// 解析参数中的 @引用
/// <para>★ 2026-07-09 新增：支持 @FunctionName 语法引用前一个函数的返回值。</para>
/// <para>例如 args=["@GetWorkflowNames"] 表示引用 GetWorkflowNames 的返回值。</para>
/// </summary>
private static object?[]? ResolveArgReferences(
    object?[]? args,
    Dictionary<string, object?> results)
{
    if (args == null || args.Length == 0)
        return args;

    var resolved = new object?[args.Length];
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] is string str && str.StartsWith('@'))
        {
            var refName = str.Substring(1);
            if (results.TryGetValue(refName, out var refValue))
            {
                resolved[i] = refValue;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ContainerBinderPipeline] @引用未找到: {refName}，使用原始值");
                resolved[i] = args[i];
            }
        }
        else
        {
            resolved[i] = args[i];
        }
    }
    return resolved;
}
```

### 文件 5：`AgentConfigUI.json`

**路径：** `FileDriven/DrivenConfig/JsonConfig/AgentConfigUI.json`

**改动：** `initCommands` 增加 `FillWorkflowListView`，使用 `@GetWorkflowNames` 引用前一个函数的返回值

```json
{
  "initCommandMode": "function",
  "initCommands": [
    "GetWorkflowNames",
    "{\"function\":\"FillWorkflowListView\",\"args\":[\"@GetWorkflowNames\"]}"
  ]
}
```

---

## 四、完整调用链（重构后）

```
AgentConfigUI.json
  initCommands: [
    "GetWorkflowNames",
    "{\"function\":\"FillWorkflowListView\",\"args\":[\"@GetWorkflowNames\"]}"
  ]

ContainerBinderPipeline.ExecuteInitCommandsAsync()
  → 检测 InitCommandMode == "function"
  → ExecuteInitCommandsAsFunctionsAsync(commands, context.FunctionExecutor)
    → 迭代 commands:
    
    → 第1条: "GetWorkflowNames"
      → ParseInitCommand → ("GetWorkflowNames", null)
      → ResolveArgReferences → null（无参数）
      → functionExecutor.ExecuteAsync("GetWorkflowNames", null, null)
        → WorkflowMethodExecutionService.ExecuteFunctionAsync("GetWorkflowNames", null)
          → IFunctionRegistry.TryGetConfig → 找到函数配置
          → ParameterBuilder.Build → 无参数
          → NodeExecutor.ExecuteNodeAsync → 执行
          → 返回 ExecutionResult.Success(["workflow1", "workflow2", ...])
      → executionResult.Success == true
      → results["GetWorkflowNames"] = ["workflow1", "workflow2", ...]
      → executedCount++
    
    → 第2条: "{\"function\":\"FillWorkflowListView\",\"args\":[\"@GetWorkflowNames\"]}"
      → ParseInitCommand → ("FillWorkflowListView", ["@GetWorkflowNames"])
      → ResolveArgReferences → results["GetWorkflowNames"] → ["workflow1", ...]
      → functionExecutor.ExecuteAsync("FillWorkflowListView", ["workflow1", ...], null)
        → WorkflowMethodExecutionService.ExecuteFunctionAsync("FillWorkflowListView", ["workflow1", ...])
          → IFunctionRegistry.TryGetConfig → 找到函数配置
          → ParameterBuilder.Build → 匹配参数
          → NodeExecutor.ExecuteNodeAsync → 执行
          → TryWriteWorkflowResult → 结果回写 funcListView
          → 返回 ExecutionResult.Success
      → executionResult.Success == true
      → results["FillWorkflowListView"] = ...
      → executedCount++
```

---

## 五、不变的文件

| 文件 | 原因 |
|------|------|
| `WorkflowMethodExecutionService.cs` | 已实现 `IFunctionExecutor`，无需改动 |
| `UIConfiguration.cs` | `InitCommands` 保持 `List<string>`，解析在管线层 |
| `IFunctionExecutor` 接口 | 无需改动，`ExecuteAsync(string, object?[], ExecutionContext)` 已支持参数 |
| `IEventExecutionService` 接口 | 继承自 `IFunctionExecutor`，无需改动 |
| `MainForm.cs` | `FunctionExecutor` 属性类型改为 `IFunctionExecutor`，但由 DI 注入，MainForm 不直接使用 |

---

## 六、为什么这是 IoC

| 原则 | 体现 |
|------|------|
| **控制反转** | `ContainerBinderPipeline` 不再控制"如何执行函数"（不包装委托），通过 `IFunctionExecutor` 接口将控制权交给 WorkFlowMethod 管线 |
| **依赖注入** | `IFunctionExecutor` 由 DI 容器管理，`CreateAndConfigureBlock` 通过 `AppServiceProvider.Resolve` 获取 |
| **接口隔离** | `ContainerBinderPipeline` 只依赖 `IFunctionExecutor` 接口，不依赖具体实现 |
| **复用现有管线** | WorkFlowMethod 的 `ParameterBuilder`、`NodeExecutor`、`TryWriteWorkflowResult` 全部自动生效 |

---

## 七、风险与回退

| 风险 | 缓解措施 |
|------|---------|
| `IFunctionExecutor` 在 DI 容器中未注册 | `CreateAndConfigureBlock` 有 null 检查，未注册时输出日志，不崩溃 |
| JSON 格式的 initCommands 解析失败 | `ParseInitCommand` 有 try-catch，解析失败回退到纯字符串模式 |
| `@引用` 的函数名不存在 | `ResolveArgReferences` 有 null 检查，未找到时使用原始值 |
| 现有纯字符串格式的 initCommands 不兼容 | `ParseInitCommand` 对非 JSON 字符串直接返回纯字符串模式，完全向后兼容 |
