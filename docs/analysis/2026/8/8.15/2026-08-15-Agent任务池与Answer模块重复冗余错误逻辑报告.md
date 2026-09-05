# 报告：Agent 任务池 / Answer 模块 / 执行器选择链路 — 重复·冗余·错误逻辑

> **日期**：2026-08-15
> **范围**：本会话探查的 Agent 执行器选择与任务池链路（`Agent.Modules\Answer`、`Agent.Launcher\AgentOptionalModules`、`Agent.Launcher\Modules\AgentUiLibrary`、`Agent.Core\TaskPool\AgentTaskItem`、`Agent.Core\Loops\AgentRunner`、`Agent.Modules\Cline\ClineExecutorFunction`、`Agent.Runtime\State\Runtime\AgentRuntimeManager`、`Agent.Axioms\Scheduling\ConfigStateDirector`、`CsharpPrj\Share\AiScheduling\AiSchedulerStateMachine`、`CsharpPrj\Modules\Parsing\NamedItemResolver`）。
> **方法**：静态源码阅读（未运行程序）。标注「已确认」（直接读源码）与「文档记载」（引用既有文档）两类。

---

## 一、重复逻辑（Duplicate）

### D1. 执行器「按名解析」的双入口重载 —— 语义重叠
- **位置**：`Agent.Runtime\State\Runtime\AgentRuntimeManager.cs`（`ResolveExecutor(string?)` 与 `ResolveExecutor(StrategyDescriptor?)`）与 `Agent.Core\Loops\AgentRunner.cs`（`ResolveFromRegistry`）。
- **现象**：同一 `_executors` 注册表存在**两条并列解析入口**：`ResolveExecutor(string activeStrategy)`（按名→回退首个）与 `ResolveExecutor(StrategyDescriptor?)`（按 `ExecutorHint`→`Id`→回退首个）。`PlanRound(string?)` 只走字符串版，而 README（README.md §执行器选择）却强调「`ResolveExecutor(StrategyDescriptor?)` 重载优先 `ExecutorHint`」。两版并存，消费方不明确「该走哪个」，行为随调用点而异。
- **类型**：重复（同一解析语义的两份表述）。
- **注**：`NamedItemResolver.Resolve` 已被抽为单一实现供两者共用（D4 收敛），故「匹配算法」本身不重复；重复的是**入口选择语义**。

### D2. Answer 模块「两条执行路径」并行 —— 核心重复
- **位置**：`Agent.Modules\Answer\StatelessAnswerLoop.cs`（多轮精化，供门面 `StatelessAnswerAgent.AnswerAsync`）与 `StatelessAnswerExecutor.cs`（`IAgentExecutor.ExecuteAsync` 单轮，供任务池/`cline_execute` 消费）。
- **现象**：同一 `ILlmClient.ChatAsync` 核心，**两套**「提示词组装 → ChatAsync → 结果映射」。且在 `StatelessAnswerExecutor` 单文件内 `ExecuteRoundAsync` 与 `ExecuteRequestAsync` 又各自拼 prompt + 调 `ChatAsync` + 映射结果，同文件内即存在「拼提示→聊天→映射」重复片段。
- **类型**：重复 + 冗余（见 R2）。

### D3. `AiSchedulerStateMachine` 的 `SchedulerDecision` 构造重复
- **位置**：`CsharpPrj\Share\AiScheduling\AiSchedulerStateMachine.cs`（`ApplySwitch`/`ApplyRunStatus`/`Record`/`Fallback`/`Stay`）。
- **现象**：五个方法各自 `new SchedulerDecision(Applied, Action, Verdict, TargetStrategyId, TargetRunStatusId, TargetExecutorId, Intent, Reason, Confidence, ArbitrationKey)` 重复填充 10+ 命名参数，结构完全同构。
- **类型**：重复（可用工厂/局部函数收敛）。

---

## 二、冗余逻辑（Redundant）

### R1. 调度状态机命名/文件漂移（文档 vs 实际）
- **位置**：README.md / `docs/2026-08-13-*.md` 引用 `HybridSchedulerStateMachine`（`Agent.State.Runtime`）；实际类为 `AiSchedulerStateMachine`（`CsharpPrj\Share\AiScheduling\AiSchedulerStateMachine.cs`）。全仓无 `HybridSchedulerStateMachine.cs` 文件。
- **现象**：同一状态机两套名字，文档指向不存在的类。维护者按文档找不到实现。
- **类型**：冗余 + 误导（文档漂移）。

### R2. `StatelessAnswerExecutor` 返回 `ClineResult`（类型耦合错配）
- **位置**：`StatelessAnswerExecutor.cs` L120-136（`IAgentExecutor.ExecuteAsync` 返回 `ClineResult`）。
- **现象**：Answer 模块（非 Cline）为适配 `IAgentExecutor` 端口，产出 **Cline 类型**结果；而循环路径 `ExecuteRoundAsync` 返回 `AxiomActionResult`。同一模块两套结果类型，且其中一套强绑定 Cline 命名。
- **类型**：冗余 + 错误耦合（中立智能体混入 Cline 强类型）。

