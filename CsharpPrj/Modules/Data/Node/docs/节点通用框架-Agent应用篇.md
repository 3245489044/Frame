# 节点通用框架 · Agent 应用篇（JSON 键连接版）

> **状态**：设计稿（只出文档，未落地代码）
> **配套**：[节点通用框架设计（与位置无关）](./节点通用框架-与位置无关的设计.md) · [提取清单与复用映射](./节点通用框架-提取清单与复用映射.md)（WorkFlowMethod 侧全部节点件的逐项裁决与缺口清单） · [表层语言与 Usharp 接入](./节点通用框架-表层语言与Usharp接入.md)（定义来源端口化：Agent 侧以 `.ushar` 作定义期语言，Node 只留端口）
> **本文件回答三件事**：① Agent 侧如何**结合 `Operation\WorkFlowMethod`** 实现；② 运行流程如何**全程基于 JSON 显式拼接 + 键连接**；③ **cline 历史回写**与**实时节点转换**怎么落。
> **依据**：全部结论来自本仓库实测（文件:行）。

---

## 1. 现状台账

### 1.1 Agent 侧现有载体（与通用篇 Placement 的对应）

| 载体 | 键 | 载荷 | 写点 | 读点 | 对应 Placement |
|---|---|---|---|---|---|
| `ur_histories` | `sessionName` | `ConversationLog` JSON | `PersistHistoryBlock`；第二通道 `MediaHandlers.HistoryStoreMediaHandler:73` | UI 视图 `ConversationViewService:151-152`；会话下拉 `ListSessionsAsync:200-206` | `legacy-history`（拟退役） |
| `ur_nodes` 业务节点 | 裸 `nodeKey` | 输出端口 | `PersistNodesBlock:39` → `NodeStepExecutionService.PersistRoundAsync:303` | 面板 `RenderTaskNodesPanelAsync:490`、`BuildTaskAsync` | `task-node` |
| `ur_nodes` L-B | `session.{c}.r{r}.msg.{s}` | 单条 `ConversationMessage`（终答内联） | `HistoryProjector.ProjectMarkedAsync:97` | LLM wire `RenderPriorWireAsync:289` → `BuildSessionAsync:103` | `session-view` |
| `ur_nodes` L-C | `cline.session.{k}.round.{n}` | `messages.json` 全文 | `HistoryClineMessageArchive.ArchiveAsync:47`（由 `ClineDaemonEndpoint.TryArchiveRoundAsync:259` 调） | `IClineMessageArchive.LoadAsync/LoadLatestAsync` | `archive-raw` |
| FsSink 帧 | （未落库） | `ClinePersistFrame` | **已禁写**：`ClineDiagnosticsHosting.OnPersistFrame:57-64` | 仅日志 | — |

### 1.2 WorkFlowMethod 侧可复用能力（本次结合点）

