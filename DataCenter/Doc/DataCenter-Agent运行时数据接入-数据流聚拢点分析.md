# DataCenter · Agent 运行时数据接入 —— 数据流聚拢点分析与构建建议

> **适用**：`Frame\DataCenter` + `Projects\Plug\Agent`
> **版本**：v1（2026-09-12）
> **定位**：**治理型分析**（非新功能）。识别「数据流动节点」上"多条等价写/投影路径"的散点，给出**应构建为聚拢点的清单**、可执行判据与批次映射，使"改一处即改全链"在数据面同样成立。
> **与调用链聚拢点的分工**：`Projects\Plug\Agent\doc\Agent调用链集中化-聚拢点重构方案.md`（P1–P5）收敛的是**调用链**（LLM 取得 / 执行端构造 / 工具面装配 / 装配管线 / UI 解析）；本文收敛的是**数据流**（谁写同一事实、谁构造同一记录、谁发射同一事件）。两者**同构对偶、互不重叠**：调用链管"调谁"，本文管"写哪"。
>
> **权威边界（本文不复制既有权威）**：
> ① 缺陷事实（位置/证据/严重度/状态）→ `DataCenter-Agent运行时数据接入-调用链逻辑缺陷清单.md`
> ② 设计语义（§1 分类 / §3 通道 / §4 阶段 / §5 门禁）→ `DataCenter-Agent运行时数据接入-设计与实施.md`
> ③ 批次口径与判据命令 → `DataCenter-Agent运行时数据接入-第五轮收口实施计划.md`（W0–W6）
> ④ 归属台账（机读）→ `Projects\Plug\Agent\doc\data-ownership.json` + 人读 `Agent-DataOwnership.md`
> ⑤ 调用链聚拢点命名（P1–P5）→ 上述调用链方案，本文**不新起同职责命名**
>
> **通则**：不改真源（SQL/LiteDB）、不改 CSG 主 schema、不动落盘键；每个 DP 项独立可回退。

---

## 一、口径：什么算「数据流聚拢点」

**定义（本文口径）**：一个**数据流动节点**（一次"值/记录/事件"的产生、构造、落库或发射）构成聚拢点，当且仅当满足：

| # | 判据 | 含义 | 可执行形式 |
|---|---|---|---|
| **C1** | **单一写者** | 同一事实在任一时刻只有一个写路径 | 写目标注册唯一（`SingleWriteGuard.AssertSingleWriter`）/ 写入口 grep 计数 = 1 |
| **C2** | **单一构造** | 同一记录形态只有一个构造/编解码点 | `PayloadJson =` 赋值点计数 = 1（或全部经同一 Codec） |
| **C3** | **单一投影** | 派生面（缓存/审计/观测）只由真源单向派生，不反向成第二真源 | 派生写点不构成对上游的写；跨介质缺口对账 = 0 |
| **C4** | **单一声明** | 「哪类数据、归谁、落哪」有唯一登记点 | 台账 `data-ownership.json` 命中 / 域注册表完备 |

> **反模式判据（何时必须建聚拢点）**：同一事实出现 **≥2 条等价写路径**，或同一记录出现 **≥3 处构造点**，即触发（对应既有反模式 `ANTI-DUPLICATED-IMPLEMENTATION` / `Strategy.SingleImplementation`）。

**与四通道对齐**（通道定义权威 = 设计与实施 §3）：

| 通道 | 内容 | 本文涉及的 DP |
|---|---|---|
| **A** 状态 KV | 根状态真源分层 KV（Config/Session/Transient） | DP-3 |
| **B** 对象槽 | `root.Slots.Partition("agent")`（不可序列化对象） | DP-10（W1 已完成） |
| **C** 投影 / 观测 | 投影器 / 追踪面 / 事件流查询 | DP-5 / DP-6 |
| **D** 域真源 | SQLite `ur_nodes`/`ur_tasks`/`ur_histories`/`ur_outputs` + LiteDB `graph-log.db` | DP-1 / DP-2 / DP-4 / DP-7 / DP-8 / DP-9 |

---

## 二、数据流动节点全景（实测）

> 计数口径：`Projects\Plug\Agent\src` 下 `.cs`（排除 `obj/bin`）命中行数 / 文件数；实测 2026-09-12。

