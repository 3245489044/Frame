# StateHoldDriven「状态 × 决策」统一通用框架 —— 设计分析与应用回归方案

> **状态**：方案（待实施） | **日期**：2026-09-04
> **目标承载目录**：`CsharpPrj/Operation/StateHoldDriven`
> **关联文档 / 提交**：
> - `docs/analysis/2026/8/8.5/StateHoldDriven状态管理中心.md`（状态契约 Share / 实现 StateHoldDriven 的分层决策，R1/R2）
> - `docs/analysis/Agent/状态快照调用链.md`（`AgentTaskItem`=任务=快照 单一根重构）
> - `docs/analysis/Agent/refactor/2026-08-13-task-snapshot-merge-design.md`（快照合并设计）
> - `docs/analysis/2026/9/9.3/`（S3/S4 单写收敛、轮后 next 卡死治理、I7 决策三源 / I8 超时域）
> - 提交 P1 `8c34e1491`（Layering + StateContextAdapter 接 canonical）、P2 `c1bb2d8f4`（StateCenter.Write Transient 静默广播）、P3 `e54ba13b8/4eb16fcf8`（RunScopeSlot 透明槽）
> **上一轮分析结论**（9.3 Agent 联调）：`decision=stop(Success)` 却 `finalDecision=Timeout`，根因为"决策词/决策语义/导演"三源分叉 + 确定性核心未落统一状态 → 本方案把这一族缺陷收敛为框架级治理。

---

## 摘要（一句话）

把 **"决策"** 建成与 **"状态"** 同等的、由 `StateHoldDriven` 承载的一等框架能力：
确定性核心（`Budget/Token/Flag/Round`）、决策词与轮决策（`DecisionRecord`）**落进 `StateCenter` 分层状态**并成为唯一真源；
裁决（评估→预算→终止）收敛为**纯函数 + 状态驱动**；
让 `Share.TaskStatusPool` 的纯内存循环原语与 Agent 侧的决策桥都**回归为该框架的应用/适配层**，从根上消除"状态与决策分家、决策三源分叉、决策词无单源"三类反复出现的缺陷。

---

## §0 阅读导航

1. §1 现状盘点（状态已统一；决策仍分家 → 精确缺口）
2. §2 目标架构（StateHoldDriven 决策宿主 + 应用消费；核心命题）
3. §3 通用框架设计（StateHoldDriven 内新增组件与既有组件关系）
4. §4 分层与防环论证（方案最关键处：语义放哪层 / 宿主放哪层）
5. §5 应用：让 Agent 循环 / 任务池 / 骨架回归通用框架（迁移映射表）
6. §6 分阶段实施计划 P0–P5
7. §7 验收与回归判据
8. §8 风险与开放问题
9. §9 现状代码锚点速查

### 实施红线（沿用既有，新增 R7–R9）

| # | 红线 | 说明 |
|---|------|------|
| R1 | 契约只放 `Share.StateManagement` | 状态契约（已落地） |
| R2 | StateHoldDriven 不引用 FrontEndDriven | UI/ECS 无关（已落地） |
| R7 | **决策语义/裁决器唯一权威仍放 `Share.TaskStatusPool.Loops`，StateHoldDriven 不重定义** | SingleImplementation / TH-12：StateHoldDriven 只加"状态驱动落地层"，不复制裁决逻辑（防双份） |
| R8 | **确定性核心写入收敛到单一入口** | `Budget/Token/Flag/Round/LastEval` 落 Center 后，仅 `StateDrivenRoundLifecycle` 内部可写；导演/边界/骨架持只读投影（RO）——把 `SEP.DeterministicCoreDisjoint` 从"注释纪律"升级为"类型强制" |
| R9 | **决策词单一权威上移登记** | `DecisionWordClassifier`（现 Agent.Operation，含 Fold 依赖）不得再是唯一权威散点；决策词经 canonical 键以 `DecisionRecord` 落 Center，词表定义收敛到低层或经 Adapter，禁止第三套词表 |

---

## §1 现状盘点：状态已统一，决策仍分家

### 1.1 状态侧（已就绪，P1–P3 完成）