| 能力 | 既有件 | 现状是否被 Agent 使用 |
|---|---|---|
| JSON 链定义 | `Chain<TKey,TNode>` / `ChainInfo<TKey,TNode>`（`ChainId/StartNode/EndNode/AllNodes`）`Share\Frameworks\GraphFramework\Models\Chain.cs` | ❌ 未用（Agent 用 `RoundSpec/Steps`） |
| JSON 节点定义 | `NodeConfig`（`Id/NextNodeId` + `funcPipelineConfig{ParameterKeys,ResultKeys,FuncName}` + `nextNodeConfig{NextNodeKey,MapKeyConfig,ConnectChainInfoKey,UpdateChain}` + `basicConfig{Callkey}`）`WorkFlowMethod\NodeProcess\Models\NodeConfig.cs` | ❌ 未用 |
| JSON → 链 | `WorkflowDeserializer`（`UniversalMiddlewareEntry.FromJson<ChainInfo<..>>`）、`NodeChainSkeleton`（JSON 建链）、`NodeChainService` | ❌ 未用 |
| 执行管线 | `NodeProcessPipelineBuilder`（GraphDefinition→NodeChain→NodeExtractor→GraphChainUpdate→NodeExecution→NodeLoop{执行步→端口物化}→NodeMetrics） | ❌ 未用 |
| 端口物化 + 单写门 | `NodeOutputPersistStage` / `NodePersistCoordinator` / `INodeOutputStore`（`WorkFlowMethod\Nodes\Contracts`） | ⚠️ 部分（Agent 用同构自建 `NodeStepExecutionService`/`WriteCoordinator`） |
| 键空间读视图 | `NodeOutputReadView`（`{node}.{port}` + 裸键回退 + provenance） | ⚠️ 部分（Agent 用 `NodeReadView`/`INodeOutputStore`） |
| 键映射 | `KeyProcessor.ProcessConnectionKeyMapping`、`KeyManager.GenerateCallkey/EnsureChainSuffix`、`KeyMappingConfig` | ❌ 未用（Agent 用 `KeyMappingKeys` 拼串） |
| 图化 | `GraphFoundationBridge.ChainToGraph`（边 Id `{src}->{tgt}`） | ⚠️ 部分（Agent 另建 `ReferenceGraphCoordinator`） |
| 内存节点装配 | `LoopFoldMethod.Node`：`NodeItem/NodeSlot/NodeCategory/NodeBuilder/INodeSlotPolicy/NodeComponent/INodeSource/NodeAssemblyEngine/INodeProjector` | ⚠️ 部分（折叠算子用其子集） |
| 循环状态键空间 | `AxiomContext.Data`（`IDictionary<string,object>`）+ `RoundKeys.*`（`Agent.Axioms\Loops\RoundKeys.cs`） | ✅ 用（AI 侧种子与穿参） |
| 输出回写策略 | `Output\StrategyResolver`（text/list/selectedItem 读写策略）+ `OutputWriteStage/RollbackStage/SnapshotStage` | ❌ 未用 |

> 本表只列**本次结合点**；WorkFlowMethod 侧**全部**节点件（含未列出的端口提取器 `NodeResultExtractor`、单写门 `NodePersistCoordinator`、端口水化 `NodeParameterHydrator`、链合并去重、改图指令集、连通性校验、引用字段注册、形态编解码策略、基础设施件）的逐项裁决（复用 / 补登记 / 提取 / 迁出 / 留业务侧 / 退役）与 14 项缺口清单见 [提取清单与复用映射](./节点通用框架-提取清单与复用映射.md) §2–§4。

### 1.3 键空间双轨（当前最核心的结构问题）

```
轨 1（框架侧）：AxiomContext.Data，键名 = RoundKeys.*（user_input / l1 / l2 / round / decision / ...）
                → 用于「种子穿参 + 骨架步骤读写」
轨 2（节点侧）：NodeOutput 端口键空间，键名 = {node}.{port} / 裸 {port}
                → 用于「节点产出 → 下游入参」
两轨各自成体系，转换靠 C# 赋值搬运（SeedWorkflow / ExtractStages / PersistNodesBlock）
```

→ 这正是"节点 → 历史/任务转换逻辑模糊"的机制根源：**两轨之间没有声明式桥**，只能靠代码搬运。

---

## 2. 偏离分析（↔ 通用篇不变式）

| 现象 | 违反 | 说明 |
|---|---|---|
| 两轨键空间无声明桥（§1.3） | N8/N12 | 连接靠代码，不靠键 |
| 会话链用 `RoundSpec` 而非 `ChainInfo<string,NodeConfig>` | N8/N11 | 两套链 schema，无法共用执行管线 |
| 产出**轮末批量**落库 | N12/§3 | 每轮一次，非逐节点实时 |
| cline 帧被丢弃 | N2 | 真实事件未物化，靠"轮末重建"近似 |
| cline 回写三路并行（ur_histories / L-B / L-C） | N2/N7 | 同一事实多位置、多写者 |
| 轮次三源（`ctx.Round` / `NextRoundAsync` / 局部 `seq`） | N8 | 键内序数不单源 |
| `round_sequence` 三写者 | N3/N6 | 边表顺序 |
| `Mark` 决定"谁进历史"在**写侧** | N7 | 应移读侧 selection |

---

## 3. 目标运行流程：全程 JSON 拼接 + 键连接

### 3.1 端到端流程图

