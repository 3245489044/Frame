# FileDriven.Execution → BackEndDriven 迁移分析报告

> **日期**：2026-07-03  
> **范围**：FileDriven.Execution 全部源文件 vs BackEndDriven.Execution + BackEndDriven.Pipeline  
> **目标**：识别应迁移至 BackEndDriven 的逻辑，消除重复，统一后端执行抽象

---

## 一、已完成的迁移（现状基线）

| 迁移项 | 源位置 | 目标位置 | 状态 |
|--------|--------|----------|------|
| `ExecutionResult` 模型 | `FileDriven.Execution/ExecutionResult.cs` | `BackEndDriven.Models.ExecutionResult` | ✅ 已迁移 |
| `CommandLineResult` 模型 | `FileDriven.Abstractions/CommandFileLineResult.cs` | `BackEndDriven.Models.CommandLineResult` | ✅ 已迁移 |
| `IExecutionResult` 接口 | `FileDriven.Execution/IExecutionResult.cs` | `BackEndDriven.Models.IExecutionResult` | ✅ 已迁移 |
| `IBackEndPipelineContext` 接口 | —（新增） | `BackEndDriven.Abstractions.IBackEndPipelineContext` | ✅ 已创建 |
| `ExecutionResult.FromContext()` 工厂方法 | `FileDriven.Execution/ExecutionResult.cs` | `BackEndDriven.Models.ExecutionResult.FromContext()` | ✅ 已迁移 |
| `INonEventCommandRegistry` 接口 | `FunctionStorage.Abstractions` | `CommandFramework.Abstractions.INonEventCommandRegistry` | ✅ 已迁移 |
| `ICommandParser` 接口 | `FileDriven.Abstractions` | `CommandFramework.ICommandParser` | ✅ 已迁移 |
| `CommandParser` 实现 | `FileDriven.Pipeline` | `CommandFramework.Parsing.CommandParser` | ✅ 已迁移 |

---

## 二、待迁移项（按优先级排列）

### P1 — 应尽快迁移

#### 1. `ExecutionHelper.CreateCommandExecutor()` → `BackEndDriven.Execution.CommandExecutionService`

**当前位置**：`FileDriven.Execution/ExecutionHelper.cs`（第 35-80 行）

**当前代码摘要**：
```csharp
public static Func<string, CancellationToken, Task<OperationResult<string>>> CreateCommandExecutor()
{
    return async (command, ct) =>
    {
        var registry = NonEventCommandCenter.Registry;
        var parser = new CommandParser();
        var normalized = parser.Normalize(command);
        if (normalized == null) return OperationResult<string>.SuccessResult(null, "skip");
        var parsed = parser.Parse(normalized);
        if (registry.IsRegistered(parsed.CommandName))
        {
            var context = new ExecutionContext(parsed, ...);
            registry.Execute(parsed.CommandName, context);
            return OperationResult<string>.SuccessResult(context.Result, "ok");
        }
        return OperationResult<string>.FailureResult("未注册的命令");
    };
}
```

**问题**：
- 直接依赖 `NonEventCommandCenter.Registry`（静态全局单例）
- 每次调用都 `new CommandParser()`，没有复用
- 其他后端项目（如 EventTriggerFramework）无法复用此逻辑
- 与 `BackEndDriven.Execution.ExecutionEngine` 中的命令执行逻辑重复

**迁移方案**：

在 `BackEndDriven.Execution` 中创建：

```csharp
// BackEndDriven.Execution/ICommandExecutionService.cs
namespace BackEndDriven.Execution;

public interface ICommandExecutionService
{
    Task<OperationResult<string>> ExecuteCommandAsync(
        string command, CancellationToken ct = default);
}
```

```csharp
// BackEndDriven.Execution/CommandExecutionService.cs
namespace BackEndDriven.Execution;

public class CommandExecutionService : ICommandExecutionService
{
    private readonly INonEventCommandRegistry _registry;
    private readonly ICommandParser _parser;

    public CommandExecutionService(
        INonEventCommandRegistry registry,
        ICommandParser parser)
    {
        _registry = registry;
        _parser = parser;
    }

    public async Task<OperationResult<string>> ExecuteCommandAsync(
        string command, CancellationToken ct = default)
    {
        var normalized = _parser.Normalize(command);
        if (normalized == null)
            return OperationResult<string>.SuccessResult(null, "skip");

        var parsed = _parser.Parse(normalized);
        if (_registry.IsRegistered(parsed.CommandName))
        {
            var context = new ExecutionContext(parsed, ...);
            _registry.Execute(parsed.CommandName, context);
            return OperationResult<string>.SuccessResult(context.Result, "ok");
        }
        return OperationResult<string>.FailureResult("未注册的命令");
    }
}
```

