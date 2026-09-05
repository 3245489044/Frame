# WorkflowExecution 代码审查报告

> 分析范围：`CsharpPrj/Share/WorkflowExecution/` 下 28 个源文件
> 分析日期：2026-06-26
> 分析维度：重复逻辑、冗余逻辑、错误逻辑

---

## 一、重复逻辑（Duplicate Logic）

### 1. 🔴 两个独立的 `WorkflowContext` 实现

| 项目 | 值 |
|------|-----|
| **文件** | `Abstractions/WorkflowContext.cs` vs `CentralPipeline/WorkflowContext.cs` |
| **类名** | `WorkflowContext` vs `PipelineContext` |
| **接口** | 都实现 `IWorkflowContext` |

**问题描述：**
两个类都实现了 `IWorkflowContext` 接口，功能高度重叠（都管理 ExecutionId、WorkflowId、Parameters、Items、CancellationToken 等）。

`PipelineContext` 的注释明确提到：
> "★ 2026-06 重构：不再继承 WorkflowExecution.Abstractions.WorkflowContext，消除 Items 数据分裂 Bug。父类 WorkflowContext 的 Items（Dictionary）和子类 _sharedData（ConcurrentDictionary）是两个独立对象，通过接口和类访问可能得到不同数据。"

这说明之前继承时存在数据不同步的 Bug，现在直接实现接口但代码重复。

**影响：**
- 维护成本翻倍：修改 IWorkflowContext 接口需要同步修改两个实现
- 行为不一致：两个实现的 `CreateSubContext`、`GetParameter` 等方法的细节行为不同
- 调用方困惑：不知道该用哪个实现

**建议：**
- 合并为一个实现，或明确职责分离（一个作为纯数据容器，一个作为管道上下文）
- 如果必须保留两个，应提取公共基类或使用组合模式

---

### 2. 🟡 `DataContext` 与 `WorkflowContext.Items` 功能重叠

| 项目 | 值 |
|------|-----|
| **文件** | `Session/DataContext.cs` vs `Abstractions/WorkflowContext.cs` |
| **接口** | `IDataContext` vs `IWorkflowContext.Items` |

**问题描述：**
`DataContext` 是独立的线程安全键值存储，提供 `GetItem<T>/SetItem<T>/ContainsKey/RemoveItem/Merge` 等操作。而 `WorkflowContext.Items` 也是 `IDictionary<string, object>`。两者提供几乎相同的数据共享能力。

**代码对比：**
```csharp
// DataContext
public T GetItem<T>(string key) { ... }
public void SetItem<T>(string key, T value) { ... }
public bool ContainsKey(string key) { ... }
public void Merge(IDataContext other, bool overwrite = true) { ... }

// WorkflowContext.Items
IDictionary<string, object> Items { get; }
// 通过 Items["key"] = value 访问
```

**影响：**
- 调用方需要决定使用 DataContext 还是 WorkflowContext.Items
- 数据可能分散在两个存储中，导致不一致

**建议：**
- 移除 DataContext，统一使用 WorkflowContext.Items
- 或让 DataContext 成为 WorkflowContext 的内部实现细节

---

### 3. 🟡 两套并行执行逻辑

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/NodeExecutor.cs:ExecuteParallelNodeAsync` vs `Pipeline/PipelineStages.cs:CreateParallelStage` |
| **层级** | 节点级 vs 管线级 |

**问题描述：**
两者都实现了"并行执行多个子任务 → 收集结果 → 检查失败 → 返回"的模式，代码结构高度相似。

**代码对比：**
```csharp
// NodeExecutor (节点级)
var tasks = node.Children
    .Where(c => c.IsEnabled && !c.IsSkipped)
    .Select(child => ExecuteNodeAsync(child, context, cancellationToken));
var results = await Task.WhenAll(tasks);
var failedResult = results.FirstOrDefault(r => !r.IsSuccess);
if (failedResult != null) return failedResult;
return BasicExecutionResult.Success(results.Select(r => r.Value).ToList());