```
[定义期] 会话链 JSON（ChainInfo<string, NodeConfig>）
         · 由模板/声明生成（替代 RoundSpec/Steps），Steps[i] ↔ NodeConfig[i]
         · 连接：Id/NextNodeId/NextNodeKey；数据流：ParameterKeys ← 上游 ResultKeys
         · 键映射：MapKeyConfig（JSON，经 KeyProcessor 归一）
                │  WorkflowDeserializer（UniversalMiddlewareEntry.FromJson）
                ▼
[装配期] NodeProcessPipelineBuilder 装配执行管线（复用既有 stage 序）
         + 本框架：PlacementRegistry（domain=conversation，装载 §4 声明）
                │
                ▼
[运行期·每轮]
 ① 种子：把"本轮输入 JSON"写入键空间
      · 写入 NodeOutput 端口键空间（{node}.{port} + 裸键）  ← 新增：以节点为载体的种子
      · 兼容轨：AxiomContext.Data（RoundKeys.*）            ← 现有，逐步并入（§3.3）
 ② 节点执行：NodeLoop 内逐节点
      · 入参：ParameterKeys 逐键在键空间查找（NodeOutputReadView.BuildInputSourceAsync）
      · 执行：FuncName → 执行端（answer / cline / 本地函数）
      · 产出：ResultKeys → NodeOutput.OutputPorts（JSON）
 ③ 实时物化（§6）：每个产出**立即**经 NodeMaterializer 落 Placement
      · placement=session-view / archive-raw / task-node（声明决定）
      · 单写者判定 + 幂等 + 只增不丢
 ④ 连接推进：下一节点按键取参（键空间已含上一步产出）→ 回到 ②
 ⑤ 轮末：仅做"对账 + 收尾"（不再承担批量投影职责）
```

**与现状的关键差异**：③ 从"轮末批量"变为"逐产出即时"；④ 从"代码搬运（`SeedWorkflow`/`ExtractStages`）"变为"键空间查找"。

### 3.2 每步的 JSON 形状（示例）

```jsonc
// ① 种子（轮 r 的用户输入）——写成节点产出 JSON，键即地址
{ "NodeKey": "session.work.r2.msg1", "WorkflowId": "agent-round",
  "OutputPorts": { "user_input": "把 X 改成 Y", "round": 2, "session_id": "work", "max_rounds": 1 } }

// ② 节点定义（会话链中的一步）
{ "Id": "guard_input", "NextNodeId": "cline_execute", "Symbol": "guard_input",
  "funcPipelineConfig": { "FuncName": "guard_input",
                          "ParameterKeys": "[\"user_input\",\"session_id\"]",
                          "ResultKeys":    "[\"filtered_input\"]" },
  "nextNodeConfig": { "NextNodeKey": "cline_execute",
                      "MapKeyConfig": "[{\"from\":\"filtered_input\",\"to\":\"prompt\"}]" },
  "basicConfig": { "Callkey": "work#guard_input" } }

// ③ 产出物化（端口 JSON；placement 决定落到哪、什么形态）
{ "NodeKey": "guard_input", "WorkflowId": "agent-round",
  "OutputPorts": { "filtered_input": "把 X 改成 Y" } }
```

### 3.3 双轨键空间的合并规则（**待裁决**，见 §10 U4）

三条候选（互斥）：

| 案 | 规则 | 评价 |
|---|---|---|
| **A（推荐）** | **节点端口键空间为唯一**；`AxiomContext.Data` 退为"节点产出视图"（写入即物化同一 JSON，读时经 `NodeOutputReadView`） | 一次改动触达面大，但彻底消灭搬运代码（N12 达成） |
| B | 双轨保留，加**声明式桥**（`RoundKeys.X ⇄ {node}.{port}` 映射表） | 改动小；但"桥"本身是新的隐式层 |
| C | 维持现状（两轨并行 + 代码搬运） | 零风险；复杂度不变 |

---

## 4. Agent 侧声明草案（`domain: conversation`）