| 层 | 承载 | 已落地物 |
|----|------|----------|
| 契约 | `Share.StateManagement` | `IStateCenter/IStateTransaction/IStateSubscriptionHub/IStateManager/…`；Loops 已迁出 |
| 实现（宿主） | `Operation.StateBlock`（StateHoldDriven） | `StateCenter`（Config/Session/Transient 三层唯一权威 + 事务 + 订阅 + scope 分区 + Actor 保留）；`Layering/{StateLayerScheme, LayeredStateKey, LayeredStoreClient}`（canonical 读写、前缀剥离收进框架） |
| 应用（Agent） | `Agent.Axioms.StateAdapters` | `StateContextAdapter`：Config(scope=threadHash)/Session(scope=sessionId)/Transient(scope=roundId)，经 `LayeredStoreClient` 委托 `StateCenter`；`AgentStateKeys` 提供三段命名空间 |

现状结论：**业务数据状态（骨架 Vars=Transient、跨轮 Data=Session、配置=Scheduler=Config）已收敛到 StateCenter**，广播分级（Transient 轮内静默隔离）与 run 作用域透明槽已为 Agent 装配铺垫完成。

### 1.2 决策 / 循环侧（缺口：尚未状态化、尚未单源）

| # | 决策要素 | 现状承载 | 问题 |
|---|---------|---------|------|
| D1 | 确定性核心 `Budget/Token/Flag/Round/LastEval` | `Share.TaskStatusPool.Loops` 内存原语（`LoopStatePrimitives`），作为 `AgentTaskItem/AxiomContext`（`ILoopState`）的**内存字段** | **不落 Center**；引擎/骨架直写字段，靠"共享引用 + 单写纪律"隔离，越权写风险高（对应前几轮 E 类双写、S3 单写治理） |
| D2 | 五步裁决 `RoundLifecycle` / `TerminationDecider` / `ProgressUpdater` / `BudgetEscalator` | `Share.TaskStatusPool.Loops`（纯内存、状态中心无关） | 裁决以"传入 ctx 内存对象"执行，输入/产物不经状态层，无法订阅、无法审计回放 |
| D3 | 骨架决策词 `continue/stop/error` | `Projects/Agent/src/Agent.Operation/Loops/DecisionWordClassifier.cs`（**Agent 项目内**、依赖 `Operation.LoopFoldMethod` 的 `FoldDecision`） | 决策词权威在 Agent 侧、且带 Agent 依赖，无法直接上移 Share → 决策词与 `Share.TaskStatusPool.Loops.LoopDecision`（枚举）**割裂两处** |
| D4 | 决策词→评估桥 `LoopDecisionMapper` / `DecisionPoint` | `Agent.Core`（`MapEval` 从 `Outputs["decision"]` 读字符串词） | 决策靠**魔法键 `"decision"` + 大小写折叠**传递；`Ok` 与词是否落键各自独立 → `stop` 已判、引擎仍 `Continue`（本次 `Timeout` 根因） |
| D5 | 轮决策记录 / 终止理由 | 仅 `lastDecision` 闭包 + `FinalDecision` | 无结构化 `DecisionRecord`（决策值/词/理由/来源），终止语义（TO 兜底 vs 真超时）无法区分 |

> 一句话根因：**"决策"没有像"状态"那样获得一个唯一承载宿主**——数据状态进了 Center，决策核心仍散在"Share 内存原语 + Agent 决策词 + Agent 决策桥"三处。

### 1.3 目标（精确表述）

让 `StateHoldDriven` 成为"**状态 × 决策**"统一的通用框架宿主：

1. **确定性核心状态化**：`Budget/Token/Flag/Round/LastEval` 作为受管状态（Session 层 scope=任务/轮）落 `StateCenter`，单一写口，导演/边界只读。
2. **决策一等化**：轮决策产出强类型 `DecisionRecord`（含决策值 `RoundDecision`、词、`EvalOutcome`、理由、来源、`ErrorClass`）落 Center，可订阅、可审计、可回放；决策词单源收敛。
3. **裁决纯函数 + 状态驱动**：评估/预算/终止仍是**纯裁决器**（唯一实现沿用 TaskStatusPool.Loops），但其输入是状态快照、产物写回状态——引擎变成"状态驱动循环"。
4. **应用回归**：Agent Runner/骨架、任务池 `RuleTaskExecutor`、`RoundLoopDriver` 改消费上述框架，TaskStatusPool 纯内存原语降级为"无 Center 直跑"的退化实现（零回归保障），不再作为生产主路径的多源。


