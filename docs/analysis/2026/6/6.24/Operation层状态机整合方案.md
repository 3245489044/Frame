# Operation 层状态机整合方案

> 分析 `Operation/WorkFlowMethod/Execution` 如何应用 Share 层和 BasicMethod 层的状态机基础设施

---

## 1. 现状：三层状态机体系

### 1.1 BasicMethod 层（通用状态机框架）

**位置**：`BasicMethod/StateMachine/`  
**命名空间**：`Modules.StateMachine`

| 组件 | 职责 | 泛型参数 |
|------|------|---------|
| `ConditionalStateMachine<TState>` | **核心**：条件转换状态机，支持 `TryTransitionTo`/`TryTransitionFrom`，线程安全，时间追踪，事件通知 | `TState : struct, Enum` |
| `LifecycleStateMachine<TOperation>` | 操作驱动状态机，`Permit`/`Fire` 模式，非法转换抛异常 | `TOperation : Enum` |
| `SequentialStateMachine<TInput,TContext,TOutput>` | 输入流状态机，`Feed`/`Complete` 模式，适合 Tokenizer | 三个泛型 |
| `AsyncEventStateMachine<TEvent,TResult>` | 异步事件状态机，`WaitAsync`/`OnEvent` 模式 | 两个泛型 |
| `StateMachineStore<TState>` | 多实例状态机存储，TTL 自动清理 | `TState : struct, Enum` |
| `IConditionalStateMachine<TState>` | 条件转换状态机接口 | `TState : struct, Enum` |
| `IStateMachineStore<TState>` | 状态机存储接口 | `TState : struct, Enum` |
| `IPauseManager` | 暂停/恢复管理器接口 | 无 |

**关键接口** `IConditionalStateMachine<TState>`：

```csharp
public interface IConditionalStateMachine<TState> : IStateMachine<TState>
    where TState : struct, Enum
{
    DateTime StartTime { get; }
    DateTime? EndTime { get; }
    bool IsActive { get; }
    bool IsFinished { get; }
    bool TryTransitionTo(TState newState, Func<TState, bool>? condition = null);
    bool TryTransitionFrom(TState expectedState, TState newState);
}
```

### 1.2 Share 层（执行状态管理）

**位置**：`Share/StateManagement/`  
**命名空间**：`Share.StateManagement`

| 组件 | 职责 | 与 BasicMethod 的关系 |
|------|------|---------------------|
| `ExecutionStatus` 枚举 | 定义 `NotStarted/Running/Paused/Completed/Faulted/Terminated` | 独立定义 |
| `ExecutionStateMachine` | 继承 `ConditionalStateMachine<ExecutionStatus>`，添加 `IsActiveState`/`IsFinishedState` 实现 | **继承自 BasicMethod** |
| `ExecutionExtensions` | 扩展方法：`TryStart()`/`TryPause()`/`TryResume()`/`TryComplete()`/`TryTerminate()`/`TryFault()` | 操作 `IConditionalStateMachine<ExecutionStatus>` |
| `IExecutionStateMachine<TState>` | 接口别名，继承 `IConditionalStateMachine<TState>` | 向后兼容 |
| `IExecutionStateStore<TState>` | 接口别名，继承 `IStateMachineStore<TState>` | 向后兼容 |
| `INodeExecutionCoordinator` | 节点执行协调器接口 | 独立 |
| `IPauseManager` | 接口别名，继承 `Modules.StateMachine.IPauseManager` | 向后兼容 |

**Share 层 `ExecutionStateMachine` 的核心代码**：

```csharp
public class ExecutionStateMachine : ConditionalStateMachine<ExecutionStatus>, IExecutionStateMachine<ExecutionStatus>
{
    public ExecutionStateMachine(ExecutionStatus initialState = ExecutionStatus.NotStarted) : base(initialState) { }

    protected override bool IsActiveState(ExecutionStatus state)
        => state == ExecutionStatus.Running || state == ExecutionStatus.Paused;

    protected override bool IsFinishedState(ExecutionStatus state)
        => state == ExecutionStatus.Completed || state == ExecutionStatus.Faulted || state == ExecutionStatus.Terminated;

    public void Reset() => Reset(ExecutionStatus.NotStarted);
}
```

