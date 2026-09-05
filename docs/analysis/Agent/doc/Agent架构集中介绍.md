# Agent 架构集中介绍

> **定位**：本文档将《数据驱动反应式 Agent 架构设计》《状态快照调用链》《Agent 主干道》《任务池调度循环执行链》《引用关系维护完善方案》五篇文档整合为一篇自洽的集中介绍，按「**愿景 → 数据模型 → 主干道 → 执行链路 → 子系统专题**」叙事顺序展开，回答三个问题：**为什么这样设计（愿景）**、**一次运行怎么从头走到尾（主干道 + 执行链路）**、**关键子系统如何维护（关系维护）**。
> **文档关系**：愿景（Why）→ 结构（What）→ 流转（How）→ 专题（Subsystem）。各原始文档为权威细节源，本文档为全景导读。
> **状态**：与 2026-08-18 代码同步——任务=快照、主干道、任务池执行链 v1.3 已落地；引用关系维护 P0 收敛 + P1 声明式构建/重建/端点落盘已落地，P2 outbox 为计划项。

---

## 一、总览：Agent 是什么

**一句话**：Agent 是一条「**任务写库即激活 → 快照驱动混合调度循环 → 骨架五步执行 → 结果回写**」的自动化闭环；数据库是中枢与唯一事实源，外部一切交互最终退化为「读写数据」。

整体链路图：

```
【外部入口】交互启动.bat 管道 → AgentUi_RunJson（单一 JSON 入口，action 判别式分发）
   ▼ 写 ur_tasks（写即激活）
【任务池层】TaskPool 消费循环（按 Priority 出队、TaskKey 幂等、可暂停/重放）
   ▼
【路由】TaskRouter 结合裁决（任务自主标记 × 后台规则驱动）→ 需 LLM / 零 LLM
   ▼
【调度循环】SnapshotDrivenLoopEngine（快照驱动唯一 while；预算终止 + 混合调度）
   ▼ 每轮 execute（骨架五步）→ next（确定性计算 + 收割 + 调度 + 回写）
【执行层】guard_input → provide_prompt → cline_execute → rule_match → guard_output
   ▼
【回写】结果写输出区 ur_outputs；确定性文件操作经 Invoke-AgentStep → 标准存储 resource（LiteDB 图库端点）
```

**三层透视**（同一套系统从三个角度看）：
- **数据模型层**：`AgentTaskItem` = 任务 = 快照，唯一权威状态根（§三）。
- **控制流层**：一次 run 的单一主干道 [A]→[F]（§四）。
- **链路层**：从外部投喂到执行回写的完整管道（§五）。

---

## 二、设计愿景：数据驱动反应式 Agent

> 来自《数据驱动反应式 Agent 架构设计》（2026-08-10）。本部分回答「为什么这样设计」。

### 2.1 最终目的：数据库为中枢的愿景闭环

**数据库 = 系统的中枢与唯一事实源**：在指定区域写入数据 → 自动反应式触发 AI 处理 → AI 把处理结果写回数据库 → 一切外部 API 都退化为「读写数据的适配器」。

```
外部/任意来源 → 写输入区（逻辑 ur_inputs，唯一事实源 + 触发信号源）
   → 反应式触发（提交即发布，事件订阅）
   → Agent 循环（非限定快照引擎 + 混合调度）→ 处理结果结构化写回
   → 输出区 / 快照 / 资源 / 引用（写回形成闭环）
   → 外部消费方（只做：写输入、读输出、登记事件）
```

**关键约束**：已有 API **不删除**。数据层是叠加在现有 API 之上的**门面/总线**——只做「调用 API」⇄「读写数据」的桥接，不改、不删、可回退。

### 2.2 设计原则（不变式）

1. **叠加门面**：现有 API 是引擎、是底座；数据层只做桥接，不复制业务逻辑；关闭数据层系统仍可原 API 直接可用。
2. **数据即真相**：运行时事实只存在于 DB；日志降级为「DB 写入失败时的兜底」，不承担观测职责。
3. **单写入口 + 可重建投影**：SQLite 是实体唯一事实源与唯一写入口；图库只是从 SQLite 派生、可幂等重建的只读投影，永不作权威。
4. **契约即扩展**：每个数据/事件项有一份 `DataContract`；新增观测点 = 注册契约，生产/消费方零改动。
5. **效率为门槛**：任何结构决策（建表、索引、投影、压缩、批写）以效率 KPI 通过为前置，禁止「为设计而设计」。
6. **数据库与 Agent 解耦（使用层实现）**：Agent 核心只声明 `PersistencePort` 抽象（端口），零数据库依赖；落库实现由**使用层（宿主/消费方）**提供并注入。

### 2.3 总体架构（分层）

```
【外部世界】→ 数据适配器（只读写"数据区"，不碰业务 API）
  ▼
【使用层（宿主，与 Agent 完全解耦）】── 数据库在这里实现
   · SQLite（agent.db）+ DataStoreEngine + 关系库（Agent 无关，Agent 不知其存在）
   · PersistencePort 适配器：实现 Agent 声明的抽象，落库/重建
   · 触发/编排：写输入区 → Agent 反应式处理 → 读结果落库
  ▼
【Agent 框架（核心，不含数据库，仅声明 PersistencePort 抽象）】
   · ECS 反应式循环（原生抽象层：写即触发 → 反应式管线 → 写回组件）
   · 现有 API 层（底座，原样保留）：AgentRunner.RunAsync / AgentOrchestrator
   · 非限定引擎 + 混合调度（SchedulerState/AgentTaskItem 根）
```

### 2.4 存储层：三库收敛（SQLite 实体 / LiteDB 关系 / 内存缓存）

「双库」理念已演进为**三库收敛**：实体、关系、缓存各归其位，互不复制。

| 介质 | 库/表 | 内容 | 权威 |
|------|-------|------|------|
| **SQLite**（`agent.db`） | `ur_tasks`（状态集合） | 任务 = 快照根（ThreadId+SessionId+SnapshotJson） | 实体唯一 truth + 唯一写入口 |
| **SQLite**（`agent.db`） | `ur_nodes`（①基础节点库） | 极简「键名 + 一个 JSON」，`NodeType` 判别全部逻辑/记录节点——input / output / event / config / checkpoint / keymapping / relation 声明 / 业务 node | 实体唯一 truth |
| **LiteDB**（`graph-log.db`） | `ur_references`（唯一集合，纯边） | task ↔ node / node ↔ node 引用边 | 只读投影（由 SQL 写透时序同步） |
| **LiteDB**（`graph-log.db`） | `ur_endpoints` | 端点符号/metadata 落盘缓存（可重建） | 非权威（可重建缓存，权威在 SQL） |
| **内存** | `DataStoreEngine`/InternalStorage + 热邻接 + 端点注册表 | 会话数据缓存、图热邻接、会话内端点符号 | 会话级缓存，不落盘 |