## §2 目标架构：StateHoldDriven 决策宿主 + 应用消费

### 2.1 核心命题

- **命题 1（决策即状态）**：决策值、决策词、裁决理由、确定性核心是**状态**的一等公民，与业务数据同享分层、事务、订阅、持久化，而非游离的内存闭包。
- **命题 2（裁决纯函数）**：评估→预算→终止的裁决逻辑是纯函数 `f: StateSnapshot × Event → (StateMutation, RoundDecision)`，唯一实现，不因承载（内存/状态层）改变。
- **命题 3（引擎状态驱动）**：循环引擎不再"绕一个可写 ctx 对象转"，而是"读状态 → 纯裁决 → 状态单一写口提交 → 订阅广播 → 进入下轮"，天然获得审计/回放/单写。
- **命题 4（单源）**：决策词与轮决策收敛为一套强类型事实 + canonical 键，任何消费方（骨架/引擎/导演/日志）读同一真源；分叉即编译错误或告警，不再靠手工对齐。

### 2.2 组件总览（StateHoldDriven 内新增 `Decision/`，前缀随 RootNamespace `Operation.StateBlock`）

```
StateHoldDriven（宿主 = Operation.StateBlock）
├─ Core/StateCenter           既有：三层状态唯一权威 + 事务 + 订阅
├─ Layering/…                既有：StateLayerScheme/Key/LayeredStoreClient canonical 通道
├─ Decision/                  【本方案新增】状态驱动的决策框架
│   ├─ LoopStateStore.cs      StateBackedLoopState：确定性核心(Budget/Token/Flag/Round/LastEval)
│   │                           映射到 Center Session 层（scope=任务/轮），单一写口
│   ├─ RoundDecision.cs       决策域值类型(收敛 DecisionWord + LoopDecision 投影的强类型)
│   ├─ DecisionRecord.cs      轮决策记录（词/值/Eval/理由/来源/ErrorClass/时间）
│   ├─ DecisionCenter.cs      门面：写决策记录/订阅轮决策/读最近决策（状态投影）
│   ├─ StateDrivenRoundLifecycle.cs
│   │                         状态驱动五步(Execute→Collect→Evaluate→Decide→Adjust)，
│   │                         唯一写确定性核心，内部复用 TaskStatusPool.Loops 裁决器
│   └─ LoopKeys.cs            决策/确定性核心在 Center 的 canonical 键（替代 magic "decision"）
└─ Blocks/StateCenterBlock    既有：供注册表装配 StateCenter
```

> 命名空间建议：`Operation.StateBlock.Decision`（契约值类型如需 Share 复用 → 放 `Share.StateManagement.Decision`，见 §4）。

### 2.3 状态驱动五步（目标时序，取代"绕内存 ctx"）

```
① Execute    executor 产出 WorkProduct → 写 Transient(轮内，静默隔离)
② Collect    归一 ErrorClass（纯函数，读产物/错误）
③ Evaluate   纯裁决：ProgressUpdater/BudgetEscalator 的输入=状态投影
             → 唯一写口对 Session 层确定性核心提交（Budget/Token）
④ Decide     纯裁决：TerminationDecider / 成功判定 → 产 RoundDecision
             → 落 DecisionRecord(Session) + 广播（订阅方：导演/UI/日志）
⑤ Adjust     Round++；回写 Center；清理轮内 Transient
事务边界：Execute→Decide 之间为单事务，未 Commit 前状态不对外发布（对齐 R4）
```

---

## §3 通用框架设计要点（StateHoldDriven 内部）

### 3.1 决策域：RoundDecision 收敛"双词表"

把 Agent 骨架词（`stop/continue/error`）与引擎决策枚举（`LoopDecision{TS,TO,TM,Continue,SwitchStrategy,None}`）投影为**一层强类型**，消除双轨：

