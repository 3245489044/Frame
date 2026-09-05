# 混合调度机制 + 非限定快照驱动循环（LLM + 计算）

> 日期：2026-08-10 | 范围：`Agent.Contract/Scheduling`、`Agent.State/Runtime`、`Agent.Axioms/Scheduling`、`WorkFlowMethod.LoopFramework/Axioms`
> 关联：`2026-08-10-snapshot-unified-data-standard.md`（统一快照 / 快照管理循环）、`2026-08-09-strategy-state-machine.md`（非限定策略机）、`LoopFramework_综合设计文档.md`（v1.2 预留 `HybridStateMachine`）
> 目标：把**状态变更**改为 **LLM + 计算混合机制**；把**整体循环驱动**改为**非限定状态机 + 快照数据**驱动，既保证策略完全实施、又保留循环的自由探索能力。

---

## 1. 目标与背景

基于统一状态快照文档，进一步状态重构的两个核心诉求：

1. **状态变更混合化**：调度模型每次迭代调用 LLM（自由探索源），根据快照 + 数据做计算（确定性校验），再变更整体状态——取代"纯计算推进状态、LLM 只在 switch_strategy 极窄路径出现"的现状。
2. **循环驱动非限定化**：整体循环由**非限定状态机 + 快照数据**驱动，取代"过程式五步 while + 引擎内置终止"——机器自身不限制死循环，继续/终止责任外置（`AX-CORE.EssentiallyUndecidable`）。

**分工原则**：LLM 提议 = 自由探索（可提出策略合法空间内的新方向）；计算层守卫 = 策略完全实施（防幻觉 / 离散状态合法边 / 不变量，越界拒绝）。二者以统一快照为决策与变更载体。

---

## 2. 新增组件

### 2.1 契约层 `Agent.Contract/Scheduling/`（纯端口，零实现依赖）

| 文件 | 职责 |
|---|---|
| `SchedulerAction.cs` | 调度动作域：`Continue / SwitchStrategy / AdvanceRun / PauseRun / Retry / Replan / Explore`（非限定，无终止语义） |
| `StateTransitionProposal.cs` | LLM 提议的整体状态转移（Action + 目标策略/执行器/状态 + Intent + Reason + Confidence），Id 均字符串承载（契约纯化） |
| `TransitionVerdict.cs` | 校验裁决：`Pass / Held / Reject` |
| `SnapshotTelemetry.cs` | 快照级遥测（Round/Budget/Token/Halt/连续失败/评估/错误） |
| `SchedulerDirectorContext.cs` | 导演输入：整体状态只读投影（当前策略/可用集/遥测/执行状态/近期转移） |
| `SchedulerDecision.cs` | 一次混合调度决策产出（Applied + Action + Verdict + 目标 + 推理） |
| `IStateDirector.cs` | LLM 提议端口（`DecideAsync` → proposal? / null=stay） |
| `ITransitionGuard.cs` | 确定性校验端口（策略完全实施单一落地点） |
| `ISchedulerStateMachine.cs` | 混合调度状态机门面（非限定，无终止逻辑） |

### 2.2 状态层 `Agent.State/Runtime/`

| 文件 | 职责 |
|---|---|
| `SchedulerState.cs` | **统一整体状态快照**：根循环状态（Budget/Token/Halt/Round）+ 运行中执行状态（executor→status）+ 策略视图 + 近期转移 + 数据 |
| `HybridSchedulerStateMachine.cs` | **混合调度状态机**：LLM 提议 → 计算守卫校验 → 原子施加（Held/Changed/Reject）到 `SchedulerState`；导演失败/非法 → fail-safe stay/回退 |

### 2.3 公理层 `Agent.Axioms/Scheduling/`

| 文件 | 职责 |
|---|---|
| `TransitionGuard.cs` | 确定性守卫：策略防幻觉（目标∈可用集）、离散状态合法边（复用 `AgentRunStateMachine`）、探索放行 |
| `LlmStateDirector.cs` | LLM 导演默认实现：编码整体状态进提示 → `GenerateJsonAsync` → 解析 JSON 提议（宽容 action 名解析），fail-safe null |

### 2.4 循环框架 `WorkFlowMethod.LoopFramework/Axioms/`

| 文件 | 职责 |
|---|---|
| `SnapshotDrivenLoopEngine.cs` | **非限定快照驱动循环引擎**：每轮 `execute`（单轮执行体）→ `next`（混合机制变更整体状态）→ 更新快照；**无内部 max-iterations/halt**，仅响应外部取消令牌 / `externalTerminate` 谓词 |

---

## 3. 混合机制流转（核心）

```
整体循环每轮：
  SnapshotDrivenLoopEngine.RunAsync(snapshot, execute, next, ct, externalTerminate)
    ↓ next（混合机制核心 = HybridSchedulerStateMachine.AdvanceAsync）
   ① IStateDirector（LlmStateDirector）编码整体状态 → LLM 提议 StateTransitionProposal   // 自由探索
   ② ITransitionGuard（TransitionGuard）确定性校验                                      // 策略完全实施
   ③ 原子施加到 SchedulerState（Held/Changed/Reject；离散状态合法边由 AgentRunStateMachine 防御）
   ④ 返回 SchedulerDecision（可观测，宿主记录）
```