| # | 数据流动节点 | 载体 | 写入口 | 读入口 | 现状 |
|---|---|---|---|---|---|
| N1 | 节点输出端口写 | SQLite `ur_nodes`（`NodeType=node` + 内嵌 `Outputs`） | **3** | 2 | 🔴 有旁路 |
| N2 | 节点记录构造 / 编解码 | `NodeRecord.PayloadJson` | **9+** | 30 文件 | 🔴 构造点分散 |
| N3 | 跨轮状态 KV | 根状态真源（`AxiomContext.Data` / Transient `vars`） | **4 + 6** | 多 | 🔴 双写 |
| N4 | 任务快照 | SQLite `ur_tasks.PayloadJson` | **5** | 多 | 🟠 权威待唯一 |
| N5 | 轮决策记录 | `DecisionCenter`（Session 层）+ `DecisionRecorder` | **2 面** | 2 | 🟠 两面并存 |
| N6 | 观测事件 | `ur_events` / 内存 `RunEvent` / `events.jsonl` | **2 面（14 埋点 / N emit）** | 多 | 🟠 两面并存 |
| N7 | 会话历史 | SQLite `ur_histories` | **3** | 4 | 🟠 入口偏多 |
| N8 | 输出区（三区输出） | `ur_outputs` | **3** | 若干 | 🟡 低优先 |
| N9 | 引用边 | LiteDB `graph-log.db` | **1**（`ReferenceGraphCoordinator`） | 多 | ✅ 已聚拢 |
| N10 | KeyMapping | SQLite（`KeyMappingCategory`） | **1**（`NodeExecutionService:332/334`） | 若干 | ✅ 已聚拢 |
| N11 | 技能运行态快照 | 槽 `skills`（`SkillRuntimeState.Set`） | **1** | 多 | ✅ 已聚拢（W1） |
| N12 | 运行时槽对象 | 根分区 `agent/*`（29 键） | **1/键** | 多 | ✅ 已聚拢（W1） |
| N13 | 域别名 / Kind 声明 | `AgentStateDomain` | **1** | 1 | ✅ 已聚拢 |

**关键实测证据（file:line）**：

| 节点 | 写/构造点 |
|---|---|
| N1 | `Agent.Core\Nodes\NodeExecutionService.cs:325`（经 R3 写门）· `Agent.Core\Nodes\RoundStepInvoker.cs:315`（**直写，绕过写门**；调用方 `WorkflowRunner.cs:178/180/182`）· `Agent.Orchestration\SessionWorkflow\NodeWrite\NodeWritePipeline.cs:85` |
| N2 | `NodeExecutionService.cs:366`（`NodeOutputCodec.EncodeBusiness`）· `Agent.McpRegistryHost\MCP\LocalDbMcpTools.cs:120` · `Agent.Launcher\Modules\DatabaseModule.cs:98` · `Agent.Launcher\Modules\Libraries\AgentUiLibraryData.cs:62` · `Agent.StorageBridge\Storage\StorageBridgeCommands.cs:97/109/121/141` · `Agent.Launcher\Api\Dispatch\Handlers\AgentApiIntentSource.cs:265` · `Agent.Core\OutputZoneWriter.cs:21/40` |
| N3 | `Agent.Core\Loops\AgentRunner.Pipeline.cs:112`（`state.Data[Executor]`）· `Agent.Core\Nodes\RoundStateMapper.cs:124`（`Harvest` 二次收割）· `Agent.Core\Nodes\WorkflowRunner.cs:254/258`（`SessionId`/`ExecResult`）· `vars.Set`：`NodeExecutionService.cs:254` · `RoundStateMapper.cs:98` · `RoundStepInvoker.cs:187/199` · `WorkflowRunner.cs:140/154` |
| N4 | `Agent.Core\Loops\AgentRunner.Persistence.cs:136`（`PersistRuntimeAsync`，自述"单一状态基座"）· `Agent.Core\TaskPool\AgentTaskPool.cs:312` · `AgentApiIntentSource.cs:128` · `Agent.Orchestration\SessionWorkflow\ConversationCentralPipeline.cs:250` · `ConversationPipeline.cs:221` |
| N5 | `DecisionCenter.Record`：`AgentRunner.Persistence.cs:72`（唯一）· `DecisionRecorder.Default.Record` / `_recorder.Record`：`Agent.Contract\Llm\LocalLlmClient.cs:212/228/240/262/267` · `Agent.Modules\Invoke\Tool\DecisionActionExecutor.cs:41` · `Agent.Core\Loops\SemanticVerdictObservation.cs:37` · `Agent.Launcher\Composition\ConversationHosting.cs:485` |
| N6 | `AgentTrace.*`（14 命中 / 5 文件）：`Agent.Core\Loops\AgentRunner.cs:318/447/481` · `AgentRunner.Persistence.cs:71` · `Agent.Core\Nodes\RoundStepInvoker.cs:95/102/109/116` · `Agent.Launcher\Composition\AgentFramework.cs:107/121/124/127`；另 `_observabilitySink.Emit(RunEvent)`：`AgentRunner.Persistence.cs:143` · `Agent.Core\DataStream\EventSink.cs:51` · `Agent.Core\DataStream\ReactiveZoneBridge.cs:104/113` |
| N7 | `_history.UpsertAsync`：`AgentApiIntentSource.cs:85/108/123` · `ConversationPipeline.cs:224`；另 `NodeWritePipeline.CreateSessionAsync`（`ConversationPipeline.cs:214`） |
| N8 | `Agent.Core\TaskPool\AgentTaskPool.cs:490` · `Agent.Launcher\Modules\Libraries\AgentUiLibraryConversation.cs:297` · `Agent.Core\OutputZoneWriter.cs` |
| N9 | `Agent.Launcher\Composition\DatabaseBootstrapper.cs:154`（`ReferenceGraphCoordinator.CreateAsync`，唯一 owner） |