// PipelineStages (管线级)
var tasks = stages.Select(s => s.ExecuteAsync(context, cancellationToken));
var results = await Task.WhenAll(tasks);
var failedResult = results.FirstOrDefault(r => !r.IsSuccess);
if (failedResult != null) return BasicExecutionResult.Failure(...);
return BasicExecutionResult.Success(results.Select(r => r.Value).ToList());
```

**建议：**
- 提取公共的并行执行辅助方法
- 或让节点级并行委托给管线级实现

---

### 4. 🟡 两套工作流执行入口

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/ExecutionOrchestrator.cs` |
| **方法** | `ExecuteTopologyAsync` vs `OrchestrateAsync` |

**问题描述：**
`ExecuteTopologyAsync` 已标记 `[Obsolete("请使用 OrchestrateAsync 方法替代")]`，但保留了完整实现（约 100 行），包括递归遍历、分支并行执行等逻辑。它与 `OrchestrateAsync`（通过 CentralPipeline 执行）是两套独立的执行路径。

**影响：**
- 废弃代码增加了维护负担
- 调用方可能误用废弃方法
- 两套路径的行为可能不一致

**建议：**
- 立即移除 `ExecuteTopologyAsync` 及其辅助方法 `ExecuteNodeAndDescendantsAsync`
- 如果暂时不能移除，应添加编译警告

---

### 5. 🟢 `PipelineStageNames.AllStages` 与 `CentralPipeline` 固定阶段不一致

| 项目 | 值 |
|------|-----|
| **文件** | `Abstractions/IPipelineStage.cs` vs `Pipeline/CentralPipeline.cs` |

**问题描述：**
`AllStages` 定义了 7 个阶段（Initialization, GraphDefinition, FunctionExec, Execution, PostProcessing, UpdateCheck, Finalization），但 CentralPipeline 只内置了 Initialization 和 Finalization 两个，其余需要外部注入。两者没有对齐关系。

**建议：**
- 让 CentralPipeline 的默认阶段与 AllStages 对齐
- 或移除 AllStages 列表，避免误导

---

## 二、冗余逻辑（Redundant Logic）

### 6. 🔴 `PipelineDefinition` — 未使用的废弃代码

| 项目 | 值 |
|------|-----|
| **文件** | `Models/PipelineDefinition.cs` |
| **标记** | `[Obsolete("当前未使用，保留以供扩展")]` |

**问题描述：**
整个类标记了 `[Obsolete]`，包含 `PipelineDefinition` 和 `PipelineStageDefinition` 两个类，约 38 行代码。注释明确说"当前未在项目中使用"。

**影响：**
- 增加编译输出体积
- 增加代码阅读负担
- 如果将来需要，可以通过 git 历史找回

**建议：**
- 立即移除

---

### 7. 🔴 `WorkflowExceptionInfo` — 未使用的废弃代码

| 项目 | 值 |
|------|-----|
| **文件** | `Logging/WorkflowExceptionInfo.cs` |
| **标记** | `[Obsolete("当前未使用，保留以供扩展")]` |

**问题描述：**
整个类标记了 `[Obsolete]`，约 61 行代码。注释明确说"当前未在项目中使用"。

**建议：**
- 立即移除

---

### 8. 🟡 `HotReloadService._cancellationSources` 参数冗余

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/HotReloadService.cs:35` |

**问题描述：**
构造函数接受 `ConcurrentDictionary<string, CancellationTokenSource>? cancellationSources` 参数，但 `ISessionManager` 已经提供了 `RegisterCancellationToken/GetCancellationToken/RemoveCancellationToken` 方法。HotReloadService 直接操作字典绕过了 SessionManager 的封装。

**代码：**
```csharp
public HotReloadService(
    ISessionFactory sessionFactory,
    ISessionManager? sessionManager = null,
    ConcurrentDictionary<string, CancellationTokenSource>? cancellationSources = null) // 冗余