> 三库收敛要点：
> - **SQL 仅两表**（`ur_tasks` + `ur_nodes`）：`ur_inputs`/`ur_outputs`/`ur_events`/`ur_config`/`ur_checkpoints` 等均为**逻辑名/契约名**，Sqlite 后端统一落 `ur_nodes`（NodeType 判别，见 §2.5）；**无独立 SQL `ur_resources` 表**（资源端点符号/metadata 落 LiteDB `ur_endpoints`，实体事实在 SQL）。
> - **LiteDB 只存边，不复制实体**：`ReferenceGraphCoordinator` 是唯一读写 LiteDB 引用关系的组件；端点存在性由 SQL 写透时序保证。
> - 配置即数据行：`IConfigStore` + `NodeConfigStore` → `ur_nodes`（NodeType=Config），改行即改行为（§2.8）。
> - 一致性：SQLite 提交即 truth；图库投影异步/惰性重建、幂等（可全量可增量）、崩溃可一键 `graph.rebuild` 重投影。

### 2.5 数据区模型（三区契约）

三个逻辑区由 `IZoneStore` 端口承载（append-only 事件库语义：写即触发、观测 = 查数据）；Sqlite 后端经 `NodeZoneStore` 落 `ur_nodes`（NodeType 判别），非独立建表。

| 数据区 | 逻辑表 | 物理承载 | 用途 | 生产者 → 消费者 |
|--------|--------|----------|------|-----------------|
| **输入区** | `ur_inputs` | `ur_nodes`(NodeType=Input) | 外部写入待处理任务/数据；**写入即触发信号** | 外部适配器 → 反应式触发器 → Agent 循环 |
| **输出区** | `ur_outputs` | `ur_nodes`(NodeType=Output) | AI 处理结果、完成状态 | Agent 循环（写回）→ 外部适配器 |
| **事件区** | `ur_events` | `ur_nodes`(NodeType=Event) | 运行时结构化事件（观测 + 触发信号源） | `IObservabilitySink` → 观测/报告/审计 |

### 2.6 反应式触发（核心机制：写入即触发）

**数据写入的过程本身就是触发**（write-as-trigger）——一次数据写入 = 原子提交 = 触发信号。触发与观测**同源**（同一行既是事实记录又是事件），**无第二通道**；触发不是「轮询/检测写入」，而是写入这一动作本身即提交即发布。

```
写入者 → 写输入区：提交即发布 = 触发信号（写与触发同一步，无二次检测）
  → 反应式订阅者被唤醒 → 转调 AgentOrchestrator.RunAsync(userInput, ct, threadId, inputKey)  // 复用现有 API（ReactiveRunHandler 委托）
  → AxiomLoopResult → 写循环内输出组件（写即触发，链式反应）→ 框架 PersistencePort 落输出区
  → 写回本身就是下一个写入 → 触发下一个消费方（链式反应）
```

**现成实现**（`Agent.Core/DataStream/`）：
- `ReactiveZoneBridge`：把 `IZoneStore`（持久化端口）与 `ComponentDataStore`（ECS 数据本体 + "写入即触发"信号源）桥接——写输入 = ①落库 + ②镜像进 ComponentDataStore，`Set(input.*/output.*)` 即提交即发布 `DataChanged`。
- `ReactiveTrigger`：订阅 zone 写信号，push 无轮询，转调 `AgentOrchestrator.RunAsync` 并把结果写回输出区；InputKey 幂等 + 启动回填 + 有界并发。

四条设计要点：**原子性**（事件行单事务提交，提交即发布）、**同源**（触发与观测共享同一事件行，低延迟与持久化天然合一）、**可重放**（失败保留事件行 + 状态，依行重放，配幂等键去重）、**写回数据库 = 框架决定，非 AI 决定**（AI 只写循环内 ECS 组件，落库由框架在 commit/checkpoint 边界经 `PersistencePort` 确定性执行，对齐 `DeterministicCoreDisjoint`）。

### 2.7 数据流观测体系（以数据替代日志）

- **现状问题**：观测全靠 `AgentLog.Log`（数十处埋点），读日志文本，不可查询/关联/聚合。
- **方案**：新增观测端口 `IObservabilitySink.Emit(RunEvent)`，把日志点升级为**事件行**——`RunEvent{ ThreadId, SessionId, RoundId, LeaseId, EventType, Phase(execute|next|budgetTerminate), Stage, PayloadJson, CreatedAt, Version }`，经 `EventSink`（有界通道 + 后台批写）异步落事件区（物理 `ur_nodes` NodeType=Event，逻辑 `ur_events`）。
- **可观测视角全部是数据查询**：时间线（thread/session/round 完整事件序列）、单轮报告（execute→next 全阶段耗时/决策词/评估/token 用量）、决策迹（TS/TO/TM、switch_strategy 因果链）、效率指标（每轮延迟/token 成本直接 SQL 聚合）。
- **兜底**：`AgentLog` 保留为 DB 写入失败时的 fallback，不再是观测源。

### 2.8 动态更新维护（数据驱动）

1. Schema 幂等迁移（`SqliteSchema.InitializeAsync` / `SqliteNodeStore.MigrateLegacySchema`）；图库投影 schema 演进时可全量重建。
2. **配置/策略即数据行**：`IConfigStore` + `NodeConfigStore`（逻辑 `ur_config`，物理 `ur_nodes` NodeType=Config），阈值、`ExecutorHint`、PromptPolicy、熔断参数做成行 + `LoopConfigHotReload` 每轮热读——改数据库即改框架行为，无需部署。
3. 契约驱动扩展：新增数据/事件 = 注册契约 + 迁移，生产/消费方零改动。
4. 治理回流：观测数据（效率指标）反向驱动，把「某策略 token 成本高」写回配置/策略建议表，形成「数据→行为」闭环（P4 计划）。

### 2.9 外部 API 数据化适配器

| 现有执行器 | 数据化门面 |
|------------|------------|
| Cline（`ClineAgentExecutor`） | 输入区写入任务 → 触发 → 结果写输出区；内部复用 `IAgentExecutor` |
| Guardrails（`guard_input/output`） | 输入/输出区的安全过滤适配器 |
| MCP（`mcp_call_tool/list_tools`） | 工具调用/列表的数据适配器 |
| 观测（`AgentLog`） | `IObservabilitySink` 事件化 |

**不做**：删除或绕过现有 API；执行器仍是底层，只是外部经数据区间接触发。

### 2.10 实施状态（P0→P4）