### 2.1 散点计分基线（实测 2026-09-12；**只允许下降**）

> 口径：`Projects\Plug\Agent\src` 下 `.cs`（排除 `obj/bin`）。**语义入口数**与**文本命中数**分列 —— 后者含接口声明/包装转发，不直接等于散点数。计数命令见 §六。

| 关键模式 | 文本命中 / 文件 | 语义入口数（本文判定） | 归属 DP |
|---|---|---|---|
| `WriteOutputAsync` | 8 / 5 | **3 条写路径**（1 经写门 + 1 旁路 + 1 会话侧） | DP-1 |
| `PayloadJson\s*=` | **17 / 11** | **9+ 处构造点**（其余为 ur_outputs/ur_events 侧） | DP-2 |
| `state\.Data\[[^\]]+\]\s*=` | **4 / 3** | **4**（无包装 ⇒ 全为真实直写） | DP-3 |
| `vars\.Set` | **6 / 4** | **6**（无包装） | DP-3 |
| `UpdatePayloadAsync` | 8 / 6 | **5 个调用点**（另 2 命中为接口声明 `ConversationStore.cs:35` + 包装 `AgentTaskPool.cs:387/388`） | DP-4 |
| `_recorder\.Record` / `DecisionRecorder\.Default\.Record` / `DecisionCenter\.Record` | **13 / 8** | **2 个记录面**（引擎侧 / 执行工具侧） | DP-5 |
| `AgentTrace\.` | **14 / 5** | **1 个埋点面**（vs 业务事件面，见 DP-6） | DP-6 |
| `UpsertAsync` | 6 / 4 | **3 个调用点**（`AgentApiIntentSource.cs:85/108/123`；另 1 为 `ConversationPipeline.cs:224`，2 命中为接口/实现） | DP-7 |

---

## 三、已成立的聚拢点（既有，**不得再造第二份**）

> 这些已是权威单点，本文只**登记**并在 §6 给出回归判据；任何新实现必须复用，不得平行登记。

