# FileDriven.Execution 事件管线 → Container 框架重构方案

> **目标**：将 FileDriven.Execution 的事件管线逐步迁移到 Container 通用容器框架，消除重复的管线编排逻辑，统一弹性策略（重试/熔断/超时/日志）。

---

## 目录

1. [现状分析](#1-现状分析)
2. [已完成的修复（Phase 8）](#2-已完成的修复phase-8)
3. [进一步重构方向](#3-进一步重构方向)
4. [Phase 9：统一执行结果模型](#phase-9统一执行结果模型)
5. [Phase 10：消除 FileDrivenPipelineRunner 的 CentralPipeline 依赖](#phase-10消除-filedrivenpipelinerunner-的-centralpipeline-依赖)
6. [Phase 11：将 Stage 管线迁移到 Container Behavior](#phase-11将-stage-管线迁移到-container-behavior)
7. [Phase 12：消除 ExecutionPipelineEntry 的管线构建职责](#phase-12消除-executionpipelineentry-的管线构建职责)
8. [Phase 13：统一 DI 注册与配置入口](#phase-13统一-di-注册与配置入口)
9. [迁移路线图](#9-迁移路线图)

---

## 1. 现状分析

### 1.1 当前架构

```
FileDriven.Execution 事件管线（当前）
┌─────────────────────────────────────────────────────┐
│ ExecutionPipelineEntry                              │
│  ├─ CreateContext()          → FileDrivenContextFactory │
│  ├─ CreatePipeline()        → PipelineFactory        │
│  ├─ RunPipeline()           → CentralPipeline        │
│  ├─ RunPipelineAsync()      → ExecutionCenter        │
│  └─ IEventPipelineExecutor  → 手动点击路由           │
├─────────────────────────────────────────────────────┤
│ PipelineFactory                                      │
│  ├─ CreateFileDrivenPipeline()  → CentralPipeline    │
│  └─ BuildCommandPipeline()     → DelegatePipeline    │
├─────────────────────────────────────────────────────┤
│ ContainerPipelineConfigurator                        │
│  └─ BuildContainerWrappedCommandPipeline()           │
│     └─ FileDrivenExecutionEngine                     │
│        └─ IExecutionCell.ExecuteAsync()              │
│           └─ MediatR PipelineBehavior 链             │
└─────────────────────────────────────────────────────┘
```

### 1.2 已完成的修复（Phase 8）

| 修复项 | 文件 | 说明 |
|--------|------|------|
| `IExecutionEngine` → `IExecutionCell` | `FileDrivenExecutionEngine.cs` | 消除 `RunAsync` 硬编码 `CommandText` 的架构缺陷 |
| 参数类型对齐 | `ContainerPipelineConfigurator.cs` | `IExecutionEngine?` → `IExecutionCell?` |
| 返回类型修复 | `FileDrivenPipelineRunner.cs` | `IExecutionResult?` → `Container.Abstractions.ExecutionResult?` |
| 编译错误修复 | `FileDrivenResultBuilder.cs` | 删除对不存在类型 `BackEndDriven.Abstractions.ExecutionResult` 的引用 |

### 1.3 已完成的 Phase 9 重构

| 重构项 | 文件 | 说明 |
|--------|------|------|
| `ExecutionResult` 继承 `BackEndDriven.Models.ExecutionResult` | `ExecutionResult.cs` | 统一执行结果模型，废弃原独立 class |
| 隐式转换 `(FileDrivenPipelineContext, TimeSpan)` → `ExecutionResult` | `ExecutionResult.cs` | 消除手动调用 `FromContext()` 的冗余 |
| `FromBackEndResult` 工厂方法 | `ExecutionResult.cs` | 支持从基类结果创建 FileDriven 结果 |
| `CommandResults` 类型改为 `List<CommandLineResult>` | `ExecutionPipelineEntry.cs` | 统一使用 `BackEndDriven.Models.CommandLineResult` |
| `BuildResults` 返回 `List<CommandLineResult>` | `ExecutionPipelineEntry.cs` | 通过隐式转换兼容 `CommandFileLineResult` |
| 移除 `ConvertToFileDrivenResult` | `ExecutionHelper.cs` | 统一使用 `CreateBackEndResult` |
| `FrameworkInitializer` 提取 | `Lifecycle/FrameworkInitializer.cs` | 从 `FileDrivenExecutor` 提取初始化逻辑 |
| `FrameworkShutdown` 提取 | `Lifecycle/FrameworkShutdown.cs` | 从 `FileDrivenExecutor` 提取关闭逻辑 |

### 1.4 剩余问题

| 问题 | 严重程度 | 说明 |
|------|---------|------|
| `FileDrivenPipelineRunner` 依赖 `CentralPipeline` | ⚠️ 高 | 仍使用旧管线框架，未迁移到 Container |
| `PipelineFactory` 的 Stage 管线未迁移 | ⚠️ 中 | `ParseCommandStage` / `ExecuteCommandStage` / `ResultCollectStage` 仍是手动编排 |
| `ExecutionPipelineEntry` 职责过重 | ⚠️ 中 | 同时承担上下文创建、管线构建、管线执行、结果构建 |
| DI 注册分散 | ⚠️ 低 | `ServiceCollectionExtensions` 注册了多个组件，但未统一配置入口 |

---

## 2. 已完成的修复（Phase 8）

### 2.1 核心修复：FileDrivenExecutionEngine

**问题**：`FileDrivenExecutionEngine` 通过 `IExecutionEngine.RunAsync()` 桥接 Container，但 `ExecutionEngine.RunAsync` 内部会创建硬编码的 `ExecutionRequest`：

```csharp
// ExecutionEngine.RunAsync 内部（问题代码）
var executionRequest = new ExecutionRequest(
    CommandText: $"Engine:{_name}",  // ← 硬编码！传入的命令文本被丢弃
    ...
);
```

**修复**：改为直接通过 `IExecutionCell.ExecuteAsync()` 发送命令：

```csharp
// FileDrivenExecutionEngine（修复后）
public class FileDrivenExecutionEngine
{
    private readonly IExecutionCell _executionCell;

    public FileDrivenExecutionEngine(IExecutionCell executionCell) { ... }

    public async Task<ExecutionResult> ExecuteFileDrivenCommandAsync(
        string commandText,
        FileDrivenPipelineContext pipelineContext,
        CancellationToken cancellationToken = default)
    {
        var runtimeCtx = new FileDrivenRuntimeContext(pipelineContext);
        var request = new ExecutionRequest(
            CommandText: commandText,
            Source: "FileDriven",
            Mode: pipelineContext.Mode == "GUI" ? ExecutionMode.Gui : ExecutionMode.Cli,
            RuntimeContext: runtimeCtx);

        // ★ 直接通过 IExecutionCell 发送，自动经过 MediatR 的 IPipelineBehavior 链
        var result = await _executionCell.ExecuteAsync(request, cancellationToken);
        return result;
    }
}
```

### 2.2 接口对齐：ContainerPipelineConfigurator

**问题**：`BuildContainerWrappedCommandPipeline` 的参数类型为 `IExecutionEngine?`，与修复后的 `FileDrivenExecutionEngine` 不兼容。

**修复**：参数改为 `IExecutionCell?`，并添加架构说明注释。

### 2.3 类型修复：FileDrivenPipelineRunner

**问题**：`ExecuteSingleCommandWithContainer` 返回 `IExecutionResult?`，但 `FileDrivenExecutionEngine.ExecuteFileDrivenCommand` 返回 `Container.Abstractions.ExecutionResult`。

**修复**：返回类型改为 `Container.Abstractions.ExecutionResult?`。

### 2.4 编译错误修复：FileDrivenResultBuilder

**问题**：`ToBackEndExecutionResult` 引用了不存在的 `BackEndDriven.Abstractions.ExecutionResult` 类型。

**修复**：`FileDriven.Execution.ExecutionResult` 已实现 `IExecutionResult` 接口，直接返回自身。

---

## 3. 进一步重构方向

### 3.1 总体目标

```
目标架构
┌─────────────────────────────────────────────────────────┐
│ Container 统一执行框架                                   │
│  ├─ IExecutionCell.ExecuteAsync(ExecutionRequest)       │
│  ├─ IPipelineBehavior 链（重试/熔断/超时/日志）          │
│  └─ ExecuteCommandHandler（命令处理）                    │
├─────────────────────────────────────────────────────────┤
│ FileDriven.Execution（薄门面层）                         │
│  ├─ FileDrivenExecutionEngine（已修复）                  │
│  ├─ FileDrivenRuntimeContext（运行时依赖注入）            │
│  └─ DI 注册（services.AddFileDrivenExecution()）         │
└─────────────────────────────────────────────────────────┘
```

### 3.2 重构优先级

| 优先级 | 方向 | 影响范围 | 风险 |
|--------|------|---------|------|
| P0 | [Phase 9](#phase-9统一执行结果模型) 统一执行结果模型 | 高 | 低 |
| P0 | [Phase 10](#phase-10消除-filedrivenpipelinerunner-的-centralpipeline-依赖) 消除 CentralPipeline 依赖 | 高 | 中 |
| P1 | [Phase 11](#phase-11将-stage-管线迁移到-container-behavior) Stage → Behavior 迁移 | 中 | 高 |
| P1 | [Phase 12](#phase-12消除-executionpipelineentry-的管线构建职责) 精简 ExecutionPipelineEntry | 中 | 中 |
| P2 | [Phase 13](#phase-13统一-di-注册与配置入口) 统一 DI 注册 | 低 | 低 |

---

## 4. Phase 9：统一执行结果模型

### 4.1 问题

当前存在两个 `ExecutionResult` 类型：

| 类型 | 命名空间 | 性质 | 用途 |
|------|---------|------|------|
| `ExecutionResult` | `FileDriven.Execution` | class，实现 `IExecutionResult` | FileDriven 管线执行结果 |
| `ExecutionResult` | `Container.Abstractions` | record，不可变 | Container 单条命令执行结果 |

两个类型字段高度重叠，但互不兼容，导致 `FileDrivenExecutionEngine` 返回 `Container.Abstractions.ExecutionResult` 后，调用方需要手动转换。

### 4.2 方案

**方案 B（已实施）**：`FileDriven.Execution.ExecutionResult` 继承 `BackEndDriven.Models.ExecutionResult`

```csharp
// FileDriven.Execution.ExecutionResult（Phase 9 已实施）
public class ExecutionResult : BackEndDriven.Models.ExecutionResult
{
    /// <summary>
    /// 从 BackEndDriven.Models.ExecutionResult 创建 FileDriven ExecutionResult
    /// </summary>
    public static ExecutionResult FromBackEndResult(BackEndDriven.Models.ExecutionResult? baseResult)
    {
        if (baseResult == null)
            return EmptySuccess();

        return new ExecutionResult
        {
            Success = baseResult.Success,
            TotalCommands = baseResult.TotalCommands,
            SuccessCount = baseResult.SuccessCount,
            FailCount = baseResult.FailCount,
            ExecutionTime = baseResult.ExecutionTime,
            Mode = baseResult.Mode ?? "CLI",
            BatchId = baseResult.BatchId,
            ErrorMessage = baseResult.ErrorMessage,
            CommandResults = baseResult.CommandResults
        };
    }

    /// <summary>
    /// 从 FileDrivenPipelineContext 隐式转换
    /// </summary>
    public static implicit operator ExecutionResult(
        (FileDrivenPipelineContext Context, TimeSpan ExecutionTime) source)
        => new()
        {
            Success = source.Context.FailCount == 0,
            TotalCommands = source.Context.TotalCommands,
            SuccessCount = source.Context.SuccessCount,
            FailCount = source.Context.FailCount,
            CommandResults = new List<CommandLineResult>(
                source.Context.CommandResults.Select(r => (CommandLineResult)r)),
            ExecutionTime = source.ExecutionTime,
            Mode = source.Context.Mode ?? "CLI",
            BatchId = source.Context.BatchId
        };
}
```

**选择理由**：
- `BackEndDriven.Models.ExecutionResult` 已实现 `IExecutionResult` 接口
- 继承方式保持 `FileDriven.Execution.ExecutionResult` 类型名不变，现有代码的 `using ExecutionResult` 引用自动指向新类型
- `CommandFileLineResult` → `CommandLineResult` 通过隐式转换兼容
- 避免破坏 `IExecutionResult` 接口兼容性

### 4.3 影响范围

- `FileDriven.Execution.ExecutionResult` 改为继承 `BackEndDriven.Models.ExecutionResult`
- `CommandResults` 类型从 `List<CommandFileLineResult>` 改为 `List<CommandLineResult>`
- `CommandFileLineResult` → `CommandLineResult` 通过隐式转换兼容
- `FileDrivenPipelineRunner`、`FileDrivenResultBuilder` 使用隐式转换 `((Context, ExecutionTime))`
- `ExecutionPipelineEntry.BuildResults` 返回 `List<CommandLineResult>`
- `FileDrivenExecutor.Run()`/`RunGui()`/`RunCli()` 使用 `FromBackEndResult` 转换

### 4.4 迁移步骤（已完成）

1. ✅ `ExecutionResult.cs` — 改为继承 `BackEndDriven.Models.ExecutionResult`，添加 `FromBackEndResult` 工厂方法和隐式转换
2. ✅ `FileDrivenPipelineRunner.cs` — 使用隐式转换 `((Context, ExecutionTime))`
3. ✅ `FileDrivenResultBuilder.cs` — 使用隐式转换 `((Context, ExecutionTime))`
4. ✅ `ExecutionPipelineEntry.cs` — `BuildResults` 和 `RunPipeline` 返回 `List<CommandLineResult>`
5. ✅ `FileDrivenExecutor.cs` — `Run()`/`RunGui()`/`RunCli()` 使用 `FromBackEndResult`
6. ✅ `ExecutionHelper.cs` — 移除 `ConvertToFileDrivenResult`，保留 `CreateBackEndResult`
7. ✅ `FrameworkInitializer.cs` — 从 `FileDrivenExecutor` 提取初始化逻辑
8. ✅ `FrameworkShutdown.cs` — 从 `FileDrivenExecutor` 提取关闭逻辑

---

## 5. Phase 10：消除 FileDrivenPipelineRunner 的 CentralPipeline 依赖 ✅ 已完成

### 5.1 问题

`FileDrivenPipelineRunner` 依赖 `CentralPipeline<FileDrivenPipelineContext>` 执行管线：

```csharp
public class FileDrivenPipelineRunner
{
    public ExecutionResult RunPipeline(
        CentralPipeline pipeline,        // ← 依赖旧管线框架
        FileDrivenPipelineContext context)
    {
        pipeline.Run(context);           // ← 使用旧管线引擎
        ...
    }
}
```

`CentralPipeline` 是 Modules 层的旧管线框架，与 Container 的 `IExecutionCell` 不兼容。

### 5.2 方案

将 `FileDrivenPipelineRunner` 改为直接使用 Container 的 `IExecutionCell`：

```csharp
public class FileDrivenPipelineRunner
{
    private readonly IExecutionCell _executionCell;

    public FileDrivenPipelineRunner(IExecutionCell executionCell)
    {
        _executionCell = executionCell;
    }

    public async Task<ExecutionResult> RunPipelineAsync(
        string[] commands,
        FileDrivenPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CommandLineResult>();
        var startTime = DateTime.UtcNow;

        foreach (var command in commands)
        {
            var runtimeCtx = new FileDrivenRuntimeContext(context);
            var request = new ExecutionRequest(
                CommandText: command,
                Source: "FileDriven",
                RuntimeContext: runtimeCtx);

            var containerResult = await _executionCell.ExecuteAsync(request, cancellationToken);

            results.Add(new CommandLineResult
            {
                CommandText = command,
                Success = containerResult.Success,
                Message = containerResult.ErrorMessage
            });
        }

        return new ExecutionResult
        {
            Success = results.All(r => r.Success),
            TotalCommands = results.Count,
            SuccessCount = results.Count(r => r.Success),
            FailCount = results.Count(r => !r.Success),
            CommandResults = results,
            ExecutionTime = DateTime.UtcNow - startTime,
            Mode = context.Mode ?? "CLI"
        };
    }
}
```

### 5.3 影响范围

- `FileDrivenPipelineRunner` 不再需要 `CentralPipeline` 引用
- `PipelineFactory.CreateFileDrivenPipeline` 可以逐步废弃
- `ExecutionPipelineEntry.RunPipeline` 需要改为使用 `FileDrivenPipelineRunner`

### 5.4 迁移步骤（已完成）

1. ✅ 创建新的 `FileDrivenPipelineRunner` 实现（基于 `IExecutionCell`）
2. ✅ 标记旧的 `CentralPipeline` 重载为 `[Obsolete]`
3. ✅ 新增 `FileDrivenExecutionEngine` 桥接类
4. ✅ 新增 `ExecuteSingleCommandWithContainer` 方法

---

## 6. Phase 11：将 Stage 管线迁移到 Container Behavior ✅ 已完成

### 6.1 问题

当前 `PipelineFactory.BuildCommandPipeline` 手动编排三个 Stage：

```csharp
void ExecuteStages(FileDrivenPipelineContext ctx)
{
    parseStage.Process(ctx);     // ParseCommandStage
    executeStage.Process(ctx);   // ExecuteCommandStage
    resultStage.Process(ctx);    // ResultCollectStage
}
```

这些 Stage 的逻辑与 Container 的 `IPipelineBehavior` 机制重复。Container 已经提供了 `RetryBehavior`、`CircuitBreakerBehavior`、`TimeoutBehavior`、`LoggingBehavior`，但 FileDriven 的 Stage 是独立实现的。

### 6.2 方案

将 FileDriven 的 Stage 逻辑迁移到 Container 的 `IPipelineBehavior` 实现：

```csharp
// FileDriven.Execution 的 IPipelineBehavior 实现
public class FileDrivenParseCommandBehavior : IPipelineBehavior<ExecutionRequest, ExecutionResult>
{
    public async Task<ExecutionResult> Handle(
        ExecutionRequest request,
        RequestHandlerDelegate<ExecutionResult> next,
        CancellationToken cancellationToken)
    {
        // 通过 IRuntimeContext 获取 FileDriven 的运行时依赖
        var runtimeCtx = request.RuntimeContext;
        if (runtimeCtx != null)
        {
            var preprocessor = runtimeCtx.GetService<ICommandPreprocessor>();
            if (preprocessor != null)
            {
                var processedCommand = preprocessor.Preprocess(commandText, runtimeCtx);
                if (processedCommand != null && processedCommand != commandText)
                {
                    var processedRequest = request with { CommandText = processedCommand };
                    return await next(processedRequest, cancellationToken);
                }
            }
        }
        return await next(request, cancellationToken);
    }
}

public class FileDrivenResultCollectBehavior : IPipelineBehavior<ExecutionRequest, ExecutionResult>
{
    public async Task<ExecutionResult> Handle(
        ExecutionRequest request,
        RequestHandlerDelegate<ExecutionResult> next,
        CancellationToken cancellationToken)
    {
        var result = await next(request, cancellationToken);

        // 通过 IRuntimeContext 获取 FileDriven 的运行时依赖
        var runtimeCtx = request.RuntimeContext;
        if (runtimeCtx != null)
        {
            var postprocessor = runtimeCtx.GetService<IResultPostprocessor>();
            if (postprocessor != null)
            {
                var processedResult = postprocessor.Postprocess(result, runtimeCtx);
                if (processedResult != null)
                    return processedResult;
            }
        }
        return result;
    }
}
```

### 6.3 Behavior 链顺序

```
MediatR PipelineBehavior 链（注册顺序 = 执行顺序）：
┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌───────────────┐   ┌──────────────────────┐
│ Logging      │ → │ Timeout      │ → │ Retry        │ → │ Circuit       │ → │ ExecuteCommandHandler │
│ Behavior     │   │ Behavior     │   │ Behavior     │   │ Breaker       │   │ (最终处理)            │
└──────────────┘   └──────────────┘   └──────────────┘   └───────────────┘   └──────────────────────┘
                                                                                    │
                                                                                    ↓
┌──────────────────────┐   ┌──────────────────────────┐   ┌──────────────────────┐
│ ExecuteCommandHandler │ ← │ FileDrivenResultCollect  │ ← │ FileDrivenParseCommand│
│ (返回结果)            │   │ Behavior (可选)           │   │ Behavior (可选)       │
└──────────────────────┘   └──────────────────────────┘   └──────────────────────┘
```

### 6.4 影响范围

- 新增 `FileDrivenParseCommandBehavior`、`FileDrivenResultCollectBehavior` 在 FileDriven.Execution 项目中
- 新增 `ICommandPreprocessor`、`IResultPostprocessor` 扩展接口
- `PipelineFactory.BuildCommandPipeline` 可以逐步废弃
- `ContainerPipelineConfigurator.BuildContainerWrappedCommandPipeline` 不再需要手动编排 Stage

### 6.5 迁移步骤（已完成）

1. ✅ 在 FileDriven.Execution 项目中创建 `FileDrivenParseCommandBehavior`
2. ✅ 在 FileDriven.Execution 项目中创建 `FileDrivenResultCollectBehavior`
3. ✅ 新增 `ICommandPreprocessor` 和 `IResultPostprocessor` 扩展接口
4. ✅ 通过 `IRuntimeContext.GetService<T>()` 桥接 FileDriven 运行时依赖
5. ✅ Behavior 为可选注册，未注册时由 `ExecuteCommandHandler` 内部处理

---

## 7. Phase 12：消除 ExecutionPipelineEntry 的管线构建职责 ✅ 已完成

### 7.1 问题

`ExecutionPipelineEntry` 当前承担了过多职责：

| 职责 | 当前实现 | 目标 |
|------|---------|------|
| 上下文创建 | `CreateContext()` | → `FileDrivenPipelineRunner` |
| 管线构建 | `CreatePipeline()` | → `FileDrivenPipelineRunner` |
| 管线执行 | `RunPipeline()` | → `FileDrivenPipelineRunner` |
| 结果构建 | `BuildResults()` | → `FileDrivenResultBuilder` |
| 手动点击路由 | `IEventPipelineExecutor.Execute()` | → 保留 |
| 异步执行 | `RunPipelineAsync()` | → `FileDrivenPipelineRunner` |

### 7.2 方案

将 `CreateContext` 和 `CreatePipeline` 方法迁移到 `FileDrivenPipelineRunner`，`ExecutionPipelineEntry` 通过 `_runner` 字段委托调用：

```csharp
// FileDrivenPipelineRunner（Phase 12 新增）
public class FileDrivenPipelineRunner
{
    private readonly Pipeline.PipelineEntry? _pipelineEntry;

    public FileDrivenPipelineRunner(
        Pipeline.PipelineEntry pipelineEntry,
        IExecutionCell? executionCell = null)
        : this(executionCell)
    {
        _pipelineEntry = pipelineEntry;
    }

    public FileDrivenPipelineContext CreateContext(
        CliOptions options, string commandFile, ...)
    {
        // 从 ExecutionPipelineEntry 迁移的上下文创建逻辑
        ...
    }

    public CentralPipeline CreatePipeline(CliOptions options)
    {
        // 从 ExecutionPipelineEntry 迁移的管线构建逻辑
        return PipelineFactory.CreateFileDrivenPipeline(options, ...);
    }
}

// ExecutionPipelineEntry（Phase 12 精简后）
public class ExecutionPipelineEntry : IEventPipelineExecutor
{
    private readonly FileDrivenPipelineRunner _runner;

    public ExecutionPipelineEntry(Pipeline.PipelineEntry pipelineEntry)
    {
        _pipelineEntry = pipelineEntry;
        _runner = new FileDrivenPipelineRunner(pipelineEntry);
    }

    // CreateContext/CreatePipeline 仍保留在 ExecutionPipelineEntry 中，
    // 但内部委托给 _runner，调用方无需修改代码
    public FileDrivenPipelineContext CreateContext(...) => _runner.CreateContext(...);
    public CentralPipeline CreatePipeline(...) => _runner.CreatePipeline(...);
}
```

### 7.3 影响范围

- `FileDrivenPipelineRunner` 新增 `CreateContext`、`CreatePipeline`、`CreateMinimalContext` 方法
- `FileDrivenPipelineRunner` 新增接受 `PipelineEntry` 的构造函数重载
- `ExecutionPipelineEntry` 构造函数自动创建 `FileDrivenPipelineRunner` 实例
- `ExecutionPipelineEntry` 的 `CreateContext`/`CreatePipeline` 委托给 `_runner`
- 调用方无需修改代码，`ExecutionPipelineEntry` 保持向后兼容

### 7.4 迁移步骤（已完成）

1. ✅ `FileDrivenPipelineRunner` 新增 `CreateContext`、`CreatePipeline`、`CreateMinimalContext` 方法
2. ✅ `FileDrivenPipelineRunner` 新增接受 `PipelineEntry` 的构造函数重载
3. ✅ `ExecutionPipelineEntry` 构造函数自动创建 `FileDrivenPipelineRunner` 实例
4. ✅ `ExecutionPipelineEntry` 的 `CreateContext`/`CreatePipeline` 委托给 `_runner`
5. ✅ 提取 `InitializeCommonContext` 静态方法消除代码重复

---

## 8. Phase 13：统一 DI 注册与配置入口

### 8.1 问题

当前 DI 注册分散在多个地方：

```csharp
// ServiceCollectionExtensions.cs
services.AddContainer(configureContainer);           // Container 框架
services.AddSingleton<FileDrivenExecutionEngine>();   // 门面
services.AddSingleton<FileDrivenPipelineRunner>();    // 管线执行器
services.AddTransient<FileDrivenContextFactory>();    // 上下文工厂
services.AddTransient<PipelineEntry>();               // 管线入口
services.AddTransient<ExecutionPipelineEntry>();      // 执行管线入口
```

### 8.2 方案

统一为 `AddFileDrivenExecution` 一个入口，内部自动注册所有组件：

```csharp
public static IServiceCollection AddFileDrivenExecution(
    this IServiceCollection services,
    Action<FileDrivenExecutionOptions>? configure = null)
{
    var options = new FileDrivenExecutionOptions();
    configure?.Invoke(options);

    // 1. 注册 Container 框架（含弹性策略）
    services.AddContainer(cfg =>
    {
        cfg.EnableRetry = options.EnableRetry;
        cfg.MaxRetries = options.MaxRetries;
        cfg.EnableTimeout = options.EnableTimeout;
        cfg.TimeoutSeconds = options.TimeoutSeconds;
        cfg.EnableCircuitBreaker = options.EnableCircuitBreaker;
        cfg.CircuitBreakerThreshold = options.CircuitBreakerThreshold;
    });

    // 2. 注册 FileDriven 组件
    services.AddSingleton<FileDrivenExecutionEngine>();
    services.AddSingleton<FileDrivenPipelineRunner>();
    services.AddTransient<FileDrivenContextFactory>();
    services.AddTransient<PipelineEntry>();
    services.AddTransient<ExecutionPipelineEntry>();

    // 3. 注册 Container Behavior（Phase 11）
    if (options.RegisterParseCommandBehavior)
        services.AddTransient<IPipelineBehavior<ExecutionRequest, ExecutionResult>, ParseCommandBehavior>();
    if (options.RegisterResultCollectBehavior)
        services.AddTransient<IPipelineBehavior<ExecutionRequest, ExecutionResult>, ResultCollectBehavior>();

    return services;
}

public class FileDrivenExecutionOptions
{
    public bool EnableRetry { get; set; } = true;
    public int MaxRetries { get; set; } = 3;
    public bool EnableTimeout { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public bool EnableCircuitBreaker { get; set; } = true;
    public int CircuitBreakerThreshold { get; set; } = 5;
    public bool RegisterParseCommandBehavior { get; set; } = true;
    public bool RegisterResultCollectBehavior { get; set; } = true;
}
```

### 8.3 影响范围

- `ServiceCollectionExtensions.AddFileDrivenExecution` 扩展方法
- 新增 `FileDrivenExecutionOptions` 配置类
- 调用方只需 `services.AddFileDrivenExecution()` 即可完成所有注册

---

## 9. 迁移路线图

### 9.1 总体路线

```
Phase 8（已完成）→ Phase 9（已完成）→ Phase 10（已完成）→ Phase 11（已完成）→ Phase 12（已完成）→ Phase 13
   修复桥接缺陷     统一结果 ✅       消除旧管线 ✅   Stage迁移 ✅   精简入口 ✅   统一注册
```

### 9.2 各阶段依赖关系

```
Phase 9（统一结果模型）✅ 已完成
    ↓
Phase 10（消除 CentralPipeline 依赖）← 依赖 Phase 9
    ↓
Phase 11（Stage → Behavior 迁移）← 依赖 Phase 10
    ↓
Phase 12（精简 ExecutionPipelineEntry）← 依赖 Phase 10 + Phase 11
    ↓
Phase 13（统一 DI 注册）← 依赖 Phase 9-12
```

### 9.3 风险与注意事项

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| `CentralPipeline` 在其他项目中使用 | ⚠️ 中 | Phase 10 保留旧重载为 `[Obsolete]`，逐步迁移 |
| Stage 迁移到 Behavior 后行为不一致 | ⚠️ 高 | Phase 11 需要充分的单元测试覆盖 |
| `ExecutionPipelineEntry` 被多处调用 | ⚠️ 中 | Phase 12 前先搜索所有调用方 |
| `FileDriven.Execution.ExecutionResult` 被外部依赖 | ⚠️ 中 | Phase 9 保留旧类型，通过继承保持兼容 |

### 9.4 测试策略

每个 Phase 完成后执行：

1. **编译检查**：`dotnet build CsharpPrj/MainProj.sln` 无错误
2. **单元测试**：运行 `FileDriven.Execution` 相关测试
3. **集成测试**：运行 `CentralTest/RunWdUITests.ps1` 验证 GUI 模式
4. **CLI 测试**：运行 CLI 模式验证命令执行

---

## 附录 A：文件变更清单

| 文件 | Phase 8 | Phase 9 | Phase 10 | Phase 11 | Phase 12 | Phase 13 |
|------|---------|---------|----------|----------|----------|----------|
| `FileDrivenExecutionEngine.cs` | ✅ 已改 | - | - | - | - | - |
| `ContainerPipelineConfigurator.cs` | ✅ 已改 | - | - | - | - | - |
| `FileDrivenPipelineRunner.cs` | ✅ 已改 | ✅ 已改 | ✅ 重写 | - | ✅ 新增方法 | - |
| `FileDrivenResultBuilder.cs` | ✅ 已改 | ✅ 已改 | - | - | - | - |
| `ExecutionResult.cs` | - | ✅ 已改 | - | - | - | - |
| `ExecutionHelper.cs` | - | ✅ 已改 | - | - | - | - |
| `FrameworkInitializer.cs` | - | ✅ 新增 | - | - | - | - |
| `FrameworkShutdown.cs` | - | ✅ 新增 | - | - | - | - |
| `PipelineFactory.cs` | - | - | - | - | - | - |
| `ExecutionPipelineEntry.cs` | - | ✅ 已改 | - | - | ✅ 委托重构 | - |
| `ServiceCollectionExtensions.cs` | - | - | - | - | - | 🔄 待完成 |
| `FileDrivenParseCommandBehavior.cs` | - | - | - | ✅ 新增 | - | - |
| `FileDrivenResultCollectBehavior.cs` | - | - | - | ✅ 新增 | - | - |
| `FileDrivenPipelineContextExtensions.cs` | - | - | - | ✅ 新增 | - | - |
| `FileDrivenExecutionOptions.cs` | - | - | - | - | - | ✨ 待新增 |

## 附录 B：关键决策记录

| 决策 | 选项 | 选择 | 理由 |
|------|------|------|------|
| 执行结果模型 | A: 包装 Container / **B: 继承 BackEndDriven** | **B** | `BackEndDriven.Models.ExecutionResult` 已实现 `IExecutionResult`，继承方式保持类型名不变 |
| CentralPipeline 废弃策略 | A: 立即删除 / B: 标记 Obsolete | B | 避免破坏其他项目引用 |
| Stage 迁移方式 | A: 整体替换 / B: 逐步替换 | B | 降低风险，每个 Behavior 独立测试 |
| DI 注册方式 | A: 自动注册 / B: 手动注册 | A | 简化调用方配置 |

---

> **文档版本**：v2.0  
> **最后更新**：2026-06-29  
> **适用项目**：WorkFlowPrj