### 1.3 Operation 层（工作流执行 - 待整合）

**位置**：`Operation/WorkFlowMethod/Execution/`  
**命名空间**：`WorkFlowMethod.Execution`

| 组件 | 当前状态 | 与上层的关系 |
|------|---------|-------------|
| `ExecutionStatus` 枚举 | **重复定义**（有 `Cancelled`，无 `Terminated`） | 与 Share 层不一致 |
| `ExecutionStateMachine` | **重复实现**（手写状态转换逻辑） | 未使用 BasicMethod/Share 层 |
| `NodeExecutionState` | 节点级状态追踪（独立） | 与状态机不同粒度 |
| `DataContext` | 数据上下文（独立） | 与状态机无关 |
| `DebugLog` | 调试日志（独立） | 与状态机无关 |
| `ExecutionSession` | 执行会话（使用上述组件） | 依赖重复的 ExecutionStateMachine |
| `ExecutionManager` | 会话管理器（使用 ExecutionSession） | 间接依赖重复的状态机 |

---

## 2. 问题分析

### 2.1 核心问题：重复定义

Operation 层的 `ExecutionStateMachine` 与 Share 层的 `ExecutionStateMachine` **功能完全重叠**，但实现方式不同：

| 特性 | Operation 层（手写） | Share 层（继承 BasicMethod） |
|------|-------------------|---------------------------|
| 线程安全 | 手写 `lock` | 基类自带 |
| 时间追踪 | 手写 `StartTime/EndTime/Duration` | 基类自带 |
| 状态转换验证 | `IsValidTransition` switch 表达式 | `TryTransitionTo` 条件委托 |
| 事件通知 | 无 | 基类自带 `StateChanged` 事件 |
| 状态枚举 | `NotStarted/Running/Paused/Completed/Faulted/Cancelled` | `NotStarted/Running/Paused/Completed/Faulted/Terminated` |
| 代码行数 | ~120 行 | ~30 行（继承基类） |

### 2.2 枚举差异

```
Operation 层: NotStarted → Running ↔ Paused → Completed / Faulted / Cancelled
Share 层:     NotStarted → Running ↔ Paused → Completed / Faulted / Terminated
```

- Operation 层有 `Cancelled`（取消），无 `Terminated`（终止）
- Share 层有 `Terminated`（终止），无 `Cancelled`（取消）
- 语义上 `Cancelled` 和 `Terminated` 不同：取消是用户主动行为，终止是系统强制行为

### 2.3 引用关系

Operation 层中引用 `ExecutionStateMachine` 和 `ExecutionStatus` 的文件：

| 文件 | 使用方式 |
|------|---------|
| `Session/ExecutionSession.cs` | 属性 `StateMachine` 类型为 `ExecutionStateMachine`，`Dispose()` 中调用 `TryTransitionTo(ExecutionStatus.Cancelled)` |
| `Session/ExecutionStateMachine.cs` | 定义自身 |
| `Engine/ExecutionManager.cs` | `CancelSession()` 中调用 `TryTransitionTo(ExecutionStatus.Cancelled)` |
| `Engine/ExecutionOrchestrator.cs` | `TryTransitionTo(ExecutionStatus.Running/Completed/Faulted/Cancelled)` |
| `Engine/WorkflowExecutionEngine.cs` | 间接使用（通过 ExecutionSession） |

---

## 3. 整合方案

### 3.1 第一步：统一 ExecutionStatus 枚举

**目标**：在 Share 层的 `ExecutionStatus` 中增加 `Cancelled` 状态，使枚举同时满足两种语义。

**修改文件**：`Share/StateManagement/Abstractions/IOrchestrationAbstractions.cs`