| # | 聚拢点 | 位置 | 强制机制（既有） |
|---|---|---|---|
| **DP-0.1** | **确定性写门** | `Frame\CsharpPrj\Operation\HierarchyMethod\Center\WriteCoordinator.cs` | `SingleWriteGuard.AssertSingleWriter(target, writer)` 运行时 fail-fast；`NodeExecutionService.cs:60-61` 注册 `ur_nodes.persist` → `NodeExecutionService.PersistAsync` |
| **DP-0.2** | **库级单写通道** | `SqliteWriteChannel` / 按库有界写门 | `Check-SingleWriter.ps1`（A 无界进程写门 / B 读解耦 / C 事务经单写通道 / D 告警）EXIT 0 |
| **DP-0.3** | **边写唯一 owner** | `ReferenceGraphCoordinator`（`graph-log.db`） | 设计与实施 F3；`DatabaseBootstrapper.cs:154` 单点创建 |
| **DP-0.4** | **引用写门面** | `NodeRelationGateway`（权威边 + task 归属边） | `NodeExecutionService.PersistNodeReferencesAsync` 单一消费 |
| **DP-0.5** | **域别名/Kind 唯一登记** | `Agent.StateDomain.AgentStateDomain` | `Check-DataCenterGenericity` K5 未注册 Kind = 0 |
| **DP-0.6** | **取根通道唯一入口** | `Agent.Contract.Runtime.AgentRuntimeRoot` | `Check-DataCenterGenericity` K9 / D5；唯一白名单持有（W-DEC-3①） |
| **DP-0.7** | **槽键唯一权威** | `Agent.Contract.Runtime.AgentRuntimeSlotKeys`（29 键） | `Check-AgentRuntimeSlots` K10 违规 0 / 存储基线 0（W1 已归零） |
| **DP-0.8** | **归属声明唯一台账** | `data-ownership.json`（20 条） | `Check-AgentDataSeparation` D5 无主 = 0 |
| **DP-0.9** | **轮次决策唯一对外投影** | `DecisionCenter.Record`（scope=任务键） | F-66 分工：Agent 侧不接 `LoopStateStore` |

---

## 四、应构建的聚拢点（DP-1 … DP-8）

> 排序 = 优先级（风险 × 收益）。每项含：**目标 / 散点证据 / 收敛形态 / 判据 / 批次 / 风险**。

### DP-1 · 节点写通道（ur_nodes）—— 写路径收敛为 1（**P0**）

| 项 | 内容 |
|---|---|
| **目标** | `ur_nodes` 的全部写路径经 **DP-0.1 写门** + **同一 writer 名**，消除旁路 |
| **散点（3 条）** | ① `NodeExecutionService.cs:325`（`WriteNodeOutputsAsync`；**已经写门**，writer=`NodeExecutionService.PersistAsync`）② `RoundStepInvoker.cs:315`（`WriteNodeOutputAsync` → `_nodeOutputs.WriteOutputAsync` **直写，不经 `WriteCoordinator`**；调用方 `WorkflowRunner.cs:178/180/182` 写 `guard_output`/`cline_execute` 端口）③ `NodeWritePipeline.cs:85`（会话侧 `PersistAsync`） |
| **风险实质** | ②是**真实旁路**：与①写同一表同一事实（`ur_nodes` 输出端口），却不受 R3 串行写门保护 ⇒ 与 Outbox 后台/同步路径可并发；违反 `SEP.DeterministicCoreDisjoint`（确定性核心写路径单一） |
| **收敛形态** | `RoundStepInvoker.WriteNodeOutputAsync` 改为经同一 `WriteCoordinator`（注入而非自建）；`NodeWritePipeline` 的落点若与①同表，同样经写门；**保留** `INodeOutputStore` 端口（不新增实现） |
| **判据** | ① `SingleWriteGuard` 对 `ur_nodes.persist` 注册仍唯一（平行实现 → fail-fast）② `Check-SingleWriter` 违规 0 ③ 写路径 grep：`WriteOutputAsync` 经写门的比例 = 100%（新增断言：旁路调用点 = 0）④ `Agent.Core.Tests` / `Agent.Operation.Tests` 全绿 |
| **批次** | **W2-1 前置**（与 W2-1「单写 + 投影重算」同一热路径，建议同批判审） |
| **风险** | 中（触写热路径，但只增门不删路径 ⇒ 开关不需要） |

### DP-2 · 节点记录构造/编解码 —— 收敛为 1 个 Codec（**P0**）