```jsonc
// Config\node.ushar（JSON 段）—— 落 Placement 声明；U1 身份粒度未决，此处按"轮粒度"给样例
{ "domain": "conversation",
  "identity": "{container}.r{round}",
  "sequence": ["round"],
  "placements": [
    { "id": "archive-raw", "level": "archive", "carrier": "sql-nodes",
      "key": "cline.session.{container}.round.{round}", "form": "raw",
      "retention": "audit", "selection": "all", "writer": "writer-archive" },
    { "id": "session-view", "level": "session", "carrier": "sql-nodes",
      "key": "session.{container}.r{round}", "form": "view",
      "retention": "contextual", "selection": "marked", "writer": "writer-session" },
    { "id": "task-node", "level": "task", "carrier": "sql-nodes",
      "key": "{storage}.{node}", "form": "view",
      "retention": "presentational", "selection": "all", "writer": "writer-task-node" },
    { "id": "session-membership", "level": "session", "carrier": "graph-edge",
      "refType": "session_owns", "writer": "writer-membership" },
    { "id": "task-membership", "level": "task", "carrier": "graph-edge",
      "refType": "cross_db_link", "writer": "writer-membership" },
    { "id": "legacy-history", "level": "session", "carrier": "sql-kv",
      "key": "{container}", "form": "view",
      "retention": "legacy", "selection": "all", "writer": "writer-legacy" }
  ],
  "selections": { "marked": "items[mark != none]", "all": "true" },
  "retentions": { "contextual": "LLM wire/续聊", "presentational": "面板", "audit": "归档/回灌", "legacy": "兼容读禁写" } }
```

**关键角色搬迁**：`HistoryMarkPolicy`（现写入侧决定谁进历史）→ `selection: marked` 的**谓词实现**（读侧过滤）；`round_sequence`（边表顺序）→ **键内 `(round, seq)`**（N6）。

---

## 5. cline 历史回写（从"三路并行"到"一次物化 · 多位置"）

### 5.1 现状：同一轮被写三次

| 路径 | 写入 | 位置 | 触发点 |
|---|---|---|---|
| ① 归档 | `messages.json` 全文 | `ur_nodes/cline_messages`（`cline.session.{k}.round.{r}`） | `ClineDaemonEndpoint.TryArchiveRoundAsync:259-276` |
| ② 会话对话 | assistant 终答 | `ur_histories`（`ConversationLog`） | `ConversationCentralPipeline.SaveAsync:222-224` → `PersistHistoryBlock` |
| ③ 入史投影 | 终答 + 标记中间项 | `ur_nodes/conversation_message`（`session.{k}.r{r}.msg.{s}`） | `HistoryProjector.ProjectMarkedAsync:97` |

三路**各自拼键、各自推导轮次**（① 用 `NextRoundAsync`，②③ 用 `ctx.Round`）→ 同一轮在三处可对不上，且正文重复 3 份（N2/N5/N7 全违反）。

### 5.2 目标：回写 = 键连接的物化声明

```
cline 本轮产出（帧流 / messages.json）
        │  规范化为"轮节点"JSON（一次）
        ▼
   Key = session.{container}.r{round}                    ← 身份（§4 identity）
        │
        ├── Placement: archive-raw   form=raw   → cline.session.{c}.round.{r}   （原文，audit）
        ├── Placement: session-view  form=view  → session.{c}.r{r}              （规范化 + mark，contextual）
        └── Placement: task-node     form=view  → {storage}.{node}              （业务节点产出）
                     + 归属边 session_owns / cross_db_link（writer-membership）
```

**要点**

1. **回写不再是"转换"，而是"同一 Key 的多个位置物化"**（N2）：键内已含 `round`，三处天然对齐，无需各自推导轮次。
2. **一次写、读处裁剪**（N7）：`session-view` 的 `form=view` 载荷内含逐条 `mark`；"只投喂标记项"由 `selection: marked` 在**读侧**过滤——不再为了让 LLM 只看一部分而另存一份裁剪副本。
3. **分工澄清（易混点）**：
   - `Placement` 声明决定**回写到哪个位置、什么形态**（本框架职责）；
   - `MapKeyConfig` / `ParameterKeys` 决定**链内数据流键改名**（WorkFlowMethod 既有职责）。
   两者不叠加使用：回写不走 `MapKeyConfig`。
4. **旧路退役**：`ur_histories` 置 `legacy`（禁新写、兼容读）→ 读侧切换（UI 下拉/视图）→ `retired`；②③ 两条写路合一。

### 5.3 回写的 JSON 形状

```jsonc
// 位置1 archive-raw（原文，逐帧/逐轮追加）
{ "NodeKey": "cline.session.work.round.2", "ThreadId": "work",
  "NodeType": "cline_messages", "Status": "completed",
  "Payload": { "messages": [ /* SDK 原文数组，零改写 */ ] } }

// 位置2 session-view（规范化 + mark，供 LLM/UI）
{ "NodeKey": "session.work.r2", "ThreadId": "agent-round",
  "NodeType": "conversation_message",
  "Payload": { "round": 2, "items": [
      { "role": "user",      "text": "把 X 改成 Y", "mark": "none" },
      { "role": "tool",      "stage": "guard_input", "text": "...", "mark": "numbered" },
      { "role": "assistant", "text": "已修改",      "mark": "terminal" } ] } }
```

