# FileDriven 调用函数逻辑整合 ExecutionCenter 重构方案

## 概述

本文档分析 FileDriven 项目中调用函数逻辑部分（`FileDriven.Execution` 和 `FileDriven.Pipeline`）的执行状态问题，并设计如何将 `Share/ExecutionCenter`（执行中心）作为统一任务调度层插入其中，以解决状态不一致、时序依赖、GUI 阻塞等问题。

---

## 一、ExecutionCenter 实际架构（当前实现）

ExecutionCenter 位于 `Share/ExecutionCenter/`，其核心实现**不定义自己的 Worker 上下文**，而是直接使用 CommandFramework 的上层基础设施 `PipelineEntryInstance`：

```
TaskExecutorPool
  ├── TaskQueue（包装 PriorityMessageQueue，请求-响应模式）
  ├── PipelineEntryInstance（来自 CommandFramework，作为执行上下文）
  │     ├── LoadedConfigs 字典
  │     ├── CommandParser
  │     ├── INonEventCommandLookup
  │     └── 通用服务（IConfigLoader / IEventBindingResolver / IUIController / IStateQuery）
  └── 处理器字典：ConcurrentDictionary<string, Func<ITaskMessage, PipelineEntryInstance, CancellationToken, Task<ITaskResponse>>>
        ├── "LoadConfig"      → ctx.LoadConfig(msg.ConfigPath, msg.ShowForm)
        └── "ExecuteCommand"  → ctx.RunCommandAsync(msg.Command)
```

**关键设计决策**：
- `TaskExecutorPool` 的构造函数接收外部传入的 `PipelineEntryInstance`，遵循"谁创建谁释放"原则
- 处理器签名直接使用 `PipelineEntryInstance` 作为上下文，不额外包装
- `CommandFrameworkAdapter` 负责注册 `LoadConfig` 和 `ExecuteCommand` 两个标准处理器

---

## 二、FileDriven 调用函数逻辑现状

### 2.1 两条执行路径

#### 路径 A：自动脚本执行（CLI / GUI 模式）

```
FileDrivenExecutor.Run()
  → StartCliMode() / StartGuiMode()
    → new PipelineEntry()                    ← FileDriven.Pipeline.PipelineEntry
    → new ExecutionPipelineEntry(pipelineEntry)
    → CommandFrameworkUsage.RegisterServices(configLoader: pipelineEntry)
    → executionEntry.RunPipeline(options, commandFile)
      → CreateContext()                      ← 创建 FileDrivenPipelineContext
      → CreatePipeline()                     ← PipelineFactory.CreateFileDrivenPipeline()
        → CentralPipeline<FileDrivenPipelineContext>
          → [PreProcessing] ScriptLoadSkeleton
          → [Execution] ForEach(CommandLoop)
            → 对每条命令：
              → ctx.Reset()
              → commandPipeline.ExecuteAsync(ctx)
                → ParseCommandStage
                → ExecuteCommandStage
                → ResultCollectStage
      → 返回 (Results, Success)
```

#### 路径 B：手动点击路由（GUI 模式）

```
WinForms 控件事件触发
  → WinFormsEventBinder (IWinFormsEventBinder)
    → ExecutionPipelineEntry.Execute()       ← IEventPipelineExecutor 同步接口
      → GetOrCreateSingleCommandPipeline()
        → PipelineFactory.BuildCommandPipeline()
          → CommandFramework.PipelineEntry.CreateDefaultPipeline()
            → ParseCommandStage
            → ExecuteCommandStage
            → ResultCollectStage
      → pipeline.Execute(context)            ← 同步执行
```

### 2.2 关键类职责

