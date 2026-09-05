# 任务与快照统一模型设计文档

> 日期：2026-08-13
> 状态：设计方案（v2 — 补充 ILoopState 契约、直跑路径、持久化模型、不变量保护）
> 核心原则：**修改任务 = 修改快照**——任务是快照的入口契约，快照是任务的运行时演化，两者是同一实体的不同视图

---

## 1. 问题陈述

### 1.1 当前状态

Agent 系统中存在两个独立概念：

| 概念 | 载体 | 标识键 | 职责 |
|------|------|--------|------|
| **任务（Task）** | `AgentTaskItem` | `TaskKey`（GUID） | 输入载荷：`UserInput`、`SchedulingMode`、优先级、重试策略 |
| **快照（Snapshot）** | `SchedulerState` | `hash(userInput)` | 运行时状态：轮次、预算、进度、策略、执行状态、数据空间 |

两者通过 `AgentTaskPoolAdapter` 桥接——桥接层负责：
- 任务入池时创建/加载快照
- 任务暂停时注入暂停门
- 任务完成时写回输出区

### 1.2 核心矛盾

**任务和快照本质是同一个东西的不同生命周期阶段：**

```
创建时 → 任务（"我要做什么"：UserInput + SchedulingMode）
执行中 → 快照（"正在做什么"：Round + Budget + Strategy + Status）
完成时 → 任务结果（"做成了什么"：AxiomLoopResult）
```

将二者分离导致：

1. **身份脱节**：`TaskKey`（GUID）与 `hash(userInput)` 是两套独立标识，相同输入的不同任务会共享快照
2. **双状态机**：`TaskPool` 的 `Running/Paused/Done` 与 `SchedulerState.AgentStatus` 的 `Pending/Running/Paused/Done` 并行追踪同一生命周期
3. **冗余持久化**：`ur_tasks` 表存任务、`IPersistenceStore` 存快照，两份数据需手动对齐
4. **语义断裂**：UI 要展示"任务完整状态"需同时查询两张表

### 1.3 设计洞察

> **一个任务在其整个生命周期中就是一个不断演化的快照。**
>
> 创建任务 = 创建快照的初始状态
> 暂停任务 = 冻结快照的当前状态
> 恢复任务 = 从冻结态继续演化
> 任务完成 = 快照进入终态
>
> 因此：**修改任务 = 修改快照**

---

## 2. 统一模型设计

### 2.1 核心实体：`AgentTaskItem` 实现 `ILoopState`

**关键设计决策**：`AgentTaskItem` 直接实现 `ILoopState` 接口，成为循环引擎的权威状态根。这使得：
- `SnapshotDrivenLoopEngine<TState>` 的 `TState` 直接是 `AgentTaskItem`
- `RoundLifecycle`/`ProgressUpdater`/`BudgetEscalator` 等算法层零改动
- `AxiomContext` 可以从 `AgentTaskItem` 直接派生租约

```csharp
/// <summary>
/// 统一实体：同时承载输入契约与运行时快照。
/// 实现 ILoopState 接口，成为循环引擎的权威状态根。
/// 修改任务 = 修改快照：二者是同一对象的不同视图。
/// </summary>
public sealed class AgentTaskItem : ITaskPoolItem, ILoopState
{
    // ========== 输入端（创建时赋值，不可变）==========
    public string TaskKey { get; set; }              // 唯一标识，同时作为快照持久化键
    public int Priority { get; set; }
    public int MaxRetries { get; set; }
    public string UserInput { get; set; } = string.Empty;
    public string? ThreadId { get; set; }
    public string? InputKey { get; set; }
    public SchedulingMode SchedulingMode { get; set; }
    public string? SchedulerRulesKey { get; set; }

    // ========== 运行时端（从 SchedulerState 迁移，实现 ILoopState）=========
    // ★ 核心对象不可外部直接修改——BudgetEscalator/ProgressUpdater 是唯一写入入口
    public FailureBudget Budget { get; } = new();       // ILoopState 契约
    public ProgressToken Token { get; } = new();        // ILoopState 契约
    public ControlFlag Flag { get; } = new();           // ILoopState 契约
    public int Round { get; set; }                       // ILoopState 契约
    public string? UserInput_ILoopState { get; set; }    // ILoopState 契约（与 UserInput 共享存储）
    public string? LastResultText { get; set; }          // ILoopState 契约
    public EvalOutcome LastEval { get; set; } = EvalOutcome.None; // ILoopState 契约
    public IDictionary<string, object> Data => _data;    // ILoopState 契约（显式接口实现）

    // ========== 生命周期状态（统一状态机，写入快照）==========
    /// <summary>任务生命周期状态：NotStarted/Running/Paused/Completed/Faulted/Cancelled。
    /// 暂停/恢复直接修改此字段——修改任务 = 修改快照。</summary>
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.NotStarted;

    // ========== Agent 扩展字段（非 ILoopState 契约）==========
    public Scope Scope { get; set; } = Scope.LoopBody;
    public int ContinuousFailures { get; set; }
    public string CurrentStrategyId { get; set; } = "";
    public List<string> AvailableStrategyIds { get; } = new();
    public Dictionary<string, string> AgentStatus { get; } = new(StringComparer.Ordinal);
    public List<string> RecentTransitions { get; } = new();
    public DataSpace _data = new(validate: false);

    // ========== ILoopState 显式接口实现 ==========
    string? ILoopState.UserInput { get => UserInput_ILoopState ?? UserInput; set => UserInput_ILoopState = value; }
    IDictionary<string, object> ILoopState.Data => _data;

    // ========== 投影方法 ==========
    public AgentRuntimeSnapshot ToRuntimeSnapshot(string stage)
        => new(
            Round: Round,
            Stage: stage,
            BudgetLevel: Budget.Level,
            TokenValue: Token.Value,
            TokenMax: Token.Max,
            Halt: Flag.Halt,
            ContinuousFailures: ContinuousFailures,
            CurrentStrategyId: string.IsNullOrWhiteSpace(CurrentStrategyId) ? null : CurrentStrategyId,
            AvailableStrategyIds: new List<string>(AvailableStrategyIds),
            AgentStatuses: AgentStatus
                .Select(kv => new ExecutorRunStatus(kv.Key, kv.Value))
                .ToList(),
            RecentTransitions: new List<string>(RecentTransitions),
            UpdatedAt: DateTimeOffset.UtcNow);
}
```