```
RoundDecision = { Kind ∈ {Success, Continue, Timeout, TripTerminated, SwitchStrategy},
                  Word ∈ {stop,continue,error,…}?,        // 骨架决策词（保留供日志/兼容）
                  Signal,                                  // 调度信号投影
                  Reason?, Source }                        // 理由/来源（可审计）
```

- 裁决器仍产出底层 `LoopDecision`（唯一语义）；`DecisionCenter` 只在**落记录**时把 Word 一并封入 `DecisionRecord`，不再要求下游在字符串词与枚举之间自行换算。
- `DecisionWordClassifier` 的词表收敛为 `RoundDecision` 上的**唯一映射**（`§4` 处置其 Agent 依赖）。

### 3.2 StateBackedLoopState（确定性核心状态化，R8）

把 `TaskStatusPool.Loops` 的 `ILoopState.Budget/Token/Flag`（现为内存原语）替换为**状态层支撑视图**：

- 持久键落在 Center **Session 层** `scope = {taskKey/round}`，经 `LoopKeys` canonical 键读写（沿用 `LayeredStoreClient` 通道）。
- 写入只经 `StateDrivenRoundLifecycle` 内部；导演（`LlmStateDirector`）/边界（`DefaultBoundaryFramework`）/骨架**只读快照**（RO 投影）。`Token.Max` 仅框架可调（原 Boundary Escalate 改走框架提议，不直改 Token）。

> 效果：根治"多源写确定性核心 / 越权改预算"族（对应前几轮 `SEP.DeterministicCoreDisjoint` 靠注释纪律的脆弱性）；单写 + 订阅使审计/回放/对账成为默认能力。

### 3.3 DecisionCenter（决策门面）

- `Record(scope, DecisionRecord)` → 写 Session 层 + 广播；下游（导演决策输入、UI 状态、`ur_outputs` 回写）订阅投影，不再各自为政。
- `Recent(scope)` → 最近决策投影（替代 `lastDecision` 闭包，供引擎终止/`ConvergeFinal` 读取）。
- 依赖 `StateSubscriptionHub`（既有）分发；轮内决策为 Session（跨轮可见/可回放），Transient 仍只放轮内瞬态产物。

### 3.4 与既有 StateCenter/Layering 的关系（复用不重定义）

- 新增 `Decision/` **不重复**状态存储/事务/订阅逻辑——全部委托既有 `StateCenter`/`LayeredStoreClient`。
- `StateContextAdapter` 扩展为同时暴露确定性核心读写（写仅框架用）+ 决策记录读写，Agent 骨架侧无需自行拼接键。
- 裁决逻辑**不复制**：`StateDrivenRoundLifecycle` 内部 `using Share.TaskStatusPool.Loops` 复用 `RoundLifecycle` 的决策序列（纯函数形态），仅把 I/O 从内存对象换成状态层（§5.1 给出最小改动封装）。

---

## §4 分层与防环论证（方案关键）

### 4.1 明确"语义放哪层 / 宿主放哪层 / 应用放哪层"

| 承载对象 | 放哪层 | 依据 |
|---------|--------|------|
| 状态契约 `IStateCenter/…` | `Share.StateManagement` | 既有 R1 |
| 状态实现（StateCenter/Layering） | `Operation.StateBlock`（StateHoldDriven） | 既有 R2 |
| 决策语义 / 裁决器（`LoopDecision`/`RoundLifecycle`/`TerminationDecider`…） | **`Share.TaskStatusPool.Loops`（保持唯一权威）** | 无状态中心也须可用；StateHoldDriven 依赖 Share 方向向下；防双份（R7） |
| 决策 **状态落地层**（LoopStateStore / DecisionCenter / StateDrivenRoundLifecycle / LoopKeys） | **`Operation.StateBlock.Decision`（StateHoldDriven）** | 本方案新增宿主；依赖 `Share.TaskStatusPool` + `Share.StateManagement`（均向下） |
| 决策**词表 canonical**（Word→Kind） | 收敛为 RoundDecision 上的唯一映射，实现放 StateHoldDriven；**词表语义登记**下沉 `Share.TaskStatusPool.Routing`（去掉 `Operation.LoopFoldMethod` 依赖后） | 消除 D3/D4 双词表 |
| 应用桥（Agent 骨架 / 任务池 / 导演） | `Projects/Agent` + `Operation.DataStoreDriven`（消费以上） | 上层依赖下层，单向 |