| 项 | 内容 |
|---|---|
| **目标** | `NodeRecord.PayloadJson` 的构造只经**唯一 Codec**（`NodeOutputCodec`），其余调用方经其投影 |
| **散点（9+ 处赋值）** | `NodeExecutionService.cs:366`（`EncodeBusiness`，已合规）· `LocalDbMcpTools.cs:120` · `DatabaseModule.cs:98` · `AgentUiLibraryData.cs:62` · `StorageBridgeCommands.cs:97/109/121/141` · `AgentApiIntentSource.cs:265` · `OutputZoneWriter.cs:21/40`（ur_outputs） |
| **风险实质** | 形态漂移：F-63 残余已核（API `node.write` 缺省 `NodeType=output` + 载荷非 `NodeOutputDoc`，与执行链「单形 `NodeType=node` + `BusinessNodePayload`」不一致）⇒ 读侧 `EnumerateOutputsAsync` 读不回 |
| **收敛形态** | ① 形态唯一：`ur_nodes` 业务节点恒为 `NodeType=node` + `NodeOutputCodec.EncodeBusiness`；② `AgentApiIntentSource.ToRecord` 缺省形归位（**W-DEC-4①**）；③ `StorageBridge`/`DatabaseModule` 的直赋改经 Codec |
| **判据** | ① `PayloadJson =` 直赋点（不经 Codec）= **1**（仅 Codec 内部）② F-63 形态断言：写入记录可被读侧枚举读回 ③ `Check-DataContracts` / `Agent.Persistence.Tests` 全绿 |
| **批次** | **W-DEC-4 落地**（独立提交）；与 **W2-2**（PayloadJson ↔ state.Data 权威单一化）同批 |
| **风险** | 中（改 API 缺省形 ⇒ 须先全域 grep 未显式传 `nodeType` 的调用面） |

### DP-3 · 跨轮状态 KV —— 写入口收敛为 1（**P0，即 W2-2 的数据面表述**）

| 项 | 内容 |
|---|---|
| **目标** | 轮内/跨轮状态 KV（`AxiomContext.Data` 与 Transient `vars`）只有一个语义写入口；其余为**投影重算**或读取 |
| **散点** | 直写 4 处：`AgentRunner.Pipeline.cs:112`（`Executor`）· `RoundStateMapper.cs:124`（`Harvest` 二次收割）· `WorkflowRunner.cs:254/258`（`SessionId`/`ExecResult`）；`vars.Set` 6 处：`NodeExecutionService.cs:254`（与 `ur_nodes` 双写）· `RoundStateMapper.cs:98`（Seed）· `RoundStepInvoker.cs:187/199` · `WorkflowRunner.cs:140/154` |
| **风险实质** | ①`NodeExecutionService.cs:254` 与 `:325` 构成**同一事实双写**（Transient KV ↔ ur_nodes）②`Harvest`（`:124`）把数据空间值二次写回 `state.Data`，形成"落库 + 内存"双权威 |
| **收敛形态** | ① 写侧单写 `ur_nodes`（权威），`vars` 侧改为**投影重算**（自权威回填）② `state.Data` 直写收敛至 `AgentRunner`（轮边界唯一），`WorkflowRunner`/`RoundStateMapper` 降为投影/读取 ③ 开关默认关（W2-0），关 = 旧路径 |
| **判据** | ① `state.Data[...] =` 直写点 = 1 ② 开关关 = 13 工程全绿（行为等价）③ 开关开 = 故障注入（写后崩溃）重启后状态与落盘一致 ④ `NodeGraphReconciler` 跨介质缺口 0 |
| **批次** | **W2-2**（与 W2-1 同批，单独评审） |
| **风险** | **最高**（触写热路径）；回退 = 关开关 |

### DP-4 · 任务快照写（ur_tasks）—— 权威单一（**P1**）

| 项 | 内容 |
|---|---|
| **目标** | `AgentTaskItem` 快照（`PayloadJson`）以**统一任务快照为唯一权威根**，写入口 = 1 |
| **散点（5 条）** | `AgentRunner.Persistence.cs:136`（自述"单一状态基座"，但仅覆盖**轮边界**）· `AgentTaskPool.cs:312` · `AgentApiIntentSource.cs:128` · `ConversationCentralPipeline.cs:250` · `ConversationPipeline.cs:221` |
| **收敛形态** | ① 轮内：`AgentRunner.PersistRuntimeAsync` 唯一写 ② 会话/API 侧：经 `NodeWritePipeline`（会话读写收敛点）单一投影，不直调 `UpdatePayloadAsync` ③ `state.Data` 降为缓存 |
| **判据** | ① `UpdatePayloadAsync` 调用点（任务快照语义）= 1（或其上层门面唯一）② W2 判据同款（故障注入无漂移）③ `Agent.Persistence.Tests` / `Agent.Session.Tests` 全绿 |
| **批次** | **W2-2 同批**（与 DP-3 同一收敛动作的两侧） |
| **风险** | 高（会话链 + 任务池两处装配面） |