| 阶段 | 内容 | 状态 |
|------|------|------|
| P0 数据驱动基底 | `CsharpPrj/Operation/DataStoreDriven`（Agent 无关使用层：IDataStore/IPersistencePort/IZoneStore/IConfigStore + 节点库/键映射/引用维护端口族 + Sqlite/Graph/Local 后端 + 工厂）+ `Agent.Runtime/Persistence/DataStoreEngine`（内存 ⇄ IDataStore 落盘） | ✅ 已实现 |
| P1 数据区 + 反应式触发 | `ReactiveTrigger` + `ReactiveZoneBridge`（ECS 写即触发信号源，push 无轮询 + InputKey 幂等 + 启动回填 + 有界并发）→ 转调 `AgentOrchestrator.RunAsync` → 写回输出区 | ✅ 已实现 |
| P2 事件化观测 | `IObservabilitySink` + `EventSink`（有界通道 + 批写 → 事件区）；run 级事件收敛至 `AgentOrchestrator.RunAsync` | ✅ 已实现 |
| P3 数据驱动维护 | `IConfigStore` + `NodeConfigStore`（逻辑 `ur_config`）+ `LoopConfigHotReload` 每轮热读，改行即生效 | ✅ 已实现 |
| P4 效率治理 | KPI 采集、保留/压缩、读模型、治理回流 | ○ 未开工（按设计延后） |

---

## 三、核心数据模型：任务 = 快照（单一状态基座）

> 来自《状态快照调用链》。回答「状态存哪里、怎么存」。

### 3.1 核心重构：任务与快照合并

关键重构（提交 `de6bd25be`）：把「**任务**」与「**快照**」合并为**同一实体**。重构前以 `SchedulerState` 为唯一持久根、`AxiomContext` 每轮派生存根、走 `RestoreSchedulerStateAsync` 恢复路径；重构后 **`AgentTaskItem` 同时实现 `ITaskPoolItem + ILoopState + IAgentLoopState`**（另有辅助门面 `IStateFacet` / `IStatusAwareTaskPoolItem`），成为唯一权威状态根——**「修改任务 = 修改快照」**，单条 `PayloadJson` 即唯一权威状态基座（重启恢复 / 状态查询 / 投影均由此字符串派生）。

### 3.2 快照体系（两条入口）

**① 任务快照（主路径，单一状态基座）**：`AgentTaskItem`（`Agent.Core/TaskPool/`）
- **载荷**：TaskKey / Priority / MaxRetries / UserInput / ThreadId / InputKey / SchedulingMode / SchedulerRulesKey / ExecutorId（显式执行器）/ AnswerSpec（无状态回答参数）
- **运行时契约（`ILoopState`）**：Budget / Token / Flag / Round / LastEval / LastResultText + `DataSpace`
- **生命周期（`Status`）** + **Agent 扩展**：Scope / ContinuousFailures / CurrentStrategyId / AvailableStrategyIds / `AgentStatus` / RecentTransitions
- 投影内聚于状态类自身：`ToRuntimeSnapshot(stage)` 为唯一投影入口（`SchedulerStateProjector` 已随重构移除，彻底消除「双快照并存」缺陷）。

**② 租约快照（每轮派生）**：`SnapshotSerializer`（`Agent.Runtime/Persistence/Core/`）保留 `AxiomContext` ↔ `Snapshot` DTO 序列化，仅测试/遗留路径使用；主持久化路径为任务快照 `PayloadJson`（§3.3）。`AxiomContext` 本轮不再是持久根，仅作**每轮派生执行租约**（共享 Data/Budget/Token/Flag 引用 + 播种标量 Round/LastEval/Scope）。

### 3.3 持久化

`AgentRunner.PersistRuntimeAsync` → `ITaskPoolPersistence<AgentTaskItem>.UpdatePayloadAsync(TaskKey, state)`，写回 `PayloadJson`（每轮 `next()` 边界）——唯一持久根，避免双持久根。

---

## 四、主干道：一次运行的单一控制流

> 来自《Agent 主干道》。回答「一次 Agent 运行，代码怎么从头走到尾」。

### 4.1 为什么需要一条主干道

重构前，主循环逻辑散落在一个**约 80 行的跳转式大方法**里，真实数据流被三处机制打散：**隐式 AsyncLocal 透传**（token/budget/taskKey 等业务状态藏在 Functor 里，方法签名不体现）、**双执行路径**（LLM 与零 LLM 各自装配决策，主干在入口分叉）、**可变组合根**（`AgentRunner` 暴露运行期可变 setter 属性，装配态与运行态混在一起）。收敛为一条显式主干道后，主控制流 = `RunMainPipelineAsync`，五阶段从头读到尾即懂全部流转。

### 4.2 五阶段总览

```
RunAsync(input)
 ├─[A] LoadState            装载/播种单根状态（AgentTaskItem）
 ├─[B] BuildRunEnvironment  装配运行环境（决策枢纽 + 调度器 + 取消源 + run 作用域 + hooks）
 ├─[C] RunMainLoop          循环主干（SnapshotDrivenLoopEngine 唯一 while）→ 每轮 RoundLoopDriver → RoundLifecycle 五步
 ├─[E] FinalizeRunAsync     终止收敛 + 终态发布
 └─[F]                      边界持久化（PayloadJson 落库）+ 完成日志
```

| 阶段 | 方法 | 职责 | 关键产物 |
|------|------|------|----------|
| [A] 装载 | `LoadState` | 校验输入、播种默认策略集、同步执行器选择视图 | 就绪的 `AgentTaskItem`（单根状态） |
| [B] 装配 | `BuildRunEnvironment` | 启动日志、发布 started、建决策枢纽/调度器/取消源/run 作用域、构造 `RunContext`（单一状态容器）+ hooks 注入 | `AgentRunEnvironment`（一次性，using 持有） |
| [C] 循环 | `RunMainLoopAsync` | 快照驱动引擎唯一 while：单轮五步 + 轮后混合调度/发布/持久化 | `AxiomLoopResult` |
| [E] 收敛 | `FinalizeRunAsync` | 终止收敛（引擎承载终值）、终态发布 | 最终决策 |
| [F] 终态 | `PersistRuntimeAsync` | 唯一持久根写回 `PayloadJson`、完成日志 | 落库结果 |

### 4.3 阶段要点

**[A] 装载**：校验输入（user_input 非空）→ `SeedStrategyDefaults`（幂等，统一播种默认策略集）→ `SyncActiveStrategyView`。单根原则（AX.StateRootSingleSource）：`AgentTaskItem` 既是循环 TState 也是唯一持久根；`AxiomContext` 仅作每轮派生的执行租约。