### R3. 默认策略/策略集「文档有、代码无」
- **位置**：`Agent.Core\TaskPool\AgentTaskItem.cs` L89 `CurrentStrategyId = ""`、L92 `AvailableStrategyIds` 空集；设计文档 `docs/2026-08-13-task-snapshot-merge-design.md` 却描述默认 `CurrentStrategyId="standard"`、`AvailableStrategyIds=["standard","aggressive","careful"]`。
- **现象**：文档声明了一套代码并不持有的默认值；任何依赖「已播种策略视图」的推理（如 SwitchStrategy 守卫、`strategy.current` 视图）在代码层落空。
- **类型**：冗余 + 漂移（见 E3）。

### R4. 快照投影历史重复（已收敛，备忘）
- **位置**：`AgentTaskItem.ToRuntimeSnapshot`（内聚投影）+ `SchedulerStateProjector.Project`（文档称已降为「委托薄壳」）。
- **现象**：历史曾「双快照并存」（`SchedulerState` vs `AgentTaskItem`）；文档记载已收敛为单根 + 薄壳。**非现症，仅记录**，避免误报为现问题。

---

## 三、错误逻辑（Erroneous）

### E1. `continue` 动作静默忽略 `targetExecutor`（误导性 DTO）
- **位置**：`AiSchedulerStateMachine.cs` `Apply`（L138-145）：`SchedulerAction.Continue` → `Stay`（**不施加**）；`ConfigStateDirector.cs` L97 规则 `targetExecutor` → `StateTransitionProposal.TargetExecutorId`，但该字段仅在 `AdvanceRun/Retry/PauseRun`（`ApplyRunStatus`）生效。
- **现象**：`{"action":"continue","targetExecutor":"stateless-answer"}` 会被解析、却**静默不生效**（走 `Stay`）。DTO 暴露字段，路径下被丢弃，用户配置后无任何提示。
- **影响**：本会话早期规划曾依赖「continue + targetExecutor 选中执行器」，即此隐患；属误导性/静默失败逻辑。

### E2. 执行器解析「回退首个」静默选错通道
- **位置**：`NamedItemResolver.Resolve`（L20-22）：按名不匹配 → 回退 `items[0]`（注册序首个 = cline）。
- **现象**：`AgentTaskItem.CurrentStrategyId` 默认空（R3），`ResolveExecutor("")` 静默回退**首个执行器（cline）**，而非报错。用户本意走 `stateless-answer`，却可能静默走 cline（错误通道消耗额度）。
- **类型**：错误逻辑 / 静默失败（缺 fail-fast）。

### E3. SwitchStrategy 守卫与「默认空策略集」冲突
- **位置**：`AgentTaskItem.AvailableStrategyIds` 默认空（R3）；`TransitionGuard`（`ConfigStateDirector.cs` L89-91 亦校验）要求 SwitchStrategy 目标在可用集内。
- **现象**：任务池路径若无播种，`AvailableStrategyIds` 为空 → 任何 `SwitchStrategy` 恒被拒（静默 `Stay`）。与文档「已播种策略视图」矛盾。
- **类型**：错误逻辑 / 文档漂移导致的运行时拒跳。

### E4. 执行器选择语义双轨（策略名 vs 执行器 Id）交叉
- **位置**：`AgentRunner`（`PlanRound(activeStrategy)` 用 `state.CurrentStrategyId` 按名匹配执行器 `Name`）vs `AiSchedulerStateMachine`（写 `TargetExecutorId` 到 `AgentStatus`，写 `CurrentStrategyId` 到策略）。`cline_execute` 实际消费 `ctx.Data[RoundKeys.Executor]`（AgentRunner 注入的 `plan.Executor`）。
- **现象**：执行器到底由「策略名 `CurrentStrategyId`」还是「执行器 Id `TargetExecutorId`」决定，链路交叉，本会话需多处交叉核实才能确定。存在按错来源解析而静默选错的风险。
- **类型**：错误逻辑 / 设计歧义。

---

## 四、优先级与建议

| 项 | 严重度 | 建议 |
|----|--------|------|
| E1 continue 忽略 targetExecutor | 高 | 要么让 `continue`+`targetExecutor` 生效，要么配置解析时 fail-fast 报「该动作不支持 targetExecutor」 |
| E2 回退首个静默选错 | 高 | 未命中时显式回退 `DefaultAgent`（配置），或记录 Warning，勿静默走 cline |
| E4 执行器双语义 | 高 | 统一 `PlanRound`/`ResolveExecutor` 单一消费调度决策的 `TargetExecutorId` |
| E3 空策略集拒跳 | 中 | 播种默认策略到 `AgentTaskItem.AvailableStrategyIds` 或放宽守卫 |
| R1 状态机命名漂移 | 中 | 文档 `HybridSchedulerStateMachine` → 改指 `AiSchedulerStateMachine` |
| R2 ClineResult 强类型耦合 | 中 | `IAgentExecutor` 端口改用中立结果类型，移除 Answer→Cline 依赖 |
| R3 默认策略文档漂移 | 中 | 对齐 `AgentTaskItem` 默认值与设计文档 |
| D1 执行器双入口 | 低 | 收敛为单一入口，明确消费方 |
| D2 提示词/映射重复 | 低 | 提取共享「拼提示→ChatAsync→映射」方法 |
| D3 SchedulerDecision 构造 | 低 | 工厂/局部函数收敛 |

> 注：以上为静态阅读结论；具体命中行号随仓库演进可能偏移，实施前以对应文件为准。