### DP-5 · 决策记录 —— 两个记录面收敛为 1（**P1**）

| 项 | 内容 |
|---|---|
| **目标** | 轮决策只有**一个记录面**（入口唯一）与**一个落点**（真源单一） |
| **散点（2 面）** | ① **引擎侧**：`DecisionCenter.Record`（`AgentRunner.Persistence.cs:72`，Session 层落 Center）② **执行/工具侧**：`DecisionRecorder.Record`（41 命中 / 12 文件）：`LocalLlmClient.cs:212/228/240/262/267` · `DecisionActionExecutor.cs:41` · `SemanticVerdictObservation.cs:37` · `ConversationHosting.cs:485` · `DecisionRecorderAuditSink.cs:28` |
| **风险实质** | 同一次"决策/工具命中"可能同时进两处（Center 快照 + 决策记录），查询需 join 且口径不同；F-66 已界定**边界分工**（Agent 不接 `LoopStateStore`）但未唯一化**记录面** |
| **收敛形态** | ① 记录面唯一：`DecisionRecorder` 降为 `DecisionCenter` 的写适配器（或反之，按 W-DEC-2 口径择一）② 真源 = 统一事件流（DP-6），`DecisionCenter`/决策记录表降为投影缓存 |
| **判据** | ① 决策记录入口 = 1（两面不并存）② 「提议→裁决→工具命中→落库」`WHERE ScopeId=RunId` 一次查全 ③ 观测契约词表（`DecisionContractWords`）与骨架词表（`DecisionWords`）不混用（F-60 既有断言） |
| **批次** | **W3-2（T4 观测内核）+ W-DEC-2**（观测源唯一化，建议 W6 之后） |
| **风险** | 中（只读旁路为主；记录面合并触审计路径） |

### DP-6 · 观测事件 —— 两个发射面收敛为 1 条事件流（**P1**）

| 项 | 内容 |
|---|---|
| **目标** | 统一事件流（`ScopeId=RunId`）+ 通用投影；埋点与业务事件**同源** |
| **散点（2 面）** | ① **埋点面** `AgentTrace.*`（14 命中 / 5 文件）：`AgentRunner.cs:318/447/481` · `AgentRunner.Persistence.cs:71` · `RoundStepInvoker.cs:95/102/109/116` · `AgentFramework.cs:107/121/124/127` ② **业务事件面** `_observabilitySink.Emit(RunEvent)`：`AgentRunner.Persistence.cs:143` · `EventSink.cs:51` · `ReactiveZoneBridge.cs:104/113`（`ur_events` + `events.jsonl` 双落点） |
| **风险实质** | 「一轮事实跨介质」：埋点（events.jsonl）与业务事件（ur_events）无共享键 ⇒ 单轮"执行→决策"无法一次查全 |
| **收敛形态** | ① 两面共用同一 `ScopeId`/`RoundId` 信封 ② `events.jsonl` 降级为**导出/投影**（W-DEC-2②）③ E1–E8 语义下沉 Frame（四族契约实例化） |
| **判据** | ① `WHERE ScopeId=RunId` 一次查全「提议→裁决→工具命中→落库」② K7 追踪四要素（`writer/at/seq/causedBy`）缺失 0 ③ `Ancestors(终答节点)` 闭合 |
| **批次** | **W3-2 → W3-3**（W3-3 受框架侧 D2/D3 约束，阻塞项） |
| **风险** | 中低（只读旁路，撤出不影响写入） |

### DP-7 · 会话历史写（ur_histories）—— 入口收敛（**P2**）

| 项 | 内容 |
|---|---|
| **目标** | `ur_histories` 写入口唯一（会话写入收敛点） |
| **散点（3 条）** | `AgentApiIntentSource.cs:85/108/123`（`_history.UpsertAsync`）· `ConversationPipeline.cs:224`（`_history.UpsertAsync`）· `NodeWritePipeline.CreateSessionAsync`（`ConversationPipeline.cs:214`） |
| **收敛形态** | `NodeWritePipeline`（会话读写收敛点）为唯一写门面；API/会话侧降为调用方；`SqliteHistoryStore` 保持唯一实现 |
| **判据** | ① `UpsertAsync` 调用点 = 1（或其上层门面唯一）② `Agent.Api.Tests` / `Agent.Session.Tests` 全绿 ③ `ur_histories` 与 `ur_tasks` 格式镜像不漂移 |
| **批次** | W4/W5 之后（低优先，无阻塞） |
| **风险** | 低 |