| 类 | 项目 | 职责 |
|----|------|------|
| `FileDrivenExecutor` | `FileDriven.Execution` | 五层架构统一执行门面，管理完整生命周期 |
| `ExecutionPipelineEntry` | `FileDriven.Execution/Command` | 执行管线入口，持有 `PipelineEntry` 引用，提供上下文创建/管线构建/执行方法 |
| `PipelineEntry` (FileDriven) | `FileDriven.Pipeline` | 共享状态持有者：`LoadedConfigs`、`EventBindingResolver`、`FunctionExecutor`、`ConfigCache` |
| `PipelineFactory` | `FileDriven.Execution/Command` | 静态工厂，构建 `CentralPipeline<FileDrivenPipelineContext>` |
| `FileDrivenPipelineContext` | `FileDriven.Pipeline/CommandFramework` | 管线上下文，继承 `CommandContext`，包含脚本加载/命令执行/运行时依赖 |
| `PipelineEntryInstance` | `CommandFramework.Pipeline` | CommandFramework 的管线入口实例类，管理 `LoadedConfigs` 和执行历史 |

### 2.3 两个 PipelineEntry 的状态分裂

这是最核心的问题：

```
FileDriven.Pipeline.PipelineEntry                  CommandFramework.Pipeline.PipelineEntryInstance
┌─────────────────────────────────────┐            ┌─────────────────────────────────────┐
│  _loadedConfigs: ConcurrentDict     │            │  _loadedConfigs: Dictionary         │
│  _bindingResolver: EventBindingRes  │            │  _configLoader: IConfigLoader?       │
│  _functionExecutor: IFunctionExec   │            │  _bindingResolver: IEventBindingRes? │
│  _configCache: ConfigurationCache   │            │  _parser: CommandParser              │
│                                     │            │  _nonEventLookup: INonEventCmdLookup │
│  IConfigLoader.LoadConfig() →       │  ──注册──→  │  (通过 RegisterServices 注入)        │
│    写入 _loadedConfigs              │            │                                     │
└─────────────────────────────────────┘            │  LoadConfig() → 委托给 _configLoader │
                                                   │  FindBinding() → 委托给 _bindingRes  │
                                                   └─────────────────────────────────────┘
```

- `FileDriven.Pipeline.PipelineEntry` 是**真正的状态持有者**，`LoadedConfigs` 在这里维护
- `CommandFramework.Pipeline.PipelineEntryInstance` 通过 `IConfigLoader` 接口委托配置加载
- 但 `PipelineEntryInstance` 有自己的 `_loadedConfigs` 字典（用于 `CreateContext()` 方法），与 FileDriven 的字典**不是同一个实例**
- `ExecutionPipelineEntry.CreateContext()` 第 159 行：`LoadedConfigs = new Dictionary<string, object>(_pipelineEntry.LoadedConfigs, ...)` — **复制了一份副本**

---

## 三、执行状态问题详细分析

### 问题 1：LoadedConfigs 副本导致状态不一致

**位置**：`ExecutionPipelineEntry.CreateContext()` 第 159 行

```csharp
LoadedConfigs = new Dictionary<string, object>(_pipelineEntry.LoadedConfigs, StringComparer.OrdinalIgnoreCase),
```

每次创建 `FileDrivenPipelineContext` 时，都从 `PipelineEntry.LoadedConfigs` **复制一份快照**。如果：
1. 先创建了 Context A（快照了当时的 LoadedConfigs）
2. 然后执行了 sideload 命令（修改了 PipelineEntry.LoadedConfigs）
3. 再使用 Context A 执行 click 命令

→ Context A 中的 LoadedConfigs 是旧快照，找不到新加载的配置绑定。

**影响**：GUI 模式下，`EventBindingResolver` 查找控件绑定时可能找不到 sideload 加载的配置。

### 问题 2：GUI 点击事件同步阻塞

**位置**：`ExecutionPipelineEntry.Execute()`（`IEventPipelineExecutor` 实现）

```csharp
bool IEventPipelineExecutor.Execute(string controlName, string eventType, string value)
{
    // ...
    pipeline.Execute(context);  // 同步执行，在 GUI 线程上阻塞
    // ...
}
```

`IEventPipelineExecutor` 接口是同步的，但内部管线执行是异步的（`StagePipeline.ExecuteAsync`）。同步包装可能导致：
- GUI 线程阻塞
- 在特定同步上下文中死锁（虽然当前用 `Task.Run` 避免了）

