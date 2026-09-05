# ExecutionCenter + Container 整合方案

> **版本**：v1.0  
> **日期**：2026-06-30  
> **状态**：已确认，待实施

---

## 1. 架构总览

### 1.1 三层嵌套架构

```
┌──────────────────────────────────────────────────────────┐
│  ExecutionCenter（任务调度层）                             │
│  TaskExecutorPool                                        │
│  ┌──────────────────────────────────────────────────────┐│
│  │  CentralPipeline（管线编排层）                         ││
│  │  ScriptLoadSkeleton → ForEach                        ││
│  │  ┌────────────────────────────────────────────────┐  ││
│  │  │  Container（命令执行层）                         │  ││
│  │  │  IExecutionCell.ExecuteAsync()                 │  ││
│  │  │  ├── IPipelineBehavior: 重试                   │  ││
│  │  │  ├── IPipelineBehavior: 熔断                   │  ││
│  │  │  ├── IPipelineBehavior: 超时                   │  ││
│  │  │  └── ExecuteCommandHandler → 实际执行          │  ││
│  │  └────────────────────────────────────────────────┘  ││
│  └──────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────┘
```

### 1.2 核心原则

**所有执行路径统一经过三层架构，不存在"可选/回退"逻辑。**

| 组件 | CLI 模式 | GUI 模式（脚本执行） | GUI 模式（手动点击） |
|------|---------|---------------------|---------------------|
| ExecutionCenter | ✅ 必须 | ✅ 必须 | ✅ 必须 |
| CentralPipeline | ✅ 必须 | ✅ 必须 | ✅ 必须（单命令管线） |
| Container | ✅ 必须 | ✅ 必须 | ✅ 必须 |

---

## 2. 调用链

### 2.1 CLI 模式

```
FileDrivenExecutor.Run(args)
  → [5] InitializeFramework(cliMode=true)
    → FrameworkInitializer.Initialize()
      → 创建 TaskExecutorPool（worker=PipelineEntry）
      → 注册 "FileDrivenPipeline" handler
      → 创建 IExecutionCell
      → pool.Start()
  → [6] StartCliMode(options)
    → CreateExecutionEntry()
      → new ExecutionPipelineEntry(pipelineEntry, pool, executionCell)
    → executionEntry.RunPipeline(options, commandFile)
      → pool.SubmitAsync(FileDrivenPipelineMessage)
        → handler:
          → FileDrivenPipelineRunner.RunPipelineAsync()
            → CreatePipeline() → CentralPipeline.Execute()
              → ScriptLoadSkeleton
              → ForEach:
                → ParseCommandStage
                → ExecuteCommandStage.Process()
                  → IExecutionCell.ExecuteAsync()  ← Container 弹性策略
                → ResultCollectStage
```

### 2.2 GUI 模式（脚本执行）

```
FileDrivenExecutor.Run(args)
  → [5] InitializeFramework(cliMode=false)
    → FrameworkInitializer.Initialize()
      → 创建 TaskExecutorPool（worker=PipelineEntry）
      → 注册 "FileDrivenPipeline" handler
      → 创建 IExecutionCell
      → pool.Start()
  → [6] StartGuiMode(options)
    → CreateExecutionEntry()
      → new ExecutionPipelineEntry(pipelineEntry, pool, executionCell)
    → FileDrivenGuiInitializer.CreateAndConfigure()
      → executionEntry.RunPipeline(options, commandFile)
        → pool.SubmitAsync(FileDrivenPipelineMessage)
          → handler:
            → FileDrivenPipelineRunner.RunPipelineAsync()
              → CreatePipeline() → CentralPipeline.Execute()
                → ScriptLoadSkeleton
                → ForEach:
                  → ParseCommandStage
                  → ExecuteCommandStage.Process()
                    → IExecutionCell.ExecuteAsync()  ← Container 弹性策略
                  → ResultCollectStage
```

### 2.3 GUI 模式（手动点击）

```
用户点击按钮
  → WinFormsEventBinder 触发
    → IEventPipelineExecutor.Execute(controlName, eventType, value)
      → GetOrCreateSingleCommandPipeline()
        → PipelineFactory.BuildCommandPipeline(..., executionCell)
      → pipeline.Execute(context)
        → ParseCommandStage
        → ExecuteCommandStage.Process()
          → IExecutionCell.ExecuteAsync()  ← Container 弹性策略
        → ResultCollectStage
```

---

## 3. 改造步骤