### DP-8 · 输出区写（ur_outputs）—— 入口收敛（**P2**）

| 项 | 内容 |
|---|---|
| **目标** | `ur_outputs`（三区输出）写入口唯一 |
| **散点（3 条）** | `AgentTaskPool.cs:490` · `AgentUiLibraryConversation.cs:297`（`OutputVisibility.WorkflowLocal`）· `OutputZoneWriter.cs:21/40`（记录构造） |
| **收敛形态** | `OutputZoneWriter` 升为唯一构造 + 写入门面；另两处改为调用方 |
| **判据** | ① 输出区写入口 = 1 ② 三区（`outputZone`）语义不旁路 ③ `Agent.Persistence.Tests` 全绿 |
| **批次** | W4/W5 之后（低优先） |
| **风险** | 低 |

---

## 五、散点 → 聚拢点映射表

| 现文件/类型（写/构造点） | 归属聚拢点 | 处置 |
|---|---|---|
| `Agent.Core\Nodes\NodeExecutionService.cs`（`PersistAsync`/`WriteNodeOutputsAsync`/`PersistNodeReferencesAsync`） | **DP-1** + DP-2 | 保持唯一 writer；`PayloadJson` 经 Codec |
| `Agent.Core\Nodes\RoundStepInvoker.cs:312-316` | **DP-1** | `WriteNodeOutputAsync` 改为经写门（注入 `WriteCoordinator`，不自建） |
| `Agent.Orchestration\SessionWorkflow\NodeWrite\NodeWritePipeline.cs` | **DP-1** / DP-4 / DP-7 | 会话侧写门面统一 |
| `Agent.Launcher\Api\Dispatch\Handlers\AgentApiIntentSource.cs`（`ToRecord`/`UpdatePayloadAsync`/`UpsertAsync`） | DP-2 / DP-4 / DP-7 | 降为调用方；缺省形归位（W-DEC-4） |
| `Agent.StorageBridge\Storage\StorageBridgeCommands.cs` | DP-2 | `PayloadJson` 经 Codec |
| `Agent.Launcher\Modules\{DatabaseModule,AgentUiLibraryData,AgentUiLibraryConversation}.cs` | DP-2 / DP-8 | 经 Codec / 经输出区门面 |
| `Agent.Core\Nodes\{RoundStateMapper,WorkflowRunner}.cs`（`state.Data[..]=`、`vars.Set`） | **DP-3** | 降为投影/读取（须逐处取证读侧用途，见 §七-3） |
| `Agent.Core\Loops\{AgentRunner.Persistence,AgentRunner.Pipeline}.cs` | DP-3 / DP-4 | 保持轮边界唯一写 |
| `Agent.Contract\Llm\LocalLlmClient.cs`（`_recorder.Record`） | DP-5 | 经唯一记录面 |
| `Agent.Modules\Invoke\Tool\DecisionActionExecutor.cs` · `Agent.Core\Loops\SemanticVerdictObservation.cs` | DP-5 | 同上 |
| `Agent.Core\Loops\AgentRunner.cs` · `Agent.Core\Nodes\RoundStepInvoker.cs` · `Agent.Launcher\Composition\AgentFramework.cs`（`AgentTrace.*`） | DP-6 | 并入统一事件流（保持埋点调用形态） |
| `Agent.Core\DataStream\{EventSink,ReactiveZoneBridge}.cs` · `Agent.Core\RunEventFactory.cs` | DP-6 | 统一信封（`ScopeId`/`RoundId`） |
| `Agent.Core\OutputZoneWriter.cs` | DP-8 | 升为输出区唯一门面 |

---

## 六、判据命令（复制即用）