**[B] 装配**：`DecisionPoint`（唯一决策枢纽，纯函数化，`EvaluateRound(lease,...)` 显式传参）→ 调度器按任务 `SchedulingMode` 选导演：Hybrid→`LlmStateDirector`（LLM 提议）/ Config→`ConfigStateDirector`（ur_config 规则集，零 LLM）/ Deterministic→`NoopStateDirector`（纯规则迭代，零 LLM），三者均经 `TransitionGuard` + `ApplyGuarded` 校验施加，落在同一混合调度框架内；`AiSchedulerStateMachine` 承载。业务状态（SchedulingMode/RulesKey）由 `AgentRunEnvironment.State` **显式承载**，不写入 AsyncLocal（消除死通道）。**状态容器单一化**：构造 `RunContext`（唯一可变持有者，承载取消令牌/预算/任务键/取消检查/LLM 门），装配 `hooks?.CancellationCheck` / `hooks?.LlmGate`，经 `RunContext.Enter` 置为 `RunContext.Current`（AsyncLocal 兜底透传），供深层执行端只读桥读取。

**[C] 循环**：`RoundLoopDriver.RunSingleRootAsync`（executeStep=五步管线 / evaluate=DecisionPoint / onRoundCompleted=NextHybridRoundAsync）。**循环驱动分层（自下而上，单一载体）**：

| 层 | 载体 | 职责 |
|----|------|------|
| 引擎 | `SnapshotDrivenLoopEngine<TState,TResult>` | 唯一 while 引擎（非限定快照驱动） |
| 驱动 | `LoopDriver`（Agent.Operation） | lastDecision 闭包 + 预算终止收敛 + 结果收敛，TState 解耦 |
| 单根包裹 | `RoundLoopDriver`（Agent.Core） | `AgentTaskItem` 专属薄外观：租约派生/收割 + 五步包裹 + 结果转换 |
| 五步 | `RoundLifecycle`（WorkFlowMethod） | Execute→Collect→Evaluate→Decide→Adjust 单一载体 |

> LLM 路径与零 LLM 规则路径**共用** `RoundLoopDriver` + `RoundLifecycle`（消除两套循环脚手架），区别仅在单轮动作 `executeStep` 与评估来源。

**单轮五步（RoundLifecycle）**：
1. **①Execute**：执行单轮动作（骨架五步管线 pre→lease→run→post→wrap，产出 `AxiomActionResult`）；
2. **②Collect**：归一错误分类（显式优先，否则按文本分类）；
3. **③Evaluate**：进度（`ProgressUpdater`）与熔断（`BudgetEscalator`）分别更新 `ProgressToken` / `FailureBudget`；
4. **④Decide**：终止优先（`TerminationDecider`：halt / token 耗尽 → 预算终止）；否则成功即 TS、未成功则 Continue；
5. **⑤Adjust**：推进轮次号、生成下一步提示。

**轮后钩子（NextHybridRoundAsync）**：混合调度（`env.Scheduler.AdvanceAsync`，只改策略/执行状态、不触碰确定性核心，回写 ActiveStrategy/ExplicitExecutor）→ 运行时发布（`ToRuntimeSnapshot("running")`）→ 边界持久化（`PersistRuntimeAsync` 写回 PayloadJson）。

**[E][F] 收敛 + 终态**：终止二分（AX.TerminationBipartition）——预算终止（TS/TO/TM，可判定）由引擎内裁决，探索终止（不可判定）外置为取消令牌；终态发布 `"completed"` → `PersistRuntimeAsync` → 完成日志。**唯一持久根 = `AgentTaskItem`**（不再持久化 `AxiomContext` 快照，避免双持久根）。

### 4.4 关键载体

**① `AgentRunEnvironment`**（一次 run 的 [B]→[F] 共享环境，`using` 持有、`Dispose` 释放取消源与 run 作用域）：

| 成员 | 类型 | 职责 |
|------|------|------|
| `State` | `AgentTaskItem` | 单根状态（唯一持久根；含业务状态 SchedulingMode/RulesKey） |
| `DecisionPoint` | `DecisionPoint` | 决策枢纽（主循环唯一决策点） |
| `Scheduler` | `AiSchedulerStateMachine<AgentTaskItem>` | 混合调度状态机（LLM + 计算；路由/策略单一裁决） |
| `Cts` | `CancellationTokenSource` | 取消源（超时 + 外部令牌） |
| `Scope` | `IFunctorScope` | run 作用域（AsyncLocal 兜底透传载体） |
| `Hooks` | `AgentRunHooks?` | 运行期注入（取消检查 / LLM 门 / 任务池持久化） |
| `RunContext` | `RunContext` | run 作用域横切状态单一容器（取消令牌/预算/任务键/取消检查/LLM门；收敛 AgentRunCancellation 静态副作用） |
| `RunScope` | `IDisposable` | run 作用域进入令牌（`RunContext.Enter` 返回；`Dispose` 恢复先前 `RunContext.Current`） |
| `RunToken` | `CancellationToken` | 取消令牌（探索终止信号之一） |

**② `AgentRunHooks`**（运行期注入，显式参数）：`record AgentRunHooks(CancellationCheck, LlmGate, TaskPoolPersistence)`。装配态（`AgentRunnerAssemblyInput`）与运行态分离，`AgentRunner` 实例装配后完全不可变；`null = 未注入`，对应 no-op（直跑 / 非任务池驱动）。

**③ `AgentRunCancellation`**（run 作用域横切量**退化只读端口**）：状态唯一可变持有者已收敛至 `RunContext`（AgentRunner 在 [B] 构造并经 `RunContext.Enter` 置为 `Current`）；本类不再有任何静态 `Set*` 副作用，全部读取（取消令牌 / 整体预算 / 任务键 / 取消检查 / LLM 门 / 调度导演上下文）委托 `RunContext.Current`（AsyncLocal 兜底透传）。**已消除**：`SchedulingMode` / `SchedulerRulesKey` 原为 AsyncLocal 死通道（无消费方），由主干显式承载。

**④ 调用链聚拢整合**（对应《LLM 调用链与状态集中化分析.md》）：
- **客户端单一来源**：`SemanticKernelLlmClient` 删除 `BindInstance` 静态门面（及自建静态 Kernel/`Initialize`），`Instance` 改经 `LlmClientFactory`（Runtime 唯一注册表）解析；组合根 `LlmHosting` 一次性装配注册，消除 SK 双客户端入口。
- **门控分发聚拢**：`GlobalLlmGate` 删除静态 `Instance`；`GlobalLlmGateHost` 持有门控权威根（组合根经 `Initialize` 注册），`GlobalLlmGateFilter` 缺省走 `GlobalLlmGateHost.Gate`，所有 LLM 入口经其单一裁决。
- **转发去重**：`RunJsonAsync` 的 relations/graph 分支直接调 Guard 服务（`AgentUiHost.ReferenceGrounding`），移除 Runtime→`AgentUiLibraryData`→Guard 双层转发，守卫保持纯服务。

### 4.5 三条数据流（主干道如何"显式"）

