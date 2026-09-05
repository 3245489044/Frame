# 混合调度 + 非限定快照驱动循环 —— 当前实现状态

> 生成日期：2026-08-10
> 范围：把 Agent 主循环的状态变更改为 **LLM + 计算混合机制**、把整体循环驱动改为**非限定状态机 + 快照数据驱动**，并把统一状态快照接入持久化、实现 `ILoopState` 的当前全部实现状态。
> 关联：`2026-08-10-snapshot-unified-data-standard.md`（统一快照 / 快照管理循环）、`docs/refactor/2026-08-10-hybrid-scheduler.md`（设计+演进记录）、`2026-08-09-strategy-state-machine.md`（非限定策略机）、`LoopFramework_综合设计文档.md`（v1.2 预留 `HybridStateMachine`）。
> 验证：`dotnet build Agent.sln -c Debug` → 0 警告 0 错误；`HybridSchedulerCheck` → 28/28 PASS。

---

## 1. 目标与背景

1. **状态变更混合化**：调度模型每次迭代调用 LLM（自由探索源），根据快照 + 数据做计算（确定性校验），再变更整体状态——取代"纯计算推进状态、LLM 只在 switch_strategy 极窄路径出现"的现状。
2. **循环驱动非限定化**：整体循环由**非限定状态机 + 快照数据**驱动，取代"过程式五步 while + 引擎内置终止"——机器/引擎自身不限制死循环，继续/终止责任外置（`AX-CORE.EssentiallyUndecidable`）。
3. **快照单一事实源**：统一快照承载根状态，逐步取代 `AxiomContext` 作为平行根状态的"多事实源"格局。

**分工原则**：LLM 提议 = 自由探索；计算层守卫 = 策略完全实施。二者以统一快照为决策与变更载体。

---

## 2. 架构总览