### 4.2 依赖方向与防环

```
Share.StateManagement ◄── Share.TaskStatusPool ──► Share(其余)
        ▲                      ▲
        └──────── StateHoldDriven(Operation.StateBlock) ─┘   // 复用裁决(Loops) + 状态契约(StateManagement)
                              ▲
        Projects/Agent ├ Agent.Core/Runner/骨架            （应用：消费 StateHoldDriven Decision）
        Operation.DataStoreDriven └ TaskPool 应用层
```

- **防环判据**：`StateHoldDriven → Share.TaskStatusPool` 与 `StateHoldDriven → Share.StateManagement` 均为 Share 方向向下；**Share 不引用 Operation**，故 TaskStatusPool 不含任何 Operation/Agent 类型——这是"决策语义留在 Share、宿主放 Operation"的硬前提。
- **TaskStatusPool 现状引用**：`TaskStatusPool → EventBindingOrchestrator → CommandFramework → StateManagement`（task_progress 已核无环）；StateHoldDriven 引用 TaskStatusPool 不引入环。
- **词表下沉的前提改造**：`DecisionWordClassifier` 现位于 Agent.Operation 且依赖 `Operation.LoopFoldMethod.FoldDecision`。上移 Share 前须把 `FoldDecision` 依赖剥掉（词表只依赖 `LoopDecision`/枚举，Fold 信号由 Agent 侧 Adapter 提供）——**本方案将该改造列为 P3 前置**，避免循环依赖。


## §5 应用：让 Agent 循环 / 任务池 / 骨架回归通用框架

> 目标不是"重写循环"，而是把生产主路径的**决策 I/O 与单源**切到 `Operation.StateBlock.Decision`，底层裁决/引擎仍复用 `Share.TaskStatusPool.Loops`。

### 5.1 迁移映射表（现状 → 通用框架）

| 现状要素（文件） | 现承载 | 迁移目标（消费 Operation.StateBlock.Decision） |
|-----------------|--------|-----------------------------------------------|
| 确定性核心 `ILoopState.Budget/Token/Flag/Round/LastEval`（`AgentTaskItem`/`AxiomContext` 内存字段，`Loops/LoopStatePrimitives`） | 内存，共享引用 | `LoopStateStore`（Session 层落 Center，单一写口）；`AxiomContext`/`AgentTaskItem` 改为**状态投影/薄租约** |
| `RoundLoopDriver`（`Loops/RoundLoopDriver.cs`）lastDecision 闭包 + budgetTerminate | 闭包 | 改读 `DecisionCenter.Recent(scope)`；引擎头 `budgetTerminate` 读状态投影（保留 TaskStatusPool 引擎，只是状态源替换） |
| `DecisionPoint`/`LoopDecisionMapper.MapEval` 读 `Outputs["decision"]`（`Loops/DecisionPoint.cs`、`Nodes/LoopDecisionMapper.cs`） | 魔法键字符串 | 决策词改由骨架以强类型经 `StateContextAdapter` 写 `DecisionRecord`；`MapEval` 读 `DecisionRecord.Word/Kind` 投影，**不再依赖魔法键位置**（根治 D4） |
| `RuleMatchFunction` / `RuleDecisionEvaluator` 产决策词（`rule_match` 匿名 `{decision=…}`） | 匿名对象 → orchestrator.Decision 与 Vars 双副本 | 骨架决策经 canonical 通道写 Center 决策（**一条事实**）；日志/引擎/导演同源 |
| `DecisionWordClassifier`（`Agent.Operation/Loops`） | Agent 侧词表 | 词表唯一映射收敛到 `RoundDecision`（§4.1），Agent 保留薄 Adapter 兼容 Fold |
| 导演/边界读 Token/Budget/写 `Token.Max`（`LlmStateDirector`、`DefaultBoundaryFramework` Escalate） | 直写内存/直改预算 | 读 RO 投影；边界 `Escalate` 改为**框架受管提议**（`DecisionCenter` 授予，Token.Max 仅框架写） |
| `AgentTaskPool`/`RuleTaskExecutor`（零 LLM 循环，`TaskPool/`） | 自建 SnapshotDrivenLoopEngine 循环 | 复用 `StateDrivenRoundLifecycle` + 状态驱动，任务池写即激活语义不变 |
| 终止收敛 `ConvergeFinal` / `AgentLoopTermination` | lastDecision 兜底 | 读 `DecisionCenter.Recent`；`FinalDecision` 附带 `termReason`（区分"预算耗尽兜底"与"真超时"，§3.1） |

