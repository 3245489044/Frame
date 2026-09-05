# 统一数据传递标准 + 统一状态快照（快照管理循环）—— 当前实现状态

> 生成日期：2026-08-10
> 范围：Agent 项目在「统一数据传递标准」「统一状态快照 / 快照管理器」「离散状态消费管理」「方向 C 执行器选择」四方面的当前实现状态。
> 验证：`dotnet build Agent.sln` 0 警告 0 错误；`PowerShellScript/DSL/Check-DataContracts.ps1 -Check` 29/29 exit=0。

---

## 1. 目标与背景

基于对 `Agent/README.md` 调用路径的缺陷分析（决策权威分裂、文档漂移、错误策略不一致、职责夸大），确定的架构演进方向：

1. **数据传递统一但不定死**：构建一套数据传递标准 —— 定死的是文法（契约声明形态），不定死的是内容（数据项开放注册、版本化演进）。
2. **主循环 = 状态快照管理循环**：每轮唯一职责是管理一个统一状态快照，快照包含所有运行中的智能体执行（executor-agnostic，Cline 只是其中一种执行器状态）+ 策略视图 + 根循环状态。
3. **状态离散、可保持可改变**：每个运行中的执行具有离散状态，更新时经状态机校验 —— 可保持（Held）也可改变（Changed），非法转移被防御。
4. **多 Agent 原生化**：执行器选择数据化（方向 C），策略机切换 → 执行器切换，而非硬编码单一执行器。

---

## 2. 新增架构层

```
┌──────────────────────────────────────────────────────────────┐
│ Agent.Contract/DataContracts   —— 数据契约标准（定死文法）        │
│   DataContract / DataDirection / DataContractRegistry / DataBus │
├──────────────────────────────────────────────────────────────┤
│ Agent.State/Runtime            —— 统一状态快照 + 管理器（状态层） │
│   AgentRuntimeSnapshot / AgentRuntimeManager /                 │
│   AgentRun / AgentRunStatus / AgentRunStateMachine             │
├──────────────────────────────────────────────────────────────┤
│ Agent.Axioms/Loops/DataContractRegistrar —— 契约播种器          │
│ PowerShellScript/DSL/Check-DataContracts.ps1 —— 一致性校验器    │
└──────────────────────────────────────────────────────────────┘
```

依赖方向不变（Agent.State 为最底层，不引用 Agent.Axioms；Agent.Contract 纯端口）。

---

## 3. 架构件一：统一数据传递标准

**设计原则**：定死文法、开放内容、版本化、注册式扩展。

| 类型 | 位置 | 职责 |
|---|---|---|
| `DataDirection` | `Agent.Contract/DataContracts/DataDirection.cs` | 数据流向四分类：`Seed / Harvest / PassThrough / CrossCutting` |
| `DataContract` | `Agent.Contract/DataContracts/DataContract.cs` | 契约声明 `{Key, ValueType, Direction, SchemaVersion, Producer, Consumer}` |
| `DataContractRegistry` | `Agent.Contract/DataContracts/DataContractRegistry.cs` | 进程级契约注册表（单一事实源），`Register / TryGet / All` |
| `DataBus` | `Agent.Contract/DataContracts/DataBus.cs` | 类型化数据总线：`Put/Get<T>` 写前校验"已注册且类型匹配"，杜绝魔法键/弱类型静默流通 |
| `DataContractRegistrar` | `Agent.Axioms/Loops/DataContractRegistrar.cs` | 用 `RoundKeys` + 强类型登记主循环数据项（开放内容），启动期幂等播种 |
| `Check-DataContracts.ps1` | `PowerShellScript/DSL/Check-DataContracts.ps1` | 一致性校验：断言每个被使用的 `RoundKeys.X` 都有契约（CI 可 `-Check`） |

**启动钩子**：`Agent.Axioms/Infrastructure/AxiomBindingModule.ReportAndLog()` 调用 `DataContractRegistrar.EnsureRegistered()`。

---

## 4. 架构件二：统一状态快照 + 快照管理器

**统一状态快照（Unified State Snapshot）** —— 主循环作为快照管理循环时每轮维护的唯一快照，含三部分：
1. 根循环状态（Budget / Token / Flag，承载于 `ILoopState` / `AxiomContext`）；
2. 所有运行中的智能体执行（executor-agnostic），`Agents`；
3. 策略视图（`Strategies` 的 Current / Available / History）。

**快照管理器（AgentRuntimeManager）** —— 唯一控制器，收拢快照所有生命周期逻辑：