### 步骤 1：FrameworkInitializer — GUI/CLI 统一创建 ExecutionPool

**文件**：`FrameworkInitializer.cs`

**改动**：
- 删除 `RegisterExecutionCenterHandler()` 中 `if (!cliMode) return null` 的 GUI 跳过逻辑
- GUI 模式和 CLI 模式都创建 `TaskExecutorPool`
- 将 `PipelineEntry` 作为 `worker` 参数传入 `TaskExecutorPool` 构造函数
- handler 改为实际执行管线（见步骤 2）

```csharp
// 修改前：
private static ITaskExecutorPool? RegisterExecutionCenterHandler(bool cliMode)
{
    if (!cliMode)
    {
        // GUI 模式跳过 ← 删除
        return null;
    }
    // ...
}

// 修改后：
private static ITaskExecutorPool RegisterExecutionCenterHandler(
    IExecutionCell executionCell)
{
    var pool = new TaskExecutorPool();
    pool.RegisterHandler("FileDrivenPipeline",
        (msg, worker, ct) => HandleFileDrivenPipeline(msg, worker, executionCell, ct));
    return pool;
}
```

**注意**：`FrameworkInitializer.Initialize()` 的签名需要调整：
- 删除 `cliMode` 参数（不再需要）
- 新增 `IExecutionCell executionCell` 参数
- `executionPool` 输出改为非 nullable

### 步骤 2：修复 ExecutionCenter handler — 实际执行管线

**文件**：`FrameworkInitializer.cs`

**改动**：将空操作 handler 替换为实际调用 `FileDrivenPipelineRunner.RunPipelineAsync()`

```csharp
private static async Task<ITaskResponse> HandleFileDrivenPipeline(
    ITaskMessage msg,
    object? worker,
    IExecutionCell executionCell,
    CancellationToken ct)
{
    var pipelineMsg = (FileDrivenPipelineMessage)msg;

    // 从 SharedData 获取 PipelineEntry
    var pipelineEntry = pipelineMsg.SharedData?["PipelineEntry"]
        as FileDriven.Pipeline.PipelineEntry;
    if (pipelineEntry == null)
        return new TaskResponse(pipelineMsg.MessageId, false,
            error: "PipelineEntry not found in SharedData");

    // 从 SharedData 获取 GUI 上下文
    var targetForm = pipelineMsg.SharedData?["TargetForm"] as Form;
    var uiCommandManager = pipelineMsg.SharedData?["UICommandManager"];
    var winFormsEventBinder = pipelineMsg.SharedData?["WinFormsEventBinder"]
        as IWinFormsEventBinder;

    // 重建 CliOptions
    var options = new CliOptions
    {
        ConfigPath = pipelineMsg.ConfigPath,
        AutoClose = pipelineMsg.AutoClose,
        PipeName = pipelineMsg.PipeName,
        PipeMode = pipelineMsg.PipeMode
    };

    // 创建 Runner（IExecutionCell 是必填参数）
    var runner = new FileDrivenPipelineRunner(pipelineEntry, executionCell);

    // 执行管线
    var (results, success) = await runner.RunPipelineAsync(
        options, pipelineMsg.CommandFile,
        targetForm, uiCommandManager, winFormsEventBinder, ct);

    // 构建结果
    var executionResult = new BackEndDriven.Models.ExecutionResult
    {
        Success = success,
        CommandResults = results,
        Mode = pipelineMsg.Mode
    };

    return new TaskResponse(pipelineMsg.MessageId, success,
        result: executionResult);
}
```

### 步骤 3：ExecuteCommandStage 集成 Container

**文件**：`ExecuteCommandStage.cs`

**改动**：
- 新增 `IExecutionCell` 构造函数参数
- 事件命令执行委托给 Container
- Process 改为异步（`ProcessAsync`）

