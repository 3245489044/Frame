# ExecutionCenter（执行中心）框架设计文档

## 概述

ExecutionCenter 是一个基于消息队列的统一任务调度框架，位于 `Share/ExecutionCenter/` 项目下。

### 设计目标

1. **解耦配置加载与命令执行** — 消除当前架构中 `LoadedConfigs` 副本导致的状态不一致问题
2. **统一任务调度** — GUI 点击事件、脚本命令、配置加载统一通过消息队列调度
3. **优先级管理** — GUI 交互事件最高优先级，确保界面响应流畅
4. **状态隔离** — 每个 Worker 持有独立 `PipelineEntry`，天然线程安全

### 设计原则

- **极简** — 只解决当前问题，不做过度抽象
- **复用现有基础设施** — 基于 `BasicMethod/BasicAlgorithm/MessageQueue.cs`（优先级队列）构建
- **渐进式集成** — 通过适配器模式逐步替换现有调用

---

## 一、架构总览

```
┌─────────────────────────────────────────────────┐
│                 TaskExecutorPool                  │
│                                                   │
│  ┌───────────────────────────────────────────┐   │
│  │              TaskQueue                     │   │
│  │  PriorityMessageQueue<ITaskMessage>        │   │
│  │  + ConcurrentDictionary<Guid, TCSSource>   │   │
│  └──────────────────┬────────────────────────┘   │
│                     │                             │
│  ┌──────────────────┴────────────────────────┐   │
│  │              QueueWorker                   │   │
│  │  Loop: Dequeue → Execute → CompleteTask    │   │
│  │  WorkerContext(PipelineEntry)              │   │
│  └───────────────────────────────────────────┘   │
└─────────────────────────────────────────────────┘
         ▲                          ▲
         │                          │
┌────────┴────────┐    ┌────────────┴────────────┐
│  GUI 点击事件    │    │  脚本命令 / sideload     │
│ (WinForms)      │    │  (FileDriven)           │
└─────────────────┘    └─────────────────────────┘
```

### 核心流程

1. **提交任务**：调用方（GUI 点击 / 脚本命令）构造 `ITaskMessage` 提交到 `TaskQueue`
2. **优先级排队**：`TaskQueue` 按优先级将消息入队（GUI 事件 Critical 最高优先级）
3. **Worker 处理**：`QueueWorker` 从队列取出任务，在独立 `WorkerContext` 中执行
4. **结果回调**：执行完成后，通过 `TaskCompletionSource` 通知调用方

---

## 二、项目结构

```
Share/
└── ExecutionCenter/
    ├── ExecutionCenter.csproj
    │
    ├── Abstractions/                   ← 抽象接口（3 个）
    │   ├── ITaskMessage.cs             ← 任务消息接口
    │   ├── ITaskResponse.cs            ← 任务响应接口
    │   └── ITaskExecutorPool.cs        ← 执行池接口（合并队列 + 提交 + 启动/停止）
    │
    ├── Core/                           ← 核心实现（3 个）
    │   ├── TaskQueue.cs                ← 任务队列（包装 PriorityMessageQueue）
    │   ├── WorkerContext.cs            ← Worker 上下文（包装 PipelineEntry）
    │   └── TaskExecutorPool.cs         ← 执行池（Worker 管理 + 任务循环）
    │
    ├── Messages/                       ← 消息定义（3 个）
    │   ├── TaskMessageBase.cs          ← 消息基类
    │   ├── LoadConfigMessage.cs        ← 配置加载消息
    │   └── ExecuteCommandMessage.cs    ← 命令执行消息
    │
    └── Integration/                    ← 集成适配器（2 个）
        ├── CommandFrameworkAdapter.cs  ← 适配 CommandFrameworkUsage
        └── FileDrivenAdapter.cs        ← 适配 ExecutionPipelineEntry
```

---

## 三、接口定义

### ITaskMessage — 任务消息

```csharp
public interface ITaskMessage
{
    /// <summary>消息唯一标识</summary>
    Guid MessageId { get; }
    
    /// <summary>任务类型（"LoadConfig" / "ExecuteCommand"）</summary>
    string TaskType { get; }
    
    /// <summary>优先级（Critical=0, High=1, Normal=2, Low=3）</summary>
    int Priority { get; }
    
    /// <summary>创建时间</summary>
    DateTime CreatedAt { get; }
}
```

### ITaskResponse — 任务响应