### 问题 3：非事件命令处理分散

**位置**：`ExecuteCommandStage.HandleNonEvent()` 第 306-376 行

非事件命令的处理逻辑分散在**三处**：
1. `ExecuteCommandStage.HandleNonEvent()` — 内联处理（回退路径）
2. `FunctionStorageRegistry.NonEventRegistry` — 注册的处理函数
3. `FileDrivenExecutor.RegisterNonEventCommands()` — 注册 sideload/sleep/close/exit/echo

虽然步骤四（P1）已引入 `INonEventCommandLookup` 优先查找，但仍有内联回退路径，导致同一命令可能被处理两次或行为不一致。

### 问题 4：配置加载与命令执行时序

**位置**：`ExecuteCommandStage.HandleSideload()` 第 429-454 行

```csharp
private static void HandleSideload(CommandContext context, string? target)
{
    // ...
    var result = CommandFrameworkUsage.LoadConfig(target, showForm: true);
    // ...
}
```

`sideload` 直接调用 `CommandFrameworkUsage.LoadConfig()`，这是一个同步调用，直接写入 `PipelineEntryInstance` 的 `LoadedConfigs`。但：
- 如果此时有其他命令正在执行，存在并发写入风险
- 配置加载完成后，已创建的 `FileDrivenPipelineContext` 中的 `LoadedConfigs` 副本不会自动更新

### 问题 5：异常处理分散

异常在以下多处捕获，缺乏统一策略：
- `TaskExecutorPool.LoopAsync()` — 记录到 Debug
- `ExecutionPipelineEntry.Execute()` — 记录到日志
- `PipelineFactory` 的 `onItemAfter` 回调 — 记录到日志
- `FileDrivenExecutor.Run()` — 记录到日志

---

## 四、整合方案：ExecutionCenter 作为统一任务调度层

### 4.1 核心思路

将 ExecutionCenter 的 `TaskExecutorPool` 作为 **FileDriven 所有命令执行的统一入口**，替代当前的直接管线调用。具体来说：

1. **FileDriven 不再直接创建和执行 `CentralPipeline`**
2. **改为将命令封装为 `ITaskMessage` 提交到 `TaskExecutorPool`**
3. **`TaskExecutorPool` 的 `PipelineEntryInstance` 作为唯一状态持有者**
4. **`FileDriven.Pipeline.PipelineEntry` 通过 `IConfigLoader` 接口与 `PipelineEntryInstance` 共享状态**

### 4.2 架构变更

```
当前架构：

FileDrivenExecutor
  → ExecutionPipelineEntry.RunPipeline()    ← 直接创建 CentralPipeline 并执行
  → ExecutionPipelineEntry.Execute()         ← 直接创建单命令管线并执行

目标架构：

FileDrivenExecutor
  → ExecutionPipelineEntry.RunPipelineAsync()
    → 逐条命令封装为 ExecuteCommandMessage
    → TaskExecutorPool.SubmitAsync()         ← 消息队列调度
      → PipelineEntryInstance.RunCommandAsync()
        → CommandFramework 管线执行
    → 收集结果返回

  GUI 点击事件
    → WinFormsEventBinder
      → FileDrivenAdapter.Execute()
        → TaskExecutorPool.SubmitAsync(ExecuteCommandMessage.CreateGuiClick())
          → PipelineEntryInstance.RunCommandAsync()
            → CommandFramework 管线执行
```

### 4.3 状态共享方案

消除两个 PipelineEntry 的状态分裂：