```csharp
public enum ExecutionStatus
{
    NotStarted,   // 未开始
    Running,      // 运行中
    Paused,       // 已暂停
    Completed,    // 已完成
    Faulted,      // 已故障
    Terminated,   // 已终止（系统强制）
    Cancelled     // 已取消（用户主动）  ← 新增
}
```

**修改文件**：`Share/StateManagement/Core/ExecutionStateMachine.cs`

```csharp
protected override bool IsFinishedState(ExecutionStatus state)
    => state == ExecutionStatus.Completed 
    || state == ExecutionStatus.Faulted 
    || state == ExecutionStatus.Terminated
    || state == ExecutionStatus.Cancelled;  // 新增
```

**修改文件**：`Share/StateManagement/Extensions/ExecutionExtensions.cs`

```csharp
/// <summary>尝试取消执行（从任意非终态转换到 Cancelled）</summary>
public static bool TryCancel(this IConditionalStateMachine<ExecutionStatus> machine)
{
    return machine.TryTransitionTo(ExecutionStatus.Cancelled,
        s => s != ExecutionStatus.Completed 
          && s != ExecutionStatus.Faulted 
          && s != ExecutionStatus.Terminated
          && s != ExecutionStatus.Cancelled);
}
```

### 3.2 第二步：Operation 层改用 Share 层的 ExecutionStateMachine

**修改文件**：`Operation/WorkFlowMethod/Execution/Session/ExecutionSession.cs`

```csharp
// 删除：using WorkFlowMethod.Execution.Session;
// 新增：
using Share.StateManagement;

public class ExecutionSession : IDisposable
{
    // 类型不变，但 ExecutionStateMachine 现在指向 Share 层版本
    public ExecutionStateMachine StateMachine { get; }
    
    public ExecutionSession(string sessionId, GraphSnapshot graphSnapshot)
    {
        // ...
        StateMachine = new ExecutionStateMachine();  // ← Share 层版本
        // ...
    }
    
    public void Dispose()
    {
        // ...
        if (StateMachine.IsRunning)
        {
            StateMachine.TryCancel();  // ← 使用扩展方法
        }
        // ...
    }
}
```

**修改文件**：`Operation/WorkFlowMethod/Execution/Engine/ExecutionManager.cs`

```csharp
public bool CancelSession(string sessionId)
{
    if (_sessions.TryGetValue(sessionId, out var session))
    {
        return session.StateMachine.TryCancel();  // ← 使用扩展方法
    }
    return false;
}
```

**修改文件**：`Operation/WorkFlowMethod/Execution/Engine/ExecutionOrchestrator.cs`

```csharp
// 所有 TryTransitionTo(ExecutionStatus.Cancelled) 改为 TryCancel()
// TryTransitionTo(ExecutionStatus.Running) 改为 TryStart()
// TryTransitionTo(ExecutionStatus.Completed) 改为 TryComplete()
// TryTransitionTo(ExecutionStatus.Faulted) 改为 TryFault()

// 示例：
// 旧：session.StateMachine.TryTransitionTo(ExecutionStatus.Running)
// 新：session.StateMachine.TryStart()

// 旧：session.StateMachine.TryTransitionTo(ExecutionStatus.Cancelled)
// 新：session.StateMachine.TryCancel()
```

**删除文件**：`Operation/WorkFlowMethod/Execution/Session/ExecutionStateMachine.cs`

### 3.3 第三步：评估 NodeExecutionState 的通用化

`NodeExecutionState` 追踪单个节点的生命周期（Pending→Running→Completed/Failed/Skipped），与 `ExecutionStateMachine`（会话级状态机）是不同粒度的状态管理。

**建议**：暂时保留在 Operation 层，因为：
1. 它的状态枚举 `NodeStatus` 是工作流特有的（`Skipped` 状态）
2. 它包含 `ExecutionCount`（重试计数）和 `ErrorMessage` 等节点特定字段
3. 目前只在 `ExecutionSession` 中使用

**未来可提取**：如果其他模块也需要"子任务级状态追踪"，可以提取到 BasicMethod 作为 `TaskExecutionState<TKey>`。