```csharp
public interface ITaskResponse
{
    /// <summary>对应的请求消息 ID</summary>
    Guid RequestMessageId { get; }
    
    /// <summary>是否成功</summary>
    bool Success { get; }
    
    /// <summary>错误信息</summary>
    string? Error { get; }
    
    /// <summary>结果值</summary>
    object? Result { get; }
}
```

### ITaskExecutorPool — 执行池（唯一的外部入口）

```csharp
public interface ITaskExecutorPool : IDisposable
{
    /// <summary>提交任务并等待结果</summary>
    Task<ITaskResponse> SubmitAsync(ITaskMessage task, CancellationToken ct = default);
    
    /// <summary>启动执行池</summary>
    void Start();
    
    /// <summary>停止执行池</summary>
    void Stop();
}
```

---

## 四、核心实现

### TaskQueue — 任务队列

```csharp
/// <summary>
/// 任务队列
/// 包装 PriorityMessageQueue，添加请求-响应模式
/// </summary>
public class TaskQueue : IDisposable
{
    private readonly PriorityMessageQueue<ITaskMessage> _queue = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ITaskResponse>> _pending = new();
    
    /// <summary>提交任务并等待结果</summary>
    public async Task<ITaskResponse> EnqueueAsync(ITaskMessage task, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ITaskResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[task.MessageId] = tcs;
        
        using var reg = ct.Register(() => TryCancel(task.MessageId));
        
        _queue.StoreMessage(task, task.TaskType, task.Priority);
        return await tcs.Task;
    }
    
    /// <summary>消费者获取任务（阻塞）</summary>
    public ITaskMessage Dequeue(CancellationToken ct)
        => _queue.RetrieveMessage(-1);
    
    /// <summary>完成任务，回调结果</summary>
    public void Complete(ITaskResponse response)
    {
        if (_pending.TryRemove(response.RequestMessageId, out var tcs))
            tcs.TrySetResult(response);
    }
    
    private void TryCancel(Guid id)
    {
        if (_pending.TryRemove(id, out var tcs))
            tcs.TrySetCanceled();
    }
    
    public void Dispose() => _queue.Dispose();
}
```

### WorkerContext — Worker 上下文

```csharp
/// <summary>
/// Worker 上下文
/// 每个 Worker 持有独立的 PipelineEntry 实例
/// 配置加载和命令执行在同一个 Worker 内完成，天然状态一致
/// </summary>
public class WorkerContext : IDisposable
{
    private readonly PipelineEntry _entry;
    
    public string WorkerId { get; }
    public IEventBindingResolver BindingResolver => _entry.BindingResolver;
    
    public WorkerContext(string workerId, IFunctionExecutor? executor = null)
    {
        WorkerId = workerId;
        _entry = new PipelineEntry(executor);
    }
    
    /// <summary>加载配置到当前 Worker</summary>
    public LoadResult LoadConfig(string path, bool showForm = false)
        => _entry.LoadConfig(path, showForm);
    
    /// <summary>查找绑定</summary>
    public IEventBindingInfo? FindBinding(string controlName, string action)
        => _entry.FindBinding(controlName, action);
    
    /// <summary>获取 LoadedConfigs 字典（用于 CommandContext）</summary>
    public IDictionary<string, object> GetLoadedConfigs() => _entry.LoadedConfigs;
    
    public void Dispose() => _entry.Dispose();
}
```

### TaskExecutorPool — 执行池

```csharp
/// <summary>
/// 任务执行池
/// 管理 Worker，从队列取任务并执行
/// </summary>
public class TaskExecutorPool : ITaskExecutorPool
{
    private readonly TaskQueue _queue = new();
    private readonly WorkerContext _worker;
    private readonly ConcurrentDictionary<string, Func<ITaskMessage, WorkerContext, CancellationToken, Task<ITaskResponse>>> _handlers = new();
    private Task? _loopTask;
    private CancellationTokenSource? _cts;
    
    public TaskExecutorPool(string? workerId = null, IFunctionExecutor? executor = null)
    {
        _worker = new WorkerContext(workerId ?? "Worker-0", executor);
    }
    
    /// <summary>注册任务处理器</summary>
    public void RegisterHandler(string taskType, Func<ITaskMessage, WorkerContext, CancellationToken, Task<ITaskResponse>> handler)
        => _handlers[taskType] = handler;
    
    /// <summary>提交任务</summary>
    public Task<ITaskResponse> SubmitAsync(ITaskMessage task, CancellationToken ct = default)
        => _queue.EnqueueAsync(task, ct);
    
    /// <summary>启动（开始处理队列中的任务）</summary>
    public void Start()
    {
        if (_loopTask != null) return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }
    
    /// <summary>停止</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _loopTask = null;
    }
    
    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var task = _queue.Dequeue(ct);
                
                if (_handlers.TryGetValue(task.TaskType, out var handler))
                {
                    var response = await handler(task, _worker, ct);
                    _queue.Complete(response);
                }
                else
                {
                    _queue.Complete(new TaskResponse(task.MessageId, false, error: $"Unknown task type: {task.TaskType}"));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // 记录日志，继续处理下一个任务
            }
        }
    }
    
    public void Dispose()
    {
        Stop();
        _queue.Dispose();
        _worker.Dispose();
        _cts?.Dispose();
    }
}
```