```powershell
# ── 既有门禁（DP-0.x 回归；应全 EXIT 0）──
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-SingleWriter.ps1'                 # DP-0.1 / DP-0.2
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-AgentDataSeparation.ps1' -Check   # DP-0.8
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-DataCenterGenericity.ps1' -Check  # DP-0.5 / DP-0.6
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-AgentRuntimeSlots.ps1' -Check     # DP-0.7
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-AgentDecisionCoreBoundary.ps1' -Check

# ── 散点计数（改动前基线 / 改动后回归；计数只允许下降）──
$all = (Get-ChildItem 'D:\Project\Main\Projects\Plug\Agent\src' -Recurse -File -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }).FullName
foreach ($p in @('WriteOutputAsync',            # DP-1：8 命中/5 文件（3 条写路径）
                 'PayloadJson\s*=',             # DP-2：17 命中/11 文件（9+ 构造点）
                 'state\.Data\[[^\]]+\]\s*=',   # DP-3：4 命中/3 文件（全为真实直写）
                 'vars\.Set',                   # DP-3：6 命中/4 文件
                 'UpdatePayloadAsync',          # DP-4：8 命中/6 文件（5 调用点）
                 'AgentTrace\.',                # DP-6：14 命中/5 文件
                 'UpsertAsync')) {              # DP-7：6 命中/4 文件（3 调用点）
  $m = Select-String -Path $all -Pattern $p
  Write-Output ("{0,-30} hits={1,-4} files={2}" -f $p, $m.Count, ($m | Select-Object -ExpandProperty Path -Unique).Count)
}
```

**DP 项专属判据**：见 §四 各项「判据」行。

---

## 七、诚实边界

| # | 说明 |
|---|---|
| 1 | **本文只做识别与排序，不改代码**：DP-1…DP-8 的落地须经对应批次（W2/W3/W4/W5）的独立评审与单独提交。 |
| 2 | **DP-0.x 不得重造**：`WriteCoordinator`/`SqliteWriteChannel`/`ReferenceGraphCoordinator`/`NodeRelationGateway`/`AgentRuntimeRoot`/`AgentRuntimeSlotKeys`/`AgentStateDomain`/`data-ownership.json` 是既有权威单点，禁止新起平行实现（`SingleImplementation` / TH-12）。 |
| 3 | **`vars` 的"语义双写"边界**：`vars.Set` 并非全部为冗余双写 —— `NodeExecutionService.cs:251-254` 自述为「扁平兼容写入（供后续步骤直接读原键；P3 拷贝语义）」。DP-3 收敛前须**逐处取证读侧用途**，不得一律删除。 |
| 4 | **DP-6 受框架侧排期约束**：K7 追踪四要素依赖框架 D2（`TraceStamp`/schema 扩）与 D3（`ITraceQuery`）；`IDataCenterRoot` 实测**无 `Trace` 成员** ⇒ 等待期只做无依赖项。 |
| 5 | **计数口径**：本文 hits/files 为 2026-09-12 实测（`Projects\Plug\Agent\src`，排除 `obj/bin`）；`PayloadJson` 全量命中 69 处含**读**点，构造点以「赋值」为准（§二 N2 已分列）。 |
| 6 | **不承诺"零散点"**：部分散点属**分层职责**（如 `NodeWritePipeline` 与会话链分工），收敛目标是"同一事实单一写者"而非"全局只有一个类"。 |
| 7 | **DP-1 的旁路结论须以改动前取证为准**：`RoundStepInvoker.WriteNodeOutputAsync` 不经 `WriteCoordinator` 系静态阅读所得，落地前须以断言（旁路调用点 = 0）验证。 |

---

## 八、建议执行顺序

```
[P0 · 与 W2 同批（最高风险，单独评审）]
  DP-1（ur_nodes 写通道：消除 RoundStepInvoker 旁路）   ──► W2-1 前置
  DP-3（状态 KV 单写 + 投影重算）                        ──► W2-1 / W2-2
  DP-4（任务快照权威单一）                              ──► W2-2
  DP-2（节点记录 Codec 唯一 + W-DEC-4 缺省形归位）      ──► W-DEC-4 独立提交

[P1 · 框架侧就绪后（只读旁路）]
  DP-5（决策记录面唯一）► DP-6（统一事件流）             ──► W3-2 → W3-3 → W-DEC-2

[P2 · 收尾批次（低风险）]
  DP-7（ur_histories 入口）► DP-8（ur_outputs 入口）     ──► W4/W5
```

**批末统一动作**：① 跑 §六 全部门禁 + 散点计数；② 回写 `data-ownership.json` 状态列与 `Agent-DataOwnership.md`；③ 同步第五轮计划 §0 基线；④ 若 DP 项落地，回到本文更新对应「散点证据」行（**保留原计数**以便对比）。


