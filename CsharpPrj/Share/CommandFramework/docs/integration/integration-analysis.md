# CommandFramework 整合分析报告

## 一、系统架构现状

### 1.1 当前两套管线系统

CommandFramework 中存在两套独立的管线（Pipeline）系统，职责重叠但实现完全独立：

| 维度 | CommandFramework Pipeline | WorkflowExecution CentralPipeline |
|------|---------------------------|-----------------------------------|
| **位置** | `Pipeline/` | `WorkflowExecution/Pipeline/` |
| **职责** | 命令解析→执行→结果收集 | 工作流初始化→预处理→执行→后处理→完成 |
| **核心组件** | `PipelineEntryInstance`, `PipelineStageBase`, 各 Stage | `CentralPipeline`, `StageRunner`, `IPipelineStage` |
| **上下文模型** | `CommandContext`（继承 `BP.PipelineContext`） | `PipelineContext`（继承 `BP.PipelineContext`）, `RuntimeContext<TChainInfo>` |
| **扩展性** | 固定阶段顺序 | 支持动态阶段注册/移除、脏标记重建 |
| **状态管理** | 无内置状态机 | 内置 `ConditionalStateMachine<ExecutionStatus>` |

### 1.2 管线阶段对比

**CommandFramework Pipeline 阶段（固定顺序）：**

| 阶段 | 职责 | 顺序 |
|------|------|------|
| `ParseCommandStage` | 解析命令字符串 | 1 |
| `ValidationStage` | 验证解析结果 | 2 |
| `ExecuteCommandStage` | 执行命令 | 3 |
| `RetryStage` | 重试失败命令 | 可选 |
| `LoggingStage` | 日志记录 | 贯穿 |
| `ResultCollectStage` | 收集执行结果 | 4 |

**WorkflowExecution CentralPipeline 阶段（可扩展）：**

| 阶段 | 职责 | 顺序 |
|------|------|------|
| `Initialization` | 管线初始化 | 0 |
| `Execution`（动态注册） | 工作流节点执行 | 动态 |
| `Finalization` | 管线收尾 | 4 |

### 1.3 上下文模型对比

当前存在三层上下文，职责重叠：

| 字段 | CommandContext | PipelineContext (Workflow) | RuntimeContext<TChainInfo> |
|------|----------------|---------------------------|---------------------------|
| `ExecutionId` | ✓ (继承自 BP) | ✓ (ContextId) | ✓ |
| `SessionId` | ✓ (继承自 BP) | ✓ (反射获取) | - |
| `Result` | ✓ (FunctionResultValue) | ✓ (CurrentOutput) | - |
| `ErrorMessage` | ✓ (继承自 BP) | ✓ (继承自 BP) | ✓ |
| `StartTime` | ✓ (继承自 BP) | ✓ (继承自 BP) | ✓ |
| `Elapsed` | ✓ (继承自 BP) | ✓ (继承自 BP) | ✓ |
| `Status` | ✓ (继承自 BP) | ✓ (继承自 BP) | ✓ (RuntimeExecutionStatus) |
| `IsSuccess` | ✓ (HasError) | ✓ (HasError) | - |
| `Items` | ✓ (继承自 BP) | ✓ (ObservableDictionaryWrapper) | - |
| `SharedData` | ✓ (继承自 BP) | ✓ (映射到 Items) | - |
| `RuntimeData` | - | - | ✓ (ConcurrentDictionary) |
| `NodeResults` | - | - | ✓ (ConcurrentDictionary) |
| `ChainInfo` | - | - | ✓ (TChainInfo) |
| `CurrentNodeKey` | - | ✓ | ✓ |
| `CancellationToken` | - | ✓ (独立管理) | ✓ |
| `GraphSnapshot` | - | ✓ | - |
| `Session` | - | ✓ | - |
| `Parameters` | - | ✓ (param.* 前缀) | - |
| `StageSuccess/Errors` | ✓ (继承自 BP) | ✓ (继承自 BP) | - |
| `CreateSubContext` | - | ✓ | - |
| `VirtualStore` | ✓ (CLI 模式) | - | - |
| `LoadedConfigs` | ✓ (CLI 模式) | - | - |
| `CurrentCommand` | ✓ | - | - |
| `ParsedCommand` | ✓ | - | - |
| `BoundFunctionName` | ✓ | - | - |
| `ScriptPath` | ✓ | - | - |