```csharp
public class ExecuteCommandStage : PipelineStageBase<FileDrivenPipelineContext>
{
    private readonly IExecutionCell _executionCell;  // ★ 必填
    // ... 原有字段保留（findBindingFunc, functionExecutor 等作为回退）

    public ExecuteCommandStage(
        Func<string, string, (...)>? findBindingFunc,
        IFunctionExecutor? functionExecutor,
        IExecutionCell executionCell,  // ★ 必填参数
        INonEventCommandLookup? nonEventCommandLookup = null)
        : base("ExecuteCommand", order: 20)
    {
        _findBindingFunc = findBindingFunc;
        _functionExecutor = functionExecutor;
        _executionCell = executionCell;
        _nonEventCommandLookup = nonEventCommandLookup;
    }

    public override async Task ProcessAsync(FileDrivenPipelineContext ctx)
    {
        if (!ctx.ParseSuccess || ctx.ParsedCommand == null)
            return;

        try
        {
            var action = ctx.ParsedAction;
            if (string.IsNullOrWhiteSpace(action))
            {
                ctx.FunctionExecuteSuccess = false;
                ctx.FunctionErrorMessage = "Command action is empty";
                return;
            }

            // 非事件命令处理（不变）
            if (FileDrivenPipelineContext.IsNonEventCommandAction(action))
            {
                HandleNonEventCommand(ctx);
                return;
            }

            // ★ 事件命令：统一走 Container
            var request = FileDrivenExecutionEngine.CreateExecutionRequest(
                ctx.CurrentCommand, ctx);
            var result = await _executionCell.ExecuteAsync(request);

            ctx.FunctionExecuteSuccess = result.Success;
            ctx.FunctionResultValue = result.OutputValue;
            ctx.FunctionErrorMessage = result.ErrorMessage;
        }
        catch (Exception ex)
        {
            ctx.FunctionExecuteSuccess = false;
            ctx.FunctionErrorMessage = $"Execute error: {ex.Message}";
        }
    }
}
```

**注意**：`PipelineStageBase` 需要支持异步 `ProcessAsync`。如果基类只有同步 `Process`，则需要：
- 在 `PipelineStageBase` 中新增 `virtual Task ProcessAsync(TContext ctx)` 方法
- 或使用 `Task.Run(() => Process(ctx))` 包装（不推荐）

### 步骤 4：PipelineFactory 传递 IExecutionCell

**文件**：`PipelineFactory.cs`

**改动**：`BuildCommandPipeline` 和 `CreateFileDrivenPipeline` 增加 `IExecutionCell` 参数

```csharp
public static IPipeline<...> BuildCommandPipeline(
    IEventBindingResolver? bindingResolver,
    IFunctionExecutor? functionExecutor,
    IExecutionCell executionCell)  // ★ 必填
{
    var executeStage = new ExecuteCommandStage(
        findBindingFunc, functionExecutor, executionCell);
    // ...
}

public static CentralPipeline CreateFileDrivenPipeline(
    CliOptions options,
    IEventBindingResolver? bindingResolver,
    IFunctionExecutor? functionExecutor,
    IExecutionCell executionCell)  // ★ 必填
{
    var commandPipeline = BuildCommandPipeline(
        bindingResolver, functionExecutor, executionCell);
    // ...
}
```

### 步骤 5：FileDrivenPipelineRunner — IExecutionCell 改为必填

**文件**：`FileDrivenPipelineRunner.cs`

**改动**：
- `IExecutionCell` 改为必填构造函数参数
- 删除基于 `IExecutionCell` 的独立 `RunPipelineAsync(string[], ...)` 方法
- 统一走 `CreatePipeline` → `CentralPipeline.Execute()`

```csharp
public class FileDrivenPipelineRunner
{
    private readonly IExecutionCell _executionCell;  // ★ 必填
    private readonly FileDriven.Pipeline.PipelineEntry _pipelineEntry;

    // ★ 唯一构造函数
    public FileDrivenPipelineRunner(
        FileDriven.Pipeline.PipelineEntry pipelineEntry,
        IExecutionCell executionCell)
    {
        _pipelineEntry = pipelineEntry ?? throw new ArgumentNullException(nameof(pipelineEntry));
        _executionCell = executionCell ?? throw new ArgumentNullException(nameof(executionCell));
    }

    // ★ 删除：RunPipelineAsync(string[], FileDrivenPipelineContext, ...)
    //   此方法绕过了 CentralPipeline 的 ScriptLoadSkeleton 和 ForEach

    // ★ CreatePipeline 传入 executionCell
    public CentralPipeline CreatePipeline(CliOptions options)
    {
        return PipelineFactory.CreateFileDrivenPipeline(
            options,
            _pipelineEntry.BindingResolver as IEventBindingResolver,
            _pipelineEntry.FunctionExecutor as IFunctionExecutor,
            _executionCell);
    }
}
```

### 步骤 6：ExecutionPipelineEntry — 删除回退路径

**文件**：`ExecutionPipelineEntry.cs`

**改动**：
- `_pool` 改为必填（非 nullable）
- 删除 `_pool == null` 的回退路径
- `RunPipelineAsync` 简化为纯调度分发

