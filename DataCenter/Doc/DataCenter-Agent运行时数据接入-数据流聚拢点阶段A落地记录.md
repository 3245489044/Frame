# DataCenter · Agent 运行时数据接入 —— 数据流聚拢点「阶段 A」落地记录

> **日期**：2026-09-12 | **性质**：`DataCenter-Agent运行时数据接入-数据流聚拢点分析.md` §八 建议执行顺序中 **阶段 A（P0：DP-1 / DP-3 / DP-4 / DP-2）** 的执行记录。
> **权威边界**：散点识别与聚拢点定义以聚拢点分析为唯一权威；批次口径与判据命令以 `DataCenter-Agent运行时数据接入-第五轮收口实施计划.md`（§3.8 / §4）为唯一权威；本文件只登记**已落地的改动与实测证据**。
> **通则遵守**：不改真源（SQL/LiteDB）、不改 CSG 主 schema、不动落盘键；每子项独立提交、独立可回退；`DP-0.x` 既有聚落点只复用不重建。

---

## §0 阶段 A 提交清单

| # | 项 | 仓库 | 提交信息（摘要） |
|---|---|---|---|
| A1-a | DP-1：`ur_nodes` 端口写经确定性写门 | Projects | `DP-1 判据：ur_nodes 端口写经确定性写门（NodeExecutionService.WritePortAsync 单点入口，消除 RoundStepInvoker 直写旁路）` |
| A1-b | DP-1 门禁（`Check-SingleWriter` E 项） | Frame | `DP-1 门禁：Check-SingleWriter 增 E 项（ur_nodes 端口写旁路调用点 = 0，白名单 1 项 DP-1-R1）` |
| A1-c | DP-1 断言（`Agent.Core.Tests` 408/408） | Main | `DP-1 判据：端口写经写门共享断言（Agent.Core.Tests 408/408）+ 子模块指针同步` |
| A2-a | DP-3：W2-0 开关 + W2-1 收敛点 1 | Projects | `W2-0/W2-1 判据：双写收敛开关（默认关）+ 收敛点1（写侧单写 ur_nodes，读侧投影重算）` |
| A2-b | DP-3：W2-2 收敛点 2 | Projects | `W2-2 判据：收敛点2（state.Data 直写降为轮边界单点提交 StateProjection）` |
| A2-c | DP-3 断言（`Agent.Core.Tests` 414/414） | Main | `W2-0/W2-1/W2-2 判据：双写收敛开关两侧断言（Agent.Core.Tests 414/414）+ 子模块指针同步` |
| A3-a | DP-4：会话侧唯一门面 | Projects | `DP-4 判据：任务快照会话侧写入口收敛（NodeWritePipeline.UpdateSnapshotAsync 唯一门面）` |
| A3-b | DP-4 门禁（`Check-SingleWriter` F 项） | Frame | `DP-4 门禁：Check-SingleWriter 增 F 项（会话流水线不得直调 UpdatePayloadAsync）` |
| A4-a | DP-2：Codec 单形归一口 | Frame | `DP-2 判据：NodeOutputCodec.EncodeExternal 单形归一口（输出文档载荷归位业务节点单形）` |
| A4-b | DP-2：4 处构造点归位 + W-DEC-4① | Projects | `DP-2 判据：ur_nodes 记录构造经唯一 Codec + W-DEC-4 缺省形归位（NodeTypes.Node）` |
| A4-c | DP-2 断言（Persistence 93/93 · Api 35/35） | Main | `DP-2 判据：单形归一与缺省形断言（Persistence 93/93 · Api 35/35）` |

> `Agent.sln` 与宿主 `PrjBridge.csproj` 各提交点均 **0 错 0 警**；A4 触及框架源码（`CsharpPrj`），已按权威烘焙轨执行 `dotnet build Frame\CsharpPrj\MainProj.sln` → `dotnet build Projects\Plug\Agent\Agent.sln`。

---

## §1 DP-1 · `ur_nodes` 写路径收敛为 1