**FileDriven 侧改造**：
```csharp
// FileDriven.Execution/ExecutionHelper.cs — 委托版本
public static Func<string, CancellationToken, Task<OperationResult<string>>> CreateCommandExecutor(
    ICommandExecutionService? executionService = null)
{
    executionService ??= new CommandExecutionService(
        NonEventCommandCenter.Registry,
        new CommandParser());
    return (cmd, ct) => executionService.ExecuteCommandAsync(cmd, ct);
}
```

---

#### 2. `FileDrivenResultBuilder.BuildResults()` → `BackEndDriven.Pipeline.ResultBuilder`

**当前位置**：`FileDriven.Execution/Pipeline/FileDrivenResultBuilder.cs`

**当前代码摘要**：
```csharp
public static class FileDrivenResultBuilder
{
    public static ExecutionResult BuildResults(
        FileDrivenPipelineContext context, TimeSpan executionTime)
    {
        return ExecutionResult.FromContext(
            context.TotalCommands,
            context.SuccessCount,
            context.FailCount,
            context.CommandResults.Select(r => new CommandLineResult { ... }).ToList(),
            executionTime,
            context.Mode,
            context.BatchId);
    }
}
```

**问题**：
- `BuildResults()` 方法本身仍然留在 FileDriven 层
- 其他后端项目需要重复实现相同的上下文→结果转换逻辑
- 方法签名依赖 `FileDrivenPipelineContext`，限制了复用

**迁移方案**：

```csharp
// BackEndDriven.Pipeline/ResultBuilder.cs
namespace BackEndDriven.Pipeline;

public static class ResultBuilder
{
    /// <summary>
    /// 从 IBackEndPipelineContext 构建 ExecutionResult
    /// </summary>
    public static ExecutionResult BuildFromContext(
        IBackEndPipelineContext context,
        TimeSpan executionTime)
    {
        return ExecutionResult.FromContext(
            context.TotalCommands,
            context.SuccessCount,
            context.FailCount,
            context.CommandResults,
            executionTime,
            context.Mode,
            context.BatchId);
    }
}
```

**FileDriven 侧改造**：
```csharp
// FileDriven.Execution/Pipeline/FileDrivenResultBuilder.cs — 委托版本
public static class FileDrivenResultBuilder
{
    public static ExecutionResult BuildResults(
        FileDrivenPipelineContext context, TimeSpan executionTime)
    {
        return ResultBuilder.BuildFromContext(context, executionTime);
    }
}
```

---

### P2 — 建议迁移

#### 3. `IPipelineEntryAccessor` 接口提取

**当前位置**：`FileDriven.Pipeline/Core/PipelineEntry.cs` + `PipelineEntryExtensions.cs`

**当前代码摘要**：
```csharp
// PipelineEntryExtensions.cs
public static class PipelineEntryExtensions
{
    public static T GetOrAdd<T>(this PipelineEntry entry, string key, Func<T> factory)
    {
        if (entry.Properties.TryGetValue(key, out var value) && value is T result)
            return result;
        var newValue = factory();
        entry.Properties[key] = newValue;
        return newValue;
    }
}
```

**问题**：
- `PipelineEntryExtensions` 直接操作 `PipelineEntry` 类型
- 其他后端项目无法复用这些扩展方法
- `PipelineEntry` 是 FileDriven 层的具体类

**迁移方案**：

```csharp
// BackEndDriven.Abstractions/IPipelineEntryAccessor.cs
namespace BackEndDriven.Abstractions;

public interface IPipelineEntryAccessor
{
    Dictionary<string, object?> Properties { get; }
    T? GetProperty<T>(string key);
    void SetProperty(string key, object? value);
}
```

```csharp
// BackEndDriven.Abstractions/PipelineEntryExtensions.cs — 迁移后的扩展方法
public static class PipelineEntryExtensions
{
    public static T GetOrAdd<T>(this IPipelineEntryAccessor entry,
        string key, Func<T> factory)
    {
        if (entry.Properties.TryGetValue(key, out var value) && value is T result)
            return result;
        var newValue = factory();
        entry.Properties[key] = newValue;
        return newValue;
    }
}
```

---

#### 4. `ExecutionEngine` 中的 `ExecuteBatchAsync` 通用化

**当前位置**：`FileDriven.Execution/FileDrivenExecutionEngine.cs`（第 100-160 行）

**当前代码摘要**：
```csharp
public async Task<ExecutionResult> ExecuteBatchAsync(
    List<string> commands, CancellationToken ct = default)
{
    var context = new FileDrivenPipelineContext();
    var stopwatch = Stopwatch.StartNew();

    foreach (var cmd in commands)
    {
        if (ct.IsCancellationRequested) break;
        var result = await _executor(cmd, ct);
        context.RecordCommandResult(result);
    }

    stopwatch.Stop();
    return FileDrivenResultBuilder.BuildResults(context, stopwatch.Elapsed);
}
```