```csharp
public class ExecutionPipelineEntry : IEventPipelineExecutor
{
    private readonly ITaskExecutorPool _pool;  // ★ 必填，不再为 null
    private readonly IExecutionCell _executionCell;  // ★ 新增

    // ★ 唯一构造函数
    public ExecutionPipelineEntry(
        FileDriven.Pipeline.PipelineEntry pipelineEntry,
        ITaskExecutorPool pool,
        IExecutionCell executionCell)
    {
        _pipelineEntry = pipelineEntry ?? throw new ArgumentNullException(nameof(pipelineEntry));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _executionCell = executionCell ?? throw new ArgumentNullException(nameof(executionCell));
        _runner = new FileDrivenPipelineRunner(pipelineEntry, executionCell);
    }

    public async Task<(List<...> Results, bool Success)> RunPipelineAsync(
        CliOptions options, string commandFile, ..., CancellationToken ct)
    {
        using var guard = SetScriptExecutionMode(winFormsEventBinder);

        // ★ 统一走 ExecutionCenter 调度，无回退路径
        var sharedData = BuildSharedData(/* ... */);
        var message = new FileDrivenPipelineMessage(commandFile, mode)
        {
            ConfigPath = options.ConfigPath,
            AutoClose = options.AutoClose,
            PipeName = options.PipeName,
            PipeMode = options.PipeMode,
            SharedData = sharedData
        };

        var response = await _pool.SubmitAsync(message, ct);

        if (response.Success && response.Result is BackEndDriven.Models.ExecutionResult er)
            return (er.CommandResults, er.Success);

        return (new List<...> { new() { ... } }, false);
    }
}
```

### 步骤 7：删除 ContainerPipelineConfigurator

**文件**：`ContainerPipelineConfigurator.cs`

**理由**：Container 集成已内化到 `ExecuteCommandStage` 和 `PipelineFactory` 中，不再需要独立的 `ContainerPipelineConfigurator`。

**操作**：删除整个文件。

### 步骤 8：FileDrivenExecutor — 统一创建 ExecutionPool 和 IExecutionCell

**文件**：`FileDrivenExecutor.cs`

**改动**：
- `InitializeFrameworkImpl` 中创建 `IExecutionCell` 和 `TaskExecutorPool`
- `CreateExecutionEntry` 传入 `IExecutionCell`
- 在 9 步流程的合适位置调用 `pool.Start()`

```csharp
public class FileDrivenExecutor : FrameworkExecutor
{
    internal ITaskExecutorPool ExecutionPool { get; set; }
    internal IExecutionCell ExecutionCell { get; set; }

    private void InitializeFrameworkImpl()
    {
        // 1. 创建 IExecutionCell（Container 初始化）
        ExecutionCell = CreateExecutionCell();

        // 2. 初始化框架其他组件
        FrameworkInitializer.Initialize(
            out var projectRoot,
            out var commandManager,
            out var templateProcessor,
            out var functionStorageLifecycle,
            out var fileDrivenLifecycle,
            out var executionPool,
            ExecutionCell);  // ★ 传入 IExecutionCell

        ExecutionPool = executionPool;
        // ...
    }

    private IExecutionCell CreateExecutionCell()
    {
        // Container 初始化逻辑：
        // 1. 创建 MediatR 的 ISender
        // 2. 注册 IPipelineBehavior（重试/熔断/超时）
        // 3. 注册 IRequestHandler<IExecutionRequest, ExecutionResult>
        // 4. 创建并返回 IExecutionCell 实例
        // ...
    }

    private PipelineEntry CreateExecutionEntry(out ExecutionPipelineEntry executionEntry)
    {
        var pipelineEntry = new PipelineEntry(new FunctionStorageFunctionExecutor());
        executionEntry = new ExecutionPipelineEntry(
            pipelineEntry, ExecutionPool, ExecutionCell);
        CommandFrameworkUsage.RegisterServices(configLoader: pipelineEntry);
        return pipelineEntry;
    }
}
```

**TaskExecutorPool.Start() 调用时机**：在 `InitializeFrameworkImpl` 中创建 `ExecutionPool` 后立即调用 `pool.Start()`，或在 9 步流程的步骤 5（InitializeFramework）和步骤 6（ExecuteMode）之间调用。

---

## 4. 涉及文件清单