### 5.2 优先落地的三个应用点（对应近期缺陷，效益最高）

1. **确定性核心状态化 + 单写（P2）**：`Budget/Token/Flag/Round` 移入 `LoopStateStore`；先让 Agent Runner 与 `RuleTaskExecutor` 都经 `StateDrivenRoundLifecycle` 写，一处代码即可让"多源写预算/双写"族收敛。保留 `TaskStatusPool` 内存 `ILoopState` 供直跑/单测（零回归）。
2. **决策词单源（P3）**：骨架 `rule_match` 的决策改为强类型 `DecisionRecord` 落 Center；`DecisionPoint`/`MapEval` 读该记录 → 消灭 `stop(Success)` 打印与引擎 `Continue` 分叉（本次 `Timeout` 根因）；加入"词=成功 ∧ eval≠Pass 即告警"的闭链断言。
3. **导演/边界只读 + 决策可订阅（P4）**：`LlmStateDirector`/`DefaultBoundaryFramework` 改为读 RO 状态投影；轮决策经 `DecisionCenter` 广播给 UI/`ur_outputs`，决策与运行态可审计回放。


## §6 分阶段实施计划（每阶段编译 + 单测通过再进下一阶段）

| 阶段 | 内容 | 产出 / 验收 | 红线 |
|------|------|------------|------|
| P0 预备 | StateHoldDriven 引用 `Share.TaskStatusPool`；核对 TaskStatusPool 无 Operation 依赖；`Operation.StateBlock.Decision` 命名空间 + csproj 就绪 | 编译 0 Error；引用图无环脚本通过 | R7 |
| P1 契约值类型 | `RoundDecision` / `DecisionRecord` / `LoopKeys`（若需 Share 复用则契约放 `Share.StateManagement.Decision`） | 类型单测：Word/Kind 投影等价 | R9 前置 |
| P2 确定性核心状态化 | `LoopStateStore`（Session 落 Center 单写）+ `StateDrivenRoundLifecycle`（内部复用 Loops 裁决，I/O 换状态层） | Runner 单测：单写、RO 只读、事务回滚 | R8 |
| P3 决策词单源 + 词表下沉 | `DecisionWordClassifier` 剥离 `FoldDecision` 依赖 → 词表唯一映射；骨架决策强类型落 Center；`MapEval` 改读记录；闭链断言 | `HybridSchedulerCheck`/Agent.Core.Tests 全绿；`stop` 不再空转到 TO 的回归用例 | R9 |
| P4 导演/边界只读 + 决策订阅 | 导演/边界 RO 投影；Boundary `Escalate` 改受管提议；`DecisionCenter` 广播接 UI/`ur_outputs` | Boundary.Tests / UI 运行验收 | R2/R8 |
| P5 应用回归 + 旧路径降级 | `RuleTaskExecutor`/任务池回归状态驱动；`TaskStatusPool` 内存 `ILoopState` 标注"无 Center 直跑/测试"退化实现 | 全套测试绿；GUI 复跑 `agent_mcpinvoke_create_task_test.json` 无 `Timeout` 空转 | — |

---

## §7 验收与回归判据