---

## 二、重复逻辑分析

### 2.1 字符串拆分逻辑重复

**问题位置：**
- [CommandParser.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/CommandParser.cs#L482-L542) — `SplitCommand()`
- [StringUtils.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Execution/StringHelper.cs#L20-L64) — `SplitEscaped()`
- [ArgumentBuilder.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Execution/ArgumentBuilder.cs#L42-L82) — `SplitArguments()`

**问题描述：** 三处独立实现了带引号/转义支持的字符串拆分逻辑，功能重叠度高。

**整合方案：**
```
┌─────────────────────────────────────────────────────────┐
│                    StringUtils (统一入口)               │
├─────────────────────────────────────────────────────────┤
│  SplitCommand()      → 空格分隔，支持引号和转义         │
│  SplitEscaped()      → 自定义分隔符，支持转义           │
│  SplitArguments()    → 管道符分隔，支持引号             │
│  ────────────────────────────────────────────────────  │
│  内部统一使用：SplitWithQuotesAndEscape(separator)      │
└─────────────────────────────────────────────────────────┘
         ↓ 引用
┌─────────────────────────────────────────────────────────┐
│  CommandParser / ArgumentBuilder / 其他调用方           │
└─────────────────────────────────────────────────────────┘
```

### 2.2 命令注册检查方法重复

**问题位置：**
- [CommandEngine.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Execution/CommandEngine.cs#L245-L248) — `IsCommandRegistered()`
- [EngineCommandRegistry.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Registries.cs#L157-L160) — `IsCommandRegistered()`

**整合方案：** 移除 `CommandEngine` 中的重复方法，外部直接调用 `EngineCommandRegistry`。

### 2.3 执行报告失败查找重复

**问题位置：**
- [CommandContext.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Models/CommandContext.cs#L158-L176) — `GetFirstFailure()`
- [CommandExecutionReport.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Models/CommandExecutionReport.cs#L68-L70) — `GetFirstFailure()`

**整合方案：** `CommandExecutionReport.GetFirstFailure()` 委托给 `CommandContext.GetFirstFailure()`。

---

## 三、冗余逻辑分析

### 3.1 ControlFinder/ControlValueWriter 双重赋值

**问题位置：** [CommandFrameworkHost.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/CommandFrameworkHost.cs#L196-L225) — `Initialize()` 方法

**问题描述：** 步骤 5-6 从 Options 设置属性，步骤 7 又将相同值传递给 PipelineInstance 构造函数，Host 层属性无实际用途。

**整合方案：** 移除 Host 层的属性，直接通过 PipelineInstance 使用。

### 3.2 DelegateCommand 双执行路径

**问题位置：** [CommandEngine.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Execution/CommandEngine.cs#L356-L464) — `DelegateCommand` 类

**整合方案：** 统一为 `_executeWithContext` / `_undoWithContext` 单一路径。

### 3.3 两套独立的管线基础设施

**整合方案：** 以 `CentralPipeline` 为核心管线框架，将 CommandFramework Pipeline 的阶段作为其子阶段集成。

### 3.4 BatchResult<T> 泛型参数未使用

**问题位置：** [ICommand.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Abstractions/ICommand.cs#L189-L220) — `BatchResult<T>` 类

**问题描述：** `BatchResult<T>` 是泛型类，但类型参数 `T` 只在 `AddResult()` 方法签名中使用，内部实际存储的是 `ICommandExecutionResult`，与 `T` 无关。

**整合方案：** 改为非泛型类，或真正使用泛型参数约束存储的结果类型。

---

## 四、错误逻辑分析

### 4.1 注释行被错误标记为非事件命令（严重）

**问题位置：** [CommandParser.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/CommandParser.cs#L289-L297)

**代码问题：**
```csharp
if (normalized == null)
{
    return new ParsedCommand
    {
        IsSuccess = true,
        IsNonEventCommand = true,  // ★ 错误：注释不是非事件命令
        NormalizedCommand = null
    };
}
```

**修复方案：**
```csharp
if (normalized == null)
{
    return new ParsedCommand
    {
        IsSuccess = true,
        IsNonEventCommand = false,
        IsComment = true,          // 新增字段
        NormalizedCommand = null
    };
}
```

### 4.2 WorkflowEngine.GetStatus 枚举转换脆弱

**问题位置：** [WorkflowEngine.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/WorkflowExecution/Engine/WorkflowEngine.cs#L184-L201)

**代码问题：**
```csharp
return (TConfig.ExecutionStatus)(int)(session?.Status ?? ExecutionFrameworkStatus.Pending);
```

**修复方案：** 使用显式映射函数
```csharp
private static readonly Dictionary<ExecutionFrameworkStatus, TConfig.ExecutionStatus> 
    StatusMapping = new()
{
    { ExecutionFrameworkStatus.Pending, TConfig.ExecutionStatus.Pending },
    { ExecutionFrameworkStatus.Running, TConfig.ExecutionStatus.Running },
    { ExecutionFrameworkStatus.Completed, TConfig.ExecutionStatus.Completed },
    { ExecutionFrameworkStatus.Failed, TConfig.ExecutionStatus.Failed },
    { ExecutionFrameworkStatus.Cancelled, TConfig.ExecutionStatus.Cancelled },
    { ExecutionFrameworkStatus.Paused, TConfig.ExecutionStatus.Paused },
};

public TConfig.ExecutionStatus GetStatus(string workflowId)
{
    var sessions = GetSessionsByWorkflowId(workflowId);
    var session = sessions.OrderByDescending(s => s.StartTime).FirstOrDefault();
    var status = session?.Status ?? ExecutionFrameworkStatus.Pending;
    
    if (!StatusMapping.TryGetValue(status, out var mappedStatus))
        throw new InvalidOperationException($"Unknown execution status: {status}");
    
    return mappedStatus;
}
```

### 4.3 NodeExecutor.ExecuteParallelNodeAsync 未取消失败任务

**问题位置：** [NodeExecutor.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/WorkflowExecution/Engine/NodeExecutor.cs#L338-L357)

**代码问题：**
```csharp
var tasks = node.Children
    .Where(c => c.IsEnabled && !c.IsSkipped)
    .Select(child => ExecuteNodeAsync((NodeDefinition)child, context, cancellationToken));

var results = await Task.WhenAll(tasks);  // 失败后仍等待所有任务
```

**修复方案：** 使用 `Task.WhenAll` + 链接 CTS，任一任务失败时取消其他任务
```csharp
private async Task<BasicExecutionResult> ExecuteParallelNodeAsync(
    NodeDefinition node,
    IWorkflowContext context,
    CancellationToken cancellationToken)
{
    if (node.Children.Count == 0)
        return BasicExecutionResult.Success(null);

    var childTasks = node.Children
        .Where(c => c.IsEnabled && !c.IsSkipped)
        .ToList();

    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    
    async Task<BasicExecutionResult> ExecuteWithCancellation(NodeDefinition child)
    {
        try
        {
            return await ExecuteNodeAsync(child, context, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            return BasicExecutionResult.Cancelled();
        }
    }

    var tasks = childTasks.Select(child => ExecuteWithCancellation((NodeDefinition)child));
    var results = await Task.WhenAll(tasks);
    
    var failedResult = results.FirstOrDefault(r => !r.IsSuccess);
    if (failedResult != null)
    {
        linkedCts.Cancel();  // 取消其他可能仍在运行的任务
        return failedResult;
    }

    return BasicExecutionResult.Success(results.Select(r => r.Value).ToList());
}
```

### 4.4 StateStoreExtensions.GetOrSet<T> 序列化不一致

**问题位置：** [StateStoreExtensions.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Execution/StateStoreExtensions.cs#L88-L110)

**代码问题：**
```csharp
var stringValue = defaultValue?.ToString();  // 写入：ToString()
// ...
return JsonSerializer.Deserialize<T>(result);  // 读取：JSON反序列化
```

**修复方案：** 与 `Set<T>` 保持一致的序列化策略，区分字符串类型和复杂类型
```csharp
public static T GetOrSet<T>(this IStateStore store, string key, T defaultValue)
{
    ArgumentNullException.ThrowIfNull(store);

    string? stringValue;
    if (defaultValue == null)
        stringValue = null;
    else if (typeof(T) == typeof(string))
        stringValue = (string)(object)defaultValue;
    else
        stringValue = JsonSerializer.Serialize(defaultValue);

    var result = store.GetOrAdd(key, stringValue);
    
    if (result == null)
        return default!;

    try
    {
        if (typeof(T) == typeof(string))
            return (T)(object)result;

        return JsonSerializer.Deserialize<T>(result) ?? defaultValue;
    }
    catch
    {
        return defaultValue;
    }
}
```

### 4.5 ObservableStateStore.Clear 事件触发不一致

**问题位置：** [StateStoreExtensions.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/CommandFramework/Execution/StateStoreExtensions.cs#L265-L269)

**代码问题：**
```csharp
public void Clear()
{
    _innerStore.Clear();
    StateChanged?.Invoke(this, new StateChangedEventArgs("*", null, null, StateChangeType.Clear));
}
```

**修复方案：** 使用 `RaiseStateChanged()` 方法保持一致性
```csharp
public void Clear()
{
    _innerStore.Clear();
    RaiseStateChanged(new StateChangedEventArgs("*", null, null, StateChangeType.Clear));
}
```

---

## 五、整合方案

### 5.1 管线统一方案

以 `CentralPipeline` 为核心，将 CommandFramework Pipeline 阶段作为动态注册的子阶段集成：

```
┌─────────────────────────────────────────────────────────────────┐
│                      CentralPipeline（统一管线）                 │
├─────────────────────────────────────────────────────────────────┤
│  Fixed Stages:                                                  │
│    ├─ Initialization (Order=0)                                  │
│    └─ Finalization (Order=100)                                  │
│                                                                 │
│  Dynamic Stages (按 Order 排序):                                │
│    ├─ CommandParsing (Order=10)     ← 原 ParseCommandStage      │
│    ├─ CommandValidation (Order=20)  ← 原 ValidationStage        │
│    ├─ CommandExecution (Order=30)   ← 原 ExecuteCommandStage    │
│    ├─ ResultCollection (Order=40)   ← 原 ResultCollectStage     │
│    ├─ LoggingStage (Order=50)                                  │
│    ├─ RetryStage (Order=60)                                    │
│    ├─ WorkflowExecution (Order=70)  ← 工作流节点执行            │
│    └─ 其他自定义阶段                                             │
└─────────────────────────────────────────────────────────────────┘
```

**迁移步骤：**
1. 将 `ParseCommandStage`、`ValidationStage`、`ExecuteCommandStage`、`ResultCollectStage` 适配为 `IPipelineStage` 接口
2. 在 `CentralPipeline` 中通过 `RegisterStage()` 动态注册这些阶段
3. 移除 `PipelineEntryInstance` 和 `PipelineStageBase`

### 5.2 上下文统一方案

基于现有的 `BP.PipelineContext` 基类，合并三层上下文为统一的 `ExecutionContext<TChainInfo>`：

```csharp
public class ExecutionContext<TChainInfo> : Modules.Pipeline.PipelineContext, 
    IWorkflowContext, IExecutionContext, IDisposable
    where TChainInfo : class
{
    // === 基础标识 ===
    public string ContextId { get; }
    public string ExecutionId => ContextId;
    
    // === 执行状态 ===
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
    
    // === 时间追踪 ===
    public DateTime StartTime { get; } = DateTime.UtcNow;
    public TimeSpan Elapsed => DateTime.UtcNow - StartTime;
    
    // === 数据存储 ===
    private ConcurrentDictionary<string, object> _sharedData => (ConcurrentDictionary<string, object>)Items;
    private readonly InMemoryStateStore _dataStore = new();
    
    // === 节点执行结果缓存（语义上独立于共享数据） ===
    public ConcurrentDictionary<string, object> NodeResults { get; } = new();
    
    // === 工作流特定 ===
    public TChainInfo? ChainInfo { get; set; }
    public string? CurrentNodeKey { get; set; }
    public object? CurrentInput { get; set; }
    public object? CurrentOutput { get; set; }
    public string? NextNodeKey { get; set; }
    
    // === 图快照和会话（通过共享数据存储，避免循环引用） ===
    public object GraphSnapshot
    {
        get => _sharedData.TryGetValue("GraphSnapshot", out var v) ? v : null;
        private set => _sharedData["GraphSnapshot"] = value;
    }
    
    public object Session
    {
        get => _sharedData.TryGetValue("ExecutionSession", out var v) ? v : null;
        private set => _sharedData["ExecutionSession"] = value;
    }
    
    // === 命令特定 ===
    public string? CurrentCommand { get; set; }
    public int CurrentCommandLineNumber { get; set; }
    public IParsedCommand? ParsedCommand { get; set; }
    public string? BoundFunctionName { get; set; }
    public object? FunctionResultValue { get; set; }
    public TimeSpan FunctionExecutionTime { get; set; }
    public string? ScriptPath { get; set; }
    
    // === 执行统计 ===
    public int TotalCommands { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    
    // === CLI 模式特定 ===
    public Dictionary<string, string?>? VirtualStore { get; set; }
    public IDictionary<string, object>? LoadedConfigs { get; set; }
    
    // === 控制 ===
    private readonly object _lock = new();
    private CancellationTokenSource _cts;
    
    public CancellationToken CancellationToken
    {
        get
        {
            lock (_lock)
            {
                return _cts?.Token ?? CancellationToken.None;
            }
        }
        set
        {
            lock (_lock)
            {
                var oldCts = _cts;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(value);
                oldCts?.Dispose();
            }
        }
    }
    
    // === 构造函数 ===
    public ExecutionContext(string contextId = null)
        : base(default, null)
    {
        ContextId = contextId ?? $"ctx-{Guid.NewGuid():N}";
        _cts = new CancellationTokenSource();
    }
    
    // === 图快照和会话设置 ===
    public void SetGraphSnapshot(object snapshot)
    {
        GraphSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }
    
    public void SetSession(object session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }
    
    public TSession? GetSession<TSession>() where TSession : class
        => Session as TSession;
    
    public TSnapshot? GetGraphSnapshot<TSnapshot>() where TSnapshot : class
        => GraphSnapshot as TSnapshot;
    
    // === 数据操作方法 ===
    public void SetData(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("键不能为空", nameof(key));
        _sharedData[key] = value;
    }
    
    public object GetData(string key)
    {
        _sharedData.TryGetValue(key, out var value);
        return value;
    }
    
    public T GetData<T>(string key)
    {
        if (_sharedData.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }
    
    public bool TryGetData(string key, out object value)
    {
        return _sharedData.TryGetValue(key, out value);
    }
    
    public bool RemoveData(string key)
    {
        return _sharedData.TryRemove(key, out _);
    }
    
    // === 节点结果操作方法 ===
    public void SetNodeResult(string nodeKey, object result)
    {
        NodeResults[nodeKey] = result;
    }
    
    public T? GetNodeResult<T>(string nodeKey)
    {
        if (NodeResults.TryGetValue(nodeKey, out var result) && result is T typed)
            return typed;
        return default;
    }
    
    // === 参数操作方法 ===
    public T GetParameter<T>(string key)
    {
        var paramKey = $"param.{key}";
        if (_sharedData.TryGetValue(paramKey, out var value) && value is T typed)
            return typed;
        return default;
    }
    
    public bool TryGetParameter<T>(string key, out T value)
    {
        var paramKey = $"param.{key}";
        if (_sharedData.TryGetValue(paramKey, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }
    
    // === 子上下文 ===
    public IWorkflowContext CreateSubContext(string? nodeKey)
    {
        var subContext = new ExecutionContext<TChainInfo>($"{ContextId}.{nodeKey ?? "sub"}");
        
        // 复制核心状态
        subContext.SetGraphSnapshot(GraphSnapshot);
        subContext.SetSession(Session);
        subContext.CurrentNodeKey = nodeKey ?? CurrentNodeKey;
        subContext.CurrentInput = CurrentInput;
        subContext.CurrentOutput = CurrentOutput;
        
        // 复制共享数据
        foreach (var kvp in _sharedData)
        {
            subContext._sharedData[kvp.Key] = kvp.Value;
        }
        
        // 复制节点结果缓存（浅拷贝，子上下文独立管理）
        foreach (var kvp in NodeResults)
        {
            subContext.NodeResults[kvp.Key] = kvp.Value;
        }
        
        return subContext;
    }
    
    // === 阶段结果管理（继承自 BP.PipelineContext） ===
    // SetStageResult(), GetStageSuccess(), GetStageError() 等
    
    // === 重置 ===
    public override void Reset()
    {
        base.Reset();
        
        CurrentCommand = null;
        CurrentCommandLineNumber = 0;
        ParsedCommand = null;
        FunctionResultValue = null;
        FunctionExecutionTime = TimeSpan.Zero;
        ScriptPath = null;
        
        // 重置节点结果缓存
        NodeResults.Clear();
        
        ResetCancellation();
    }
    
    // === 取消管理 ===
    public void Cancel()
    {
        lock (_lock)
        {
            _cts?.Cancel();
        }
    }
    
    public void ResetCancellation()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }
    }
    
    // === IDisposable ===
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            
            // 释放 Session（如果实现了 IDisposable）
            if (Session is IDisposable disposableSession)
                disposableSession.Dispose();
        }
        base.Dispose(disposing);
    }
}
```

### 5.3 基础设施统一方案

| 模块 | 当前状态 | 整合方案 |
|------|----------|----------|
| **字符串拆分** | 3处独立实现 | 统一到 `StringUtils` |
| **命令注册** | `CommandEngine` + `EngineCommandRegistry` | 移除 `CommandEngine` 重复方法 |
| **状态存储** | `InMemoryStateStore` + `ObservableStateStore` | 统一接口，合并实现 |
| **参数解析** | `ArgumentBuilder` + `ParameterResolver` | 合并为统一的参数解析器 |

### 5.4 PipelineContext 反射访问消除策略

**问题描述：** 当前 `PipelineContext` 大量使用反射访问 `GraphSnapshot`、`Session` 的属性（如 `GetGraphSnapshotId`、`GetSessionId`、`GetSessionLogger`），这是为了避免循环引用（WorkflowExecution → Operation）。

**消除策略：**

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      消除反射访问策略                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                        │
│  当前方案（反射）:                                                      │
│    PipelineContext ──(反射)──→ GraphSnapshot.SnapshotId               │
│    PipelineContext ──(反射)──→ Session.SessionId                      │
│    PipelineContext ──(反射)──→ Session.Log (IWorkflowLogger)          │
│                                                                        │
│  优化方案（接口抽象）:                                                  │
│    PipelineContext ──(接口)──→ IGraphSnapshot.SnapshotId              │
│    PipelineContext ──(接口)──→ IExecutionSession.SessionId            │
│    PipelineContext ──(接口)──→ IExecutionSession.Logger               │
│                                                                        │
│  步骤:                                                                 │
│    1. 在 CommandFramework.Abstractions 中定义接口：                     │
│       - IGraphSnapshot { string SnapshotId; IReadOnlyList<string> NodeKeys; }│
│       - IExecutionSession { string SessionId; IWorkflowLogger Logger; }│
│    2. Operation 层实现这些接口                                         │
│    3. PipelineContext 使用接口而非反射访问                              │
│    4. 通过泛型约束确保类型安全                                          │
│                                                                        │
└─────────────────────────────────────────────────────────────────────────┘
```

**代码示例：**
```csharp
// 在 CommandFramework.Abstractions 中定义
public interface IGraphSnapshot
{
    string SnapshotId { get; }
    IReadOnlyList<string> NodeKeys { get; }
}

public interface IExecutionSession : IDisposable
{
    string SessionId { get; }
    IWorkflowLogger? Logger { get; }
}

// 统一上下文模型（泛型约束）
public class ExecutionContext<TChainInfo, TSession, TSnapshot> 
    : Modules.Pipeline.PipelineContext, IWorkflowContext, IExecutionContext, IDisposable
    where TChainInfo : class
    where TSession : class, IExecutionSession
    where TSnapshot : class, IGraphSnapshot
{
    public TSnapshot? GraphSnapshot { get; private set; }
    public TSession? Session { get; private set; }
    
    public void SetGraphSnapshot(TSnapshot snapshot) => GraphSnapshot = snapshot;
    public void SetSession(TSession session) => Session = session;
    
    // 直接访问，无需反射
    public string? GetSessionId() => Session?.SessionId;
    public IWorkflowLogger? GetSessionLogger() => Session?.Logger;
}
```

### 5.5 CommandStageBase 辅助方法适配策略

**问题描述：** `CommandStageBase` 包含 CommandFramework 特有的管线能力：
- `EventCommandOnly` 属性 — 控制是否仅处理事件命令
- `IsEventCommand()` 方法 — 判断是否为事件命令
- `ResolveDependency<T>()` 方法 — 从多个来源解析依赖项
- `FindControl()` / `GetControlText()` / `SetControlText()` — 控件查找和读写

**适配策略：**

| 方法 | 当前位置 | 适配方案 |
|------|----------|----------|
| `EventCommandOnly` / `IsEventCommand` | `CommandStageBase` | 提取为 `CommandStageHelper` 静态类，在适配后的 Stage 中调用 |
| `ResolveDependency<T>` | `CommandStageBase` | 提取为 `DependencyResolver` 静态类，支持 DI 注入、SharedData、IServiceProvider |
| `FindControl` / `GetControlText` / `SetControlText` | `CommandStageBase` | 提取为 `ControlAccessor` 静态类，依赖 `IControlFinder` / `IControlValueWriter` |

**适配后的 Stage 示例：**
```csharp
public class ParseCommandStage : IPipelineStage
{
    public string Name => "CommandParsing";
    public float Order => 10;
    
    public async Task<TConfig.ExecutionResult> ExecuteAsync(IWorkflowContext context, CancellationToken ct)
    {
        var ctx = (ExecutionContext<TChainInfo>)context;
        
        // 使用辅助类判断是否为事件命令
        if (ctx.ParsedCommand != null && !CommandStageHelper.IsEventCommand(ctx))
        {
            return TConfig.ExecutionResult.Success(null);
        }
        
        // 使用辅助类解析依赖
        var parser = DependencyResolver.Resolve<CommandParser>(ctx, _parser);
        
        // 执行解析逻辑
        ctx.ParsedCommand = parser.Parse(ctx.CurrentCommand);
        
        return TConfig.ExecutionResult.Success(null);
    }
}
```

---

## 六、分阶段迁移路线图

### Phase 1：基础修复（独立执行，无依赖）

| 任务 | 优先级 | 预计工时 | 依赖 |
|------|--------|----------|------|
| 修复注释行标记问题（4.1） | P0 | 2h | 无 |
| 修复枚举转换问题（4.2） | P1 | 3h | 无 |
| 修复并行取消问题（4.3） | P1 | 4h | 无 |
| 修复序列化不一致问题（4.4） | P1 | 2h | 无 |
| 修复事件触发不一致问题（4.5） | P2 | 1h | 无 |
| 统一字符串拆分逻辑（2.1） | P2 | 8h | 无 |

### Phase 2：上下文统一（需 Phase 1 完成）

| 任务 | 优先级 | 预计工时 | 依赖 |
|------|--------|----------|------|
| 设计 `ExecutionContext<TChainInfo>` 统一模型 | P1 | 4h | Phase 1 |
| 迁移 `CommandContext` 到新模型 | P1 | 6h | 设计完成 |
| 迁移 `PipelineContext` 到新模型 | P1 | 6h | 设计完成 |
| 更新所有引用点 | P2 | 8h | 迁移完成 |

### Phase 3：管线统一（需 Phase 2 完成）

| 任务 | 优先级 | 预计工时 | 依赖 |
|------|--------|----------|------|
| 提取 `CommandStageBase` 辅助方法为独立静态类 | P1 | 4h | Phase 2 |
| 适配 `ParseCommandStage` 为 `IPipelineStage` 接口 | P1 | 2h | 辅助方法提取完成 |
| 适配 `ValidationStage` 为 `IPipelineStage` 接口 | P1 | 2h | 辅助方法提取完成 |
| 适配 `ExecuteCommandStage` 为 `IPipelineStage` 接口 | P1 | 3h | 辅助方法提取完成 |
| 适配 `ResultCollectStage` 为 `IPipelineStage` 接口 | P1 | 2h | 辅助方法提取完成 |
| 在 `CentralPipeline` 中动态注册 CommandFramework 阶段 | P1 | 4h | 所有 Stage 适配完成 |
| 更新 CommandFrameworkHost 使用 CentralPipeline | P2 | 4h | 注册完成 |
| 移除 `PipelineEntryInstance` 和 `PipelineStageBase` | P2 | 4h | Host 更新完成 |

**Phase 3 详细步骤：**

1. **提取辅助方法**：将 `CommandStageBase` 中的 `IsEventCommand()`、`ResolveDependency<T>()`、`FindControl()`/`GetControlText()`/`SetControlText()` 提取为独立静态类
   - 创建 `CommandStageHelper.cs` — 包含 `IsEventCommand()`、`EventCommandOnly` 逻辑
   - 创建 `DependencyResolver.cs` — 包含依赖解析逻辑
   - 创建 `ControlAccessor.cs` — 包含控件访问逻辑

2. **适配 Stage**：将每个 CommandFramework Stage 从继承 `CommandStageBase` 改为实现 `IPipelineStage` 接口
   - `ParseCommandStage` → 实现 `IPipelineStage`，使用 `CommandStageHelper` 处理命令类型判断
   - `ValidationStage` → 实现 `IPipelineStage`，使用 `DependencyResolver` 解析验证器
   - `ExecuteCommandStage` → 实现 `IPipelineStage`，使用 `ControlAccessor` 处理 GUI/CLI 模式
   - `ResultCollectStage` → 实现 `IPipelineStage`，直接访问上下文收集结果

3. **注册阶段**：在 `CentralPipeline` 中通过 `RegisterStage()` 方法动态注册所有 CommandFramework 阶段

4. **更新 Host**：修改 `CommandFrameworkHost` 使用 `CentralPipeline` 替代 `PipelineEntryInstance`

5. **清理代码**：移除 `PipelineEntryInstance`、`PipelineStageBase` 及其相关引用

### Phase 4：基础设施整合（需 Phase 3 完成）

| 任务 | 优先级 | 预计工时 | 依赖 |
|------|--------|----------|------|
| 合并 `ArgumentBuilder` 和 `ParameterResolver` | P2 | 6h | Phase 3 |
| 统一状态存储接口 | P2 | 4h | Phase 3 |
| 移除冗余的 Host 层包装 | P3 | 2h | Phase 3 |

---

## 七、风险评估

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| 上下文合并导致现有代码编译失败 | 高 | 高 | 采用适配器模式，逐步迁移 |
| 管线统一后执行顺序变化 | 中 | 高 | 保留原阶段顺序，添加 Order 属性验证 |
| 字符串拆分逻辑变更影响命令解析 | 中 | 高 | 编写单元测试覆盖所有拆分场景 |
| 枚举映射失败导致运行时异常 | 低 | 高 | 添加异常处理和日志 |
| 并行取消逻辑变更影响工作流执行 | 中 | 中 | 在测试环境充分验证 |
| 序列化策略变更导致数据兼容性问题 | 中 | 中 | 添加向后兼容的反序列化逻辑 |

---

## 八、总结

CommandFramework 当前存在以下主要问题：

1. **两套独立管线系统**：CommandFramework Pipeline 和 WorkflowExecution CentralPipeline 功能重叠，应统一为以 `CentralPipeline` 为核心的框架，所有阶段动态注册
2. **三层上下文模型**：`CommandContext`、`PipelineContext`、`RuntimeContext<TChainInfo>` 职责重叠，应合并为统一的 `ExecutionContext<TChainInfo>`，继承 `BP.PipelineContext` 基类
3. **多处重复逻辑**：字符串拆分、命令注册检查、失败查找等存在重复实现
4. **多个错误逻辑**：注释行标记错误、枚举转换脆弱、并行取消缺失、序列化不一致、事件触发不一致

建议按照 **Phase 1 → Phase 2 → Phase 3 → Phase 4** 的顺序进行迁移，每个阶段完成后进行充分测试，确保向后兼容性。