> 现有 `conversation_message` 载荷是**单条消息**；上例为 U1 案 A（轮粒度）下的"轮载荷"。若维持消息粒度，则 Key 退为 `session.{c}.r{r}.msg{s}`、`items` 退为单条——**由 U1 决定**，其余不变。

---

## 6. 实时节点转换（帧级物化，取代轮末批量）

### 6.1 输入面（已存在，只是未被消费）

| 帧源 | 类型 | 现状处置 | 目标处置 |
|---|---|---|---|
| daemon `trace` 帧 | `DaemonResponse.Stage` | 池级只读投影（`ClineDaemonPool.LastStage`） | 保留诊断；可选物化为"阶段节点" |
| daemon `persist` 帧（FsSink 拦截 SDK 写盘） | `ClinePersistFrame{Kind,Sdk,SessionId,Payload}` | **丢弃**（`ClineDiagnosticsHosting.OnPersistFrame:57-64` 只日志） | **实时物化**（本节） |
| 轮末回传 | `ClineRunResult.ArchivedMessages` | 归档（§5 路径①） | 作为"补齐"+ 对账 |

> persist 帧本就是 SDK 侧**逐次写盘事件**——它天然是"实时节点"的粒度来源，此前被禁写是为了避免与轮末归档双写（`P3` 反模式）。**实时物化正是把"谁写"从"两处都写"改为"帧级唯一写、轮末只对账"**，因此禁写理由在新设计下自动消解。

### 6.2 设计

```
persist 帧到达
   │ ① 帧 → 节点 JSON（规范形状：{NodeKey, WorkflowId, OutputPorts}）
   │      键：{session}.{node}.{seq}（seq 由 ISequenceAuthority 单源分配）
   │      载荷：帧 Payload 原文（form=raw）或规范化消息（form=view）
   │ ② 合并窗口（背压）：按 (Key, port) 在同窗口内合并 → 最新值 + 帧计数
   │ ③ NodeMaterializer.Materialize(Key, json, placement)
   │      · 单写者判定（writer-stream，与轮末 writer-archive 不重叠）
   │      · 幂等：同 (Key, port, Digest) → 跳过（重放安全）
   │      · 只增不丢：不同 Digest → 版本化（N5）
   │ ④ 读侧即时可见：面板/LLM 上下文按 placement 读到最新（Live→Snapshot 无缝）
   ▼
轮末：仅"补齐 + 对账"（缺帧补写、跨帧一致性检查），不再承担批量重建
```

### 6.3 约束与取舍

| 议题 | 设计 |
|---|---|
| 写放大 | 合并窗口（毫秒级）+ 既有 `OutboxWriter` 异步落库（热路径 append，不阻塞帧接收） |
| 锁竞争 | 复用 `NodePersistCoordinator` 单写门；帧写与轮末写**位置不重叠**（writer 不同） |
| 顺序 | 键内 `seq` 由单源分配 → 帧序即键序（不依赖到达顺序）（N6） |
| 与轮末归档的关系 | 帧级写 `raw`，轮末写"校验摘要"；二者同 Key **不同 port** → 不冲突 |
| 崩溃恢复 | 已物化帧不丢（Live→Snapshot 已发生）；未物化 = 未产生 |
| 读侧稳定 | `NodeReadProjection` 的 Origin 优先级保证"帧未落库时读 Live、落库后读 Snapshot"（字段级叠加，无空窗） |

---

## 7. 读侧 / 写侧映射

### 7.1 读侧（按 placement + selection）