---

## 五、消息定义

### TaskMessageBase

```csharp
public abstract class TaskMessageBase : ITaskMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public abstract string TaskType { get; }
    public int Priority { get; init; } = 2; // Normal
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
```

### LoadConfigMessage

```csharp
public class LoadConfigMessage : TaskMessageBase
{
    public override string TaskType => "LoadConfig";
    public string ConfigPath { get; init; } = "";
    public bool ShowForm { get; init; }
    
    public LoadConfigMessage() { Priority = 1; } // High
}
```

### ExecuteCommandMessage

```csharp
public class ExecuteCommandMessage : TaskMessageBase
{
    public override string TaskType => "ExecuteCommand";
    public string Command { get; init; } = "";
    public string? ControlName { get; init; }
    public string? EventType { get; init; }
    
    /// <summary>创建 GUI 点击事件（Critical 优先级）</summary>
    public static ExecuteCommandMessage CreateGuiClick(string controlName, string eventType)
        => new()
        {
            ControlName = controlName,
            EventType = eventType,
            Command = $"{eventType} {controlName}",
            Priority = 0 // Critical
        };
}
```

### TaskResponse

```csharp
public class TaskResponse : ITaskResponse
{
    public Guid RequestMessageId { get; }
    public bool Success { get; }
    public string? Error { get; }
    public object? Result { get; init; }
    
    public TaskResponse(Guid requestMessageId, bool success, string? error = null, object? result = null)
    {
        RequestMessageId = requestMessageId;
        Success = success;
        Error = error;
        Result = result;
    }
}
```

---

## 六、集成适配器

### CommandFrameworkAdapter

```csharp
/// <summary>
/// 桥接 CommandFrameworkUsage 的静态方法到 ExecutionCenter
/// </summary>
public static class CommandFrameworkAdapter
{
    private static TaskExecutorPool? _pool;
    
    public static void Initialize(TaskExecutorPool pool)
    {
        _pool = pool;
        
        // 注册配置加载处理器
        pool.RegisterHandler("LoadConfig", async (task, ctx, ct) =>
        {
            if (task is not LoadConfigMessage msg)
                return new TaskResponse(task.MessageId, false, error: "Invalid message");
            
            var result = ctx.LoadConfig(msg.ConfigPath, msg.ShowForm);
            return new TaskResponse(task.MessageId, result.IsSuccess, error: result.ErrorMessage, result: result);
        });
        
        // 注册命令执行处理器
        pool.RegisterHandler("ExecuteCommand", async (task, ctx, ct) =>
        {
            if (task is not ExecuteCommandMessage msg)
                return new TaskResponse(task.MessageId, false, error: "Invalid message");
            
            // 使用 Worker 的 PipelineEntry 执行命令
            var result = CommandFrameworkUsage.ExecuteInContext(msg.Command, ctx);
            return new TaskResponse(task.MessageId, result.Success, error: result.Error, result: result);
        });
        
        pool.Start();
    }
    
    /// <summary>替代 CommandFrameworkUsage.LoadConfig</summary>
    public static async Task<LoadResult> LoadConfigAsync(string path, bool showForm = false)
    {
        if (_pool == null) return LoadResult.Failure("Not initialized");
        var r = await _pool.SubmitAsync(new LoadConfigMessage { ConfigPath = path, ShowForm = showForm });
        return r.Success ? (LoadResult)r.Result! : LoadResult.Failure(r.Error!);
    }
    
    /// <summary>替代 CommandFrameworkUsage.ExecuteCommandAsync</summary>
    public static async Task<CommandContext> ExecuteCommandAsync(string command)
    {
        if (_pool == null) throw new InvalidOperationException("Not initialized");
        var r = await _pool.SubmitAsync(new ExecuteCommandMessage { Command = command });
        return (CommandContext)r.Result!;
    }
}
```

### FileDrivenAdapter