| # | 文件 | 操作 | 说明 |
|---|------|------|------|
| 1 | `FrameworkInitializer.cs` | 修改 | 删除 GUI 跳过逻辑；handler 改为实际执行管线；签名调整 |
| 2 | `ExecuteCommandStage.cs` | 修改 | 新增 `IExecutionCell` 必填参数；事件命令走 Container；异步 ProcessAsync |
| 3 | `PipelineFactory.cs` | 修改 | `BuildCommandPipeline`/`CreateFileDrivenPipeline` 传递 `IExecutionCell` |
| 4 | `FileDrivenPipelineRunner.cs` | 修改 | `IExecutionCell` 改为必填；删除独立的 Container RunPipelineAsync |
| 5 | `ExecutionPipelineEntry.cs` | 修改 | `_pool`/`_executionCell` 改为必填；删除回退路径 |
| 6 | `ContainerPipelineConfigurator.cs` | 删除 | 职责已被步骤 2-3 吸收 |
| 7 | `FileDrivenExecutor.cs` | 修改 | 创建 `IExecutionCell`；`CreateExecutionEntry` 传入；`pool.Start()` |
| 8 | `FileDrivenGuiInitializer.cs` | 无改动 | ExecutionPool 已在 CreateExecutionEntry 中创建 |

---

## 5. 注意事项

### 5.1 PipelineStageBase 异步支持

`ExecuteCommandStage.Process()` 需要改为异步。如果 `PipelineStageBase<TContext>` 没有 `ProcessAsync` 方法，需要：

- 在基类中新增 `virtual Task ProcessAsync(TContext ctx)` 方法
- 默认实现：`return Task.Run(() => Process(ctx))`
- `DelegatePipeline` 的 `asyncWithCtx` 委托中调用 `ProcessAsync`

### 5.2 IExecutionCell 的创建

`IExecutionCell` 的创建涉及 Container 框架的初始化：
- MediatR 的 `ISender`/`IMediator` 实例
- `IPipelineBehavior` 链的注册（重试、熔断、超时、日志）
- `IRequestHandler<ExecutionRequest, ExecutionResult>` 的注册（`ExecuteCommandHandler`）

建议在 `FileDrivenExecutor` 中创建，或提取为独立的 `ContainerInitializer`。

### 5.3 TaskExecutorPool.Start() 的线程安全

`TaskExecutorPool.Start()` 内部有锁保护，可安全调用多次。建议在 `InitializeFrameworkImpl` 末尾调用。

### 5.4 向后兼容

- `ExecutionPipelineEntry` 的旧构造函数（无 `IExecutionCell`/`ITaskExecutorPool`）需要删除
- `FileDrivenPipelineRunner` 的旧构造函数（无 `IExecutionCell`）需要删除
- 所有调用方需要更新

---

## 6. 整合后的调用链对比

### 改造前

```
CLI 模式：
  ExecutionPipelineEntry.RunPipelineAsync()
    ├── _pool != null → SubmitAsync → handler（空操作）← Bug
    └── _pool == null → Task.Run → RunPipeline → CentralPipeline.Execute()
                                                    └── ExecuteCommandStage（无 Container）

GUI 模式（脚本）：
  ExecutionPipelineEntry.RunPipeline()
    → CentralPipeline.Execute()
      └── ExecuteCommandStage（无 Container）

GUI 模式（手动点击）：
  IEventPipelineExecutor.Execute()
    → GetOrCreateSingleCommandPipeline()
      └── ExecuteCommandStage（无 Container）
```

### 改造后

```
CLI 模式：
  ExecutionPipelineEntry.RunPipelineAsync()
    → pool.SubmitAsync(FileDrivenPipelineMessage)
      → handler → FileDrivenPipelineRunner.RunPipelineAsync()
        → CentralPipeline.Execute()
          → ExecuteCommandStage.ProcessAsync()
            → IExecutionCell.ExecuteAsync() ← Container 弹性策略

GUI 模式（脚本）：
  ExecutionPipelineEntry.RunPipelineAsync()
    → pool.SubmitAsync(FileDrivenPipelineMessage)
      → handler → FileDrivenPipelineRunner.RunPipelineAsync()
        → CentralPipeline.Execute()
          → ExecuteCommandStage.ProcessAsync()
            → IExecutionCell.ExecuteAsync() ← Container 弹性策略

GUI 模式（手动点击）：
  IEventPipelineExecutor.Execute()
    → GetOrCreateSingleCommandPipeline()
      → ExecuteCommandStage.ProcessAsync()
        → IExecutionCell.ExecuteAsync() ← Container 弹性策略
```

**三条路径统一经过 Container，不存在任何回退逻辑。**