| 数据 | 承载方式 | 说明 |
|------|----------|------|
| **业务状态** | `AgentRunEnvironment.State`（`AgentTaskItem`）显式承载 | SchedulingMode / RulesKey / token / budget / 策略等随主干阶段显式传递，不藏 AsyncLocal |
| **横切量** | `RunContext`（唯一可变持有者，经 `RunContext.Current` 兜底透传） | 取消令牌 / 预算 / 任务键 / 取消检查 / LLM 门 / 调度上下文，由 `AgentRunCancellation` 只读端口委托读取，跨深层调用链免参读取 |
| **运行期注入** | `AgentRunHooks`（RunAsync 显式参数） | 取消检查 / LLM 门 / 任务池持久化，随 env 在 [B] 注入（装配进 `RunContext`） |

三条流各归其位：**业务状态显式、横切量最小化透传、运行期注入显式参数**——这是主干道「从代码可读」的关键。

---

## 五、执行链路：任务池 → 调度循环 → 执行

> 来自《任务池调度循环执行链》（v1.3）。回答「任务怎么进来、循环怎么转、代码怎么执行」。

### 5.1 外部入口层（即时指令）

`EnableMent\交互启动.bat`（管道 JSON → FILEDRIVEN_INSTANT_BODY → `运行测试GUI模式.ps1` Raw-Body 判别）→ `AgentUi_RunJson` 合成单命令 → `TaskPoolEnqueueAsync` 写 `ur_tasks`。`AgentUi_RunJson` 为**单一 JSON 入口**：`action` 判别式分发（runLoop / task / resource 系 / reference 系）；`task` 内 `op` 分流（enqueue / status / delete / submit）；enqueue 写 `ur_tasks` 即激活消费。

### 5.2 任务池层（持久化排队 + 写即激活 + 可重放）

**目的**：把「创建任务」与「执行任务」解耦，让任务在数据库中长期存活、可排队、可重启回填、可全局暂停。

```
写 ur_tasks（EnqueueAsync，TaskKey 幂等）      // 创建任务
   │ 状态 NotStarted + 唤醒消费循环（写即激活）
   ▼
TaskPool 消费循环（Share 层状态机，MaxConcurrency 默认 1）
   │ 按 Priority 出队 → Running → UpdateStatus
   ▼
AgentTaskPoolAdapter.handler → TaskRouter 路由选执行器 → 执行
```

| 组件 | 位置 | 职责 |
|------|------|------|
| `SqliteTaskPoolStore<TItem>` | `Operation/DataStoreDriven/Sqlite/` | `ur_tasks` 表持久化，实现 `ITaskPoolPersistence`；TaskKey 幂等 / Status 镜像 / PayloadJson 载荷，重启回填 |
| `AgentTaskItem` | `Agent.Core/TaskPool/` | 任务载荷；实现 `ILoopState`+`IAgentLoopState`（任务=快照，§三） |
| `AgentRuleBackend` | `Agent.Core/TaskPool/` | 后台规则驱动（`IRuleBackend<AgentTaskItem>`）：Hybrid→null(需LLM) / Config·Deterministic→规则解析(零LLM) |
| `AgentTaskPoolAdapter` | `Agent.Core/TaskPool/` | 通用 `TaskPool` ⇄ 执行器接线；`Resolve` 按 `TaskRouter` 路由选执行器；`EnqueueAndWaitAsync` 入队即自动启动并取回 `AxiomLoopResult`；对外门面（Enqueue/EnqueueAndWait/Pause/Resume/PauseAll/ResumeAll/Status） |

> 任务池**默认启用**（`AgentConfig.EnableTaskPool=true`），`AgentHost.RunAsync` 直跑也统一走任务池（`EnqueueAndWaitAsync`）；显式关闭时回退编排器直跑。依赖方向单向无环：`Share.TaskStatusPool` → `Operation.DataStoreDriven`（`ur_tasks`）→ `Agent.Core` → `Agent.Launcher`。

### 5.3 任务路由（明确「需 LLM vs 零 LLM」，纯判定）

**目的**：让任务池在消费时按代码逻辑明确区分「该任务要不要接入 LLM」，而非一律假设 LLM。由「任务自主标记」与「后台规则驱动」**结合**裁决（`Agent.Operation/Routing/TaskRouter`）：

```
路由（按代码逻辑转换，规则优先）：
  ① 后台规则驱动命中（AgentRuleBackend：Config/Deterministic）→ RuleDriven（零 LLM）
  ② 否则按任务自主标记回退（SchedulingModeAutonomy：Hybrid=需LLM / Config·Deterministic=非自主）→ LlmDriven / RuleDriven(纯确定性)
```

**接线**：`AgentTaskPoolAdapter.Resolve` 与 `AgentRunner` 导演选择均按 `TaskRouter.Route(item, SchedulingMode, AgentRuleBackend)` 的 `Disposition` 分流——需 LLM 走 `LlmTaskExecutor`/`LlmStateDirector`；零 LLM 走 `RuleTaskExecutor`/（`ConfigStateDirector` 或 `NoopStateDirector`）。`Deterministic`（非自主）归零 LLM 规则/非自主路径。

### 5.4 调度循环层（快照驱动 + 混合调度 + 终止二分）

```
AgentOrchestrator.RunAsyncCore → AgentRunner.RunAsync → RunMainPipelineAsync（唯一主干道，见 §四）
  [A] LoadState → [B] BuildRunEnvironment → [C] RunMainLoopAsync → [E][F] FinalizeRunAsync + PersistRuntimeAsync
  [C] 内部（SnapshotDrivenLoopEngine 唯一 while）：
        while (外部未终止):
          [A] budgetTerminate(state)        // 预算终止（TO/TM/TS，引擎内可判定）
          [B] execute(state) = ExecuteHybridRoundAsync（五步编排管线）
          [C] next(state, result) = NextHybridRoundAsync（确定性 + 收割 + 混合调度 + 回写策略）
```

**每轮状态转移（`NextHybridRoundAsync`）四步**：
1. **确定性计算**：错误分类 → 评估 → 进度/熔断 → halt → 轮次 +1 → TS 判定；
2. **单根收割**：`HarvestDeterministicCore` 把确定性核心从租约回写根 `AgentTaskItem`；
3. **混合调度**：`scheduler.AdvanceAsync` = 导演提议 + `TransitionGuard`（计算守卫）→ 原子施加（Held/Changed/Reject）；失败 → fail-safe stay/continue；
4. **回写策略**：SwitchStrategy 且 Applied 时回写 `state.Data[ActiveStrategy]`；调度显式改派执行器时回写 `ExplicitExecutor`。

> 循环内部（五阶段、混合调度、单轮五步、导演按路由选择）详见 §4.3——本层与主干道共享同一载体，此处仅列任务池视角的入口与桥接。