- **架构判据**：全库 `grep`：确定性核心（Budget/Token/Flag/Round）**写入仅** `Operation.StateBlock.Decision` 单一入口；`DecisionWordClassifier` 词表全库唯一（无第二套词表）；决策词 / `Outputs["decision"]` 魔法键引用归零（改走 `LoopKeys`/`DecisionRecord`）。
- **行为判据**：复跑 9.3 `tp_mcpinvoke_ctrl` 场景——骨架判 `stop` 即被引擎以 `Success(TS)` 收敛，不再以 `Timeout` 空转 3 轮；TO 仅代表真实超时或显式预算耗尽（带 `termReason`）。
- **单测判据**：`HybridSchedulerCheck`（TO/TM 回归）、`Agent.Core.Tests`、`Boundary.Tests`、`StateManagement.Tests` 全绿；新增"成功词必收敛 TS"与"双写确定性核心被编译/运行时拒绝"用例。

---

## §8 风险与开放问题

| 风险/问题 | 说明 | 缓解 |
|-----------|------|------|
| 依赖环（词表下沉） | `DecisionWordClassifier` 现带 `Operation.LoopFoldMethod` 依赖，直接上移 Share 会成环 | P3 前置：先剥离 `FoldDecision` 依赖，Fold 信号由 Agent Adapter 提供 |
| 双实现漂移 | StateHoldDriven 决策层若复制裁决逻辑会与 `TaskStatusPool.Loops` 双份 | R7：StateHoldDriven 只做状态落地，裁决 `using Share` 复用；`Loops` 保持唯一权威 |
| 性能 / 广播阻塞 | 轮内确定性核心高频写 Center + 广播可能引入写侧开销 | 沿用 P2 广播分级：轮内 Transient 静默、Session 决策低频（每轮一次）经既有 `IPublishDispatcher` |
| 状态兼容与迁移 | `AgentTaskItem.PayloadJson` 为现有持久根；核心状态迁 Center 后需与 PayloadJson 对账 | 保留 PayloadJson 为对外快照；Center Session 为运行时真源，边界对账（沿用 S3/S4 单写/Outbox 治理） |
| 开放问题 | ①确定性核心落 Session(scope=任务) 还是专属命名空间分区；②`DecisionRecord` 是否需要独立 `Decision` 存储 vs 复用 Center Session 层；③Actor(`StateOrchestrator`) 与状态驱动循环的边界 | 各留 P1/P2 决策点，先按"复用 Center Session 层 + LoopKeys 分区"最小路径 |

---

## §9 现状代码锚点速查

| 主题 | 锚点 |
|------|------|
| 状态契约 | `Share/StateManagement/`（`IStateCenter`/`IStateTransaction`/`IStateSubscriptionHub`） |
| 状态实现宿主 | `CsharpPrj/Operation/StateHoldDriven/Core/StateCenter.cs`、`Layering/{StateLayerScheme,LayeredStateKey,LayeredStoreClient}.cs` |
| canonical 客户端（Agent） | `Projects/Agent/src/Agent.Axioms/StateAdapters/StateContextAdapter.cs`（Config/Session/Transient） |
| 循环决策原语（纯内存，唯一语义） | `Share/TaskStatusPool/Loops/{LoopStatePrimitives,LoopDeciders,RoundLifecycle,SnapshotDrivenLoopEngine,DeterministicCoreCopier,SyncProtocol}.cs` |
| Agent 决策桥（待收敛） | `Agent.Core/Loops/{RoundLoopDriver,DecisionPoint}.cs`、`Agent.Core/Nodes/LoopDecisionMapper.cs`、`Agent.Core/Loops/RuleMatchFunction.cs`、`RuleDecisionEvaluator.cs` |
| 决策词（待单源） | `Projects/Agent/src/Agent.Operation/Loops/DecisionWordClassifier.cs` |
| 导演 / 边界（待只读） | `Agent.Axioms/Scheduling/LlmStateDirector.cs`、`TransitionGuard.cs`；`Agent.Core/Boundaries/DefaultBoundaryFramework.cs` |
| 决策键现状 | `Agent.Axioms/Loops/RoundKeys.cs`（`Decision="decision"`）、`CsharpPrj/Operation/HierarchyMethod/Nodes/Execution/NodeExecutionKeys.cs`（`Decision="decision"`，同键双读点即 D4 之源） |
| 分层治理记录 | `Share/StateManagement/task_progress.md`（Loops 迁 TaskStatusPool）、`docs/analysis/2026/8/8.5/StateHoldDriven状态管理中心.md`（R1/R2） |