| 项 | 内容 |
|---|---|
| **改动** | ① `Agent.Core\Nodes\NodeExecutionService.cs` 新增 `WritePortAsync(nodeKey, port, value, ct)`：与 `PersistAsync` **共用同一 `WriteCoordinator` 实例与写者名**（`persist:{nodeKey}` / `NodeExecutionService.PersistAsync`），不新增写者；② `Agent.Core\Nodes\RoundStepInvoker.WriteNodeOutputAsync` 改为转发执行服务（消除直写 `INodeOutputStore` 旁路） |
| **判据实测** | ① `\.WriteOutputAsync\(` 调用点：改动前 3（`NodeExecutionService` ×1 + `RoundStepInvoker` ✓旁路 + `AgentUiLibraryConversation`），改动后 **2 处均在 `NodeExecutionService` 内**；② `Check-SingleWriter` 新增 E 项 → `EXIT 0`（白名单 1 = DP-1-R1）；③ `Agent.Core.Tests` 408/408（含 3 条新断言：端口写落库读回 / 未装配 no-op / **写门共享**：步骤持久化持门期间端口写不得进入存储、并发落库峰值 = 1） |
| **取证修正** | 聚拢点分析 §二 N1 第 ③ 条（`NodeWritePipeline.cs:85`）**不写 `ur_nodes` 端口**（写 `ur_tasks`/`ur_histories`/`cross_db_link`）⇒ DP-1 的 ur_nodes 写路径实为 **2 条**（非 3 条），第 ③ 条改归 **DP-4 / DP-7**。另取证新增真实残余：`AgentUiLibraryConversation.cs:297`（UI 会话节点直写）→ 登记 **DP-1-R1** |

### 剩余项

| ID | 内容 | 处置 |
|---|---|---|
| **DP-1-R1** | `AgentUiLibraryConversation.ConversationAddNodeAsync` 经 `new NodeOutputStore(...).WriteOutputAsync` 直写 ur_nodes（未持写门；非运行热路径，写独立 nodeKey 命名空间） | 已在 `Check-SingleWriter` E 项白名单登记理由；收敛需**共享写门注入面**（组合根把同一 `WriteCoordinator` 同时注入 `NodeExecutionService` 与 UI 门面）⇒ W2-1 装配面工作项 |

---

## §2 DP-3 · 跨轮状态 KV 单写 + 投影重算（开关默认关）

| 项 | 内容 |
|---|---|
| **开关（W2-0）** | `AgentConfig.EnableUnifiedStateWrite`（默认 **false** = 旧路径）→ `AgentOrchestrator.SetUnifiedStateWrite(bool)` → `NodePersistenceAssembly.EnableUnifiedStateWrite` → `NodeRuntime.From` 透传 `NodeExecutionService` / `RoundStepInvoker` / `WorkflowRunner` |
| **收敛点 1** | `NodeExecutionService.ExecuteAsync`：开关开时**停写**数据空间扁平键；读侧新增 `ProjectOutputsAsync`（自权威 ur_nodes 读回并回填，取代执行期扁平写）。`RoundStepInvoker.OnAfterStepAsync`：同步路径 → `ProjectOutputsAsync`（自权威）；Outbox 在途 → `ProjectInFlight`（同轮值桥接，权威仍为 ur_nodes） |
| **收敛点 2** | `RoundStateMapper.Harvest(state, vars, projectOnly)`：开关开时**不直写** `state.Data`，返回投影键值；`WorkflowRunner` 累积本轮投影（`_roundProjection`，轮起点重置）并经 `WorkflowRoundResult.StateProjection` 交出；`AgentRunner.CommitRoundProjection` 在**轮边界单点提交**（`AgentRunner.Pipeline.cs` RunRound 后 + 轮前执行器实例同点写入） |
| **判据实测** | ① 开关**关**（默认）：13 测试工程全绿（Core 414 / Axioms 86 / Launcher 37 / Runtime 21 / Persistence 93 / Session 20 / Modules 20 / Operation 47 / Observe 11 / Guardrails 10 / Api 35 / Api.Contract 22 / McpRegistryHost 19）；② 开关**开**：`AgentRunnerStateProjectionTests` 4 条（Harvest 投影不直写 / 旧路径直写且投影空 / 轮边界提交投影 / 无投影零动作）+ `RoundStepInvokerTests` 2 条（投影自权威回填 / 旧路径不产投影）；③ 五道门禁 `EXIT 0`；④ 中间态（W2-0/W2-1 单独提交）经 `git stash` 隔离验证**独立可编译** |
| **静态计数（诚实说明）** | `state.Data[...] =` 文本命中 4 → **5**（新增 2 处在 `AgentRunner.CommitRoundProjection` 轮边界唯一写点），其中 `RoundStateMapper` / `WorkflowRunner` 的 3 处均为**开关关回退分支**；开关开时的**实际执行路径**仅 `CommitRoundProjection`。`vars.Set` 6 → 8（新增 2 处为投影回填，属派生读侧，非第二权威） |