**与任务池的桥接（暂停门）**：暂停门已下沉到 **LLM 调用点**——`cline_execute` 在真实 LLM 调用前经 `AgentLoopFunction.CheckLlmGateAsync`（= `taskPool.WaitIfPausedAsync(taskKey)`，合作式暂停，Running ⇄ Paused 非终止）挂起；`guard_input/provide_prompt` 等确定性前置步骤照常执行并可观测，仅在 LLM 调用前阻塞。任务键经 `RunContext`（单一状态容器，`SetTaskKey/GetTaskKey`，AsyncLocal 兜底透传）承载；暂停阻塞期间 `ClearTaskKey` 断键防自死锁/防 key 残留，恢复后回填到同一 `RunContext` 实例。

### 5.5 执行层（声明式骨架 + 快照消费驱动执行器）

**目的**：把每轮工作拆成声明式骨架五步，按当前策略**动态解析执行器**（executor-agnostic，Cline 只是其中一种状态），使执行后端可替换、无需改骨架。

```
[run_round] _runtime.PlanRound(activeStrategy) → RoundPlan(Executor)   // 快照消费驱动：执行器解析
   → ctx.State.Data[RoundKeys.Executor] = plan.Executor
   → WorkflowRunner.RunRoundAsync（骨架五步）：
       [Step 1] guard_input        → 输入安全过滤
       [Step 2] provide_prompt     → L0+L1+L2 提示词装配（首步生成/继续步重规划）
       [Step 3] cline_execute      → ResolveExecutor → executor.ExecuteAsync(request)
       [Step 4] rule_match         → 结构化信号优先 + 文本规则兜底（零 LLM）
       [Step 5] guard_output       → （条件：DecisionWordMapper.IsSuccess，stop 终态脱敏）
   → RoundStateMapper.Harvest + BindHarvestPhase + ClearTransient   // 轮结束隔离
```

**工作流拓扑**（`agent-loop.json` 声明式主路径）：

```
start → guard_input → provide_prompt → cline_execute → rule_match
                                                        ├── stop → guard_output → end
                                                        └── continue →（引擎继续下一轮，回到 provide_prompt）
```

### 5.6 确定性动作层（PsRuntimeBinding）与标准存储

**目的**：把执行器（Cline 等）在骨架执行内的**确定性文件操作与落库**收敛到统一标准化动作层——一律 `Invoke-AgentStep -Action <verb.object> -Params @{}`，返回 `{status,message,data,errors}` 信封；读到的有价值文件 / 分析结论经**单一 C# 写入口**写入标准存储。

```
执行器（Cline）确定性操作 → 统一调用 Invoke-AgentStep -Action <verb.object> -Params @{}
  → PsRuntimeBinding（薄绑定，加载 PsRuntime + Config/ps1-runtime.json；注入存储桥配置；组合 read→store）
  → 标准化动作集（BasicMethod 契约，零 LLM，确定性）
       read.file / write.file / replace.file.batch / resolve.path（已有）
       list.dir / store.resource / read.file(-Persist)（新增）
  → 存储桥（单一 C# 写入口）：store.resource / read.file-Persist 委托 Agent.StorageBridge.exe
       → IResourceReferenceStore.AddResourceAsync → 标准存储（LiteDB 图库端点缓存；实体事实在 SQL）
```

> **存储分工**（对齐三库收敛，无独立 SQL `ur_resources` 表）：
> - **标准存储 resource**：确定性产物（分析结论 `analysis:<folder>`、读到的有价值文件 `file:<path>`），经 `IResourceReferenceStore`（`ReferenceGraphCoordinator`）落 LiteDB `graph-log.db` 端点符号/metadata（实体事实在 SQL），是「写即持久化」的引用图端点来源。
> - **输出区 ur_outputs**：主循环每轮执行结果（decision/rounds/duration），物理落 `ur_nodes`（NodeType=Output）。
> 两者不混用（storage-template.dsl.md `resource` 模板 → 标准存储）。

### 5.7 错误处理与可观测性（管道防阻塞 + 卡死可定位）

**目的**：外部入口经管道投喂时无 TTY（stdin 被重定向），任何 `Read-Host` 等待按键都会**无限阻塞**且不抛错；且 Launcher 以 WinExe 静默运行，函数库缺失/执行异常无回显。

```
管道模式防阻塞（PS 层）
   Test-PipeMode（FILEDRIVEN_INSTANT_BODY 非空 ∨ stdin 被重定向）
     ├─ Get-RunMode 强制返回 auto（覆盖 manual 的 Read-Host）
     ├─ ScriptRunner.Invoke 手动暂停块跳过
     └─ ShowInteractive 直接跳过（交互菜单依赖 Read-Host）
   ↓
行级诊断日志（PS 层）：Write-LauncherLog → %TEMP%\fileDriven_launcher.log
   （启动/Raw-Body 判别/合成脚本/Launcher PID/管道连接/卡死强杀/退出码）
   ↓
函数库加载失败显式化（C# 层）：FrameworkInitializer → FunctionLibraryLoader.Load 结果 failed.Count>0
   ├─ 写 %TEMP%\FileDriven_FunctionLibFallback_<时间>.log（失败库名/错误/DLL 检查指引）
   └─ 抛 InitializationException（阻断启动，输出 fallback 路径）
```

| 手段 | 位置 | 职责 |
|------|------|------|
| `Test-PipeMode` | `FileDrivenTestUtils.ps1` | 管道模式统一判定（环境变量 + `[Console]::IsInputRedirected` 双信号） |
| `Get-RunMode` 管道强制 auto | 同上 | manual 的 `Read-Host` 在管道下无 TTY → 强制 auto 跳过所有手动暂停 |
| `Write-LauncherLog` | `运行测试GUI模式.ps1` | 行级诊断日志（带时间戳追加，共享 `%TEMP%\fileDriven_launcher.log`），卡死时保留最后轨迹 |
| 管道空闲超时收紧（30s，管道模式） | 同上 | 管道模式无 TTY 无法人工干预，长时间无输出按卡死处理（120s→30s）；卡死时写日志并强杀 Launcher |
| 函数库失败 fallback + 抛异常 | `FrameworkInitializer.cs` | `AgentUiLibrary` 等加载失败不再静默：写 fallback 日志 + 抛 `InitializationException` |

> **设计要点（SEP 对齐）**：错误处理属引擎执行侧可判定动作（`FailureBudgetEscalation`/`ErrorHandler`），不触碰确定性核心（`SEP.DeterministicCoreDisjoint`）；管道模式防阻塞为**入口层适配**（引擎循环本身无变化），符合 `handle(error, Scope)` 的 Scope 注入思想——管道输入作用域 `single_call` 下仅允许确定性重试，禁止交互等待。