```

**影响：**
- 存在两种管理 CancellationTokenSource 的方式
- 直接操作字典可能导致 SessionManager 中的状态不一致

**建议：**
- 移除 `_cancellationSources` 参数，统一通过 `ISessionManager` 管理

---

### 9. 🟢 `NodeExecutor._cancelledResult` 静态缓存

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/NodeExecutor.cs:21` |

**问题描述：**
缓存了 `BasicExecutionResult.Cancelled()` 实例，但只在 `BuildResiliencePipeline` 的 `ShouldHandle` 中用于比较 `result.Status != _cancelledResult.Status`。

**代码：**
```csharp
private static readonly BasicExecutionResult _cancelledResult = BasicExecutionResult.Cancelled();
```

**建议：**
- 移除缓存，直接使用 `result.IsCancelled` 属性（如果存在）或字符串比较

---

### 10. 🟢 `NodeChainSkeleton` 和 `NodeProcessSkeleton` 的空虚方法

| 项目 | 值 |
|------|-----|
| **文件** | `Skeletons/NodeChainSkeleton.cs` 和 `Skeletons/NodeProcessSkeleton.cs` |

**问题描述：**
模板方法模式定义了 PreProcess/PostProcess/HandleError/ChainError 等虚方法，但所有方法都是空实现（返回 `Success(null)`）。当前没有子类重写这些方法。

**NodeProcessSkeleton 的空方法：**
```csharp
protected virtual Task<BasicExecutionResult> PreProcessAsync(...) 
    => Task.FromResult(BasicExecutionResult.Success(null));
protected virtual Task<BasicExecutionResult> PostProcessAsync(...) 
    => Task.FromResult(BasicExecutionResult.Success(null));
protected virtual Task<BasicExecutionResult> HandleErrorAsync(...) 
    => Task.FromResult(BasicExecutionResult.Failure(...));
```

**建议：**
- 如果暂时不需要扩展，简化骨架类，移除虚方法
- 或添加抽象方法强制子类实现

---

## 三、错误逻辑（Bug / Logic Errors）

### 11. 🔴 `PipelineContext.IWorkflowContext.Items` 每次返回新对象

| 项目 | 值 |
|------|-----|
| **文件** | `CentralPipeline/WorkflowContext.cs:295-296` |
| **严重性** | **数据丢失 Bug** |

**代码：**
```csharp
IDictionary<string, object> IWorkflowContext.Items => 
    new System.Collections.Concurrent.ConcurrentDictionary<string, object>(_sharedData, StringComparer.OrdinalIgnoreCase);
```

**问题：**
每次通过接口访问 `Items` 属性都创建一个新的 `ConcurrentDictionary` 副本。外部代码修改这个副本不会同步回 `_sharedData`，导致数据丢失。

**示例——以下代码不会生效：**
```csharp
var ctx = (IWorkflowContext)pipelineContext;
ctx.Items["myKey"] = myValue; // 修改的是临时副本！
// _sharedData 中没有任何变化
```

**影响：**
- 所有通过 `IWorkflowContext.Items` 写入的数据都会丢失
- 这是一个静默失败，没有编译警告或运行时异常

**修复方案：**
```csharp
IDictionary<string, object> IWorkflowContext.Items => _sharedData;
// 直接返回 ConcurrentDictionary，它实现了 IDictionary<string, object>
```

---

### 12. 🔴 `WorkflowContext.CreateSubContext` 不复制 Items

| 项目 | 值 |
|------|-----|
| **文件** | `Abstractions/WorkflowContext.cs:108-119` |
| **严重性** | **数据丢失 Bug** |

**代码：**
```csharp
public IWorkflowContext CreateSubContext(string? nodeKey = null)
{
    return new WorkflowContext(
        workflowId: WorkflowId,
        executionId: ExecutionId,
        nodeKey: nodeKey ?? NodeKey,
        parameters: new Dictionary<string, object>(Parameters), // 复制了 Parameters
        serviceProvider: ServiceProvider)
    {
        CancellationToken = CancellationToken
        // Items 没有复制！子上下文的 Items 是空字典
    };
}
```

