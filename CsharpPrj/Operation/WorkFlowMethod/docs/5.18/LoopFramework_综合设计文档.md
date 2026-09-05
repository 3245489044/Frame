# 循环框架（LoopFramework）综合设计文档

> **版本**: v1.2 | **日期**: 2026-05-18  
> **范围**: 循环框架架构设计、LoopWorkflow 通用化改进、Orchestration 复用分离、代码分析修复记录

---

## 目录

1. [总体架构](#1-总体架构)
2. [核心接口](#2-核心接口)
3. [状态机决策层](#3-状态机决策层)
4. [LoopWorkflow 通用化改进](#4-loopworkflow-通用化改进)
5. [Orchestration 复用分离](#5-orchestration-复用分离)
6. [迁移路径](#6-迁移路径)
7. [代码分析修复记录](#7-代码分析修复记录)
8. [附录：类型对照表](#8-附录类型对照表)

---

## 1. 总体架构

### 1.1 设计理念

循环框架本质是 **"状态机驱动的自适应循环"**：

```
while (!IsCompleted) {
    StateMachine.Decide()   →  状态机决策（当前状态 + 上下文 → 下一步）
    ActionExecutor.Execute() →  执行动作
    ContextManager.Update()  →  更新上下文与状态
}
```

### 1.2 架构分层

```
┌─────────────────────────────────────────────────────────────────────┐
│                        LoopFramework                                │
├─────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    控制层 (Control)                          │   │
│  │  LoopEngine │ LoopController │ LifecycleHook               │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                  决策层 (Decision)                           │   │
│  │  IStateMachine<TState>                                      │   │
│  │  RuleBasedStateMachine │ LLMStateMachine │ HybridStateMachine│   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                  执行层 (Execution)                          │   │
│  │  ActionExecutor │ RetryHandler │ TimeoutManager             │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                  上下文层 (Context)                          │   │
│  │  ContextManager │ HistoryStore │ StateStore                 │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                  观测层 (Observability)                      │   │
│  │  LoopTracker │ EventBus │ MetricsCollector                  │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                  扩展层 (Extension)                          │   │
│  │  Middleware Pipeline: OnStart→OnDecide→OnExecute→OnUpdate→OnEnd│ │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.3 命名空间

```
WorkFlowMethod.LoopFramework/
├── Abstractions/       # 抽象接口层（7 个接口）
├── Core/               # 核心实现（LoopEngine, LoopResult, LoopState, LoopStatus, LoopContext）
├── StateMachine/       # 状态机决策层（StateTransition, StateRule, StateRecord, StateManager, 3 种状态机）
├── Execution/          # 执行层（LegacyLoopEngineAdapter）
├── Context/            # 上下文管理层（ContextManager, HistoryStore）
├── Observability/      # 观测层（LoopTracker, LoopEvent）
└── Extensions/         # 扩展方法（LoopEngineBuilder）
```

---

## 2. 核心接口

### 2.1 ILoopEngine<TContext, TState>

```csharp
public interface ILoopEngine<TContext, TState>
{
    LoopStatus Status { get; }
    Task<LoopResult<TContext, TState>> ExecuteAsync(TContext initialContext, CancellationToken ct = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task TerminateAsync();
}
```

> **v1.1 变更**：移除未使用的 TAction 泛型参数（3→2）。

### 2.2 IStateMachine<TState>

```csharp
public interface IStateMachine<TState>
{
    TState CurrentState { get; }
    string Name { get; }
    Task<StateTransition<TState>> DecideAsync<TContext>(TContext context, CancellationToken ct = default);
    void Reset();
}
```

### 2.3 IActionExecutor

```csharp
public interface IActionExecutor
{
    string Name { get; }
    Task<ActionResult> ExecuteAsync(string actionName, Dictionary<string, object> parameters, CancellationToken ct = default);
}
```

### 2.4 IContextManager<TContext>

```csharp
public interface IContextManager<TContext>
{
    TContext Current { get; }
    Task UpdateAsync(ActionResult result);
    string ToSummary();
    void Reset();
}
```

### 2.5 ILoopController

```csharp
public interface ILoopController
{
    string InstanceId { get; set; }
    int MaxIterations { get; set; }
    bool ShouldContinue(LoopContext context);
    Task OnLoopStartAsync(LoopContext context);
    Task OnLoopEndAsync(LoopContext context, LoopResult result);
}
```

### 2.6 ILoopObserver / ILoopMiddleware

```csharp
public interface ILoopObserver
{
    Task OnStateChangedAsync<TState>(TState oldState, TState newState);
    Task OnDecisionMadeAsync<TState>(StateTransition<TState> decision);
    Task OnActionExecutedAsync(ActionResult result);
    Task OnErrorAsync(Exception error, string context);
}

public interface ILoopMiddleware
{
    Task InvokeAsync(LoopContext context, Func<Task> next);
}
```

---

## 3. 状态机决策层

### 3.1 状态定义

```csharp
/// <summary>业务循环状态（与 LoopStatus 引擎运行时状态区分）</summary>
public enum LoopState
{
    NotStarted, Running, Deciding, Paused, Completed, Failed, Terminated, MaxIterationsReached
}

/// <summary>引擎运行时状态（NotStarted→Running→Paused/Completed/Terminated）</summary>
public enum LoopStatus
{
    NotStarted, Running, Paused, Completed, Terminated
}
```

### 3.2 三种状态机实现

| 状态机 | 适用场景 | 决策依据 | LLM 约束 |
|--------|---------|---------|---------|
| **RuleBasedStateMachine** | 确定性工作流 | 预定义规则表 + 条件匹配 | 无 |
| **LLMStateMachine** | 动态决策工作流 | LLM 推理 + 状态约束 | 在允许转移范围内 |
| **HybridStateMachine** | 规则优先，LLM 兜底 | 规则匹配 → 不匹配时 LLM | 仅兜底时使用 |

**HybridStateMachine 关键逻辑**（v1.1 修复：LLM 决策后同步规则状态机状态）：

```csharp
public async Task<StateTransition<LoopState>> DecideAsync<TContext>(TContext context, CancellationToken ct)
{
    var ruleResult = await _ruleMachine.DecideAsync(context, ct);
    if (!ruleResult.IsCompleted) return ruleResult; // 规则匹配成功
    if (!_fallbackToLLM) return ruleResult;

    var llmResult = await _llmMachine.DecideAsync(context, ct);
    llmResult.Reasoning = $"规则不匹配，LLM 兜底决策: {llmResult.Reasoning}";

    if (!llmResult.IsCompleted)
        _ruleMachine.SetCurrentState(llmResult.NextState); // 同步状态 ← v1.1 修复

    return llmResult;
}
```

### 3.3 状态转移

```csharp
public class StateTransition<TState>
{
    public TState NextState { get; set; }
    public string ActionName { get; set; }
    public string Reasoning { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsLLMDecision { get; set; }
}
```

---

## 4. LoopWorkflow 通用化改进

### 4.1 改进目标

1. **泛型化**：具体类型 → 泛型参数
2. **接口化**：硬编码逻辑 → 可替换接口
3. **状态机化**：条件匹配 → 状态机决策
4. **可扩展化**：增加生命周期钩子和中间件

### 4.2 LoopEngine 核心实现

> 完整源码见 `LoopFramework/Core/LoopEngine.cs`，此处仅列出关键修复点。

**v1.1 修复：**
- `_contextManager.Reset()` 从循环体内移出 → 避免每次迭代清空上下文
- `PauseAsync()` 调用 `_cts?.Cancel()` → 中断当前迭代
- `TerminateAsync()` 调用 `_cts?.Cancel()` → 确保线程安全
- 中间件管道捕获状态快照 `var currentStatus = _status` → 避免闭包陷阱
- 两个 `ExecuteWithMiddlewareAsync` 重载统一委托给泛型重载

### 4.3 LoopEngineBuilder

```csharp
public class LoopEngineBuilder<TContext, TState>
{
    // 流畅配置 API
    public LoopEngineBuilder<TContext, TState> UseStateMachine(IStateMachine<TState> sm);
    public LoopEngineBuilder<TContext, TState> UseActionExecutor(IActionExecutor executor);
    public LoopEngineBuilder<TContext, TState> UseContextManager(IContextManager<TContext> manager);
    public LoopEngineBuilder<TContext, TState> UseController(ILoopController controller);
    public LoopEngineBuilder<TContext, TState> AddObserver(ILoopObserver observer);
    public LoopEngineBuilder<TContext, TState> AddMiddleware(ILoopMiddleware middleware);
    public LoopEngineBuilder<TContext, TState> WithInstanceId(string instanceId);
    public LoopEngineBuilder<TContext, TState> WithMaxIterations(int maxIterations);
    public LoopEngine<TContext, TState> Build();
}
```

### 4.4 LegacyLoopEngineAdapter

向后兼容适配器，将 `LoopWorkflowExecutor` 包装为 `ILoopEngine<SmartContext, LoopState>`。完整源码见 `LoopFramework/Execution/LegacyLoopEngineAdapter.cs`。

---

## 5. Orchestration 复用分离

### 5.1 可复用组件

| 组件 | 来源 | 目标 | 复用方式 |
|------|------|------|---------|
| `ExecutionStatus` | `RefactoredOrchestration.cs` | `LoopState` | 合并增强 |
| `ExecutionStateRecord` | `RefactoredOrchestration.cs` | `StateRecord<TState>` | 泛型化 |
| `ExecutionStateManager` | `RefactoredOrchestration.cs` | `StateManager<TState>` | 泛型化 |
| `WorkflowExecutionTracker` | `OrchestrationFramework.cs` | `LoopTracker` | 适配 |
| `Result<T>` | `Infrastructure/ResultTypes.cs` | `Result<T>` | 直接引用 |
| `ExceptionHandler` | `Infrastructure` | `ExceptionHandler` | 直接引用 |

### 5.2 不可复用组件

| 组件 | 原因 |
|------|------|
| `GenericWorkflowStorageManager<TWorkflow>` | 基于 ECS 架构，太重 |
| `ChainInfo<TNode>` / 图结构 | 静态链式节点图，非动态状态转移 |
| `GraphFoundationBridge` / `GraphWorkflowConnector` | 图管线连接，与循环框架无关 |
| `WorkflowMediator` | JSON 创建工作流，与循环框架无关 |
| `NodeManager<TNode>` | 节点管理，与循环框架无关 |

### 5.3 复用组件说明

> 完整源码见对应文件：`StateRecord.cs`、`StateManager.cs`、`LoopTracker.cs`，此处仅列出关键设计点。

**StateRecord<TState>**（`LoopFramework/StateMachine/StateRecord.cs`）：
- 追踪实例状态、当前动作、已完成/失败动作集合
- `OnStatusChanged` 事件保留供外部扩展（框架内部通过 `ILoopObserver` 传播）

**StateManager<TState>**（`LoopFramework/StateMachine/StateManager.cs`）：
- 并发安全的状态管理（`ConcurrentDictionary` + `SemaphoreSlim`）
- v1.1 移除未使用的 `_actionSemaphores` 字段

**LoopTracker**（`LoopFramework/Observability/LoopTracker.cs`）：
- 离线数据记录器（与 `ILoopObserver` 实时通知区分）
- 两者可配合使用：`ILoopObserver` 实现类可调用 `LoopTracker` 记录数据

---

## 6. 迁移路径

| 阶段 | 内容 | 状态 |
|------|------|------|
| **一：框架定义** | 架构设计、命名空间、抽象接口、核心类型 | ✅ 已完成 |
| **二：核心实现** | LoopEngine、3 种状态机、Builder、Orchestration 复用提取 | ✅ 已完成 |
| **三：适配集成** | LegacyLoopEngineAdapter、LoopWorkflowExecutor 委托、UsageExample 更新 | ✅ 已完成 |
| **四：废弃旧代码** | 标记 `[Obsolete]`、迁移使用者、移除旧代码 | ✅ 已完成 |
| | 已删除文件：`LoopWorkflowExecutor.cs`、`ConditionEvaluator.cs`、`SelectionExecutor.cs` | |

---

## 7. 代码分析修复记录

### 7.1 逻辑错误（8项）

| # | 问题 | 严重 | 文件 | 修复 |
|---|------|------|------|------|
| 1.1 | `_iterationCount` 未重置 | 🔴 | `DefaultLoopController.cs` | `OnLoopStartAsync` 中重置计数器 |
| 1.2 | `_contextManager.Reset()` 位置错误 | 🔴 | `LoopEngine.cs` | 移出循环体 |
| 1.3 | `PauseAsync` 暂停形同虚设 | 🟡 | `LoopEngine.cs` | 调用 `_cts?.Cancel()` 中断当前迭代 |
| 1.4 | `TerminateAsync` 线程不安全 | 🟡 | `LoopEngine.cs` | 调用 `_cts?.Cancel()` |
| 1.5 | `HybridStateMachine` 状态不同步 | 🟡 | `HybridStateMachine.cs` | LLM 决策后同步规则状态机 |
| 1.6 | `LoopWorkflowExecutor` 死代码 | 🟢 | `LoopWorkflowExecutor.cs` | 移除 3 个未使用私有方法 |
| 1.7 | **RuleBasedStateMachine 空条件逻辑不一致** | 🟡 | `RuleBasedStateMachine.cs` | 空条件视为匹配（无条件规则应始终触发） |
| 1.8 | **LLMStateMachine 上下文类型硬编码** | 🟡 | `LLMStateMachine.cs` | 移除对 SmartContext 的硬编码依赖，使用泛型反射 |

### 7.2 架构缺陷（7项）

| # | 问题 | 文件 | 修复 |
|---|------|------|------|
| 2.1 | `LoopState` 与 `LoopStatus` 双枚举混淆 | `LoopState.cs`, `LoopStatus.cs` | XML 文档明确职责 |
| 2.2 | `ILoopEngine` 泛型参数 `TAction` 未使用 | `ILoopEngine.cs` | 移除（3→2 参数） |
| 2.3 | `LoopContext` 每次迭代新建 | `LoopEngine.cs` | `IContextManager` 持久化 |
| 2.4 | 中间件管道闭包陷阱 | `LoopEngine.cs` | 快照捕获 `var currentStatus = _status` |
| 2.5 | `StateManager._actionSemaphores` 未使用 | `StateManager.cs` | 移除 |
| 2.6 | `LoopTracker` 与 `ILoopObserver` 功能重叠 | `LoopTracker.cs` | 文档明确职责 |
| 2.7 | **新旧两套 API 并行存在** | `LoopWorkflowExecutor.cs` | `LegacyLoopEngineAdapter` 作为桥梁，旧 API 内部委托给新 API |

### 7.3 冗余逻辑（7项）

| # | 问题 | 文件 | 修复 |
|---|------|------|------|
| 3.1 | `ExecuteWithMiddlewareAsync` 两重载不一致 | `LoopEngine.cs` | 统一委托给泛型重载 |
| 3.2 | **ConditionEvaluator 与 ConditionEvaluatorProxy 重复** | `RuleBasedStateMachine.cs` | 合并到 `ConditionEvaluatorProxy`，内联在 `RuleBasedStateMachine.cs` 中 |
| 3.3 | `LoopWorkflowExecutor` 空壳类 | `LoopWorkflowExecutor.cs` | 精简为纯委托 |
| 3.4 | `IterationRecord` 双重定义 | `IterationRecord.cs` | 文档明确职责 |
| 3.5 | `StateRecord.OnStatusChanged` 事件未使用 | `StateRecord.cs` | 标注为外部扩展 |
| 3.6 | **UsageExample 旧版示例方法** | `UsageExample.cs` | 移除 3 个旧版示例（RunRuleBasedExampleAsync、RunLLMBasedExampleAsync、RunHybridExampleAsync） |
| 3.7 | **LegacyLoopEngineAdapter 冗余类型限定** | `LegacyLoopEngineAdapter.cs` | 使用 `using` 简化 `ConvertStatus` 中的类型引用 |

### 7.4 构建验证

```
Build succeeded. 0 Error(s), 0 Warning(s) (LoopFramework 相关)
```

---

## 8. 附录：类型对照表

### 8.1 LoopWorkflow → LoopFramework

| 旧类型 | 新类型 | 变化 |
|--------|--------|------|
| `LoopWorkflowExecutor` | `LoopEngine<TContext, TState>` | 泛型化（2 参数） |
| `LoopWorkflowDefinition` | 无（由 Builder 配置） | 移除 |
| `LoopWorkflowResult` | `LoopResult<TContext, TState>` | 泛型化 |
| `LoopStatus` | `LoopState` | 合并增强 |
| `SmartContext` | `IContextManager<TContext>` | 接口化 |
| `SelectionExecutor` | `IStateMachine<TState>` | 状态机化 |
| `SelectionRule` | `StateRule` | 增强（FromState/ToState） |
| `SelectionStrategy` / `SelectionStrategyType` | 无（由状态机类型决定） | 移除 |
| `SubWorkflowSelection` | `StateTransition<TState>` | 增强 |
| `SubWorkflowDefinition` | `IActionExecutor` 的输入 | 接口化 |
| `SubWorkflowResult` | `ActionResult` | 重命名 |
| `IterationRecord` | `LoopResult` 内部记录 | 合并 |
| `ISubWorkflowRunner` | `IActionExecutor` | 重命名 |
| `ILLMService` / `LLMServiceConfig` | 保留 | 不变 |
| `ConditionEvaluator` | **`ConditionEvaluatorProxy`（内联）** | **合并到 RuleBasedStateMachine.cs** |

### 8.2 Orchestration → LoopFramework

| Orchestration 类型 | LoopFramework 类型 | 变化 |
|-------------------|-------------------|------|
| `ExecutionStatus` | `LoopState` | 合并 |
| `ExecutionStateRecord` | `StateRecord<TState>` | 泛型化 |
| `ExecutionStateManager` | `StateManager<TState>` | 泛型化 |
| `WorkflowExecutionTracker` | `LoopTracker` | 适配 |
| `Result<T>` | `Result<T>` | 直接引用 |

### 8.3 使用示例

**旧方式（LoopWorkflow，已废弃）：**
```csharp
var workflow = new LoopWorkflowDefinition { WorkflowId = "data-pipeline", Goal = "处理数据", MaxIterations = 10 };
workflow.SubWorkflows["extract"] = new SubWorkflowDefinition { ... };
workflow.Strategy = new SelectionStrategy { Type = SelectionStrategyType.RuleBased, Rules = ... };
var context = new SmartContext { Goal = workflow.Goal };
var result = await new LoopWorkflowExecutor().ExecuteAsync(workflow, context);
```

**新方式（LoopFramework Builder API）：**
```csharp
// 1. 定义状态规则
var rules = new List<StateRule>
{
    new() { Name = "extract", FromState = LoopState.Running, ToState = LoopState.Running,
            ActionName = "extract", Condition = "context.raw_data == null", Priority = 100 },
    new() { Name = "clean", FromState = LoopState.Running, ToState = LoopState.Running,
            ActionName = "clean", Condition = "context.raw_data != null && context.clean_data == null", Priority = 90 },
    new() { Name = "report", FromState = LoopState.Running, ToState = LoopState.Completed,
            ActionName = "report", Condition = "context.transformed_data != null", Priority = 70 }
};

// 2. 构建引擎
var engine = LoopEngineBuilder.Create<SimpleContext, LoopState>()
    .WithStateMachine(new RuleBasedStateMachine(rules))
    .WithActionExecutor(new SimpleActionExecutor())
    .WithContextManager(new SimpleContextManager())
    .WithMaxIterations(10)
    .WithInstanceId("demo-001")
    .Build();

// 3. 执行
var result = await engine.ExecuteAsync(new SimpleContext());
```

---

> **文档版本**: v1.2 | **最后更新**: 2026-05-18  
> **主要变更**: 
> - v1.2: 新增 2 项逻辑错误修复（RuleBasedStateMachine 空条件、LLMStateMachine 硬编码）、1 项架构缺陷（新旧 API 并行）、2 项冗余逻辑（ConditionEvaluator 合并、UsageExample 清理、LegacyLoopEngineAdapter 简化）；删除 3 个旧代码文件
> - v1.1: 修复 6 项逻辑错误、6 项架构缺陷、5 项冗余逻辑；移除 TAction 泛型参数；精简文档结构