```
FileDriven.Pipeline.PipelineEntry                  CommandFramework.Pipeline.PipelineEntryInstance
┌─────────────────────────────────────┐            ┌─────────────────────────────────────┐
│  _loadedConfigs: ConcurrentDict     │            │  _loadedConfigs: Dictionary         │
│  _bindingResolver: EventBindingRes  │            │  _configLoader: IConfigLoader? ──────┼──→ 委托给 FileDriven.PipelineEntry
│  _functionExecutor: IFunctionExec   │  ←──注入───│  _bindingResolver: IEventBindingRes? │
│  _configCache: ConfigurationCache   │            │  _parser: CommandParser              │
│                                     │            │  _nonEventLookup: INonEventCmdLookup │
│  IConfigLoader 实现                  │            │                                     │
│    LoadConfig() → 写入 _loadedConfigs│            │  CreateContext() → 共享 _loadedConfigs│
└─────────────────────────────────────┘            └─────────────────────────────────────┘
                                                              │
                                                    TaskExecutorPool 持有此实例
                                                    作为所有命令执行的上下文
```

**关键变更**：
1. `PipelineEntryInstance.CreateContext()` 应使用与 `IConfigLoader` 相同的 `LoadedConfigs` 字典实例（而非自己的副本）
2. `ExecutionPipelineEntry.CreateContext()` 不再复制 `LoadedConfigs`，改为引用同一实例
3. `TaskExecutorPool` 的 `PipelineEntryInstance` 是唯一执行上下文

---

## 五、具体代码修改

### 修改 1：FileDrivenExecutor.cs — 集成 ExecutionCenter

在 `StartGuiMode()` 和 `StartCliMode()` 中创建 `TaskExecutorPool`：

```csharp
// 在 InitializeFramework() 之后，创建 PipelineEntry 之后
// 创建 TaskExecutorPool，复用 CommandFrameworkUsage 的 PipelineInstance
var pool = CommandFrameworkAdapter.CreatePool(
    CommandFrameworkUsage.PipelineInstance);

// 注册 FileDriven 专用处理器（如完整管线执行）
pool.RegisterHandler("FileDrivenPipeline", async (task, ctx, ct) =>
{
    if (task is not FileDrivenPipelineMessage msg)
        return new TaskResponse(task.MessageId, false, error: "Invalid message type");

    // 执行 FileDriven 完整管线
    var context = CreateFileDrivenContext(msg.Options, msg.CommandFile, ...);
    var pipeline = CreateFileDrivenPipeline(msg.Options, ...);
    pipeline.Execute(context);

    return new TaskResponse(task.MessageId, !context.HasError, 
        result: (context.CommandResults, context.FailCount == 0));
});

pool.Start();
```

### 修改 2：ExecutionPipelineEntry.cs — 通过 ExecutionCenter 调度

#### RunPipeline 改为异步版本

```csharp
/// <summary>
/// 通过 ExecutionCenter 运行完整管线
/// 将每条命令提交到 TaskExecutorPool，由 Worker 统一调度
/// </summary>
public async Task<(List<CommandFileLineResult> Results, bool Success)> RunPipelineAsync(
    CliOptions options,
    string commandFile,
    Form? targetForm = null,
    object? uiCommandManager = null,
    IWinFormsEventBinder? winFormsEventBinder = null,
    TaskExecutorPool? pool = null)
{
    var context = CreateContext(options, commandFile, targetForm, uiCommandManager, winFormsEventBinder);
    
    if (pool != null)
    {
        // 通过 ExecutionCenter 调度
        var response = await pool.SubmitAsync(
            new FileDrivenPipelineMessage
            {
                Options = options,
                CommandFile = commandFile,
                Context = context
            });
        // 从 response.Result 中提取结果
        return ExtractResults(response, context);
    }
    else
    {
        // 回退：直接执行（向后兼容）
        var pipeline = CreatePipeline(options);
        pipeline.Execute(context);
        return (context.CommandResults, context.FailCount == 0);
    }
}
```

#### Execute 改为通过 ExecutionCenter

```csharp
bool IEventPipelineExecutor.Execute(string controlName, string eventType, string value)
{
    if (_pool == null)
    {
        // 回退：直接执行（向后兼容）
        return ExecuteDirect(controlName, eventType, value);
    }

    // 通过 ExecutionCenter 调度（Critical 优先级）
    var task = ExecuteCommandMessage.CreateGuiClick(controlName, eventType);
    var response = Task.Run(() => _pool.SubmitAsync(task)).GetAwaiter().GetResult();
    return response.Success;
}
```