| 方法/属性 | 职责 |
|---|---|
| `Snapshot` | 持有统一状态快照 |
| `StrategyMachine` / `Executors` | 持有策略机 + 执行器注册表（setter 同步快照策略视图） |
| `PlanRound(activeStrategy)` → `RoundPlan` | **快照消费驱动**：由当前策略解析本轮执行器（executor-agnostic），产出执行计划 |
| `RecordExecution(...)` → `(Run, Transition)` | 登记运行中执行，离散状态校验 |
| `ResolveExecutor(strategy)` | 按策略名匹配执行器 Name，否则回退首个 |
| `TargetStatusFrom(ok)` | 成败 → 目标离散状态（Done / Error） |
| `AdvanceStrategyAsync(telemetry)` | 推进策略状态机（委托，不裁决终止） |
| `ActiveStrategyId` | 当前生效策略 Id（快照内联） |
| `RoundPlan(ActiveStrategy, Executor)` | 一轮执行计划 DTO |

**持有者**：`AgentOrchestratorState.Runtime`（类型 = `AgentRuntimeManager`）；`AgentRunner` 经 `AgentRunnerOptions.Runtime` 注入。

---

## 5. 架构件三：离散状态消费管理

**离散状态（AgentRunStatus）**：`Pending / Running / Done / Error / Paused`。

**离散状态机（AgentRunStateMachine）** —— 合法转移边：

```
Pending → {Pending(保持), Running}
Running → {Running(保持), Done, Error, Paused}
Paused  → {Paused(保持), Running(恢复)}
Done    → {Done(保持)}     // 终态：可保持不可改变
Error   → {Error(保持)}    // 终态：可保持不可改变
```

- **保持（Held）**：目标 == 当前状态（continue 时运行仍 Running）。
- **改变（Changed）**：走到合法出边（成功 Running→Done，失败 Running→Error，恢复 Paused→Running）。
- **非法（Illegal）**：越过合法边（如 Done→Running）→ 保持原状并返回 `Illegal`。

**消费闭环**：
```
每轮成败
  → AgentRuntimeManager.TargetStatusFrom(ok)     // 目标离散状态
  → AgentRuntimeManager.RecordExecution(...)     // 经状态机：保持/合法改变/非法保持，返回 (Run, Transition)
  → AgentRun.LastTransition                      // 消费方可观测
```

---

## 6. 架构件四：方向 C 执行器选择

把 `cline_execute` 的硬编码单一执行器，改为调度侧按策略解析、注入轮内数据空间、`cline_execute` 消费。

```
策略机切换 ActiveStrategy
  → AgentRuntimeManager.PlanRound → RoundPlan(Executor)   // 快照驱动：决定本轮跑谁
  → AgentRunner 注入 ctx.Data[executor]
  → RoundStateMapper.Seed → vars[executor]
  → cline_execute.ResolveExecutor(context) → executor.ExecuteAsync(request)   // 不再硬编码 SharedExecutor
  → 快照 AgentRun.ExecutorId = 执行器名（Cline 只是其中一种状态）
```

**关键点**：`RoundKeys.Executor` 契约（PassThrough，Producer=AgentRunner，Consumer=cline_execute）。`cline_execute` 无注入时回退共享 Cline 默认执行器（行为保持）。

---

## 7. 数据契约注册表现状（29 键，全部被使用键均被登记）

分组（Producer/Consumer 标注）：
- **入参/守卫**：`input`, `filtered_input`
- **目标/约束/规划**：`user_input`, `goal`, `constraints`, `plan_summary`
- **执行/提示**：`system`, `l1`, `l2`, `prompt`, `exec_result`, `return_spec`, `next_prompt_hint`, `next_hint`
- **循环控制**：`decision`, `step_count`, `reason`, `rule`
- **边界自评**：`boundary_action`
- **任务/会话**：`task_id`, `history`, `session_id`
- **策略（横切）**：`active_strategy`（Consumer=cline_execute）
- **执行器（PassThrough）**：`executor`（Producer=AgentRunner，Consumer=cline_execute）
- **边界（空间/逻辑）**：`cwd`, `data_dir`, `reconstruct_hint`
- **输出**：`output`, `safe_output`

校验：`Check-DataContracts.ps1 -Check` 报 `使用中 29 / 登记 29 / 未覆盖 0`。

---

## 8. 文件清单

