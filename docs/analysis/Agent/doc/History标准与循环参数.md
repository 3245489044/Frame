# History 标准与循环参数

> **版本**：0.1（初始——围绕「标识符」统一 history 的标准视图/主循环/存储，并汇总循环参数权威）
> **日期**：2026-08-29
> **状态**：文档（对应代码已落地，本文为维护/排障参考索引）
> **范围**：Agent 侧 history（历史容器）的标准定义——作用域标识符、三粒度标准上下文、history 主循环、存储形态；以及循环引擎的配置参数（引擎执行侧 + 策略 L3 + 模式预设 Profile）。
> **依据（代码核实）**：
> - 标准上下文：`Projects/Agent/src/Agent.Session/StandardContext.cs`（`StandardScope`/`StandardContext`）、`IStandardContextProvider.cs`（角色→粒度单一权威）、`IStandardContextBuilder.cs`、`IStandardContextRenderer.cs`
> - 主循环：`Projects/Agent/src/Agent.Core/TaskPool/HistoryMainLoop.cs`（history 级编排器）、`HistoryLoopDecider.cs`（混合调度决策器）、`HistoryDecisionMapper.cs`（决策→编排请求纯映射）
> - history 调度状态：`Projects/Agent/src/Agent.Runtime/State/Scheduling/HistorySchedulerState.cs`（taskKey→生命周期状态 + 确定性核心只读投影）
> - 任务快照根：`Projects/Agent/src/Agent.Core/TaskPool/AgentTaskItem.cs`（TaskKey/HistoryId，任务=快照）
> - 节点存储：`CsharpPrj/Operation/DataStoreDriven/Abstractions/NodeStore.cs`（`NodeRecord`，含驱动标识符 D/决策 C）、`SqliteNodeStore.cs`（ur_nodes 键值+JSON）
> - 统一标识符：`Projects/Agent/src/Agent.Contract/Identifier/DrivingKey.cs`（`DrivingKey`/`DrivingKeyKind`/`DecisionKey`，复用 `Share.StateManagement.StateKey`）
> - 循环参数权威：`.clinerules/formula.md §3.1`（引擎执行侧）、`.clinerules/axioms.md §9`（策略 L3）、`.clinerules/formula.md §3.2`（Profile）

---

## 一、History 标准

> 核心主张：**history 是一个以「作用域标识符」键控的容器**——历史 = 一组 `HistoryId/TaskKey/NodeKey` 标识符的并集投影，所有 AI 交互经标准上下文（三粒度）统一读取，history 级推进由 history 主循环（决策→编排→收敛）驱动。

### 1.1 作用域标识符（history 的键）

| 标识符 | 载体 | 作用 | 代码 |
|---|---|---|---|
| `HistoryId` | `AgentTaskItem.Record.HistoryId` | History=Task 更上层容器 Id；归拢多个 Task/Node 键 | `Agent.Core/TaskPool/AgentTaskItem.cs` |
| `TaskKey` | `AgentTaskItem.Record.TaskKey` | 任务快照根；业务幂等键，同时作快照持久化键 | 同上 |
| `NodeKey` | `NodeRecord.NodeKey` | 节点键；ur_nodes 幂等 upsert 依据 | `Operation/DataStoreDriven/Abstractions/NodeStore.cs` |
| `ScopeId` | `StandardContext.ScopeId` | 标准上下文作用域标识（nodeKey / taskKey / historyId） | `Agent.Session/StandardContext.cs` |

**统一标识符抽象**（阶段 1）：`Agent.Contract/Identifier/DrivingKey.cs` 定义 `DrivingKeyKind { Step, Task, History, Node, State }`，把上述散落键域归一为同一可标识原子的判别域，内部以 `Share.StateManagement.StateKey`（`Namespace:Scope:Key`，轴绑定）承载，复用而不复制（TH-12）。

### 1.2 标准上下文三粒度（StandardScope）

所有 AI 交互的唯一结构化输入 = `StandardContext`（纯数据、零 LLM）：

```csharp
public sealed record StandardContext(
    StandardScope Scope,        // Node / Task / History
    string ScopeId,             // nodeKey / taskKey / historyId
    string? WorkflowId,         // 节点作用域需要；空 = NodeKeys.DefaultWorkflowId
    IReadOnlyList<ConversationMessage> Messages);  // 规范消息序列（复用 Share.Conversation）
```

| 粒度 | 作用域标识 | 内容 | 使用方 |
|---|---|---|---|
| `StandardScope.Node` | nodeKey | 单节点输出 | 节点级消费 |
| `StandardScope.Task` | taskKey | 单任务对话历史（含其节点产出） | 子代理（SubAgent） |
| `StandardScope.History` | historyId | 单历史容器（其全部任务的对话并集） | 主循环（MainLoop） |