```
┌────────────────────────────────────────────────────────────────────────┐
│ SnapshotDrivenLoopEngine（非限定状态机 + 快照驱动，无内部终止）                │
│  while (外部未终止):                                                     │
│    1. execute：五步骨架（Execute→Collect→Evaluate→Decide→Adjust 执行体）     │
│    2. next（混合机制核心）:                                               │
│       ①确定性计算（评估/进度/熔断/halt/决策词）                            │
│       ②同步统一快照 SchedulerState（根状态 + 执行状态 + 策略视图）            │
│       ③HybridSchedulerStateMachine：LLM 提议 + 计算守卫 → 原子变更整体状态    │
│       ④回写策略 / 登记执行                                               │
│    3. 终止 = 外部责任：取消令牌 / externalTerminate（宿主级）                │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 3. 组件清单

### 3.1 契约层 `Agent/src/Agent.Contract/Scheduling/`（纯端口，零实现依赖）

| 文件 | 职责 |
|---|---|
| `SchedulerAction.cs` | 动作域：`Continue/SwitchStrategy/AdvanceRun/PauseRun/Retry/Replan/Explore`（非限定，无终止语义） |
| `StateTransitionProposal.cs` | LLM 提议的整体状态转移（Action + 目标策略/执行器/状态 + Intent + Reason + Confidence；Id 字符串承载，契约纯化） |
| `TransitionVerdict.cs` | 校验裁决：`Pass / Held / Reject` |
| `SnapshotTelemetry.cs` | 快照级遥测（Round/Budget/Token/Halt/连续失败/评估/错误） |
| `SchedulerDirectorContext.cs` | 导演输入：整体状态只读投影 |
| `SchedulerDecision.cs` | 一次混合调度决策产出（Applied + Action + Verdict + 目标 + 推理） |
| `IStateDirector.cs` | LLM 提议端口（`DecideAsync` → proposal? / null=stay） |
| `ITransitionGuard.cs` | 确定性校验端口（策略完全实施单一落地点） |
| `ISchedulerStateMachine.cs` | 混合调度状态机门面（非限定，无终止逻辑） |

### 3.2 状态层 `Agent/src/Agent.State/Runtime/`

| 文件 | 职责 |
|---|---|
| `SchedulerState.cs` | **统一整体状态快照，实现 `ILoopState`**：根状态（Budget/Token/Flag/Round/UserInput/LastResultText/LastEval/Scope/Data）+ 执行状态（executor→status）+ 策略视图 + 历史 + 数据 |
| `HybridSchedulerStateMachine.cs` | 混合调度状态机：LLM 提议 → 计算守卫校验 → 原子施加（Held/Changed/Reject）到 `SchedulerState`；导演失败/非法 → fail-safe stay/回退 |
| `AgentRuntimeSnapshot.cs` / `AgentRuntimeManager.cs` | 既有统一快照管理器（Agents + 策略视图 + Round），与本快照并存，混合循环经 `PlanRound/RecordExecution` 消费 |

### 3.3 公理层 `Agent/src/Agent.Axioms/Scheduling/`

| 文件 | 职责 |
|---|---|
| `TransitionGuard.cs` | 确定性守卫：策略防幻觉（目标∈可用集）、离散状态合法边（复用 `AgentRunStateMachine`）、探索放行 |
| `LlmStateDirector.cs` | LLM 导演默认实现：编码整体状态进提示 → `GenerateJsonAsync` → 解析 JSON 提议（宽容 action 名解析 + snake_case），fail-safe null |

### 3.4 持久化 `Agent/src/Agent.State/Persistence/`

| 文件 | 职责 |
|---|---|
| `SchedulerStateSnapshotSerializer.cs` | **`SchedulerState ↔ SchedulerStateSnapshot` JSON 快照**（`SnapshotSerializer` 的 SchedulerState 对偶，`Data` JSON 安全归一化 + 类型还原） |

> 归属说明：因 `Agent.State → Agent.Persistence` 单向依赖（禁止反向引用），无法把 `SchedulerState` 序列化放入 Agent.Persistence.Core，故承载于状态层。

### 3.5 循环框架 `CsharpPrj/Operation/WorkFlowMethod/LoopFramework/Axioms/`

| 文件 | 职责 |
|---|---|
| `SnapshotDrivenLoopEngine.cs` | **非限定快照驱动循环引擎**：每轮 `execute` → `next`（混合机制变更整体状态）→ 更新快照；无内部 max-iterations/halt，仅响应取消令牌 / `externalTerminate` 谓词 |

### 3.6 接线（修改）

| 文件 | 变更 |
|---|---|
| `Agent.Core/Loops/AgentRunner.cs` | `RunInternalAsync` 分支：开关开启 → `RunHybridInternalAsync`（`execute` 复用五步骨架、`next` 确定性计算+混合调度、终止外置）；`SyncSchedulerState` 对象推进同步快照；`Restore/SaveSchedulerStateAsync` 持久化 |
| `Agent.Core/Loops/AgentRunnerOptions.cs` | 新增 `bool EnableHybridSnapshotLoop = false` |
| `Agent.Core/Orchestration/AgentOrchestrator.cs` | `EnableHybridSnapshotLoop()` / `DisableHybridSnapshotLoop()`（置位 + 重建运行器 + 透传到 Runner） |
| `Agent.State/Orchestration/AgentOrchestratorState.cs` | 新增 `EnableHybridSnapshotLoop` 状态位 |
| `Agent.Launcher/AgentConfig.cs` | 新增 `EnableHybridSnapshotLoop`（默认 false） |
| `Agent.Launcher/AgentOptionalModules.cs` | 按 `config.EnableHybridSnapshotLoop` 调用 `orchestrator.EnableHybridSnapshotLoop()` |

### 3.7 测试

| 文件 | 说明 |
|---|---|
| `CentralTest/AgentTest/HybridSchedulerCheck/`（csproj + Program.cs） | **可运行验证载体**（Agent.Core.Tests 既有编译问题，见 §6），28 项断言，exit 0 |
| `CentralTest/AgentTest/Agent.Core.Tests/HybridSchedulerTests.cs` | MSTest 用例（守卫/混合机/导演），待测试套件修复后纳入 `dotnet test` |
| `CentralTest/AgentTest/Agent.Core.Tests/SnapshotLoopEngineTests.cs` | MSTest 用例（非限定循环） |

---

## 4. 关键机制流程

### 4.1 混合状态变更（HybridSchedulerStateMachine.AdvanceAsync）
```
整体循环每轮（next）：
  ① IStateDirector（LlmStateDirector）编码整体状态 → LLM 提议 StateTransitionProposal   // 自由探索
  ② ITransitionGuard（TransitionGuard）确定性校验                                       // 策略完全实施
  ③ 原子施加到 SchedulerState（Held/Changed/Reject；离散状态合法边由 AgentRunStateMachine 防御）
  ④ 返回 SchedulerDecision（可观测，宿主记录）
