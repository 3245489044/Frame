# FileDriven.Execution 与 FunctionStorage 深度整合方案

> 生成时间: 2026-06-12
> 分析范围: FileDriven.Execution + Operation/FunctionStorage
> 文档状态: 待实施

---

## 目录

1. [背景与目标](#1-背景与目标)
2. [当前架构概览](#2-当前架构概览)
3. [整合点分析](#3-整合点分析)
4. [实施步骤](#4-实施步骤)
5. [实施顺序与依赖关系](#5-实施顺序与依赖关系)
6. [风险与注意事项](#6-风险与注意事项)

---

## 1. 背景与目标

### 1.1 背景

`FileDriven.Execution` 是 FileDriven 五层架构的统一执行门面，`FunctionStorage` 是函数注册/执行中心。两者已通过项目引用建立基本连接，但在非事件命令注册、函数执行路径、工作流注册验证等方面存在重复设计和不一致。

### 1.2 目标

1. **消除双轨制**：统一非事件命令注册表，消除 `Action<object?>` 与 `Action<IExecutionContext>` 两套签名
2. **简化调用链**：统一函数执行路径，消除多层委托和 `object?` 类型转换
3. **增强健壮性**：工作流注册时验证步骤函数存在性，提前发现配置错误
4. **清理冗余**：移除 UIToolbox 中的标记函数，消除"假注册"
5. **统一规范**：统一日志消息 ID 命名规范

---

## 2. 当前架构概览

### 2.1 项目引用关系

```
FileDriven.Execution.csproj
  └── ProjectReference → Operation\FunctionStorage\FunctionStorage.csproj
```

### 2.2 初始化阶段的整合

```
FileDrivenExecutor.InitializeFramework() 中：
  1. MathLibrary.RegisterAll()          → 注册数学函数到 FunctionStorageRegistry
  2. UIToolbox.RegisterAll()            → 注册 UI 操作函数到 FunctionStorageRegistry
  3. BuiltInUnits.RegisterAll(...)      → 注册类型转换单元
  4. PipelineFactory.RegisterBuiltInCommands() → 注册非事件命令到 Execution 的注册表
```

### 2.3 管线执行阶段的整合

```
ExecutionPipelineEntry.RunPipeline()
  → PipelineFactory.BuildCommandPipeline(bindingResolver, functionExecutor)
    → CommandFrameworkUsage.CreateDefaultPipeline()
      → 内部通过 IFunctionExecutor 查找函数并执行
```

### 2.4 工作流执行阶段的整合

```
WorkflowExecutionLayer.ExecuteWorkflowAsync()
  → FunctionStorageRegistry.ExecuteAsync(step.FuncName, stepArgs)
    → Executor.ExecuteAsync()
      → Share.Registry.FunctionRegistry
```

---

## 3. 整合点分析

### 3.1 整合点一：非事件命令注册表双轨制（P0）

**问题**：当前存在两套独立的非事件命令注册表。

| 属性 | Execution 注册表 | FunctionStorage 注册表 |
|------|-----------------|----------------------|
| **文件** | `Command/NonEventCommandRegistry.cs` | `Core/NonEventCommandRegistry.cs` |
| **Handler 签名** | `Action<object?>` | `Action<IExecutionContext>` |
| **单例方式** | `Instance` 属性（Lazy 单例） | 通过 `FunctionStorageRegistry.NonEventRegistry` 获取 |
| **命令列表** | `NonEventCommands.All`（FrozenSet） | `NonEventCommands.AllCommands`（IReadOnlyCollection） |
| **注册时机** | `PipelineFactory.RegisterBuiltInCommands()` | `FunctionStorageRegistry.InitializeNonEventRegistry()` |

**影响**：
- 两套注册表互不知晓对方的存在
- Execution 注册了 13 个业务命令（load/close/show/look/discover/list/validate/exit/sideload）
- FunctionStorage 注册了 4 个纯逻辑命令（echo/sleep/wait/assert）+ 9 个占位命令
- 占位命令被 Execution 的真实实现覆盖，但占位注册本身是冗余操作

### 3.2 整合点二：函数执行路径多层委托（P1）

**问题**：函数执行调用链过长，`PipelineEntry.FunctionExecutor` 类型为 `object?`。

```
FunctionStorageRegistry.ExecuteAsync()
  → Executor.ExecuteAsync()
    → Share.Registry.FunctionRegistry.TryGetDescriptor()
      → DynamicInvoke()
```

**影响**：
- `PipelineEntry.FunctionExecutor` 属性类型为 `object?`，需要在运行时强制转换
- 调用链每层都增加调试和排查难度

### 3.3 整合点三：工作流注册缺少步骤验证（P2）

**问题**：`WorkflowLibrary.RegisterWorkflow()` 注册工作流时，未验证步骤中的 `funcName` 是否已在 `FunctionStorageRegistry` 中注册。

**影响**：
- 工作流 JSON 配置错误（步骤函数名拼写错误）只能在运行时发现
- 缺少提前的配置验证机制

### 3.4 整合点四：UIToolbox 标记函数冗余（P2）

**问题**：`UIToolbox.RegisterAll()` 注册了 19 个函数，其中 12 个是标记函数（仅用于 `Contains()` 检查，实际逻辑在别处）。

**影响**：
- `FunctionStorageRegistry` 中注册了 12 个"假"函数
- 标记函数被调用时返回占位文本而非实际执行逻辑
- 新增非事件命令时需要同时在两处注册

### 3.5 整合点五：日志体系统一（P3）

**问题**：两个模块使用不同的日志前缀和消息 ID 命名规范。

| 模块 | 日志类 | 前缀 |
|------|--------|------|
| FunctionStorage | `FsLogPlaceholderHandler` | `fs:` |
| FileDriven.Execution | `FileDrivenLogPlaceholderHandler` | `exec:` |

---

## 4. 实施步骤

### 步骤 1：非事件命令注册表统一（P0）

#### 1.1 修改 Execution 的 `NonEventCommandRegistry` 为适配器

**文件**：`FileDriven.Execution/Command/NonEventCommandRegistry.cs`

将内部 `ConcurrentDictionary` 替换为委托给 `FunctionStorageRegistry.NonEventRegistry`：

```csharp
public class NonEventCommandRegistry : INonEventCommandRegistry
{
    private static readonly Lazy<NonEventCommandRegistry> _instance = 
        new(() => new NonEventCommandRegistry());
    
    public static NonEventCommandRegistry Instance => _instance.Value;
    
    // 获取 FunctionStorage 的注册表实例
    private FunctionStorage.Abstractions.INonEventCommandRegistry InnerRegistry 
        => FunctionStorageRegistry.NonEventRegistry;
    
    public void Register(string commandName, Action<object?> handler,
        CommandCriticality criticality = CommandCriticality.Important,
        string? description = null)
    {
        // 适配：将 Action<object?> 包装为 Action<IExecutionContext>
        InnerRegistry.Register(commandName, ctx => handler(ctx), 
            (FunctionStorage.Abstractions.CommandCriticality)(int)criticality, 
            description);
        
        SyncToCommandParser(commandName);
        SyncToNonEventCommands(commandName);
    }
    
    public bool TryGetHandler(string commandName, out Action<object?>? handler)
    {
        if (InnerRegistry.TryGetHandler(commandName, out var fsHandler))
        {
            handler = ctx => fsHandler(ctx as FunctionStorage.Abstractions.IExecutionContext 
                ?? new FunctionExecutionContextAdapter(ctx));
            return true;
        }
        handler = null;
        return false;
    }
    
    // 其他方法类似适配...
}
```

#### 1.2 消除 FunctionStorage 中的占位命令注册

**文件**：`Operation/FunctionStorage/FunctionStorageRegistry.cs`

`InitializeNonEventRegistry()` 不再注册占位命令：

```csharp
private static void InitializeNonEventRegistry()
{
    lock (_lock)
    {
        if (_nonEventRegistry != null) return;
        
        var registry = new CoreNonEventCommandRegistry();
        var handler = new CoreNonEventCommandHandler();
        
        // 仅注册纯逻辑命令（echo, sleep, wait, assert）
        Core.DefaultCommandStrategies.RegisterAllTo(registry);
        
        // ★ 移除：不再注册占位命令
        // load, close, show, look, discover, list, validate, exit, sideload
        // 这些命令由 FileDriven.Execution 在初始化时统一注册
        
        _nonEventRegistry = registry;
        _nonEventHandler = handler;
    }
}
```

#### 1.3 统一 Execution 的 `NonEventCommandHandler.Handle()` 分发入口

**文件**：`FileDriven.Execution/Command/Skeletons/NonEventCommandHandler.cs`

`Handle()` 方法同时检查两套注册表：

```csharp
public static void Handle(FileDrivenPipelineContext context)
{
    var action = context.ParsedAction?.ToLowerInvariant()
        ?? context.ParsedVerb.ToString().ToLowerInvariant();
    
    if (string.IsNullOrEmpty(action) || action == "do")
    {
        context.FunctionExecuteSuccess = false;
        context.FunctionErrorMessage = $"Non-event command action is required";
        return;
    }
    
    try
    {
        // 先查 Execution 注册表（业务命令）
        if (NonEventCommandRegistry.Instance.TryGetHandler(action, out var execHandler))
        {
            execHandler?.Invoke(context);
            return;
        }
        
        // 再查 FunctionStorage 注册表（纯逻辑命令）
        var fsRegistry = FunctionStorageRegistry.NonEventRegistry;
        if (fsRegistry.TryGetHandler(action, out var fsHandler))
        {
            var fsContext = new FunctionExecutionContextAdapter(context);
            fsHandler(fsContext);
            context.FunctionResultValue = fsContext.FunctionResultValue;
            context.FunctionExecuteSuccess = fsContext.FunctionExecuteSuccess;
            context.FunctionErrorMessage = fsContext.FunctionErrorMessage;
            return;
        }
        
        context.FunctionExecuteSuccess = false;
        context.FunctionErrorMessage = $"Unknown non-event command: {action}";
    }
    catch (Exception ex) { ... }
}
```

#### 1.4 消除 `CommandCriticality` 枚举重复定义

**文件**：`FileDriven.Execution/Command/NonEventCommandRegistry.cs`

```csharp
// 删除独立定义：
// public enum CommandCriticality { Critical, Important, Optional }

// 改为 using 别名：
using CommandCriticality = FunctionStorage.Abstractions.CommandCriticality;
```

#### 1.5 消除 `NonEventCommandValidationResult` 重复定义

**文件**：`FileDriven.Execution/Command/NonEventCommandRegistry.cs`

```csharp
// 删除整个 NonEventCommandValidationResult 类定义
// 改为 using 别名：
using NonEventCommandValidationResult = FunctionStorage.Abstractions.NonEventCommandValidationResult;
```

#### 1.6 验证清单

- [ ] `NonEventCommandRegistry.Instance.Register()` 注册的命令可通过 `FunctionStorageRegistry.NonEventRegistry.TryGetHandler()` 查到
- [ ] `FunctionStorageRegistry.NonEventRegistry.Register()` 注册的命令可通过 `NonEventCommandRegistry.Instance.TryGetHandler()` 查到
- [ ] `NonEventCommandHandler.Handle()` 能正确分发纯逻辑命令和业务命令
- [ ] `PipelineFactory.RegisterBuiltInCommands()` 注册后，`FunctionStorageRegistry.ValidateNonEventCommands()` 返回 `IsValid = true`
- [ ] 所有现有测试通过

---

### 步骤 2：函数执行路径统一（P1）

#### 2.1 `FunctionStorageRegistry` 实现 `IFunctionExecutor` 接口

**文件**：`Operation/FunctionStorage/FunctionStorageRegistry.cs`

```csharp
using FunctionStorage.Abstractions;

public static class FunctionStorageRegistry : IFunctionExecutor
{
    Task<IFunctionExecutionResult> IFunctionExecutor.ExecuteAsync(string functionName, object? parameters)
        => ExecuteAsync(functionName, parameters);
    
    Task<IFunctionExecutionResult> IFunctionExecutor.ExecuteAsync(Delegate function, object? parameters)
        => ExecuteAsync(function, parameters);
    
    Task<IFunctionExecutionResult> IFunctionExecutor.ExecuteAsync(IFunctionUnit unit, object? parameters)
        => Executor.ExecuteAsync(unit, parameters);
    
    Task<IFunctionExecutionResult[]> IFunctionExecutor.ExecuteBatchAsync(params string[] functionNames)
        => Executor.ExecuteBatchAsync(functionNames);
}
```

#### 2.2 `PipelineEntry.FunctionExecutor` 改为强类型

**文件**：`FileDriven.Pipeline/PipelineEntry.cs`

```csharp
// 修改前：
public object? FunctionExecutor { get; set; }

// 修改后：
public FunctionStorage.Abstractions.IFunctionExecutor? FunctionExecutor { get; set; }
```

#### 2.3 `PipelineFactory.BuildCommandPipeline()` 使用 `FunctionStorageRegistry` 作为默认执行器

**文件**：`FileDriven.Execution/Command/PipelineFactory.cs`

```csharp
public static BP.IPipeline<FileDrivenPipelineContext, FileDrivenPipelineContext> BuildCommandPipeline(
    IEventBindingResolver? bindingResolver,
    IFunctionExecutor? functionExecutor)
{
    // functionExecutor 为 null 时使用 FunctionStorageRegistry 作为默认值
    functionExecutor ??= FunctionStorageRegistry.Instance as IFunctionExecutor 
        ?? new FunctionExecutorAdapter(FunctionStorageRegistry.Registry);
    
    // ... 其余代码保持不变
}
```

#### 2.4 验证清单

- [ ] `FunctionStorageRegistry` 可被当作 `IFunctionExecutor` 使用
- [ ] `PipelineEntry.FunctionExecutor` 类型从 `object?` 改为 `IFunctionExecutor?`
- [ ] 管线执行时通过 `FunctionStorageRegistry` 执行函数
- [ ] 所有现有测试通过

---

### 步骤 3：工作流注册增强（P2）

#### 3.1 `WorkflowLibrary.RegisterWorkflow()` 增加步骤验证

**文件**：`Operation/FunctionStorage/Registrations/WorkflowLibrary.cs`

```csharp
public static void RegisterWorkflow(string workflowName)
{
    var def = WorkflowExecutionLayer.GetWorkflow(workflowName);
    if (def == null)
        throw new InvalidOperationException($"未找到工作流定义: {workflowName}");

    // ★ 新增：步骤函数存在性验证
    ValidateStepFunctions(def);

    RegisterIOGroup(def);

    FunctionStorageRegistry.Register(
        workflowName,
        (Func<string, string>)(input =>
        {
            return WorkflowExecutionLayer.ExecuteWorkflow(def, input);
        }),
        def.Description ?? workflowName
    );
    // ... 其余代码保持不变
}

// ★ 新增：步骤函数验证方法
private static void ValidateStepFunctions(WorkflowDefinition def)
{
    if (def.Steps == null || def.Steps.Count == 0)
        return;
    
    var missingFunctions = new List<string>();
    foreach (var step in def.Steps)
    {
        if (!FunctionStorageRegistry.Contains(step.FuncName))
        {
            missingFunctions.Add($"步骤 '{step.StepName}' 引用了未注册的函数 '{step.FuncName}'");
        }
    }
    
    if (missingFunctions.Count > 0)
    {
        var message = $"工作流 '{def.WorkflowName}' 存在 {missingFunctions.Count} 个未注册的步骤函数：\n"
            + string.Join("\n", missingFunctions);
        
        FsLogPlaceholderHandler.Log("WorkflowLibrary_ValidateStepFunctions_warning", 
            new { message });
    }
}
```

#### 3.2 验证清单

- [ ] 工作流注册时，所有步骤的 `funcName` 被检查是否已注册
- [ ] 缺失函数时记录警告日志，不阻止注册
- [ ] `ReloadAll()` 后重新执行验证
- [ ] 现有工作流 JSON 配置不受影响

---

### 步骤 4：UIToolbox 标记函数清理（P2）

#### 4.1 移除标记函数注册

**文件**：`Operation/FunctionStorage/Registrations/UIToolbox.cs`

移除函数 8-19（Load, Close, Echo, Sleep, Show, Look, Wait, Assert, Discover, List, Validate, Exit），仅保留实际可执行的函数 1-7：

```csharp
public static void RegisterAll()
{
    if (_registered) return;
    _registered = true;

    // 1. ClearOutput
    FunctionStorageRegistry.Register("ClearOutput", (Func<string>)(() => string.Empty), "清空输出文本框");

    // 2. ExecuteWorkflow
    FunctionStorageRegistry.Register("ExecuteWorkflow", 
        (Func<string, string, string>)((workflowName, input) => 
            UIConnector.ExecuteWorkflow(workflowName, input)), "执行指定工作流");

    // 3. ReloadWorkflows
    FunctionStorageRegistry.Register("ReloadWorkflows", (Func<string>)(() => { ... }), "重新加载所有工作流");

    // 4. SaveResultToFile
    FunctionStorageRegistry.Register("SaveResultToFile", (Func<string, string>)((content) => { ... }), "保存结果到文件");

    // 5. LoadWorkflowDetails
    FunctionStorageRegistry.Register("LoadWorkflowDetails", 
        (Func<string, string>)((workflowName) => UIConnector.GetWorkflowDetails(workflowName)), "获取工作流详情");

    // 6. LoadEnvironmentApiKey
    FunctionStorageRegistry.Register("LoadEnvironmentApiKey", (Func<string>)(() => { ... }), "加载环境变量 API Key");

    // 7. SetValue
    FunctionStorageRegistry.Register("SetValue", (Func<string, string>)((value) => value), "设置控件值（占位函数）");

    // ★ 移除：标记函数 8-19
}
```

#### 4.2 `FunctionStorageRegistry` 增加非事件命令检查方法

**文件**：`Operation/FunctionStorage/FunctionStorageRegistry.cs`

```csharp
public static class FunctionStorageRegistry
{
    // ★ 新增：判断函数名是否为非事件命令
    public static bool IsNonEventCommand(string functionName)
    {
        return NonEventRegistry.IsRegistered(functionName);
    }
    
    // ★ 新增：判断函数是否可执行
    public static bool IsExecutable(string functionName)
    {
        return Contains(functionName) || NonEventRegistry.IsRegistered(functionName);
    }
}
```

#### 4.3 验证清单

- [ ] `UIToolbox` 不再注册标记函数
- [ ] `FunctionStorageRegistry.Contains("Load")` 返回 `false`
- [ ] `FunctionStorageRegistry.IsNonEventCommand("Load")` 返回 `true`
- [ ] 管线中 `FunctionStorageRegistry.Contains()` 的调用点改为 `IsExecutable()`
- [ ] 所有现有测试通过

---

### 步骤 5：日志体系统一（P3）

#### 5.1 统一日志消息 ID 命名规范

所有日志消息 ID 统一使用 `模块_类_方法_状态` 格式：

```
# FunctionStorage 示例
fs:WorkflowLibrary_RegisterWorkflow_start
fs:WorkflowLibrary_RegisterWorkflow_completed
fs:WorkflowLibrary_ValidateStepFunctions_warning

# Execution 示例
exec:NonEventCommandHandler_HandleLoad_start
exec:NonEventCommandHandler_HandleLoad_completed
exec:NonEventCommandHandler_HandleLoad_failed
```

#### 5.2 验证清单

- [ ] 所有日志消息 ID 符合 `模块_类_方法_状态` 格式
- [ ] 日志消息模板占位符语法统一
- [ ] 日志输出可正确显示中文描述

---

## 5. 实施顺序与依赖关系

```
步骤 1（非事件命令注册表统一） ← 无前置依赖
    ↓
步骤 2（函数执行路径统一） ← 依赖步骤 1
    ↓
步骤 3（工作流注册增强） ← 依赖步骤 2
    ↓
步骤 4（UIToolbox 标记函数清理） ← 依赖步骤 1
    ↓
步骤 5（日志体系统一） ← 无前置依赖，可并行执行
```

### 建议实施顺序

| 顺序 | 步骤 | 工作量 | 预期收益 |
|------|------|--------|---------|
| 第 1 步 | 步骤 1：非事件命令注册表统一 | 中 | 消除双轨制，减少维护成本 |
| 第 2 步 | 步骤 2：函数执行路径统一 | 小 | 简化调用链，消除 object? 类型转换 |
| 第 3 步 | 步骤 3：工作流注册增强 | 小 | 提前发现配置错误 |
| 第 4 步 | 步骤 4：UIToolbox 标记函数清理 | 中 | 消除"假注册"，减少混淆 |
| 并行 | 步骤 5：日志体系统一 | 小 | 统一日志格式，便于排查 |

---

## 6. 风险与注意事项

### 6.1 步骤 1 的风险

- `NonEventCommandRegistry.BindingRegistration` 内部类（绑定注册历史记录）是 Execution 特有的功能，适配到 FunctionStorage 时需要保留
- `FunctionExecutionContextAdapter` 需要实现 `IExecutionContext` 接口，将 `FileDrivenPipelineContext` 适配为 `IExecutionContext`

### 6.2 步骤 2 的风险

- `PipelineEntry.FunctionExecutor` 的 `object?` 类型可能被多处代码使用，改为强类型后需要检查所有引用点
- `FunctionStorageRegistry` 是静态类，实现接口时需要返回实例引用

### 6.3 步骤 4 的风险

- 移除标记函数后，需要确保所有调用 `FunctionStorageRegistry.Contains("Load")` 的地方改为 `FunctionStorageRegistry.IsNonEventCommand("Load")` 或 `FunctionStorageRegistry.IsExecutable("Load")`
- 需要全局搜索 `FunctionStorageRegistry.Contains(` 的调用点

### 6.4 回滚策略

每个步骤完成后编译验证，确保 0 错误后再进入下一步。如某步骤导致编译失败，立即回退该步骤的所有修改。

---

## 附录：涉及文件清单

| 步骤 | 文件路径 | 改动类型 |
|------|---------|---------|
| 1 | `FileDriven.Execution/Command/NonEventCommandRegistry.cs` | 重写为适配器 |
| 1 | `Operation/FunctionStorage/FunctionStorageRegistry.cs` | 移除占位命令注册 |
| 1 | `FileDriven.Execution/Command/Skeletons/NonEventCommandHandler.cs` | 统一分发入口 |
| 2 | `Operation/FunctionStorage/FunctionStorageRegistry.cs` | 实现 IFunctionExecutor |
| 2 | `FileDriven.Pipeline/PipelineEntry.cs` | FunctionExecutor 强类型化 |
| 2 | `FileDriven.Execution/Command/PipelineFactory.cs` | 默认执行器 |
| 3 | `Operation/FunctionStorage/Registrations/WorkflowLibrary.cs` | 增加步骤验证 |
| 4 | `Operation/FunctionStorage/Registrations/UIToolbox.cs` | 移除标记函数 |
| 4 | `Operation/FunctionStorage/FunctionStorageRegistry.cs` | 增加 IsExecutable |
| 5 | 各日志消息文件 | 统一命名规范 |