### 2.2 ILoopState 契约实现细节

`ILoopState` 接口（定义于 [LoopStatePrimitives.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/TaskStatusPool/Loops/LoopStatePrimitives.cs#L72-L97)）的 7 个成员映射：

| ILoopState 成员 | AgentTaskItem 承载 | 说明 |
|---|---|---|
| `FailureBudget Budget` | `Budget` (getter only) | 直接映射，无转换 |
| `ProgressToken Token` | `Token` (getter only) | 直接映射，无转换 |
| `ControlFlag Flag` | `Flag` (getter only) | 直接映射，无转换 |
| `int Round` | `Round` | 直接映射 |
| `string? UserInput` | `UserInput_ILoopState`（回退 `UserInput`） | 显式接口实现，避免输入端 `UserInput` 属性冲突 |
| `string? LastResultText` | `LastResultText` | 直接映射 |
| `EvalOutcome LastEval` | `LastEval` | 直接映射 |
| `IDictionary<string, object> Data` | `_data` (DataSpace) | 显式接口实现，DataSpace 内部即 `Dictionary<string, object>` |

### 2.3 从 AgentTaskItem 派生 AxiomContext（CreateLeaseContext）

当前 `AgentRunner.CreateLeaseContext(SchedulerState)` 从 `SchedulerState` 派生 `AxiomContext` 租约。统一后改为：

```csharp
/// <summary>
/// 从 AgentTaskItem 直接派生 AxiomContext 租约。
/// 因为 AgentTaskItem 实现了 ILoopState，Budget/Token/Flag 引用与根对象共享，
/// ProgressUpdater/BudgetEscalator 直写根状态，无需「派生复制 + 收割回写」。
/// </summary>
private static AxiomContext CreateLeaseContext(AgentTaskItem task)
{
    // ★ 直接将 task 的核心对象引用注入 AxiomContext，零拷贝
    return new AxiomContext(
        data: task._data,           // 共享 DataSpace
        budget: task.Budget,         // 共享 FailureBudget 引用
        token: task.Token,           // 共享 ProgressToken 引用
        flag: task.Flag)             // 共享 ControlFlag 引用
    {
        Round = task.Round,
        UserInput = task.UserInput,
        LastResultText = task.LastResultText,
        LastEval = task.LastEval,
        Scope = task.Scope,
    };
}
```

**关键不变量保持**：`FailureBudget`/`ProgressToken`/`ControlFlag` 均为类引用（非值拷贝），算法层（`ProgressUpdater`、`BudgetEscalator`）的 `Advance()`/`Escalate()`/`SetHalt()` 调用直接修改根 `AgentTaskItem` 的状态，无需回写。

### 2.4 不变量保护

**核心不变量**：`FailureBudget`/`ProgressToken`/`ControlFlag` 的更新必须经 `BudgetEscalator`/`ProgressUpdater` 单一入口。

**保护机制**（已内建于现有代码，统一后继续保持）：

| 不变量 | 保护方式 |
|---|---|
| `Budget.Level` 仅由 `BudgetEscalator` 修改 | `FailureBudget.Level` 为 `private set`，`Escalate()`/`Deescalate()`/`Trip()`/`Reset()` 为唯一写方法 |
| `Token.Value` 仅由 `ProgressUpdater` 修改 | `ProgressToken.Value` 为 `private set`，`Advance()`/`Retreat()`/`Reset()` 为唯一写方法 |
| `Flag.Halt` 仅由终止裁决修改 | `ControlFlag.Halt` 为 `private set`，`SetHalt()`/`Clear()` 为唯一写方法 |
| 混合调度不触碰确定性核心 | `HybridSchedulerStateMachine.ApplyGuarded` 防御性断言：施加后检查 Budget/Token/Flag/Round 是否被篡改，违反则回退 Fallback |

```csharp
// BudgetEscalator/ProgressUpdater 的调用路径不变：
// RoundLifecycle → BudgetEscalator.EvaluateDrivenUpdate(budget, eval) → budget.Escalate()/Deescalate()
// RoundLifecycle → ProgressUpdater.EvaluateDrivenUpdate(token, eval) → token.Advance()/Retreat()
// 这些调用直接作用于 AgentTaskItem 持有的核心对象引用
```

### 2.5 统一状态机（暂停写入快照）

**核心变更**：`TaskExecutionStatus` 不再由 `TaskPool` 状态机独立管理，而是作为 `AgentTaskItem.Status` 写入快照。暂停/恢复 = 修改 `AgentTaskItem.Status` = 修改快照。

```
                    ┌──────────────────────────────────────────────────┐
                    │                                                  │
                    ▼                                                  │
  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────┐ │
  │NotStarted│→ │ Running  │→ │ Paused   │→ │ Running   │→ │Complete│ │
  │          │  │          │  │          │  │          │  │        │ │
  └──────────┘  │          │  │          │  │          │  └────────┘ │
                │          │  │          │  │                         │
                │          │  │          │  │  ┌──────────┐  ┌──────┐ │
                │          └──│──────────┘  └─→│ Faulted  │  │ Canc │ │
                │             │                                │      │ │
                │             └───────────────────────────────┘      │
                │                                                    │
                └────────────────────────────────────────────────────┘

  ★ 所有状态存储于 AgentTaskItem.Status（快照内），非 TaskPool 外部状态机
```

**状态语义**（对齐 [TaskExecutionStatus.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Share/TaskStatusPool/TaskPool/TaskExecutionStatus.cs)）：

| 状态 | 含义 | 触发条件 | 写入方式 |
|------|------|----------|----------|
| `NotStarted` | 排队中 | 任务入池 | `item.Status = NotStarted` |
| `Running` | 执行中 | 任务被消费 | `item.Status = Running` |
| `Paused` | 暂停中 | 用户/系统暂停 | `item.Status = Paused` → 持久化 |
| `Completed` | 成功完成 | 循环正常终止 | `item.Status = Completed` |
| `Faulted` | 错误终止 | 预算耗尽/不可恢复错误 | `item.Status = Faulted` |
| `Cancelled` | 主动取消 | 用户取消 | `item.Status = Cancelled` |

**暂停/恢复流程（快照驱动）**：

```csharp
// 暂停：直接修改快照
public async Task PauseAsync(string taskKey, CancellationToken ct)
{
    var item = await _store.GetAsync(taskKey, ct);
    if (item == null) return;
    item.Status = TaskExecutionStatus.Paused;
    await _store.UpdatePayloadAsync(taskKey, item, ct);  // 持久化到 PayloadJson
    _runtimeState?.Publish(item.ToRuntimeSnapshot("paused"));
}

// 恢复：直接修改快照
public async Task ResumeAsync(string taskKey, CancellationToken ct)
{
    var item = await _store.GetAsync(taskKey, ct);
    if (item == null) return;
    item.Status = TaskExecutionStatus.Running;
    await _store.UpdatePayloadAsync(taskKey, item, ct);
    _runtimeState?.Publish(item.ToRuntimeSnapshot("resumed"));
}
```

**暂停门检查（快照内查询）**：

```csharp
// AgentLoopFunction.CheckLlmGateAsync —— 改为从快照读取 Status
public static async Task CheckLlmGateAsync(AgentTaskItem item, CancellationToken ct)
{
    while (item.Status == TaskExecutionStatus.Paused)
    {
        await Task.Delay(200, ct);  // 合作式等待
    }
    if (item.Status == TaskExecutionStatus.Cancelled)
        throw new OperationCanceledException();
}
```

---

## 3. 持久化模型

### 3.1 决策：保留 PayloadJson + 扩展序列化

**选择理由**：

| 方案 | 优点 | 缺点 |
|---|---|---|
| **A: PayloadJson（保留）** | 零改动 `ITaskPoolPersistence<TItem>` 接口；序列化器自动处理新字段；查询按 taskKey 精准定位即可 | SQL WHERE 子句无法按运行时字段过滤（需反序列化后过滤） |
| B: 扁平列 | SQL 查询可按运行时字段过滤（`WHERE round > 5`） | 需改 `ITaskPoolPersistence` 接口；Schema 迁移复杂；与通用任务池设计冲突 |

**选择 A 的理由**：当前 `ITaskPoolPersistence` 仅支持：
- `GetAsync(taskKey)` —— 按主键查询（运行时字段不需要作为 SQL WHERE 条件）
- `ListAsync(status)` —— 按任务池状态过滤（仍用 `Status` 列，不涉及运行时字段）
- `UpdateStatusAsync(taskKey, from, to)` —— 状态迁移（仍用 `Status` 列）

运行时字段（Round/Budget/Token 等）仅用于**内存中判断**（`budgetTerminate`、`TransitionGuard` 等）和**发布订阅**（`RuntimeStateBroker`），不需要 SQL 级过滤。因此 PayloadJson 方案完全满足需求。

### 3.2 序列化扩展

`AgentTaskItem` 的 `PayloadJson` 序列化自动包含所有属性（`System.Text.Json` 对公共属性默认序列化）。只需确保运行时属性的默认值在构造时设置：

```csharp
// AgentTaskItem 构造函数
public AgentTaskItem()
{
    Budget = new FailureBudget();
    Token = new ProgressToken();
    Flag = new ControlFlag();
    AvailableStrategyIds = new List<string> { "standard", "aggressive", "careful" };
    CurrentStrategyId = "standard";
    _data = new DataSpace(validate: false);
    // ... 其他默认值
}
```

**PayloadJson 序列化/反序列化示例**：

```json
{
  "TaskKey": "abc123",
  "Priority": 0,
  "MaxRetries": 3,
  "UserInput": "帮我分析代码",
  "SchedulingMode": "Hybrid",
  "Budget": { "Level": 1 },
  "Token": { "Value": 2, "Max": 3 },
  "Flag": { "Halt": false },
  "Round": 3,
  "LastEval": "Pass",
  "Scope": "LoopBody",
  "ContinuousFailures": 0,
  "CurrentStrategyId": "standard",
  "AvailableStrategyIds": ["standard", "aggressive", "careful"],
  "AgentStatus": { "cline": "Running" },
  "RecentTransitions": ["round:1", "strategy:standard"],
  "Data": { "active_strategy": "standard" }
}
```

### 3.3 Status 列与 PayloadJson 的同步

`ur_tasks` 表的 `Status` 列仍保留（供 `ListAsync(status)` SQL 过滤），但**权威来源改为 `AgentTaskItem.Status`**（嵌入 `PayloadJson`）。每次 `UpdatePayloadAsync` 同时同步两处：

```sql
-- 现有 Schema（零改动）
CREATE TABLE ur_tasks (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskKey      TEXT NOT NULL UNIQUE,
    Priority     INTEGER NOT NULL DEFAULT 0,
    MaxRetries   INTEGER NOT NULL DEFAULT 0,
    Status       TEXT NOT NULL,           -- SQL 过滤用镜像（从 PayloadJson 同步）
    PayloadJson  TEXT NOT NULL,           -- 完整 AgentTaskItem 序列化（含 Status 字段）
    CreatedAt    TEXT NULL,
    UpdatedAt    TEXT NULL
);
```

**`Status` 列** vs **`PayloadJson.Status`**：

| 维度 | `Status` 列 | `PayloadJson.Status` |
|---|---|---|
| 用途 | SQL WHERE 过滤（`ListAsync(status)`） | 内存中读取（暂停门、状态投影） |
| 写入者 | `UpdatePayloadAsync` 同步写入 | `AgentTaskItem.Status` 属性修改后序列化 |
| 权威性 | 镜像（从 PayloadJson 同步） | **权威来源** |
| 同步时机 | 每次 `UpdatePayloadAsync` + 每次 `PauseAsync`/`ResumeAsync` | 实时（内存对象属性） |

**`UpdatePayloadAsync` 扩展实现**（同时更新 `Status` 列和 `PayloadJson`）：

```csharp
public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
{
    var exec = SqliteAmbientTransaction.Resolve(_executor);
    var json = JsonSerializer.Serialize(item, _jsonOptions);
    // ★ 同时更新 Status 列和 PayloadJson，保证一致
    exec.ExecuteNonQuery(
        $"UPDATE [{_table}] SET [Status]=@s, [PayloadJson]=@j, [UpdatedAt]=@c WHERE [TaskKey]=@k",
        PersistenceHelper.CreateParameters(
            ("s", GetStatusFromItem(item)),   // 从 item.Status 提取
            ("j", json),
            ("c", PersistenceHelper.NowIso()),
            ("k", taskKey)));
    return Task.CompletedTask;
}
```

---

## 4. 直跑路径（ReactiveTrigger/非任务池驱动）

### 4.1 问题

`ReactiveTrigger` 直接调用 `AgentOrchestrator.RunAsync(userInput)`，不经过任务池。此时没有 `AgentTaskItem`，需要合成一个。

### 4.2 解决方案：合成瞬态 AgentTaskItem

在 `AgentOrchestrator.RunAsync` 入口处，如果没有 `taskKey`，自动合成一个瞬态 `AgentTaskItem`：

```csharp
// AgentOrchestrator.cs / AgentRunner.cs
public async Task<AxiomLoopResult> RunAsync(
    string userInput, CancellationToken ct = default,
    string? threadId = null, string? inputKey = null,
    string? taskKey = null, ...)
{
    // ★ 合成瞬态 AgentTaskItem（直跑模式）
    var item = await EnsureTaskItemAsync(userInput, taskKey, schedulingMode, ...);
    return await _runner.RunInternalAsync(item, timeout, ct);
}

/// <summary>
/// 确保 AgentTaskItem 存在：有 taskKey → 从持久化加载；无 → 合成瞬态。
/// 合成的瞬态 item 也会写入 ur_tasks（供 UI 查询完整状态），与任务池驱动路径统一。
/// </summary>
private async Task<AgentTaskItem> EnsureTaskItemAsync(
    string userInput, string? taskKey, SchedulingMode mode, string? rulesKey,
    string? threadId, string? inputKey, CancellationToken ct)
{
    // 1. 有 taskKey → 直接加载（任务池驱动路径）
    if (!string.IsNullOrEmpty(taskKey))
    {
        var existing = await _taskStore.GetAsync(taskKey, ct);
        if (existing != null) return existing;
        throw new InvalidOperationException($"Task {taskKey} not found");
    }

    // 2. 无 taskKey → 合成瞬态 item（直跑路径：ReactiveTrigger 等）
    var synthesized = new AgentTaskItem
    {
        TaskKey = Guid.NewGuid().ToString("N"),
        UserInput = userInput,
        ThreadId = threadId,
        InputKey = inputKey,
        SchedulingMode = mode,
        SchedulerRulesKey = rulesKey,
        Priority = 0,
        MaxRetries = 3
    };

    // ★ 写入 ur_tasks（Status=Pending），统一持久化路径
    await _taskStore.EnqueueAsync(synthesized, ct);
    return synthesized;
}
```

### 4.3 直跑路径统一行为

| 维度 | 任务池驱动 | 直跑（ReactiveTrigger） |
|---|---|---|
| TaskKey | 入池时生成（GUID） | `EnsureTaskItemAsync` 合成（GUID） |
| 持久化 | `ur_tasks` 有记录（Status 从 Pending→Running→Done） | `ur_tasks` 有记录（Status 从 Pending→Running→Done） |
| 状态查询 | `GetAsync(taskKey)` 返回完整快照 | `GetAsync(taskKey)` 返回完整快照 |
| 暂停支持 | ✅ `TaskPool.PauseAsync` | ✅ `TaskPool.PauseAsync`（通过合成的 item 关联） |

**效果**：直跑路径和任务池驱动路径完全统一——都是通过 `AgentTaskItem` 作为状态载体，都写入 `ur_tasks`，都支持暂停/恢复/查询。

---

## 5. 代码变更映射

### 5.1 核心文件变更总览

| 文件 | 操作 | 变更内容 |
|---|---|---|
| `AgentTaskItem.cs` | **修改** | 添加运行时属性 + 实现 `ILoopState` + `ToRuntimeSnapshot()` |
| `AgentTaskPoolAdapter.cs` | **修改** | handler 改为直接操作 `AgentTaskItem`；移除独立 `SchedulerState` 创建 |
| `AgentRunner.cs` | **修改** | `RunInternalAsync` 改为接受 `AgentTaskItem`；`CreateLeaseContext` 从 item 派生；合成瞬态 item 支持直跑 |
| `SchedulerState.cs` | **删除** | 属性迁移到 `AgentTaskItem` |
| `SchedulerStateSnapshotSerializer.cs` | **删除** | PayloadJson 序列化自动替代 |
| `SchedulerStateProjector.cs` | **修改** | 委托给 `AgentTaskItem.ToRuntimeSnapshot()` |
| `AgentRuntimeSnapshot.cs` | **保留** | 不变 |
| `AgentTaskPoolStore.cs` | **保留** | 无需改动（PayloadJson 自动扩展） |

### 5.2 AgentRunner 核心路径变更

```csharp
// AgentRunner.cs —— 统一后的核心路径

/// <summary>
/// 统一执行入口：接受 AgentTaskItem 作为唯一状态载体。
/// 无 taskKey 时由 AgentOrchestrator 合成瞬态 item。
/// </summary>
private async Task<AxiomLoopResult> RunInternalAsync(
    AgentTaskItem item, TimeSpan timeout, CancellationToken ct)
{
    // ★ 不再需要 RestoreSchedulerStateAsync / SaveSchedulerStateAsync
    // 状态即 item：加载 = GetAsync(taskKey)，保存 = UpdatePayloadAsync

    // ★ 设置状态为 Running，写入快照
    item.Status = TaskExecutionStatus.Running;
    await _persistenceStore.UpdatePayloadAsync(item.TaskKey, item, ct);

    // ★ 透传 item 引用供暂停门读取（替代原 LlmGate delegate）
    AgentRunCancellation.SetTaskItem(scope, item);

    // 发布启动快照
    _runtimeState?.Publish(item.ToRuntimeSnapshot("started"));

    var decisionPoint = new DecisionPoint(_mapper);
    var director = CreateDirector(item.SchedulingMode, item.SchedulerRulesKey);
    var scheduler = new HybridSchedulerStateMachine(director, new TransitionGuard(), item);

    var engine = new SnapshotDrivenLoopEngine<AgentTaskItem, RoundOutcome>();
    var snapshotResult = await engine.RunAsync(
        initialState: item,                                     // ★ item 即 TState
        execute: (state, token) => ExecuteHybridRoundAsync(decisionPoint, state, token),
        next: (state, outcome, token) => NextHybridRoundAsync(scheduler, state, outcome, token),
        ct: cts.Token,
        budgetTerminate: state => ...);

    // 发布完成快照
    _runtimeState?.Publish(item.ToRuntimeSnapshot("completed"));

    // ★ 持久化：通过 TaskPool 序列化 item（PayloadJson 自动包含运行时状态）
    await _persistenceStore.UpdatePayloadAsync(item.TaskKey, item, ct);

    return loopResult;
}

/// <summary>
/// 从 AgentTaskItem 派生 AxiomContext 租约（共享核心对象引用，零拷贝）。
/// </summary>
private static AxiomContext CreateLeaseContext(AgentTaskItem item)
{
    return new AxiomContext(
        data: item._data,
        budget: item.Budget,
        token: item.Token,
        flag: item.Flag)
    {
        Round = item.Round,
        UserInput = item.UserInput,
        LastResultText = item.LastResultText,
        LastEval = item.LastEval,
        Scope = item.Scope,
    };
}

/// <summary>
/// 确定性核心收割（从租约回写 item）。
/// 注：Budget/Token/Flag 因共享引用已被轮内直写，无需回写。
/// </summary>
private static void HarvestDeterministicCore(AgentTaskItem item, AxiomContext ctx)
{
    item.Round = ctx.Round;
    item.LastEval = ctx.LastEval;
    item.Scope = ctx.Scope;
    item.ContinuousFailures = ContinuousFailureRule.Next(item.ContinuousFailures, ctx.LastEval);
}
```

### 5.3 HybridSchedulerStateMachine 适配

`HybridSchedulerStateMachine` 当前接受 `SchedulerState` 作为状态载体。统一后改为接受 `ILoopState`（或直接 `AgentTaskItem`）：

```csharp
// 选择：HybridSchedulerStateMachine 改为接受 ILoopState
public sealed class HybridSchedulerStateMachine : ISchedulerStateMachine
{
    private readonly ILoopState _state;  // ← 原 SchedulerState，现改为 ILoopState
    // ...

    public HybridSchedulerStateMachine(
        IStateDirector director,
        ITransitionGuard guard,
        ILoopState state,           // ← 改为 ILoopState，AgentTaskItem/SchedulerState 均可
        double minConfidence = 0.0)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        // ...
    }
}
```

**影响面**：`HybridSchedulerStateMachine` 仅使用 `ILoopState` 的 `Budget`/`Token`/`Flag`/`Round`/`Data` 成员 + `AgentStatus`/`CurrentStrategyId`/`AvailableStrategyIds`/`RecentTransitions`。这些成员在 `AgentTaskItem` 和 `SchedulerState` 上签名一致，改动极小。

### 5.4 持久化扩展

在 `ITaskPoolPersistence<TItem>` 上新增 `UpdatePayloadAsync` 方法（仅更新 PayloadJson，不改变 Status）：

```csharp
// ITaskPoolPersistence<TItem> 扩展
public interface ITaskPoolPersistence<TItem> where TItem : ITaskPoolItem
{
    // ... 现有方法不变 ...

    /// <summary>
    /// 仅更新 PayloadJson（运行时状态变化），不改变 Status。
    /// 供 AgentRunner 在每轮边界持久化运行时快照。
    /// </summary>
    Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default);
}
```

**默认实现**：`SqliteTaskPoolStore` 新增 `UpdatePayloadAsync`：

```csharp
public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
{
    var exec = SqliteAmbientTransaction.Resolve(_executor);
    var json = JsonSerializer.Serialize(item, _jsonOptions);
    exec.ExecuteNonQuery(
        $"UPDATE [{_table}] SET [PayloadJson]=@j, [UpdatedAt]=@c WHERE [TaskKey]=@k",
        PersistenceHelper.CreateParameters(
            ("j", json),
            ("c", PersistenceHelper.NowIso()),
            ("k", taskKey)));
    return Task.CompletedTask;
}
```

**调用时机**：在 `AgentRunner.NextHybridRoundAsync` 的每轮边界调用 `UpdatePayloadAsync`，保证运行时状态（Round/Token/Budget/Strategy 等）落盘。

---

## 6. 统一执行流

### 6.1 完整生命周期

```
[1] 创建任务
    AgentTaskPoolAdapter.EnqueueAsync(item)
        │ item.TaskKey = Guid.NewGuid().ToString("N")
        │ item.UserInput = "帮我分析代码"
        │ item.Budget/Token/Flag 初始化为默认值
        ▼
    ur_tasks: INSERT (Status='Pending', PayloadJson=完整序列化)

[2] 消费任务（任务池路径）
    TaskPool 消费循环 → AgentTaskPoolAdapter.handler(item)
        │ TaskPool 自动将 Status 置为 'Running'
        ▼
    _executor.ExecuteAsync(item, executionId, ct) → AgentRunner.RunInternalAsync(item, ...)
        │
        │ ★ item 即 TState，零拷贝传递给 SnapshotDrivenLoopEngine
        │
        │ 每轮：
        │   ExecuteHybridRoundAsync(item, ct)
        │     → CreateLeaseContext(item)  // 共享 Budget/Token/Flag 引用
        │     → 五步编排管线（guard_input→provide_prompt→cline_execute→rule_match→guard_output）
        │     → RoundLifecycle 直写 item（通过共享引用）
        │
        │   NextHybridRoundAsync(scheduler, item, outcome, ct)
        │     → HarvestDeterministicCore(item, lease)
        │     → HybridSchedulerStateMachine.AdvanceAsync(item)
        │     → RuntimeStateBroker.Publish(item.ToRuntimeSnapshot("running"))
        │     → _persistenceStore.UpdatePayloadAsync(item.TaskKey, item)
        │

[3] 直跑路径（ReactiveTrigger）
    ReactiveTrigger.ProcessAsync(input)
        │ _run(userInput, threadId, inputKey, ct)
        ▼
    AgentOrchestrator.RunAsync(userInput, threadId, inputKey)
        │ EnsureTaskItemAsync(userInput, ...)
        │   → 合成 AgentTaskItem（TaskKey=Guid，Status=Pending）
        │   → ur_tasks: INSERT (Status='Pending', PayloadJson=完整序列化)
        ▼
    AgentRunner.RunInternalAsync(item, ...)  // 与任务池路径完全相同
        // ...同上...

[4] 暂停/恢复（快照驱动）
    AgentTaskPoolAdapter.PauseAsync(taskKey)
        │ item = _store.GetAsync(taskKey)
        │ item.Status = Paused                           // ★ 直接修改快照
        │ _store.UpdatePayloadAsync(taskKey, item)        // ★ Status 列 + PayloadJson 同步
        │ _runtimeState.Publish(item.ToRuntimeSnapshot("paused"))
        ▼
    AgentLoopFunction.CheckLlmGateAsync(item, ct)
        │ while (item.Status == Paused) await Task.Delay(200, ct)
        │ // ★ 从快照读取，不依赖 TaskPool 外部状态机

    AgentTaskPoolAdapter.ResumeAsync(taskKey)
        │ item = _store.GetAsync(taskKey)
        │ item.Status = Running                           // ★ 直接修改快照
        │ _store.UpdatePayloadAsync(taskKey, item)
        │ _runtimeState.Publish(item.ToRuntimeSnapshot("resumed"))
        ▼
    暂停门检测到 item.Status == Running → 继续执行

[5] 完成
    AgentTaskPoolAdapter.handler 设置 item.Status = Completed/Faulted/Cancelled
    _store.UpdatePayloadAsync(taskKey, item)             // ★ 终态写入快照
    AgentTaskPoolAdapter.handler 写回 ur_outputs
    ▼
    查询：ur_tasks SELECT * → PayloadJson 反序列化 → 完整快照
    查询：ur_outputs SELECT * → 任务结果
```

### 6.2 暂停门机制（快照驱动）

**决策**：暂停状态写入快照——`AgentTaskItem.Status` 是暂停/恢复的唯一权威来源。

**原机制问题**：当前暂停由 `TaskPool` 状态机独立管理（`TaskExecutionStatus` 列），通过 `AgentRunCancellation.SetLlmGate` + Functor（AsyncLocal）透传到 LLM 调用点。这导致：
- 暂停状态存在于 `TaskPool` 外部状态机，不在快照中
- 重启后需要从 `ur_tasks.Status` 列恢复，与 `PayloadJson` 可能不一致
- 违反"修改任务 = 修改快照"原则

**新机制**：暂停/恢复直接修改 `AgentTaskItem.Status`，通过 `UpdatePayloadAsync` 持久化到快照。暂停门从快照读取状态：

```
[暂停流程]
  PauseAsync(taskKey)
    │ 1. 从 ur_tasks 加载 item（GetAsync）
    │ 2. item.Status = Paused
    │ 3. UpdatePayloadAsync(taskKey, item) → 同时更新 Status 列 + PayloadJson
    │ 4. Publish(item.ToRuntimeSnapshot("paused"))
    ▼
  执行循环中的 LLM 调用点
    │ AgentLoopFunction.CheckLlmGateAsync(item, ct)
    │   while (item.Status == Paused) await Task.Delay(200, ct)
    ▼
  恢复后继续执行

[恢复流程]
  ResumeAsync(taskKey)
    │ 1. 从 ur_tasks 加载 item
    │ 2. item.Status = Running
    │ 3. UpdatePayloadAsync(taskKey, item) → 同时更新 Status 列 + PayloadJson
    │ 4. Publish(item.ToRuntimeSnapshot("resumed"))
    ▼
  暂停门检测到 item.Status == Running → 退出循环继续执行
```

**Functor 透传简化**：原 `AgentRunCancellation.SetLlmGate`/`GetLlmGate` 机制可保留用于跨方法透传 `item` 引用，但门检查逻辑改为直接读取 `item.Status`：

```csharp
// AgentRunCancellation —— 透传 item 引用（替代原 gate delegate）
public static void SetTaskItem(IFunctor scope, AgentTaskItem item)
    => scope.Set(new RunTaskItem(item));

public static AgentTaskItem? GetTaskItem()
    => Functor.Current?.Get<RunTaskItem>()?.Item;

// AgentLoopFunction.CheckLlmGateAsync —— 从快照读取
public static async Task CheckLlmGateAsync(CancellationToken ct = default)
{
    var item = AgentRunCancellation.GetTaskItem();
    if (item is null) return;  // 非任务池驱动 = no-op

    while (item.Status == TaskExecutionStatus.Paused)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
    }

    if (item.Status == TaskExecutionStatus.Cancelled)
        throw new OperationCanceledException();
}
```

**消除的冗余**：
- `TaskPool.PauseAsync`/`ResumeAsync` 不再操作独立状态机，改为委托 `AgentTaskItem.Status`
- `AgentRunCancellation.SetLlmGate`/`RunLlmGate` delegate 机制简化为 `RunTaskItem` 引用透传
- `TaskStateMachine.TryPause`/`TryResume` 不再需要——状态转移由 `AgentTaskItem.Status` 直接赋值

---

## 7. 迁移与兼容策略

### 7.1 迁移步骤

| 阶段 | 内容 | 风险 |
|---|---|---|
| **P1** | `AgentTaskItem` 添加运行时属性 + 实现 `ILoopState`（带默认值） | 低 |
| **P2** | `HybridSchedulerStateMachine` 改为接受 `ILoopState` 替代 `SchedulerState` | 中 |
| **P3** | `AgentRunner` 改为 `RunInternalAsync(AgentTaskItem)`，`CreateLeaseContext` 从 item 派生 | 高 |
| **P4** | `AgentTaskPoolAdapter.handler` 改为直接传递 item，移除 `SchedulerState` 创建 | 高 |
| **P5** | `AgentOrchestrator` 添加 `EnsureTaskItemAsync` 合成瞬态 item | 中 |
| **P6** | `ITaskPoolPersistence` 添加 `UpdatePayloadAsync`，每轮边界持久化 | 低 |
| **P7** | 删除 `SchedulerState`、`SchedulerStateSnapshotSerializer` 及相关文件 | 中 |
| **P8** | 数据迁移：旧 `SchedulerState` 快照合并进 `ur_tasks`（PayloadJson 扩展序列化） | 中 |

### 7.2 向后兼容

| 场景 | 处理方式 |
|---|---|
| 直跑无 taskKey | `EnsureTaskItemAsync` 合成瞬态 `AgentTaskItem` |
| 旧 `SchedulerState` 数据 | `SchedulerStateSnapshotSerializer.Deserialize` 保留读取路径，迁移后删除 |
| 旧 API 调用 `AgentRunner.RunAsync(userInput)` | 自动走 `EnsureTaskItemAsync` 合成路径 |
| 任务池状态查询 | 继续用 `Status` 列，运行时查询用 `PayloadJson` 反序列化 |

### 7.3 验证标准

| 验证项 | 标准 |
|---|---|
| `dotnet test` | 全绿（现有测试 + 新增 `AgentTaskItem` 作为 `ILoopState` 的单元测试） |
| 功能等价 | 统一后功能与原实现 100% 一致 |
| 数据一致 | 同一任务的 `PayloadJson` 中的运行时字段值与原 `SchedulerState` 快照字段值完全相同 |
| 状态一致 | 暂停/恢复后 `Status` 列与 `PayloadJson` 中运行时状态一致 |
| 查询一致 | `GetAsync(taskKey)` 返回的 item 同时包含输入字段和运行时字段 |
| 直跑兼容 | `ReactiveTrigger` 路径通过 `EnsureTaskItemAsync` 合成 item 正常运行 |

---

## 8. 影响范围与风险评估

### 8.1 直接影响文件

| 模块 | 文件 | 变更类型 |
|---|---|---|
| **Agent.Core** | `AgentTaskItem.cs` | 扩展属性 + 实现 `ILoopState` |
| | `AgentTaskPoolAdapter.cs` | handler 简化 |
| | `AgentRunner.cs` | 核心路径重写（TState 改为 AgentTaskItem） |
| | `LlmTaskExecutor.cs` | 传递 AgentTaskItem 替代 userInput |
| | `RuleTaskExecutor.cs` | 传递 AgentTaskItem 替代 AxiomContext |
| **Agent.State** | `SchedulerState.cs` | 删除 |
| | `SchedulerStateSnapshotSerializer.cs` | 删除 |
| | `SchedulerStateProjector.cs` | 简化（委托 ToRuntimeSnapshot） |
| | `HybridSchedulerStateMachine.cs` | 改为接受 `ILoopState` |
| | `AgentRunCancellation.cs` | `SetLlmGate` → `SetTaskItem`；`RunLlmGate` → `RunTaskItem` |
| | `AgentLoopFunction.cs` | `CheckLlmGateAsync` 改为从 `item.Status` 读取 |
| **Agent.Axioms** | `WorkflowRunner.cs` | 适配新上下文类型 |
| | `RoundStateMapper.cs` | Seed/Harvest 适配 |
| | `RoundLifecycle.cs` | 无改动（已基于 ILoopState） |
| **Agent.Persistence** | `ITaskPoolPersistence.cs` | 新增 `UpdatePayloadAsync` |
| | `SqliteTaskPoolStore.cs` | 实现 `UpdatePayloadAsync` |
| **Agent.Contract** | `AgentRuntimeSnapshot.cs` | 无改动（纯投影 DTO） |

### 8.2 风险与缓解

| 风险 | 影响 | 缓解策略 |
|---|---|---|
| **`ILoopState` 实现兼容性** | 算法层（RoundLifecycle/BudgetEscalator/ProgressUpdater）是否与 AgentTaskItem 无缝兼容 | P2 阶段先验证 `ILoopState` 接口测试：`AgentTaskItem` 作为 `ILoopState` 运行 `RoundLifecycle` 的全流程测试 |
| **直跑路径行为变化** | ReactiveTrigger 合成 item 后持久化写入增加 | 直跑路径性能敏感场景可跳过 `UpdatePayloadAsync`（新增 `bool persistRuntime` 参数） |
| **暂停门行为变化** | 暂停逻辑从 TaskPool 状态机改为快照驱动（`item.Status`） | P4 阶段同时改造 `PauseAsync`/`ResumeAsync`/`CheckLlmGateAsync`；保留 `Functor` 透传 `item` 引用；验证暂停/恢复/取消的完整流程 |
| **PayloadJson 体积增长** | 运行时字段增加序列化体积 | 单任务 PayloadJson 预计 < 10KB，可接受。如超出预期再引入分表 |
| **历史数据迁移** | 旧 SchedulerState 快照需合并进 ur_tasks | 编写迁移脚本：`SELECT * FROM SchedulerState` → `UPDATE ur_tasks SET PayloadJson=@json WHERE TaskKey=@key` |

---

## 9. 一句话总结

> **任务 = 快照的初始态，快照 = 任务的演化态。** 修改任务（暂停、恢复、修改输入）就是修改快照的运行时属性，快照的每一次状态变化（轮次推进、策略切换、预算变更、暂停/恢复）都是任务本身的演化。二者合并后，系统中只有一个权威实体——`AgentTaskItem`（实现 `ILoopState` 接口，`Status` 字段承载完整生命周期状态），`ur_tasks` 表的 `PayloadJson` 既是任务注册表也是状态快照仓库，消除了身份脱节、双状态机、桥接层冗余和暂停状态外部化。算法层（`RoundLifecycle`/`BudgetEscalator`/`ProgressUpdater`）零改动，因为 `AgentTaskItem` 直接实现了 `ILoopState` 契约。暂停门从 `item.Status` 直接读取，不再依赖 `TaskPool` 外部状态机。