```
- **自由探索**：LLM 可提出 `Replan / Explore / SwitchStrategy / AdvanceRun` 等新方向（在策略合法空间内）。
- **策略完全实施**：守卫拒绝越出可用策略集的目标、拒绝非法离散状态边；导演失败/越界 → fail-safe 回退确定性默认动作（stay/continue）。
- **非限定 + 终止二分**：开放探索的【探索终止】责任外置（宿主 watchdog / 取消令牌，AX-CORE.EssentiallyUndecidable）；可判定的【预算终止】（token 耗尽→TO / halt→TM / 成功→TS）由引擎内 `budgetTerminate` 裁决、阈值到达即触发（AX.TerminationBipartition / AX.TerminationPriority），不再外置、不再决策词双算。

### 4.2 混合循环单轮（AgentRunner.NextHybridRoundAsync）
①错误分类 → ②评估（复用 `DecisionPoint.EvaluateRound`）→ ③进度/熔断/halt（`ProgressUpdater`/`BudgetEscalator` 单一入口）→ ④决策词 → ⑤同步整体快照 → ⑥混合调度变更整体状态 → ⑦回写策略（`ctx.Data[ActiveStrategy]`）→ ⑧登记执行（`AgentRuntimeManager.RecordExecution`）。

### 4.3 统一快照实现 ILoopState（Stage A）
`SchedulerState` 直接承载 `FailureBudget`/`ProgressToken`/`ControlFlag` 三个状态对象 + `Round`/`UserInput`/`LastResultText`/`LastEval`/`Scope`/`Data`，使算法层（`ProgressUpdater`/`BudgetEscalator`/`TerminationDecider`/`RoundLifecycle`）可**零改动**以 `ILoopState` 直接运行在快照上。已移除原 `BudgetLevel`/`TokenValue`/`TokenMax`/`Halt` 标量投影（避免第二事实源），消费方（`BuildContext`/`SyncSchedulerState`/序列化）统一经对象读写。

---

## 5. 配置

| 配置 | 位置 | 默认 | 说明 |
|---|---|---|---|
| `EnableHybridSnapshotLoop` | `AgentConfig` | `false` | 开启混合调度循环（默认走既有公理引擎过程式五步循环，向后兼容） |
| 运行时开关 | `AgentOrchestrator.Enable/DisableHybridSnapshotLoop()` | 关闭 | 组合根按配置调用 |
| 持久化键 | `*.scheduler`（session 优先于 thread） | — | `SchedulerState` 快照独立键，与 AxiomContext 快照区分 |

---

## 6. 验证状态

```
dotnet build Agent\Agent.sln -c Debug        → 0 警告 0 错误
CentralTest\AgentTest\HybridSchedulerCheck\   → 37/37 PASS（exit 0）
   · TransitionGuard：拒绝未知策略 / 放行合法切换 / 非法离散边防御
   · HybridSchedulerStateMachine：合法切换应用 / 非法回退 / 导演 null stay / Explore 放行
   · LlmStateDirector：JSON 提议解析 / 坏 JSON 回退 / 越界策略剔除
   · SnapshotDrivenLoopEngine：非限定（无内部 max-iterations，外部终止停）；取消令牌停
   · 预算终止（AX.TerminationBipartition）：token 耗尽→TO / halt→TM 由引擎内 budgetTerminate 裁决，初始终态即停 / 执行后阈值到达即触发（无需 externalTerminate）
   · 阶段3不变式（AX.StateRootSingleSource / DeterministicCoreDisjoint / InformationMonotonicity）：确定性核心守卫 / 历史只增归档(transitionHistory) / 仲裁键(GlobalDecisionKey)
   · 单根（AX.StateRootSingleSource）：混合循环以 SchedulerState 为唯一持久根，AxiomContext 为派生租约播种
   · 端到端：非限定循环 + 混合状态机共同驱动整体状态变更（策略切换 + 3 轮）
   · ILoopState 算法层：ProgressUpdater/BudgetEscalator/TerminationDecider 直接运行在 SchedulerState 上
   · SchedulerStateSnapshotSerializer round-trip：Round/Budget/Token/Halt/策略/可用集/执行状态/历史/Data