```csharp
/// <summary>
/// 桥接 ExecutionPipelineEntry.IEventPipelineExecutor 到 ExecutionCenter
/// </summary>
public class FileDrivenAdapter : IEventPipelineExecutor
{
    private readonly TaskExecutorPool _pool;
    
    public FileDrivenAdapter(TaskExecutorPool pool) => _pool = pool;
    
    /// <summary>
    /// GUI 点击事件 → 提交到任务队列（Critical 优先级）
    /// Worker 持有独立 PipelineEntry，配置已加载，绑定可找到
    /// </summary>
    bool IEventPipelineExecutor.Execute(string controlName, string eventType, string value)
    {
        var task = ExecuteCommandMessage.CreateGuiClick(controlName, eventType);
        var response = _pool.SubmitAsync(task).GetAwaiter().GetResult();
        return response.Success;
    }
}
```

---

## 七、如何解决当前 bug

### 当前 bug 根因

GUI 模式下，`EventBindingResolver` 查找 `addButton` 的 click 绑定时，`_loadedConfigs` 中没有 sideload 加载的配置。

### ExecutionCenter 的解决方案

1. **Worker 持有独立 PipelineEntry**：配置加载和命令执行在同一个 Worker 中完成
2. **状态天然一致**：Worker 内部单线程处理，无需锁，无需副本
3. **Critical 优先级**：GUI 点击事件最高优先级，立即处理

### 集成步骤

```
1. 创建 TaskExecutorPool（1 个 Worker）
2. 通过 CommandFrameworkAdapter.Initialize(pool) 注册处理器
3. 替换 ExecutionPipelineEntry 中的 IEventPipelineExecutor 为 FileDrivenAdapter
4. 替换 sideload 命令为 CommandFrameworkAdapter.LoadConfigAsync
```

---

## 八、实施计划

### Phase 1：基础构建（当前）

| 步骤 | 文件 | 说明 |
|------|------|------|
| 1 | `ExecutionCenter.csproj` | 项目文件 |
| 2 | `ITaskMessage.cs` | 任务消息接口 |
| 3 | `ITaskResponse.cs` | 任务响应接口 |
| 4 | `ITaskExecutorPool.cs` | 执行池接口 |
| 5 | `TaskMessageBase.cs` | 消息基类 |
| 6 | `LoadConfigMessage.cs` | 配置加载消息 |
| 7 | `ExecuteCommandMessage.cs` | 命令执行消息 |
| 8 | `TaskResponse.cs` | 任务响应 |
| 9 | `TaskQueue.cs` | 任务队列实现 |
| 10 | `WorkerContext.cs` | Worker 上下文 |
| 11 | `TaskExecutorPool.cs` | 执行池实现 |
| 12 | `CommandFrameworkAdapter.cs` | 适配器 |
| 13 | `FileDrivenAdapter.cs` | 适配器 |

### Phase 2：集成现有系统

| 步骤 | 说明 |
|------|------|
| 1 | 在 `CommandFrameworkUsage` 初始化时创建 `TaskExecutorPool` |
| 2 | 替换 `ExecutionPipelineEntry` 中的 `IEventPipelineExecutor` |
| 3 | 替换 `sideload` 命令为消息队列方式 |
| 4 | 集成测试 |

### Phase 3：高级特性（可选）

| 特性 | 说明 |
|------|------|
| 多 Worker 支持 | 多个 Worker 并行处理任务 |
| 任务路由 | 按 RoutingKey 路由到指定 Worker |
| 任务追踪 | 任务生命周期追踪 |
| IPC 通信 | 基于 MessageChannel 的进程间通信 |

---

## 九、与现有架构的对比

| 维度 | 当前架构 | ExecutionCenter |
|------|---------|----------------|
| **状态管理** | 全局共享 `_loadedConfigs`，多处副本 | Worker 内独立状态，天然一致 |
| **任务调度** | 直接方法调用 | 优先级队列调度 |
| **GUI 点击** | 同步阻塞，绑定查找可能失败 | Critical 优先级，Worker 内状态一致 |
| **配置加载** | 直接写入全局字典 | 路由到 Worker，状态隔离 |
| **并发安全** | 依赖 ConcurrentDictionary + 锁 | 队列串行化，无需锁 |
| **代码量** | — | 核心 ~300 行 |

---

## 十、项目文件

### ExecutionCenter.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>ExecutionCenter</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  
  <ItemGroup>
    <ProjectReference Include="..\..\BasicMethod\BasicMethod.csproj" />
    <ProjectReference Include="..\CommandFramework\CommandFramework.csproj" />
  </ItemGroup>
</Project>
```