### 新增（本次实现）
| 文件 | 职责 |
|---|---|
| `Agent.Contract/DataContracts/DataDirection.cs` | 数据方向枚举 |
| `Agent.Contract/DataContracts/DataContract.cs` | 数据契约声明 |
| `Agent.Contract/DataContracts/DataContractRegistry.cs` | 契约注册表 |
| `Agent.Contract/DataContracts/DataBus.cs` | 类型化数据总线 |
| `Agent.State/Runtime/AgentRuntimeSnapshot.cs` | 统一状态快照模型 |
| `Agent.State/Runtime/AgentRun.cs` | 运行中执行记录 |
| `Agent.State/Runtime/AgentRunStatus.cs` | 离散状态枚举 |
| `Agent.State/Runtime/AgentRunStateMachine.cs` | 离散状态机（保持/改变/非法） |
| `Agent.State/Runtime/AgentRuntimeManager.cs` | 快照管理器（含 `RoundPlan`） |
| `Agent.Axioms/Loops/DataContractRegistrar.cs` | 契约播种器 |
| `PowerShellScript/DSL/Check-DataContracts.ps1` | 一致性校验器 |

### 修改
| 文件 | 变更 |
|---|---|
| `Agent.State/Orchestration/AgentOrchestratorState.cs` | `Runtime` 类型改为 `AgentRuntimeManager`，策略 setter 收敛 |
| `Agent.Core/Loops/AgentRunnerOptions.cs` | `Runtime` 类型改为 `AgentRuntimeManager?` |
| `Agent.Core/Orchestration/AgentOrchestrator.cs` | 注入 `Runtime: _state.Runtime` |
| `Agent.Core/Loops/AgentRunner.cs` | `_runtime` + `RecordExecution`/`PlanRound` 消费 + `ResolveFromRegistry` 回退 |
| `Agent.Core/Loops/DecisionPoint.cs` | 策略推进经 `_runtime.AdvanceStrategyAsync` 路由 |
| `Agent.Axioms/Loops/RoundKeys.cs` | 新增 `Executor` 键 |
| `Agent.Axioms/Loops/RoundStateMapper.cs` | `Seed` 透传 executor |
| `Agent.Axioms/Loops/RoundSpecLoader.cs` | `cline_execute` 参数表加 `executor` |
| `Agent.Axioms/Infrastructure/AxiomBindingModule.cs` | 启动期播种契约 |
| `Agent.ClineUsage/ClineExecutorFunction.cs` | `ResolveExecutor(context)` 消费注入执行器 |
| `Agent/Config/workflows/agent-loop.json` | `cline_execute` 参数表加 `executor` |

> 注：`Agent.Core/Loops/DecisionPoint.cs`、`RoundOrchestrationContext.cs`、`AxiomAgentLoop.cs`、`RuleMatchFunction.cs` 为既有未提交改动（v6 重构），非本次新增。

---

## 9. 已解决的原调用路径缺陷

| 原缺陷 | 解决方式 |
|---|---|
| A1 决策权威分裂（Route 死代码） | 执行器/策略选择收敛到快照管理器，`DecisionPoint` 策略推进委托管理器 |
| 执行器硬编码单一 Cline | 方向 C：按策略解析执行器，`cline_execute` 消费注入 |
| `ActiveStrategy` 只写不读 | 登记为带 Consumer 的契约 + 快照管理器 `PlanRound` 消费 |
| 数据传递弱类型/魔法键/反射绑定 | 统一 `DataContract`/`DataBus` 标准 + 校验器（数据层，删除反射绑定为后续 P1 项） |
| 快照逻辑分散 | 集中到 `Agent.State/Runtime`（`AgentRuntimeManager`） |

---

## 10. 构建与校验验证

```
dotnet build Agent.sln          → 0 警告 0 错误
Check-DataContracts.ps1 -Check  → 使用中 29 / 登记 29 / 未覆盖 0 / exit=0
```

---

## 11. 未完成 / 待办路线（P4 本体）

| 阶段 | 内容 | 风险 |
|---|---|---|
| **P4-1** | 快照驱动的 Execute：把 `Execute` 步骤改为从快照管理器读离散状态/执行器/策略决定本轮动作，取代固定线性骨架 | 高（触碰 `RoundLifecycle`/骨架执行语义，行为级变更） |
| **P4-2** | 注册第二个演示执行器（如 Name="researcher"）+ 策略，做真实离散切换（`Pending→Running→Done/Paused`） | 中（需 `IAgentExecutor` 实现） |
| **P1** | 用 `DataBus` 迁移 `RoundStepInvoker`（删除反射绑定 + ClineResult 特判）、`RoundStateMapper`、`LeaseDeriver`（租约切片契约化） | 中 |
| **P3** | `AgentRunCancellation` 归入 `CrossCutting` 契约类 | 低 |
| 文档 | README 同步「统一数据传递标准 / 快照管理循环 / 离散状态机 / 方向 C」章节 | 低 |