dotnet test Agent.Core.Tests                → 63/63 PASS（HybridSchedulerTests / SnapshotLoopEngineTests / AgentRunnerRouteTests / DynamicWorkflowMerger·AgentLoopFunction 单元测试）
```

> **说明**：`CentralTest/AgentTest/Agent.Core.Tests` 已纳入 `dotnet test`（63/63）。其中 **`DynamicWorkflowMerger`（工作流片段解析/合并/执行）+ `AgentLoopFunction`（任务完成/片段识别/上下文压缩）已实现**并恢复编译（`DynamicWorkflowCoreTests` 已纳入）。仍排除的两个文件：`EndToEndIntegrationTests`（依赖 `UnifiedRegistryCenter` 框架初始化，裸 MSTest 宿主无法运行）；`LlmIntegrationTests`（引用已移除的 `SKPlannerFunction`/已删除的 `Agent.Planner`，且依赖真实 DEEPSEEK_API_KEY）。

---

## 7. Git 状态

- 主体已提交：`7d37426b8 LLM + 计算混合机制，循环改为非限定状态机+快照数据驱动`（HEAD）。
- 当前未提交改动（Stage A `ILoopState` + 持久化 + 文档）：
  - 修改：`SchedulerState.cs`、`HybridSchedulerStateMachine.cs`、`AgentRunner.cs`、`AgentRunnerOptions.cs`、`AgentOrchestrator.cs`、`AgentConfig.cs`、`AgentOptionalModules.cs`、`AgentOrchestratorState.cs`、`HybridSchedulerCheck/Program.cs`、`docs/refactor/2026-08-10-hybrid-scheduler.md`
  - 新增：`Agent.State/Persistence/SchedulerStateSnapshotSerializer.cs`

---

## 8. 待办 / 后续路线

| # | 内容 | 风险 |
|---|---|---|
| 8.1 | ✅ **单根收敛（完成）**：混合循环以 `SchedulerState` 为**循环 TState + 唯一持久根**（AX.StateRootSingleSource），`AxiomContext` 降级为**每轮派生执行租约**（共享 `SchedulerState.Data`，单一数据持有方）；移除 `SyncSchedulerState` 双向桥接，确定性核心由 `CreateLeaseContext`（播种）/`HarvestDeterministicCore`（收割）在轮边界单向流转，`DecisionPoint` 绑定每轮租约（ILoopState 视图） | ~~高~~ ✅ 完成 |
| 8.2 | 混合循环默认开启前需端到端回归（LLM 可用时验证真实策略切换 / 熔断/终止语义） | 高（行为级） |
| 8.3 | 数据契约注册 `scheduler_intent` / `state_transition` 等键 + `Check-DataContracts.ps1` 同步 | 低 |
| 8.4 | ✅ `Agent.Core.Tests` 已纳入 `dotnet test`（63/63）；`DynamicWorkflowMerger` + `AgentLoopFunction` 已实现并恢复编译；仍排除 `EndToEndIntegrationTests`（框架初始化依赖）/ `LlmIntegrationTests`（死代码） | 中 |
| 8.5 | README / `2026-08-10-snapshot-unified-data-standard.md` 同步「混合调度 / 非限定快照驱动循环」章节 | 低 |