### 修改 3：消除 LoadedConfigs 副本

#### ExecutionPipelineEntry.CreateContext()

```csharp
public FileDrivenPipelineContext CreateContext(...)
{
    var context = new FileDrivenPipelineContext
    {
        // ★ 不再复制副本，直接引用 PipelineEntry 的 LoadedConfigs
        //   通过 IConfigLoader 接口访问，确保状态一致
        LoadedConfigs = _pipelineEntry.LoadedConfigs as IDictionary<string, object>
            ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
        // ...
    };
    return context;
}
```

**注意**：`_pipelineEntry.LoadedConfigs` 是 `IReadOnlyDictionary<string, object>`，需要确保 `FileDrivenPipelineContext.LoadedConfigs` 是可写的 `IDictionary<string, object>`。如果 `PipelineEntry` 内部使用 `ConcurrentDictionary`，可以通过 `GetLoadedConfigsDictionary()` 方法获取可变引用。

### 修改 4：FileDrivenAdapter 增强

```csharp
public class FileDrivenAdapter : IEventPipelineExecutor
{
    private readonly TaskExecutorPool _pool;

    public FileDrivenAdapter(TaskExecutorPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    public bool Execute(string controlName, string eventType, string value)
    {
        var task = ExecuteCommandMessage.CreateGuiClick(controlName, eventType);
        // ★ 使用 Task.Run 避免 GUI 上下文死锁
        var response = Task.Run(() => _pool.SubmitAsync(task)).GetAwaiter().GetResult();
        return response.Success;
    }
}
```

### 修改 5：消除非事件命令双重注册

`ExecuteCommandStage.HandleNonEvent()` 中的内联处理应完全移除，所有非事件命令统一通过 `INonEventCommandLookup` 查找和执行：

```csharp
private async Task HandleNonEvent(CommandContext context, string action, string? target)
{
    // ★ 仅通过 INonEventCommandLookup 查找，移除内联回退路径
    if (_nonEventCommandLookup != null)
    {
        if (_nonEventCommandLookup.TryGetHandler(action, out var handler) && handler != null)
        {
            handler.Invoke(context);
            context.SetStageResult(Name, true);
            return;
        }
    }

    // ★ 不再有内联的 switch 回退
    context.SetStageResult(Name, false, $"Unknown non-event command: {action}");
}
```

所有非事件命令（sideload/sleep/close/exit/echo 等）统一在 `FunctionStorageRegistry.NonEventRegistry` 中注册。

### 修改 6：配置加载统一路由

`sideload` 命令的处理改为通过 `LoadConfigMessage`：

```csharp
// 在 FunctionStorageRegistry.NonEventRegistry 中注册 sideload
registry.Register("sideload", context =>
{
    var target = context.ParsedCommand?.Target ?? string.Empty;
    if (string.IsNullOrWhiteSpace(target))
    {
        context.FunctionExecuteSuccess = false;
        context.FunctionErrorMessage = "sideload requires a target (config path)";
        return;
    }

    try
    {
        // ★ 通过 ExecutionCenter 的 LoadConfigMessage 路由
        //   如果 ExecutionCenter 可用，提交 LoadConfigMessage
        //   否则直接调用 CommandFrameworkUsage.LoadConfig()
        if (CommandFrameworkUsage.IsInitialized)
        {
            var result = CommandFrameworkUsage.LoadConfig(target, showForm: true);
            // ...
        }
    }
    catch (Exception ex) { ... }
}, CommandCriticality.Important, "Load UI config into side container");
```

---

## 六、整合后的完整调用链

### CLI 模式