**角色 → 默认粒度（单一权威）**：`IStandardContextProvider.DefaultScopeForRole` —— `MainLoop → History`；`SubAgent → Task`。显式 `context_scope` 优先于角色默认（`BuildForScopeAsync`）。

### 1.3 History 主循环

`HistoryMainLoop.RunAsync(historyId, workflowId)` —— history 级主循环，只管理 task 状态 + 编排 nodes，LLM 工作由子代理承担：

```
循环体：
  读 StandardContext(History)（主循环视图，全部 task 对话并集）
  → IHistoryLoopDecider.NextAsync(view, enactedCount) → TaskOrchestrationRequest?（null = 批次结束）
  → 经 ITaskOrchestrator 纯逻辑落地（建/激活/暂停/恢复/终止 task、收割 node、挂 node 归属）
  → 直到决策返回 null
```

**组件**：
| 组件 | 职责 | 代码 |
|---|---|---|
| `IHistoryLoopDecider.NextAsync` | 决策端口：返回下一编排请求；null=主循环完成 | `HistoryMainLoop.cs` |
| `HistoryLoopDecider` | 混合调度决策器：`AiSchedulerStateMachine`（导演提议 + 守卫校验）+ `HistoryDecisionMapper`；零 LLM（LLM 只在导演端口注入） | `HistoryLoopDecider.cs` |
| `HistoryDecisionMapper` | 纯映射：`SchedulerDecision → TaskOrchestrationRequest`（SingleImplementation / TH-12） | `HistoryDecisionMapper.cs` |
| `HistorySchedulerState` | history 级调度状态宿主：`taskKey → 生命周期状态`（Pending/Running/Paused/Done）+ 确定性核心只读投影（IStateFacet）；状态机 Apply 只改策略/执行状态，不改写核心（确定性核心不变式） | `Agent.Runtime/State/Scheduling/HistorySchedulerState.cs` |
| `TaskOrchestrationRequest` | 编排请求：`{ Action, TaskKey, NodeKey, Input }` | `HistoryMainLoop.cs` |

**动作映射**（`HistoryDecisionMapper.Map`）：
| SchedulerAction | TaskOrchestrationAction | 说明 |
|---|---|---|
| `AdvanceRun` | `ActivateTask` / `ResumeTask` | task 原本 Paused → 恢复；否则激活 |
| `PauseRun` | `PauseTask` | 挂起 |
| `Retry` | `ActivateTask` | 重激活 |
| `SwitchStrategy` | `CreateTask` | 新策略方向开启新 task（TaskKey=TargetExecutorId 优先，其次 Intent；Input=Reason） |
| `Replan` / `Explore` | `null` | 无离散 task 变更，本批次无落地动作 |

**终止语义（对齐被动主循环，无后台空转）**：未施加 / `Held` / `Reject` / `Continue` → `null` → 主循环批次结束，避免非限定调度把被动批次拉成无限迭代。

### 1.4 存储标准（ur_histories / ur_tasks / ur_nodes）

| 存储 | 键（标识符） | 内容 |
|---|---|---|
| `ur_histories` | `HistoryId` | History=Task 更上层容器（归拢多 Task/Node 键） |
| `ur_tasks` | `TaskKey` | 快照根 `PayloadJson`（内部键以 `RoundKeys.*` 为权威，见 `Agent.Axioms/Loops/RoundKeys.cs`） |
| `ur_nodes` | `NodeKey` + `NodeType` 判别 | 每个执行步；`NodeRecord` 现携带**驱动标识符 D（`StepKey`）+ 决策 C（`Decision`）**，支撑按标识符序列重放（审计/恢复，对齐 `InformationMonotonicity` 只增不丢） |

> 注：`ur_nodes` 为「键名 + 一个 JSON」形态（`SqliteNodeStore`），新增 `StepKey`/`Decision` 字段仅向 JSON 加键，表结构零改动（阶段 4 落地）。

---


## 二、循环参数

> 权威来源：`.clinerules/formula.md §3.1`（引擎执行侧参数）、`.clinerules/axioms.md §9`（策略 L3 配置参数）、`.clinerules/formula.md §3.2`（Profile 模式预设）。**任何参数以 clinerules 权威文件为准，本文档为引用索引，不重复定义。**

### 2.1 引擎配置参数（L3，引擎执行侧，formula §3.1）