**问题**：
- 批量执行逻辑与 `BackEndDriven.Execution.ExecutionEngine` 中的逻辑重复
- 直接依赖 `FileDrivenPipelineContext` 和 `FileDrivenResultBuilder`

**迁移方案**：

```csharp
// BackEndDriven.Execution/ExecutionEngine.cs — 新增通用批量执行方法
public async Task<ExecutionResult> ExecuteBatchAsync(
    ICommandExecutionService executionService,
    List<string> commands,
    IBackEndPipelineContext context,
    CancellationToken ct = default)
{
    var stopwatch = Stopwatch.StartNew();

    foreach (var cmd in commands)
    {
        if (ct.IsCancellationRequested) break;
        var result = await executionService.ExecuteCommandAsync(cmd, ct);
        context.RecordCommandResult(new CommandLineResult
        {
            CommandText = cmd,
            Success = result.Success,
            Message = result.Message
        });
    }

    stopwatch.Stop();
    return ResultBuilder.BuildFromContext(context, stopwatch.Elapsed);
}
```

---

### P3 — 低优先级（可暂缓）

#### 5. `GlobalExceptionHandler` 统一

**当前位置**：`FileDriven.Execution/GlobalExceptionHandler.cs`

**现状**：`BackEndDriven.Execution` 已有 `GlobalExceptionHandler.cs`，但两者实现不同。

**建议**：统一异常处理策略，将通用部分提取到 `BackEndDriven.Execution`，FileDriven 特定部分保留。

#### 6. `ModeDispatcher` 模式分发逻辑

**当前位置**：`FileDriven.Execution/ModeDispatcher.cs`

**现状**：`BackEndDriven.Execution` 已有 `ModeDispatcher.cs`，但两者职责不同。

**建议**：FileDriven 的 ModeDispatcher 处理 GUI/CLI/Pipe 模式切换，BackEndDriven 的 ModeDispatcher 处理执行模式分发。保持独立，暂不合并。

---

## 三、迁移路线图

```
P1（本周）
├── CommandExecutionService 创建
│   ├── ICommandExecutionService.cs → BackEndDriven.Execution
│   ├── CommandExecutionService.cs → BackEndDriven.Execution
│   └── ExecutionHelper.cs → 委托改造
│
└── ResultBuilder 创建
    ├── ResultBuilder.cs → BackEndDriven.Pipeline
    └── FileDrivenResultBuilder.cs → 委托改造

P2（下个迭代）
├── IPipelineEntryAccessor 接口提取
│   ├── IPipelineEntryAccessor.cs → BackEndDriven.Abstractions
│   ├── PipelineEntryExtensions.cs → 迁移到 BackEndDriven.Abstractions
│   └── PipelineEntry.cs → 实现接口
│
└── ExecuteBatchAsync 通用化
    └── ExecutionEngine.cs → 新增通用批量执行方法

P3（待定）
├── GlobalExceptionHandler 统一
└── ModeDispatcher 对齐
```

---

## 四、影响范围

| 迁移项 | 影响 FileDriven 文件数 | 影响 BackEndDriven 文件数 | 新增文件数 |
|--------|----------------------|--------------------------|-----------|
| P1.1 CommandExecutionService | 1（ExecutionHelper.cs） | 0 | 2 |
| P1.2 ResultBuilder | 1（FileDrivenResultBuilder.cs） | 0 | 1 |
| P2.1 IPipelineEntryAccessor | 2（PipelineEntry.cs, PipelineEntryExtensions.cs） | 0 | 2 |
| P2.2 ExecuteBatchAsync 通用化 | 1（FileDrivenExecutionEngine.cs） | 1（ExecutionEngine.cs） | 0 |
| **合计** | **5** | **1** | **5** |

---

## 五、风险与注意事项

1. **向后兼容**：所有迁移必须保持 FileDriven 现有 API 不变，FileDriven 侧改为委托/包装模式
2. **依赖注入**：`CommandExecutionService` 应通过 DI 容器注册，FileDriven 侧通过构造函数注入获取
3. **测试覆盖**：迁移后需确保 `ExecutionHelper` 和 `FileDrivenResultBuilder` 的现有测试仍然通过
4. **NonEventCommandCenter 依赖**：`CommandExecutionService` 通过 `INonEventCommandRegistry` 接口解耦，不再直接依赖静态单例
5. **OperationResult 类型**：注意 `BackEndDriven` 使用的 `OperationResult<T>` 来自 `Modules.Parsing.Models`，与 `FileDriven.Abstractions` 中的版本可能不同，需统一