### 3.4 第四步：评估 DataContext 和 DebugLog 的通用化

**DataContext**：线程安全键值存储 + 点号路径访问 + 快照/恢复 + 合并。**完全通用**，可提取到 BasicMethod。

**DebugLog**：每个会话独立的调试日志。**完全通用**，可提取到 BasicMethod。

**建议**：作为后续优化项，当前阶段先聚焦状态机整合。

---

## 4. 整合后的架构

```
BasicMethod/StateMachine/                    # 通用状态机框架
├── ConditionalStateMachine<TState>          # 核心：条件转换状态机
├── LifecycleStateMachine<TOperation>        # 操作驱动状态机
├── SequentialStateMachine<TInput,TContext>  # 输入流状态机
├── AsyncEventStateMachine<TEvent,TResult>   # 异步事件状态机
├── StateMachineStore<TState>                # 多实例存储
└── 接口定义                                  # IConditionalStateMachine, IStateMachineStore 等

Share/StateManagement/                       # 共享层 - 执行状态管理
├── ExecutionStatus                          # 枚举：NotStarted/Running/Paused/Completed/Faulted/Terminated/Cancelled
├── ExecutionStateMachine                    # 继承 ConditionalStateMachine<ExecutionStatus>
├── ExecutionExtensions                      # TryStart/TryPause/TryResume/TryComplete/TryFault/TryTerminate/TryCancel
└── IOrchestrationAbstractions              # 编排抽象接口（别名）

Operation/WorkFlowMethod/Execution/          # 工作流执行层
├── Session/
│   ├── ExecutionSession.cs                  # 使用 Share.ExecutionStateMachine
│   ├── NodeExecutionState.cs                # 保留（节点级状态，不同粒度）
│   └── DebugLog.cs                          # 保留（或后续提取）
├── Data/DataContext.cs                      # 保留（或后续提取）
└── Engine/
    ├── WorkflowExecutionEngine.cs           # 保留（工作流引擎）
    ├── ExecutionOrchestrator.cs             # 使用 Share.ExecutionExtensions
    ├── ExecutionManager.cs                  # 使用 Share.ExecutionStateMachine
    └── SessionFactory.cs                    # 保留
```

---

## 5. 操作步骤清单

| 步骤 | 操作 | 文件 | 风险 |
|------|------|------|------|
| 1 | Share 层 `ExecutionStatus` 增加 `Cancelled` | `IOrchestrationAbstractions.cs` | 低 |
| 2 | Share 层 `ExecutionStateMachine.IsFinishedState` 增加 `Cancelled` | `ExecutionStateMachine.cs` | 低 |
| 3 | Share 层 `ExecutionExtensions` 增加 `TryCancel()` | `ExecutionExtensions.cs` | 低 |
| 4 | Operation 层 `ExecutionSession` 改用 Share 层状态机 | `ExecutionSession.cs` | 中 |
| 5 | Operation 层 `ExecutionManager` 改用 `TryCancel()` | `ExecutionManager.cs` | 低 |
| 6 | Operation 层 `ExecutionOrchestrator` 改用扩展方法 | `ExecutionOrchestrator.cs` | 中 |
| 7 | 删除 Operation 层重复的 `ExecutionStateMachine.cs` | 删除文件 | 低 |
| 8 | 编译验证 | `dotnet build` | 低 |

---

## 6. 收益总结

| 指标 | 整合前 | 整合后 |
|------|--------|--------|
| **重复代码** | Operation 层 ~120 行手写状态机 | 删除，复用 Share 层 ~30 行 |
| **状态枚举** | 两套不一致的枚举 | 统一一套枚举 |
| **功能完整性** | Operation 层无事件通知 | 获得基类的 `StateChanged` 事件 |
| **可维护性** | 修改状态逻辑需改多处 | 修改 BasicMethod 一处，全局生效 |
| **扩展性** | 无法使用 `StateMachineStore` 等高级功能 | 可无缝使用 BasicMethod 全部状态机能力 |