### 剩余项（未闭合判据）

| ID | 内容 |
|---|---|
| **DP-3-R1** | 聚拢点分析判据 ②③：**故障注入**（写后崩溃重启一致性）与 `NodeGraphReconciler` 跨介质缺口 0 未执行——需真实宿主（SQLite 后端 + 进程重启）验证；本次仅完成「开关关 = 行为等价」与「开关开 = 状态提交口径」两侧断言 |
| **DP-3-R2** | Outbox 在途路径的投影为同轮内存桥接（`ProjectInFlight`），**未覆盖**「落库完成后自权威重算等值」断言；上生产前建议补 |

---

## §3 DP-4 · 任务快照权威单一

| 项 | 内容 |
|---|---|
| **改动** | `Agent.Orchestration\SessionWorkflow\NodeWrite\NodeWritePipeline.cs` 新增 `UpdateSnapshotAsync(taskKey, item, ct)`（会话侧唯一门面）；`ConversationPipeline.cs` 与 `ConversationCentralPipeline.cs` 的会话完成/更新写改经该门面（不再直调 `IConversationStore.UpdatePayloadAsync`） |
| **写者分工（登记口径）** | 轮内快照 = `AgentRunner.PersistRuntimeAsync`（每轮边界唯一）；池状态 = `AgentTaskPool.ApplySnapshotStatusAsync`（Pause/Resume/WriteStatus 共用）；会话生命周期 = `NodeWritePipeline.UpdateSnapshotAsync`；API 归属（`HistoryId`）= `ITaskPoolPersistence` 端口（task 域单通道） |
| **判据实测** | `UpdatePayloadAsync` 语义调用点 5 → **3 层门面**（轮内 / 池状态 / 会话门面）+1（API 经 task 池端口）；`Check-SingleWriter` 新增 F 项 `EXIT 0`；回归：Core 414 · Session 20 · Persistence 93 · Api 35 · Api.Contract 22 · Launcher 37 · Modules 20 全绿 |

---

## §4 DP-2 · 节点记录构造唯一 Codec + W-DEC-4 缺省形归位

| 项 | 内容 |
|---|---|
| **改动** | ① 框架 `Operation\HierarchyMethod\Nodes\NodeOutputCodec.cs` 新增 **`EncodeExternal(payloadJson, symbol)`**（ur_nodes 记录载荷唯一构造入口）：含 `Outputs` → 规范化重编码（补 Symbol）；含 `Ports`（输出文档）→ 包成业务节点载荷；其余任意 JSON / 非 JSON → **原样返回**；空 → 空业务载荷。② `AgentApiIntentSource.ToRecord`：缺省 `NodeTypes.Output` → **`NodeTypes.Node`**（W-DEC-4①）+ 载荷经 Codec；③ `LocalDbMcpTools`、`AgentUiLibraryData`、`StorageBridgeCommands` 的 `PayloadJson` 直赋改经 Codec |
| **判据实测** | ① `PayloadJson =` 构造点**全部经 Codec**（`NodeExecutionService` 既有 + 本次 4 处）；② F-63 形态断言：`NodeOutputCodecTests.EncodeExternal_OutputDocShape_WrittenToStore_IsReadBackByEnumerateOutputs`（归位后记录可被 `EnumerateOutputsAsync` 读回端口值）；③ 缺省形断言：`AgentApiIntentSourceTests.NodeWrite_WithoutExplicitNodeType_DefaultsToSingleForm`（显式 `nodeType` 仍可覆盖）；④ `Agent.Persistence.Tests` 93/93 · `Agent.Api.Tests` 35/35；⑤ `Check-DataContracts` `EXIT 0` |