**问题：**
`CreateSubContext` 复制了 `Parameters`，但没有复制 `Items`（共享数据字典）。子上下文中的 `Items` 是空字典，导致子节点无法访问父上下文设置的共享数据。

**影响：**
- Loop 节点使用 `CreateSubContext` 创建子上下文时，子节点无法访问父上下文的数据
- Condition 节点同样受影响
- 这是一个静默失败

**修复方案：**
```csharp
public IWorkflowContext CreateSubContext(string? nodeKey = null)
{
    return new WorkflowContext(
        workflowId: WorkflowId,
        executionId: ExecutionId,
        nodeKey: nodeKey ?? NodeKey,
        parameters: new Dictionary<string, object>(Parameters),
        serviceProvider: ServiceProvider)
    {
        CancellationToken = CancellationToken,
        Items = new Dictionary<string, object>(Items) // 复制 Items
    };
}
```

---

### 13. 🟡 `NodeExecutor.ExecuteConditionNodeAsync` 忽略 false 分支

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/NodeExecutor.cs:175-213` |
| **严重性** | **功能缺失 Bug** |

**代码：**
```csharp
private async Task<BasicExecutionResult> ExecuteConditionNodeAsync(...)
{
    if (!string.IsNullOrEmpty(node.FunctionName))
    {
        var conditionResult = await ExecuteFunctionNodeAsync(node, context, cancellationToken);
        if (!conditionResult.IsSuccess) return conditionResult;

        var conditionMet = conditionResult.Value is bool b && b;

        if (conditionMet && node.Children.Count > 0)
        {
            // 条件满足，执行子节点
            foreach (var child in node.Children) { ... }
        }
        // 条件不满足时：直接返回 Success(true)，没有执行 false 分支
    }
    return BasicExecutionResult.Success(true);
}
```

**问题：**
条件节点执行时，如果条件函数返回 `false`，代码只返回 `BasicExecutionResult.Success(true)`，没有执行 `falseNodes`。`WorkflowExtensions.CreateConditionNode` 方法将 true 分支和 false 分支的节点都添加到 `Children` 中，但 `ExecuteConditionNodeAsync` 没有区分。

**影响：**
- 条件节点的 false 分支永远不会被执行
- 条件节点的功能不完整

**修复方案：**
需要一种机制区分 true 分支和 false 分支的节点，例如：
- 在 NodeDefinition 中添加 `FalseChildren` 属性
- 或在 Children 节点上标记分支类型

---

### 14. 🟡 `ExecutionOrchestrator` 暂停检查使用 `WorkflowId` 而非 `SessionId`

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/ExecutionOrchestrator.cs:158` |
| **严重性** | **功能错误** |

**代码：**
```csharp
// 使用 WorkflowId（即 sessionId）作为暂停检查键
if (SessionManager != null)
{
    await SessionManager.WaitIfPausedAsync(context.WorkflowId, cancellationToken);
}
```

**问题：**
注释说"使用 WorkflowId（即 sessionId）"，但 `context.WorkflowId` 是工作流标识（如 "WorkflowA"），不是会话标识。同一工作流可能有多个并发会话，使用 WorkflowId 作为暂停键会导致所有会话共享同一个暂停状态。

**影响：**
- 暂停一个会话会意外影响同一工作流的所有其他会话
- 注释本身也是错误的（WorkflowId != sessionId）

**修复方案：**
```csharp
await SessionManager.WaitIfPausedAsync(context.ExecutionId, cancellationToken);
// ExecutionId 是会话级别的唯一标识
```

---