- **自由探索**：LLM 可提出 `Replan / Explore / SwitchStrategy / AdvanceRun` 等新方向（在策略合法空间内）。
- **策略完全实施**：守卫拒绝越出可用策略集的目标、拒绝非法离散状态边；导演失败/越界 → fail-safe 回退确定性默认动作（stay/continue）。
- **非限定**：状态机与引擎自身不裁决 continue/终止；终止责任外置（宿主 watchdog / 取消令牌），对齐 `AX-CORE.EssentiallyUndecidable`。

---

## 4. 验证

```
dotnet build Agent\Agent.sln -c Debug        → 0 警告 0 错误
CentralTest\AgentTest\HybridSchedulerCheck\   → 14/14 PASS（exit 0）
   · TransitionGuard：拒绝未知策略 / 放行合法切换 / 非法离散边防御
   · HybridSchedulerStateMachine：合法切换应用 / 非法回退 / 导演 null stay / Explore 放行
   · LlmStateDirector：JSON 提议解析 / 坏 JSON 回退 / 越界策略剔除
   · SnapshotDrivenLoopEngine：非限定（无内部 max-iterations，外部终止停）；取消令牌停
```

> 注：`CentralTest/AgentTest/Agent.Core.Tests` 存在**既有**编译问题（`DynamicWorkflowMerger` 缺失、`Agent.Planner.csproj` 不存在，与本次改动无关），故以 `HybridSchedulerCheck` 控制台作为可运行的验证载体；MSTest 用例 `HybridSchedulerTests.cs` / `SnapshotLoopEngineTests.cs` 已就位（随该测试套件修复后即可纳入 `dotnet test`）。

---

## 5. 与既有非限定策略状态机的关系

- `AiDrivenStrategyStateMachine`（`IStrategyStateMachine`）仍是"策略切换"的既有实现，随 `DecisionPoint` 每轮推进。
- 本次新增的 `HybridSchedulerStateMachine` 是**整体状态**级别的混合机制（策略只是整体状态的一个 facet），二者并存：策略机管"切到哪个策略"，混合机管"整体状态怎么变"。
- 统一快照 `SchedulerState` 与既有 `AgentRuntimeSnapshot`/`AxiomContext` 的桥接（投影/合并）由 AgentRunner 混合循环路径承载（见 §6）。

---

## 6. AgentRunner 可选接线（已实施，默认关闭）

`SnapshotDrivenLoopEngine` + `HybridSchedulerStateMachine` 已接入 `AgentRunner`，作为**可选、默认关闭**的循环路径（不改动默认公理引擎过程式五步循环，向后兼容）：

| 位置 | 变更 |
|---|---|
| `AgentRunnerOptions` | 新增 `bool EnableHybridSnapshotLoop = false` |
| `AgentRunner` | `RunInternalAsync` 分支：开关开启 → `RunHybridInternalAsync`；`next` 内确定性计算（评估/进度/熔断/决策词，对齐 `RoundLifecycle` 语义）+ 混合调度（LLM 提议 + 计算守卫）变更整体状态；`execute` 复用五步骨架 `ExecuteRoundAsync`；终止责任外置（`externalTerminate` = 宿主级 `TerminationDecider` + 成功判定，仅取消令牌可停） |
| `AgentOrchestrator` | `EnableHybridSnapshotLoop()` / `DisableHybridSnapshotLoop()`（置位 + 重建运行器） |
| `AgentConfig` | `EnableHybridSnapshotLoop`（默认 false） |
| `AgentOptionalModules` | `config.EnableHybridSnapshotLoop` → `orchestrator.EnableHybridSnapshotLoop()` |

**混合循环每轮流转**：`execute`（五步骨架）→ `next`（①错误分类 → ②评估 → ③进度/熔断/halt → ④决策词 → ⑤同步整体快照 → ⑥混合调度变更整体状态 → ⑦回写策略 → ⑧登记执行）→ 更新快照 → 循环。

**默认关闭保证**：未启用时 `RunInternalAsync` 走既有 `AxiomLoopEngine`，行为不变；启用后即使 LLM 不可用，`LlmStateDirector` fail-safe 返回 null → 混合机 stay，循环仍确定性推进。

---

## 7. 验证

```
dotnet build Agent\Agent.sln -c Debug        → 0 警告 0 错误
CentralTest\AgentTest\HybridSchedulerCheck\   → 28/28 PASS（exit 0）
   · TransitionGuard：拒绝未知策略 / 放行合法切换 / 非法离散边防御
   · HybridSchedulerStateMachine：合法切换应用 / 非法回退 / 导演 null stay / Explore 放行
   · LlmStateDirector：JSON 提议解析 / 坏 JSON 回退 / 越界策略剔除
   · SnapshotDrivenLoopEngine：非限定（无内部 max-iterations，外部终止停）；取消令牌停
   · 端到端：非限定循环 + 混合状态机共同驱动整体状态变更（策略切换 + 3 轮）
```