```
FileDrivenExecutor.Run(args)
  → InitializeFramework()
    → CommandFrameworkUsage.Reset()           ← 初始化 CommandFramework
    → CommandFrameworkAdapter.CreatePool()    ← 创建 TaskExecutorPool
  → StartCliMode(options)
    → new PipelineEntry()                     ← FileDriven 状态持有者
    → new ExecutionPipelineEntry(pipelineEntry)
    → CommandFrameworkUsage.RegisterServices(configLoader: pipelineEntry)
    → executionEntry.RunPipelineAsync(options, commandFile, pool: _pool)
      → 创建 FileDrivenPipelineContext
      → 提交 FileDrivenPipelineMessage 到 _pool
        → TaskExecutorPool 处理
          → ScriptLoadSkeleton 加载脚本
          → ForEach 遍历命令
            → 每条命令 → ExecuteCommandMessage
              → PipelineEntryInstance.RunCommandAsync()
                → ParseCommandStage → ExecuteCommandStage → ResultCollectStage
      → 收集结果返回
```

### GUI 模式

```
FileDrivenExecutor.Run(args)
  → InitializeFramework()
  → StartGuiMode(options)
    → new PipelineEntry()
    → new ExecutionPipelineEntry(pipelineEntry)
    → CommandFrameworkUsage.RegisterServices(configLoader: pipelineEntry)
    → new WinFormsEventBinder(executionEntry)  ← FileDrivenAdapter 作为 IEventPipelineExecutor
    → Application.Run(mainForm)

  WinForms 控件事件触发
    → WinFormsEventBinder
      → FileDrivenAdapter.Execute()
        → _pool.SubmitAsync(ExecuteCommandMessage.CreateGuiClick())
          → PipelineEntryInstance.RunCommandAsync()
            → ParseCommandStage → ExecuteCommandStage → ResultCollectStage
        → 返回结果
```

---

## 七、修改清单

| # | 文件 | 修改内容 | 影响 |
|---|------|---------|------|
| 1 | `FileDrivenExecutor.cs` | 在 `InitializeFramework()` 后创建 `TaskExecutorPool`，传递给 `ExecutionPipelineEntry` | 新增 ExecutionCenter 集成 |
| 2 | `ExecutionPipelineEntry.cs` | 新增 `RunPipelineAsync()` 异步方法，支持通过 `TaskExecutorPool` 调度；`Execute()` 增加 ExecutionCenter 路径 | 核心修改 |
| 3 | `ExecutionPipelineEntry.cs` | `CreateContext()` 不再复制 `LoadedConfigs`，改为引用同一实例 | 消除状态分裂 |
| 4 | `FileDrivenAdapter.cs` | 增强实现，确保与 `TaskExecutorPool` 正确集成 | 适配层完善 |
| 5 | `ExecuteCommandStage.cs` | `HandleNonEvent()` 移除内联回退路径，完全委托给 `INonEventCommandLookup` | 消除双重注册 |
| 6 | `FileDrivenExecutor.cs` | `RegisterNonEventCommands()` 确保所有命令注册到 `NonEventRegistry` | 非事件命令统一注册 |
| 7 | `PipelineFactory.cs` | 可选：简化 `BuildCommandPipeline()`，减少对 `bindingResolver` 的直接依赖 | 简化 |

---

## 八、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| `TaskExecutorPool` 单线程处理可能成为瓶颈 | 高并发场景下命令排队 | 初始为单 Worker，后续可扩展为多 Worker |
| `LoadedConfigs` 引用共享导致并发修改 | 多线程同时修改字典 | `ConcurrentDictionary` 已保证线程安全 |
| 同步接口 `IEventPipelineExecutor.Execute()` 的限制 | GUI 事件处理可能阻塞 | 使用 `Task.Run` 包装，避免 GUI 线程阻塞 |
| 向后兼容性 | 现有 CLI 调用方可能依赖同步 API | 保留同步 `RunPipeline()` 作为回退路径 |

---

## 九、验证标准

1. **GUI 模式**：点击按钮能正确找到 sideload 加载的配置绑定
2. **CLI 模式**：命令文件中的 sideload + click 序列能正确执行
3. **状态一致性**：sideload 加载配置后，后续命令能立即看到新配置
4. **非事件命令**：sideload/sleep/close/exit/echo 全部正常工作
5. **优先级**：GUI 点击事件优先于脚本命令执行
6. **异常处理**：命令执行失败不会导致整个管线崩溃