| 参数名 | 值 | 说明 |
|---|---|---|
| `EXEC_TIMEOUT` | `30`(s) | 循环单步执行体超时 |
| `ROUNDS` | `3` | 标准轮次数（ProgressToken 语义上限对齐 `MAX_PROGRESS`） |
| `SUBROUND_MAX_ATTEMPTS` | `3` | 子轮最大尝试 |
| `SUCCESS_CRITERIA` | `pass ∧ (n≥1)` | 成功判定 |
| `VERIFICATION_CRITERIA` | `eval=pass` | 验证判定 |
| `RECENT_CHAIN_COUNT` | `3` | 压缩后加载的推理链条数（CompactProcedure） |
| `EMERGENCY_DUMP_MAX_CHARS` | `500` | 紧急转储最大字符数 |
| `COLLECT_PATTERNS` | `[{key:"[A-Za-z]+\\[.*\\]",action:"parse"},...]` | Collect 阶段解析模式 |

> 工具批操作/IO 层超时（`ExecMode.IO.timeout` 等）见 `.clinerules/project.md §1.2`，量纲与 `EXEC_TIMEOUT` 不同（IO 层 vs 循环单步），不冲突。

### 2.2 策略配置参数（L3，可运行时变，axioms §9）

> 行为阈值已收敛到 Strategy 层（`ProgressToken`/`FailureBudget` 语义承载原 CF/CP/QDC/IC 等计数器），下表为运行时可配置参数。

| 参数名 | 类型:默认 | 说明 | 权限 |
|---|---|---|---|
| `MAX_PROGRESS` | `int:3` | ProgressToken 上限（原 MAX_FAIL/MAX_ITER/MAX_PARTIAL 收敛） | config |
| `BUDGET_RETRY_LIMIT` | `int:1` | FailureBudget.level=1 时 RetryLimit 收紧值 | config |
| `ATTENTION_EPSILON` | `float:0.05` | 注意力激活阈值 | config |
| `ATTENTION_TEMP_MIN` | `float:0.3` | 注意力收敛下限 | config |
| `CTX_WATERMARK` | `float:0.85` | 上下文水位收窄触发阈值 | config |
| `META_THRESHOLD` | `float:0.9` | 元级进化触发阈值 | config |
| `RECONFIRMATION_WINDOW` | `int:2` | 重确认窗口 | config |
| `BUDGET_ALLOC` | `float:1.0` | 注意力预算上限（`Σ_r w(r) ≤ BUDGET_ALLOC`，消费方：AttentionWeighting） | config |

### 2.3 模式预设 Profile（formula §3.2）

| Profile | MODE | 参数差异（delta） | 语义 |
|---|---|---|---|
| `SAFE` | `SAFE` | `{ MAX_PROGRESS=1 }` | 单次执行，防呆 |
| `DEVELOP` | `DEVELOP` | `{ MAX_PROGRESS=5, BUDGET_RETRY_LIMIT=2 }` | 诊断级调试，熔断更宽松 |
| `CAMPAIGN` | `CAMPAIGN` | `{ MAX_PROGRESS=10, RECENT_CHAIN_COUNT=5 }` | 多轮长任务，跨轮比较依赖 TH-9 |

> Profile 属 config 级（`AX-CORE.AxiomHierarchyStructure` invariants(L3)：`config < admin`）。参数来源优先级：显式实参 > 模式预设(Profile) > 全局默认（`SM.ConfigInjection`，formula §5.3）。

### 2.4 权威与变更约定

- **引擎执行侧参数**（`EXEC_TIMEOUT`/`ROUNDS`/`SUBROUND_MAX_ATTEMPTS` 等）：权威 `.clinerules/formula.md §3.1`。
- **策略 L3 参数**（`MAX_PROGRESS`/`BUDGET_*`/`ATTENTION_*` 等）：权威 `.clinerules/axioms.md §9`（含 auth 权限）。符号索引见 `.clinerules/references.md`（`Param.*`）。
- **项目级**（ExecMode 超时/限流、阈值、废弃、异常映射、修复优先级）：权威 `.clinerules/project.md`。
- **变更流程**：改 clinerules 权威文件 → 如需新增符号，运行 `Check-ClinerulesReferences.ps1 -UpdateIndex` 重编译索引。本文件为引用索引，不承载参数定义，变更不落此。

---

## 附：关联架构

- 统一标识符抽象（history 的键的统一）：`Agent.Contract/Identifier/DrivingKey.cs`
- 分层决策框架（决策 = 产出折叠链标识符 D′ 或终止信号）：`CsharpPrj/Operation/LoopFoldMethod/Decision/`（`DecisionOutcome`/`IDecisionSkill`/`IDeterministicTermination`/`DecisionFoldEngine`）
- 快照轮次硬终止适配：`Agent.Core/Loops/AgentTaskTermination.cs`（`IDeterministicTermination<AgentTaskItem>`）
- 会话折叠软决策适配：`Agent.Core/Signal/ConversationDecisionSkill.cs`（`IDecisionSkill<...>`）
- skill 声明式绑定：`Agent.Core/Signal/SkConversationFoldOperator.cs`（`stepSkillBindings`）