### 15. 🟡 `DefaultExecutionOrchestrator` 不传 `SessionManager`

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/DefaultExecutionOrchestrator.cs:22-24` |
| **严重性** | **功能缺失** |

**代码：**
```csharp
public DefaultExecutionOrchestrator(
    CentralPipeline centralPipeline,
    INodeExecutor nodeExecutor,
    IWorkflowLogger? logger = null)
    : base(centralPipeline, nodeExecutor, null, logger) // SessionManager = null
{
}
```

**问题：**
默认编排器不传入 SessionManager，导致 `ExecuteNodeAndDescendantsAsync` 中的暂停检查（`SessionManager?.WaitIfPausedAsync`）静默跳过。

**影响：**
- 使用 `DefaultExecutionOrchestrator` 时，暂停功能完全不可用
- 调用 `WorkflowEngine.PauseAsync` 会标记会话为 Paused，但实际执行不会暂停

**修复方案：**
```csharp
public DefaultExecutionOrchestrator(
    CentralPipeline centralPipeline,
    INodeExecutor nodeExecutor,
    ISessionManager? sessionManager = null, // 新增参数
    IWorkflowLogger? logger = null)
    : base(centralPipeline, nodeExecutor, sessionManager ?? new DefaultSessionManager(), logger)
{
}
```

---

### 16. 🟡 `DataContext.GetItem<T>` 返回 `default` 而非抛出异常

| 项目 | 值 |
|------|-----|
| **文件** | `Session/DataContext.cs:23-34` |
| **严重性** | **行为不一致** |

**代码：**
```csharp
public T GetItem<T>(string key)
{
    if (_data.TryGetValue(key, out var value))
    {
        if (value is T typedValue)
            return typedValue;
        throw new InvalidCastException(...);
    }
    return default; // 键不存在时返回 default(T)
}
```

**问题：**
- 键不存在时返回 `default(T)`（如 `null`、`0`、`false`）
- 调用方无法区分"数据不存在"和"数据恰好是 default 值"
- 与 `WorkflowContext.GetData<T>` 的行为不一致（后者抛出 `KeyNotFoundException`）

**影响：**
- 可能导致静默的空引用异常
- 调用方需要额外调用 `ContainsKey` 来确认

**修复方案：**
```csharp
public T GetItem<T>(string key)
{
    if (_data.TryGetValue(key, out var value))
    {
        if (value is T typedValue)
            return typedValue;
        throw new InvalidCastException(...);
    }
    throw new KeyNotFoundException($"DataContext 中不存在键 '{key}'");
}
```

---

### 17. 🟡 `PipelineStageNames.AllStages` 缺少 `PreProcessing`

| 项目 | 值 |
|------|-----|
| **文件** | `Abstractions/IPipelineStage.cs:64-73` |

**问题描述：**
`PipelineStageNames` 定义了 `PreProcessing` 常量（第 40 行），但 `AllStages` 列表（第 64-73 行）中不包含它。列表包含的是 `GraphDefinition` 和 `FunctionExec` 等 Operation 层对齐的阶段。

**代码：**
```csharp
public static class PipelineStageNames
{
    public const string PreProcessing = "PreProcessing"; // 定义了
    // ...
    public static readonly IReadOnlyList<string> AllStages = new List<string>
    {
        Initialization,
        GraphDefinition,   // 新增
        FunctionExec,      // 新增
        Execution,
        PostProcessing,
        UpdateCheck,       // 新增
        Finalization
        // PreProcessing 不在列表中！
    }.AsReadOnly();
}
```

**建议：**
- 将 `PreProcessing` 添加到 `AllStages` 列表
- 或移除未使用的 `PreProcessing` 常量

---

### 18. 🟢 `WorkflowEngine.CancelAsync` 取消所有匹配会话

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/WorkflowEngine.cs:99-113` |

**问题描述：**
`CancelAsync(workflowId)` 遍历所有会话，取消所有 `WorkflowId` 匹配且状态为 Running/Paused 的会话。如果同一工作流有多个运行实例，会全部取消。

**建议：**
- 添加 `sessionId` 参数支持精确取消
- 或只取消最新的会话

---

### 19. 🟢 `NodeDefinition.TimeoutMs` 默认值触发超时

| 项目 | 值 |
|------|-----|
| **文件** | `Models/NodeDefinition.cs:34` + `Engine/NodeExecutor.cs:140-143` |

**问题描述：**
`TimeoutMs` 默认值为 `30000`（30 秒），而超时策略的条件是 `node.TimeoutMs > 0 && node.TimeoutMs < int.MaxValue`。默认值 30000 满足条件，意味着**所有节点默认都有 30 秒超时**。