> 注：`CentralTest/AgentTest/Agent.Core.Tests` 存在**既有**编译问题（`DynamicWorkflowMerger` 缺失、`Agent.Planner.csproj` 不存在，与本次改动无关），故以 `HybridSchedulerCheck` 控制台作为可运行的验证载体；MSTest 用例 `HybridSchedulerTests.cs` / `SnapshotLoopEngineTests.cs` 已就位（随该测试套件修复后即可纳入 `dotnet test`）。

---

## 8. 待办 / 后续

| 项 | 内容 | 状态 |
|---|---|---|
| 8.0 | **Stage A：`SchedulerState` 实现 `ILoopState`**（统一快照承载根状态 Budget/Token/Flag/Round/Scope，算法层零改动运行其上） | ✅ 已实施 |
| 8.1 | `AxiomContext` 逐步降级为每轮派生租约（`SchedulerState` 成为唯一根状态） | 中 |
| 8.2 | 混合循环默认开启前需端到端回归（LLM 可用时验证真实策略切换 / 熔断/终止语义） | 高（行为级） |
| 8.3 | 数据契约注册 `scheduler_intent` / `state_transition` 等键 + `Check-DataContracts.ps1` 同步 | 低 |
| 8.4 | 修复 `Agent.Core.Tests` 既有编译问题后，把 `HybridSchedulerTests` / `SnapshotLoopEngineTests` 纳入 `dotnet test` | 中 |
| 8.5 | README / 快照文档同步「混合调度 / 非限定快照驱动循环」章节 | 低 |

---

## 10. Stage A：统一快照实现 ILoopState（已实施）

`SchedulerState` 已实现 `ILoopState` —— 统一快照直接承载根循环状态，使公理引擎算法层可**零改动**直接运行在快照上：

- **新增接口成员**：`Budget`（FailureBudget）/ `Token`（ProgressToken）/ `Flag`（ControlFlag）三个状态对象 + `Round` / `UserInput` / `LastResultText` / `LastEval` / `Scope` / `Data`（`IDictionary` 显式实现）。
- **移除标量投影**：原 `BudgetLevel`/`TokenValue`/`TokenMax`/`Halt` 字段删除，统一改经对象读写（避免第二事实源）；消费方已同步迁移：
  - `HybridSchedulerStateMachine.BuildContext` → `_state.Budget.Level` 等；
  - `AgentRunner.SyncSchedulerState` → 对象推进（对齐 `SnapshotSerializer` 还原语义，无私有 setter 直写）+ 同步 UserInput/LastResultText/LastEval/Scope；
  - `SchedulerStateSnapshotSerializer` → Serialize 读对象、Apply 对象推进还原。
- **验证**：`HybridSchedulerCheck` 新增 `AlgorithmOnSnapshotTests`（`ProgressUpdater`/`BudgetEscalator`/`TerminationDecider` 以 `ILoopState` 直接接收 `SchedulerState` 运行 + `asInterface` 断言），随套件 **28/28 PASS（exit 0）**；`Agent.sln` 0 警告 0 错误。

> 这是向"快照 = 唯一根状态"演进的第一步（低风险，算法层零改动）；后续阶段见 §8.1（`AxiomContext` 降级为每轮派生租约）等。**28/28 PASS**。


---

## 9. 统一整体状态快照持久化（已实施）

`SchedulerState` 已接入快照序列化（`SnapshotSerializer` 的 SchedulerState 对偶）：

- **`Agent.State/Persistence/SchedulerStateSnapshotSerializer.cs`**（新增）：`SchedulerState ↔ SchedulerStateSnapshot` JSON 快照，与 `Agent.Persistence.Core.SnapshotSerializer`（AxiomContext）同构（`Data` JSON 安全归一化 + 类型还原）。
  - 归属说明：因 `Agent.State → Agent.Persistence` 单向依赖（禁止反向引用），无法把 `SchedulerState` 序列化放入 Agent.Persistence.Core，故承载于状态层（其持有方）。
- **`AgentRunner` 混合循环接线**：
  - 启动：`RestoreSchedulerStateAsync` 优先跨会话恢复 `SchedulerState`，否则 `BuildSchedulerState` 新建。
  - 结束：`SaveSchedulerStateAsync` 持久化 `SchedulerState`（与 AxiomContext 快照共用 thread/session，用独立键 `*.scheduler` 区分，避免覆盖）。
- 持久化端口复用既有 `IPersistenceStore.Snapshots`（SaveAsync/LoadAsync），无新增后端依赖。

**验证**：`HybridSchedulerCheck` 新增 `SchedulerStateSnapshotSerializer` round-trip（Round/Budget/Token/Halt/策略/可用集/执行状态/近期转移/Data 标量），随套件 **17/17 PASS（exit 0）**；`Agent.sln` 0 警告 0 错误。