### 剩余项（口径修正，待回写聚拢点分析）

| ID | 内容 |
|---|---|
| **DP-2-R1** | 聚拢点分析 §二 N2 的构造点清单中，`DatabaseModule.cs:98`（`ur_inputs`）与 `StorageBridgeCommands.cs` 的 ur_inputs/ur_outputs/ur_events 三处**不是 ur_nodes 构造点**；`OutputZoneWriter` 属 `ur_outputs`（归 DP-8）。ur_nodes 真实构造点 = **5**（1 合规 + 4 经本次收敛） |
| **DP-2-R2** | 「通用外部透传口」（storage-bridge / MCP / UI 函数库）的任意 JSON 载荷**按设计原样保留**（读侧为 `node.read` / UI 原始载荷展示，不进执行链端口读回）；若要求「全部 ur_nodes 记录恒为业务节点单形」，需先与 UI/API 消费面契约对齐（W-DEC 级决策），本次**未**强行包裹 |

---

## §5 批末统一动作与基线

| 动作 | 状态 |
|---|---|
| §4 全集（构建 + 13 回归 + 5 门禁） | ✅ 已跑（实测值见 §2/§4 与下表） |
| 台账 `data-ownership.json` | ➖ 本阶段未新增/变更登记条目（pending 2 = `observability-events` / `decision-records`，属 DP-6/DP-5，不在阶段 A 范围） |
| 第五轮收口实施计划 §3.8 状态列 | ✅ 已回写（DP-1/2/3/4 → 已落地 + 判据摘要） |
| 聚拢点分析 §2.1 散点计数回写 | ⏸ **顺延**：该文件工作树中存在**非本次改动**的未提交修改（§七/§八 被删），为避免与他方编辑冲突暂不写入；修正口径已登记于本文件 §1/§2/§4 |

### 基线对照（2026-09-12）

| 项 | 阶段 A 前 | 阶段 A 后 |
|---|---|---|
| `Agent.Core.Tests` | 405/405 | **414/414** |
| `Agent.Persistence.Tests` | 88/88 | **93/93** |
| `Agent.Api.Tests` | 34/34 | **35/35** |
| 其余 10 工程 | 不变 | 不变（Axioms 86 / Launcher 37 / Runtime 21 / Session 20 / Modules 20 / Operation 47 / Observe 11 / Guardrails 10 / Api.Contract 22 / McpRegistryHost 19） |
| 五道门禁 | 全 EXIT 0 | **全 EXIT 0**（`Check-SingleWriter` 告警仍 8；新增 E/F 项通过） |
| 构建 | 0 错 0 警 | **0 错 0 警**（`Agent.sln` / `PrjBridge` / `MainProj.sln`） |

---

## §6 诚实边界

| # | 说明 |
|---|---|
| 1 | **DP-3 开关默认关**：生产路径行为等价，收敛口径仅在开关打开时生效；开关开的生产等价性需真实宿主（SQLite + Outbox + 重启）验证（DP-3-R1/R2）。 |
| 2 | **DP-2 未强行统一外部载荷形态**（DP-2-R2）：通用透传口的任意 JSON 原样保留，UI / `node.read` 消费契约不变。 |
| 3 | **DP-1-R1**（UI 会话节点直写）仍在门禁白名单，收敛依赖共享写门注入面。 |
| 4 | `Check-BakeFramework.ps1` 当前失败项为**既有环境状态**（烘焙目录缺 `csharp-bake.manifest.json`），与阶段 A 改动无关；本次未将其纳入判据（第五轮 §4 门禁清单不含此项），也未留下 manifest/stamp 产物。 |
| 5 | 框架侧改动（A4-a）须经**权威烘焙轨**（`MainProj.sln`）刷新后才能被 Agent 侧引用；只编单工程会致烘焙副本陈旧（`Check-BakeFramework` 第 6 项即此判据）。 |
| 6 | DP-5 / DP-6 / DP-7 / DP-8（P1/P2：决策记录面、统一事件流、`ur_histories`、`ur_outputs`）**不在本阶段范围**，状态仍为「待做」。 |