| 消费方 | placement / selection | 现状实现 | 收敛动作 |
|---|---|---|---|
| 主对话框 History 视图 | `presentational`（task-node + session-view） | `ConversationViewService:151` → `BuildHistoryAsync`（读 `ur_histories`） | 改读节点集（去 legacy 依赖） |
| 主对话框 Task 视图 | `presentational`（task-node） | `:152` → `BuildTaskAsync` | 保留；去掉对 `ur_histories` 的对话读取 |
| 会话下拉 / 存在性 | 会话根枚举 | `ListSessionsAsync:200-206`（`ur_histories` 行） | 改枚举 `session.*` placement |
| LLM wire（续聊） | `contextual` + `marked` | `RenderPriorWireAsync:289` → `BuildSessionAsync:103` | 保留；U1 案 A 下改为"读 archive-raw + 读侧 marked" |
| `context_role`（cline_execute） | 角色 → placement | `StandardContextProvider:35-39`（History→`BuildHistoryAsync`） | 角色映射写入声明 |
| 节点面板 | `presentational` | `RenderTaskNodesPanelAsync:490`（threadId 并集） | 由位置枚举替代（消 E9 双约定） |
| 归档详情 / resume | `audit` → archive-raw | `IClineMessageArchive.LoadAsync` | 保留（原文真源） |

### 7.2 写侧（6 写点 → placement × writer）

| 现状写点 | 目标 location | writer |
|---|---|---|
| `PersistHistoryBlock`（`ur_histories`） | `legacy-history` | `writer-legacy`（读侧切换后拒写） |
| `MediaHandlers.HistoryStoreMediaHandler:73`（`ur_histories` 第二通道） | 同上 | 必须与上**同一 writer**（现违反 N3） |
| `PersistNodesBlock:39` → `NodeStepExecutionService.PersistRoundAsync:303` | `task-node` | `writer-task-node` |
| `HistoryProjector.ProjectMarkedAsync:97` | `session-view` + `session-membership` | `writer-session` |
| `HistoryClineMessageArchive.ArchiveAsync:47` | `archive-raw` + `session-membership` | `writer-archive` |
| `SyncRelationsBlock:59/78`（边） | `task-membership` / `history-membership` | `writer-membership` |
| **新增** persist 帧（§6） | `archive-raw`（port=stream） | `writer-stream` |

**边收敛**：`round_sequence`（3 写者）退役 → 顺序由键内 `(round, seq)`；保留 `session_owns` / `cross_db_link` / `history_owns_task`（归属语义）。

---

## 8. 去重与退役

| 载体 | 现状角色 | 处置 | 前置 |
|---|---|---|---|
| `archive-raw`（L-C） | 归档原文 | **升为原文真源** | 轮次接入 `ISequenceAuthority` |
| `session-view`（L-B） | LLM 上下文副本 | 二选一：① 缓存（引用+摘要）；② 退为纯读侧裁剪 | U1 案 A；wire 成本评估 |
| `ur_histories` | 会话存在性 + UI 对话 | 读侧切换 → `legacy`（禁写）→ `retired` | §7.1 前三行完成 |
| FsSink persist 帧 | 丢弃 | 转为**实时物化源**（§6） | 合并窗口 + writer-stream |
| `round_sequence` 边 | 时序链 | 退役（或降为派生缓存，单写者） | 键序数已可用 |

**可量化验收**：同一轮正文在库中出现次数 3 → 1（archive-raw）；`(container, round)` 一次枚举出全部位置；`ur_histories` 写命中归零。

---

## 9. 分期迁移

> 与旧版相比**重排**：先做"键/JSON 单源"（让后面的实时与回写有落脚点），再做"实时物化"，最后才动读侧与退役。

### 阶段 A：键与 JSON 单源（不动读侧）
| 步 | 内容 | 验收 |
|---|---|---|
| A1 | 轮次/序号单源 `ISequenceAuthority`（`ctx.Round` 与 `Archive.NextRoundAsync` 收敛） | 种子 / L-B 键 / L-C 键 / 载荷 `round` 四者一致 |
| A2 | 会话链 schema 收敛：`RoundSpec/Steps` → `ChainInfo<string,NodeConfig>`（字段一一对应映射，双读并行） | 同一会话可用两种入口执行且产出等价 |
| A3 | 键映射接入既有 `KeyProcessor`（替代 `KeyMappingKeys` 手拼） | 键改名可声明化 |
| A4 | 边收敛：`round_sequence` 三写者 → 单一写者或退役 | 边写点计数 ≤ 1 |
| A5 | 节点快照构造单源（消 D4） | 两 runner 共用实现 |

### 阶段 B：实时物化（§6）
| 步 | 内容 | 验收 |
|---|---|---|
| B1 | persist 帧接入 `NodeMaterializer`（含合并窗口 + `writer-stream`） | 帧到达 N 毫秒内面板可读；重放不重复写 |
| B2 | 帧写与轮末写位置分离（writer 不重叠） | 单写者判定零冲突 |
| B3 | 读侧 Origin 叠加（Live→Snapshot 无空窗） | 帧前后读一致 |