### 5.8 生命周期与回写

每轮结果写回输出区 `ur_outputs`（decision/rounds/duration），失败保留错误行可重放；暂停门（`cline_execute` 前的 `CheckLlmGateAsync` 合作式挂起）见 §5.4。

| 能力 | API | 语义 |
|------|-----|------|
| 队列级全局暂停 | `PauseAllAsync / ResumeAllAsync` | 暂停全部活动任务（在途 + 排队），Running ⇄ Paused 非终止 |
| 单任务暂停 | `PauseAsync / ResumeAsync` | 在途（Running→Paused，合作式挂起）与排队（NotStarted→Paused） |
| 重启回填 | `ProcessPendingAsync` | 死 `Running` → `NotStarted` 重放续跑；`Paused` 保持挂起 |

---

## 六、子系统专题：引用关系维护（图库）

> 来自《引用关系维护完善方案》。回答「图关系如何维护、如何主动构建、如何可重建」。**状态**：P0 收敛 + P1 声明式构建/重建/端点落盘已落地；P2 outbox 为计划项。

### 6.1 演进起点（原问题）

**① 维护逻辑分散**：原 7 个组件各管一段，最核心是**双写入口**——`AgentUiLibraryData`（`db.Resources`）直调 `ReferenceGraphCoordinator`，而写透路径（`WriteThrough*` + `ReferenceGrounding`）也写同一协调器；且 `ReferenceGrounding` 与 `ReferenceGraphCoordinator` 曾是**近乎相同的对账 diff 双实现**。**已收敛**：`ReferenceGrounding` 降级为纯编排守卫，diff 收敛到协调器 `ApplyRelationEdgesAsync`（SingleImplementation）；写透路径（`WriteThroughNodeStore` / `NodeExecutionService.PersistNodeReferencesAsync`）统一经守卫 `WriteRelationsAsync`；UI 的 resource/reference 手动增删走协调器幂等原语。

**② 脆弱点（演进中已修复/缓解）**：
1. 时序软约束：SQL 先写、协调器后同步边——由写透时序收敛到守卫单一端口缓解；
2. 镜像不落盘：`_endpoints` 为会话内存——**已修复**（§6.3.4：`ur_endpoints` 落盘 + 启动回填）；
3. 跨介质无原子：SQLite 与 LiteDB 无事务——对账兜底（Node/All 两级）；outbox 补强为 P2 计划（§6.3.5）；
4. 失败静默：镜像同步失败无补偿/重试/告警——outbox + 审计事件为 P2 计划（§6.3.5/§6.3.6）；
5. 悬空资源只报告不删除——保留诊断（`graph.status`/`graph.diff` 报告，§6.3.6）；
6. 运行时类型判断：`NodeExecutionService` 的 `is not WriteThroughNodeStore`——**已修复**（§6.3.7：装配期注入守卫）；

**③ 不支持主动写入构建**：关系只能被动派生、task 关联一次性、无声明 DSL——**已修复（G3 落地）**：`relations.declare/undeclare/list`、`task.updateRelations`（`ReconcileTaskAsync`）、`graph.rebuild` 均已实现（§6.3.3）。

### 6.2 完善目标（不变式）

| # | 目标 | 对齐原则 |
|---|------|----------|
| G1 | 单一守卫入口：所有关系写入（含 UI/存储桥/声明）经唯一守卫，协调器降级为纯执行者 | SingleImplementation |
| G2 | 权威在 SQL、图可重建：任意关系状态可从 ur_nodes / ur_tasks + 声明数据幂等重建 | 投影可重建 |
| G3 | 声明式构建：主动声明（预声明组合、增量维护任务关联、全量重建入口） | 主动不被动 |
| G4 | 跨介质不丢（P2）：outbox 模式补强，崩溃可回放 | 最终一致 + 至少一次投递 |
| G5 | 可观测：图健康检查 + 对账审计事件 | 数据即真相 / IObservabilitySink |

### 6.3 方案落地与现状

**6.3.1 收敛守卫入口（G1，✅ 已实现）**：唯一守卫 `ReferenceGrounding`（实现 `IReferenceMaintenance`，`Operation/DataStoreDriven/Core/`）。统一端口：`MirrorNodeAsync / EnsureNodeMirrorAsync / RemoveNodeMirrorAsync`、`EnsureTaskMirrorAsync / RemoveTaskMirrorAsync`（镜像维护）、`WriteRelationsAsync(wf, nodeKey, edges)`（权威边集写入，内部经 `NodeRelationDeriver` 推导 + 协调器 `ApplyRelationEdgesAsync` 幂等写边）、`DeclareRelationsAsync/UndeclareRelationsAsync/ListRelationsAsync`（主动声明）、`ReconcileAsync(scope)`（对账）、`RebuildAllAsync()`（全量重建）。`NodeExecutionService.PersistNodeReferencesAsync` 已删 `is not` 兜底分支，改由装配期注入守卫。协调器（`ReferenceGraphCoordinator`）降级为纯执行者——只提供原子原语（`ApplyRelationEdgesAsync` 删残留+补缺失、`ReconcileTaskAsync`、`AddResourceAsync/AddReferenceAsync` 幂等），无决策逻辑。

**6.3.2 消除重复对账（G1/G2，✅ 已实现）**：推导与权威集只留守卫侧（`NodeRelationDeriver` + 持久化 `RelationsJson`/声明记录）；协调器只做原子 apply，无 diff 决策。保留双实现对账的等价性测试作为回归护栏（`ReferenceGroundingWriteThroughTests`）。

**6.3.3 声明式关系构建（G3，✅ 已实现）**：
- **关系声明持久化**：新增节点类型 `NodeType=relation`（ur_nodes），`NodeKey = "relations.{workflowId}.{relationId}"`（幂等 upsert 依据），`PayloadJson = { Source{ResourceType,Id,Port}, Target{...}, RefType, KeyMappingRef, Status }`。声明即权威：撤销 = 更新 `Status=revoked`，守卫据此级联清边——**图库永不比 SQL 多出任何未声明的关系**。
- **主动声明 API（已实现为 `AgentUiLibraryData` action）**：

| 动作 | 入参 | 行为 |
|------|------|------|
| `relations.declare` | `relations[]`（JSON DSL） | 批量声明 → 写 ur_nodes（SQL 事务）→ 守卫同步图（幂等） |
| `relations.undeclare` | `relationIds[]` | 撤销声明 → 更新 Status / 删节点 → 级联清边 |
| `relations.list` | `workflowId?` | 列出全部声明（含状态） |
| `task.updateRelations` | `taskKey` + `nodeKeys[]` | **增量维护**任务关联（`ReconcileTaskAsync`，替代 enqueue 一次性 relatedNodes） |
| `graph.rebuild` | — | **全量重建入口**：清空图库 → 从 SQL 重建（业务节点镜像 + task 镜像 + keymapping 资源 + 声明关系边 + 执行推导边），幂等可重复 |
| `graph.status` / `graph.diff` | — | 图健康概览（端点数/边数/悬空资源）+ SQL 权威边集 vs 图边集差异报告（G5 概要版） |