**建议：**
- 将默认值改为 `0`（表示无超时）
- 或明确区分"未设置"和"30秒超时"

---

### 20. 🟢 `HotReloadService.ReloadSession` 不注册新会话

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/HotReloadService.cs:79-121` |

**问题描述：**
`ReloadSession` 创建了新会话，取消了旧会话，从 SessionManager 移除了旧会话，但**没有将新会话注册到 SessionManager**。

**代码：**
```csharp
var newSession = _sessionFactory.CreateSession(newWorkflow);
// ... 复制元数据 ...
oldSession.MarkCancelled();
_sessionManager?.RemoveSession(oldSession.SessionId);
// 缺少：_sessionManager?.RegisterSession(newSession);
```

**影响：**
- 新会话处于"游离"状态，无法被查询和管理
- 调用 `GetStatus` 无法获取新会话的状态

**修复方案：**
```csharp
_sessionManager?.RegisterSession(newSession);
```

---

## 四、架构/设计问题

### 21. 🟡 `ISessionManager` 定义在 `ExecutionManager.cs` 中

| 项目 | 值 |
|------|-----|
| **文件** | `Engine/ExecutionManager.cs` |

**问题描述：**
文件名是 `ExecutionManager`，但实际定义的是 `ISessionManager` 接口和 `DefaultSessionManager` 实现。文件名与内容不匹配。

**建议：**
- 重命名为 `SessionManager.cs`
- 或将接口和实现分离到不同文件

---

### 22. 🟡 `CentralPipeline` 目录与 `Pipeline` 目录并存

| 项目 | 值 |
|------|-----|
| **目录** | `CentralPipeline/` 和 `Pipeline/` |

**问题描述：**
`CentralPipeline/WorkflowContext.cs` 在 `CentralPipeline` 目录下，命名空间为 `WorkFlowMethod.CentralPipeline`，而 `Pipeline/CentralPipeline.cs` 在 `Pipeline` 目录下，命名空间为 `WorkflowExecution.Pipeline`。目录结构和命名空间混乱。

**建议：**
- 统一目录结构，将 `CentralPipeline/` 的内容合并到 `Pipeline/` 或 `Abstractions/`

---

### 23. 🟡 `StageBridge` 依赖 Operation 层类型

| 项目 | 值 |
|------|-----|
| **文件** | `Pipeline/StageBridge.cs` |

**问题描述：**
`StageBridge` 引用了 `StageBase` 和 `IStageContext`（来自 Operation 层），Share 层代码直接依赖上层模块，违反了分层架构原则。

**建议：**
- 将 `StageBridge` 移到 Operation 层
- 或在 Share 层定义适配器接口

---

## 五、优先级建议

### 立即修复（P0 - 数据正确性）
1. **#11** `PipelineContext.IWorkflowContext.Items` 返回副本导致数据丢失
2. **#12** `WorkflowContext.CreateSubContext` 不复制 Items 导致子上下文数据丢失

### 尽快修复（P1 - 功能完整性）
3. **#13** `ExecuteConditionNodeAsync` 忽略 false 分支
4. **#14** 暂停检查使用 WorkflowId 而非 SessionId
5. **#15** `DefaultExecutionOrchestrator` 不传 SessionManager
6. **#20** `HotReloadService.ReloadSession` 不注册新会话

### 清理（P2 - 代码质量）
7. **#6** 移除废弃的 `PipelineDefinition`
8. **#7** 移除废弃的 `WorkflowExceptionInfo`
9. **#4** 移除废弃的 `ExecuteTopologyAsync`
10. **#8** 移除 `HotReloadService._cancellationSources` 冗余参数

### 改进（P3 - 设计优化）
11. **#1** 合并两个 WorkflowContext 实现
12. **#2** 统一 DataContext 和 WorkflowContext.Items
13. **#16** 修复 `DataContext.GetItem<T>` 的返回值行为
14. **#19** 调整 `TimeoutMs` 默认值