### 阶段 C：回写合一（§5）
| 步 | 内容 | 验收 |
|---|---|---|
| C1 | 归档（L-C）与入史（L-B）共用同一 Key（轮粒度） | 两位置可由 `(container,round)` 互相枚举 |
| C2 | 读侧裁剪上移：`HistoryMarkPolicy` → `selection: marked` | 写侧不再按标记另存 |
| C3 | `session-view` 去重（引用+摘要 或 纯读侧裁剪） | 同轮正文库内出现次数达目标 |

### 阶段 D：读侧切换与退役
| 步 | 内容 | 验收 |
|---|---|---|
| D1' | 会话存在性枚举改位置（`ListSessionsAsync`） | 新旧会话均可枚举 |
| D2' | UI History/Task 视图切 `presentational` 读面 | 迁移会话与新建会话显示一致（对拍） |
| D3' | `ur_histories` → `legacy`（禁写）→ 观察期 → `retired` | 写命中归零；无读命中 |
| D4' | 移除 FsSink 钩子的"只日志"路径；`AgentInvocation.Round` 退役（R5） | 死路径清零 |

---

## 10. 状态对照与未决项

### 10.1 既有缺陷清单

| 编号 | 状态 | 新增说明 |
|---|---|---|
| E0（跨轮键覆盖） | ✅ 已修 | 键含轮次 + `ParseRound/Compare` |
| E1（命名空间分裂） | ✅ 已修 | 记录命名空间语义固化 |
| E2（会话根 Location） | ⚠️ 误诊 | `Location` 非身份字段，`session_owns` 可读 |
| E3（轮次 off-by-one） | ✅ 部分 | `ctx.Round` 单源；跨平面单源待 **A1** |
| E4/E5/E8 | ✅ 已修/加固 | Mark 白名单、空端口守卫、归档键解析 |
| E6（诊断进程静态） | ⏳ | 通用篇禁 P6；Agent 侧随 B1 一并改实例级 |
| E7/R5（归档轮次 vs request.Round） | ⏳ | 由 **A1** 统一 |
| E9（ThreadId 双约定） | ⏳ | 由位置枚举替代（D2'） |
| E10（wire 回退口径） | ⏳ | D2' 后退役 |
| D1（ur_histories 双写） | ⏳ | C/D 阶段 |
| D2（round_sequence 三写者） | ⏳ | **A4** |
| D3（端口写循环两处） | ⚖️ 有意保留 | 事务路径 vs 写门路径语义不同 |
| D4（快照两处构造） | ⏳ | **A5** |
| D5/D6（键解析/会话投影） | ✅ 已修 | 统一到 `SessionMessageKeys` / `ProjectSessionNodesAsync` |
| R1/R2/R3/R4/R6 | ⏳ | 随 A/B 阶段清理 |

### 10.2 未决项（需裁决）

| # | 问题 | 影响 |
|---|---|---|
| **U1** | 身份粒度：**轮为节点**（案 A，推荐）vs **消息为节点**（案 B） | 决定 §5.3 载荷形状、去重幅度、裁剪位置 |
| **U2** | `session-view`（L-B）保留为缓存还是纯读侧裁剪 | 去重幅度（3→1 或 →2）与 wire 成本 |
| **U3** | 双轨键空间合并规则（§3.3 案 A/B/C） | 决定"搬运代码"能否彻底消失（N12 达成度） |
| **U4** | 实时物化的合并窗口时长与失败重试策略 | 写放大 vs 实时性 |
| **U5** | 会话链切 `NodeConfig` 的迁移窗口（`RoundSpec` 是否保留双读） | 迁移期复杂度 |
| **U6** | 存量 `ur_histories` 行与节点位置的**对齐方式**（是否需要身份对齐表） | 存量化会话能否平滑并轨 |

> 框架侧另增未决项 **U7–U11**（反射兜底端口取舍 / 改图指令载体 / 运行态阶段映射 / 层次化键格式 / 双轨键空间合并时点），见 [提取清单与复用映射](./节点通用框架-提取清单与复用映射.md) §8；其中 U11 即本表 U4 的落地时点。 