> `graph.rebuild` 把「投影可重建」从愿景变成可调用能力：崩溃/脏数据/版本迁移后，一键让图收敛到 SQL 权威状态。

- **预声明组合 DSL**（不依赖执行溯源直接建边，跨轮复用）：显式声明「节点 B 消费节点 A 的 output 端口」「keymapping 逻辑绑定节点」。**冲突裁决**：对账/重建时权威边集 = **声明边 ∪ 推导边**（声明优先，推导不覆盖声明）。

**6.3.4 端点符号落盘 + 启动回填（✅ 已实现，修复「重启丢符号」）**：LiteDB 新增 `ur_endpoints` 集合（`{Key, ResourceType, Id, Symbol, Metadata, UpdatedAt}`）；`Initialize()` 从 `ur_endpoints` + 边重建内存邻接（替代裸造资源）；`RebuildAllAsync` 从 SQL 回填 symbol/metadata。保持「端点镜像非权威」语义（可重建缓存，权威仍在 SQL）。

**6.3.5 跨介质 outbox 补强（G4，○ P2 计划）**：SQL 事务内写 ur_nodes/ur_tasks/声明 + 写 `ur_relation_outbox(action, relationId, state=Pending)` → 后台同步器消费 → `ReferenceGraphCoordinator` 幂等应用 → 标记 Done（失败保留 Pending + 指数退避重试）；启动回放未完成 outbox → 图不丢（至少一次投递 + 幂等应用 = 恰好一次效果）。对账兜底保留为最终防线（对账是「校正」、outbox 是「不丢」，互补不互斥）。

**6.3.6 图健康检查与诊断（G5，✅ 概要版 / ○ 审计事件）**：`graph.status`（端点数/边数/孤立端点/悬空资源）、`graph.diff`（SQL 权威边集 vs 图边集差异报告）已实现；对账审计事件（每次 `ReconcileAsync`/`RebuildAllAsync` 结果写 `ur_events`）待 P2 补全。

**6.3.7 移除运行时类型判断（G1，✅ 已实现）**：`PersistNodeReferencesAsync` 不再做 `is not WriteThroughNodeStore` 判断——装配期把「图维护者」（守卫）显式注入节点执行服务，有守卫走守卫、无守卫跳过（二态由注入决定，不依赖具体装饰器类型）。

### 6.4 数据模型变更与实施状态

| 介质 | 变更 | 状态 |
|------|------|------|
| SQLite `ur_nodes` | NodeType ∈ {...} **+ `relation`**（声明关系节点，PayloadJson=声明记录） | ✅ 已实现 |
| LiteDB `graph-log.db` | **+ `ur_endpoints`**（端点符号落盘，可重建缓存） | ✅ 已实现 |
| SQLite | **+ `ur_relation_outbox`**（P2，关系同步日志） | ○ 计划 |

迁移兼容：旧数据无 `relation` 节点、无 outbox → `graph.rebuild` 走「纯推导」路径，行为与现状一致（向后兼容）。

| 阶段 | 内容 | 验收 | 状态 |
|------|------|------|------|
| P0 收敛 | 守卫统一端口；写透路径改走守卫；协调器 Reconcile 内部化；删 `is not` 判断 | 现有测试不回归；无重复对账实现 | ✅ 已实现 |
| P1 声明式构建 | `relations.declare/undeclare/list`、`task.updateRelations`、`graph.rebuild`、`NodeType=relation`、`ur_endpoints` 落盘 + 启动回填 | 声明→图可见、撤销→边清；rebuild 后 diff=0；重启符号保留 | ✅ 已实现 |
| P2 补强 | outbox 同步器 + 崩溃回放；`graph.status/diff` 审计事件 | 模拟镜像同步失败后重启，图不丢（outbox 回放）；diff 报告可读 | ○ 计划 |

每阶段纯增量、可回退（P0 收敛后 P1 未做时行为与现状等价）。

---

## 七、总结

**Agent = 愿景（数据库为中枢、写入即触发、叠加门面）× 结构（任务=快照的单一状态基座）× 流转（主干道 [A]→[F] 唯一控制流 + 任务池→路由→混合调度循环→骨架五步执行链路）× 子系统（引用关系维护收敛为单一守卫、声明可建、重建可跑、崩溃不丢）**。

- **数据怎么流**：写 ur_tasks 即激活 → TaskPool 消费 → `TaskRouter` 裁决（需 LLM/零 LLM）→ `SnapshotDrivenLoopEngine` 混合调度循环（每轮 execute 五步 + next 确定性计算/收割/调度/回写）→ `RunRound` 按当前策略解析执行器跑骨架五步 → 结果写回输出区 `ur_outputs`；确定性文件操作经 `Invoke-AgentStep`（PsRuntimeBinding）标准化动作执行 → 标准存储 resource（LiteDB 图库端点；与 ur_outputs 分工、不混用）。
- **状态怎么存**：`AgentTaskItem` = 任务 = 快照，单条 `PayloadJson` 即唯一权威状态基座；`AxiomContext` 仅作每轮派生租约；每轮 next 边界经 `PersistRuntimeAsync` 落库。
- **数据怎么落**：三库收敛——SQLite `ur_tasks` + `ur_nodes` 为实体唯一 truth（三区/配置/checkpoint/keymapping/声明均 NodeType 判别落 ur_nodes，无独立 `ur_config`/`ur_resources` 等表）；LiteDB `graph-log.db` 只存引用边 `ur_references` + 端点缓存 `ur_endpoints`；内存 `DataStoreEngine` + 热邻接为会话缓存。
- **控制流怎么读**：从 `RunMainPipelineAsync` 开始，五阶段 [A]→[F] 即完整流转；业务状态经 `AgentRunEnvironment` 显式承载，横切量收敛到 `RunContext` 单一状态容器（`RunContext.Current` 兜底透传 + `AgentRunCancellation` 只读端口），运行期注入经 `AgentRunHooks` 显式传入；决策经 `DecisionPoint` 单点、循环脚手架收敛到 `RoundLoopDriver` + `RoundLifecycle` 单一载体，LLM 与零 LLM 共用同一主干。
- **扩展与治理**：数据库与 Agent 解耦（PersistencePort 端口 + 使用层实现）；现有 API 全部保留为底座，数据层为可开关可回退的叠加门面；引用关系权威始终在 SQL，图库「声明可建、重建可跑、状态可查、崩溃不丢（P2 outbox）」。
