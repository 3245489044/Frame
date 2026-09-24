# Agent 应用篇 · 实施步骤（可执行细化版）

> **定位**：本文件是 [`节点通用框架-Agent应用篇.md`](./节点通用框架-Agent应用篇.md)（设计）与 [`下一步计划.md`](./下一步计划.md) §3（总览索引）的**可执行细化版** —— 每步给出「步 ID / 文件 / 动作 / 验收 / 回退」，可直接照做。
> **口径**：全部落点经本仓库实测核对（标 `实测`）；与设计文档冲突者以「§0 口径校正」为准。
> **通则**：每步遵循「**先加对拍用例 → 再改件 → 跑既有基线**」三步；一件一验，禁一次全改（`[CR: formula:SM.IncrementalCompileFlow@consume]` crit:micro）。

---

## §0 口径校正（实测，四条与设计文档不一致处）

| # | 设计文档口径 | 实测事实（证据） | 本文件采用 |
|---|---|---|---|
| ① | §3.1「会话链 JSON 由模板/声明生成，**替代** `RoundSpec/Steps`」 | Agent 定义源**已是 `.ushar`**：`Projects\Plug\Agent\Config\workflows\{agent-loop,collect-analyze-act,session-cline,session-answer,session-lifecycle}.ushar`；同名 `*.json` 全量在 `Config\workflows\_deprecated\`；`RoundSpecLoader.cs:18-25` 注释「真源形态 = Usharp 声明；同名 .json 模板层已退役（C2 消灭）」 | **A2 = 在既有 ushar 发射产物旁增加 `ChainInfo<string,NodeConfig>` 投影（双读等价）**，不新建 JSON 链 |
| ② | §1.2「Agent 用 `RoundSpec/Steps` ❌ 未用 `Chain<..>`」 | `RoundSpec` 是 ushar 的**编译产物**，非待淘汰临时 schema | 等价门 = 「同一 `.ushar` 两入口产出等价」 |
| ③ | §7.2 写点表 | 三处均存在：`Agent.Orchestration\SessionWorkflow\NodeWrite\PersistHistoryBlock.cs:28`、`Frame\CsharpPrj\Operation\HierarchyMethod\History\HistoryProjector.cs:100`、`Agent.Modules\Cline\HistoryClineMessageArchive.cs:47`（实测） | 写点表直接作为 C/D 阶段改造清单 |
| ④ | §10.1「E6 诊断进程静态 ⏳」 | `ClineDiagnosticsHosting` 为 `static class` + `Interlocked` 进程静态（`ClineDiagnosticsHosting.cs:18-49`，实测） | **B1 隐藏前置**：必须先把 `OnPersistFrame` 改实例级，否则多会话串味 |

**另两条直接影响排期的实测结论**：

| 结论 | 证据 | 影响 |
|---|---|---|
| `Modules.Node` **未被 Agent 引用**，且**不在烘焙闭包/manifest** | Agent 全 csproj grep `Modules.Node` = 0 命中；`Projects\BuildFramework\CsharpBakeRefs.props:94` 仅 `WorkFlowMethod`；`csharp-bake.manifest.json`（99 项）无 `Modules.Node` | P0-1 必须先把它纳入烘焙闭包 → manifest → 引用清单 |
| 载体适配器（D-1）**零实现** | 全库 grep `INodeCarrier\|CarrierRegistry` 生产侧 = 0 命中（仅框架 `Null/InMemory`） | **P0-3 是阶段 B 硬前置**，非"宿主可延后项" |

**层序安全**（实测）：`Modules.Node.csproj` = `ModuleRule_Layer=2`、`net10.0-windows`、零 `ProjectReference` ⇒ Agent（层 4）向下引用合规，不触发 `Test-ProjectReferences.ps1` 违规。

---

## §1 阶段 0：接线与声明（后续所有阶段的前置）

| 步 | 动作 | 落点（实测路径） | 验收 | 回退 |
|---|---|---|---|---|
| **P0-1** ✅ | ① 确认 `Modules.Node` 进入烘焙闭包（`Modules.csproj:38` 已有 `$(Proj_Node)` ProjectReference，实测；manifest 缺项则重跑 `Generate-CsharpBakeManifest.ps1`）；② 在烘焙引用清单 ① 段增 `<CsharpBakedAssembly Include="Modules.Node" />` | `D:\Project\Main\Projects\BuildFramework\CsharpBakeRefs.props`（① 具名清单）；消费方 = Agent 全域（经 `Agent\Directory.Build.props:56` Import） | `Check-BakeFramework.ps1` #3 通过（引用均在 manifest 内）；`dotnet build Agent.Core.csproj` / `Agent.Modules.csproj` 0 警告 0 错误；`Test-ProjectReferences.ps1` 无层序违规 | 删该行即回落（清单为单点，无消费方改动） |
| **P0-2** ✅ | 落 Placement 声明（设计 §4 草案，**按框架契约校正为 4 个可寻址位置**，见 §11 D-1/D-2）+ 宿主装载点 | 新增 `...\Agent\Config\nodes\conversation.json`；装载点 `...\Agent.Modules\NodeSpec\AgentNodeSpec.cs`（`NodeFrameworkSpecLoader` + `NodeFramework.FromSpec`；**不得**进 `Modules.Node`，门禁 D） | 声明命中且 **0 Error 诊断**；4 位置 + writer 一对一；`legacy-history` 拒写可读；`FromSpec` 写侧候选 = 3；声明缺失 → `null` + 可见化 | 声明文件删除 → 装载点返回 `null` + 日志（框架回 Null 端口 + 诊断，不阻断） |
| **P0-3** ✅ | 载体适配器（D-1）落四件（**每位置一个 carrier id**，见 §12 C-2）：`sql-nodes-archive`（归档，`cline_messages` + 容器作 ThreadId）、`sql-nodes-session`（会话视图，`conversation_message`）、`sql-nodes-task`（业务节点，`NodeTypes.Node`）、`sql-kv-legacy`（ur_histories 灰度面） | 新增 `...\Agent.Modules\NodeCarriers\{AgentSqlNodeCarrier,AgentSqlKvCarrier,AgentNodeCarriers}.cs`；声明 `placement.carrier` 同步改名 | 四原语对拍（幂等 upsert / AppendOnly 冲突不覆盖 / 前缀枚举跨分页 / 未装配拒写不抛）；**声明 carrier id ↔ 注册表** 一致性；`graph-edge` 归属边不进注册表（§12 C-4） | 单载体回退：装配处不注册该 id → 该位置 `Unbound` + 诊断，其它位置照常 |

---

## §2 阶段 A：键与 JSON 单源（不动读侧）

### A1 轮次单源 ✅（已落地；`seq` 按风险不接权威，见文末附注）
- **现状（实测双源）**：归档侧 `ClineDaemonEndpoint.cs:267` → `_archive.NextRoundAsync(sessionKey)`（归档 max+1）；执行侧 `ConversationCentralPipeline.cs:148` `ctx.Round = ((IAgentLoopState)item).Round + 1` → `RoundKeys.Round`(`:339`) → `cline_execute`(`ClineExecutorFunction.cs:141`) → `AgentExecutorRequest.Round`。
- **落地内容**：① 新增 `Agent.Modules\Cline\ClineArchiveRoundPolicy.cs`（`Resolve(requestRound, archiveDerived)` 调用方优先 + `SourceOf` 来源标签）；② `TryArchiveRoundAsync` 增 `requestRound` 参数，**优先取调用方轮次**；仅 `round ≤ 0`（直连调用/无会话归属）回退归档派生，并 emit `ClineArchive_RoundSource`（`source=request|archive-derived`）**可见化**（禁静默自推）；③ 归档方法文档同步改写。
- **验收**：新增 `ClineArchiveRoundPolicyTests`（3 例）—— 调用方优先 / 无会话上下文回退且来源可辨 / **L-C 键轮次 == L-B 键轮次 == 调用方轮次**（`cline.session.work.round.7` ↔ `session.work.r7.msg.1`）。
- **附注（A-1 发现，先登记不改）**：L-B 的 `seq`（轮内序号）由 `HistoryProjector.ProjectMarkedAsync` 每轮自 1 计数（`seq++`，`HistoryProjector.cs:116-123`），**已是轮内确定单源**；**不接** `ISequenceAuthority` —— 该权威只有 `TryNext/Peek`、**无按轮重置/set 语义**，接入后同一轮重投影将取到新序号 ⇒ 键漂移并破坏 `(round,seq)` 幂等（反使 `round_sequence`/L-B 节点重复）。故 A1 只收敛**轮次**；`seq` 权威保留为「`SessionMessageKeys.Build` + 投影计数」。

### A2 链 schema 单源（`RoundSpec` ⇄ `ChainInfo<string,NodeConfig>` 双读投影）
- **动作**：新增投影层（**不改** `UsharpRoundSpecEmitter`、**不新建** JSON 链）：`RoundStep.FuncName/Inputs/Outputs → NodeConfig.funcPipelineConfig.{FuncName,ParameterKeys,ResultKeys}`；`NextNodeId` 由 Steps 序推；`Callkey` 经 `KeyManager.GenerateCallkey`。
- **落点**：新增 `...\Agent.Core\Nodes\NodeChainProjection.cs`。
- **验收**：同一 `.ushar` 两入口执行产出**等价**（CT-U1 前置）。**A 阶段最高风险项**（N11 JSON 形态单源 / P8），必须先行对拍。
- **回退**：投影层纯新增，未被消费即零影响；出现回归即停止接线，保留 `RoundSpec` 直跑。

### A3 键映射接入既有 `KeyProcessor`
- **校正**：`KeyProcessor` 去 `AsyncLocal` + `KeyHierarchyFormat` 声明化**已在 P6-A 落地**（`下一步计划.md §1.1 A-3`）⇒ A3 退化为**消费该声明**：把 `HistoryClineMessageArchive.BuildNodeKey:103`、`SyncRelationsBlock.cs:78`、`PersistNodesBlock` 的键构造改经 `KeyProcessor.ProcessConnectionKeyMapping`。
- **验收**：键改名可声明化（改声明即生效）；`Check-NodeWriteBypass.ps1` A 项 EXIT 0。

### A4 边写点收敛（`round_sequence` 三写者 → ≤1）
- **现状（实测三写者）**：`SyncRelationsBlock.cs:78`、`HistoryProjector.cs:59`（`PersistRoundAsync`）、`HistoryProjector.cs:137`（`ProjectMarkedAsync`）。
- **动作**：`round_sequence` **退役**（顺序改由键内 `(round, seq)`）；保留 `session_owns` / `cross_db_link` / `history_owns_task`（归属语义）并统一 `writer-membership`。
- **验收**：脚本扫描"边写点计数 ≤ 1"。

### A5 节点快照构造单源（消 D4）
- **动作**：两个 runner（`WorkflowRunner` 经 `IRoundExecutionBackend`；`SignalWorkflowRunner`/`FoldConversationRunner`）的 `NodeExecutionRecord` 快照构造收敛为单实现。
- **验收**：两 runner 共用同一实现 + 对拍用例。

---

## §3 阶段 B：实时物化（设计 §6）

> **前置**：P0-1 + P0-2 + P0-3 **全部完成**（帧无处落则无法闭环）。

| 步 | 动作 | 落点 | 验收 |
|---|---|---|---|
| **B1** | ① `ClineDiagnosticsHosting` 由 `static` 改**实例级**（消 E6、去 `Interlocked` 进程静态）；② 帧 → 节点 JSON（`{NodeKey, WorkflowId, OutputPorts}`，键 `{session}.{node}.{seq}`，seq 由 `ISequenceAuthority` 分配）；③ 合并窗口（背压，毫秒级）+ 幂等（同 `(Key,port,Digest)` 跳过）；④ `NodeMaterializer.Materialize(...)`，placement=`archive-raw`/port=stream，writer=`writer-stream`；热路径 append 复用 `OutboxWriter` 模式（参照 `NodeStepPersistCoordinator.cs:58`） | `Agent.Modules\Cline\ClineDiagnosticsHosting.cs:57-64`（`OnPersistFrame`）+ 新增物化适配器 | 新增 `...\Agent.Modules.Tests\PersistFrameMaterializationTests.cs`：帧到达 N ms 内读侧可读；重放不重复写；崩溃恢复（已物化不丢） |
| **B2** | 帧写（`writer-stream`）与轮末写（`writer-task-node`/`writer-archive`）**位置分离**；结清 P1–P5 §6.3 遗留项：`NodeWriteGate` 的 target 口径（`carrier\|placementKey`）与 `NodeStepPersistCoordinator`（`workflowId`）粒度不一 → 装配处显式声明「节点级 ⊆ 库级」 | `Frame\CsharpPrj\Modules\Node\Materialization\NodeWriteGate.cs` 装配点 + `Agent.Orchestration\...\PersistNodesBlock.cs` | 单写者判定零冲突（`Check-SingleWriter.ps1` + 新增零冲突断言） |
| **B3** | 读侧 `Origin` 叠加（`Live > Snapshot > Derived`，字段级；`RunStateProjection` 单入口） | `Frame\CsharpPrj\Modules\Node\Projection\NodeReadProjection.cs` + 面板读路径 | 帧前后读一致（CT-5 风格） |

---

## §4 阶段 C：回写合一（设计 §5：三路 → 一次物化·多位置）

| 步 | 动作 | 落点（实测） | 验收 |
|---|---|---|---|
| **C1** | 归档（L-C）与入史（L-B）**共用同一 Key（轮粒度）**，改为"同一 Key 多 placement"物化 | `Agent.Modules\Cline\HistoryClineMessageArchive.cs:47/103`、`Agent.Orchestration\...\NodeWrite\PersistHistoryBlock.cs`、`Frame\...\HierarchyMethod\History\HistoryProjector.cs:100` | `(container, round)` 一次枚举出全部位置（CT-1 风格） |
| **C2** | 裁剪上移：`HistoryMarkPolicy`（写侧决定谁进历史）→ 声明 `selection: marked` 谓词（**读侧**过滤） | `Agent.Session\StandardContextBuilder.cs`、`Agent.Session\Wires\ConversationAgentWires.cs` | 写侧不再按标记另存副本（N7） |
| **C3** | `session-view`（L-B）去重（U2 裁决：缓存引用+摘要 / 纯读侧裁剪） | 同上 + `Agent.Launcher\Modules\Libraries\ConversationViewService.cs` | 同轮正文库内出现次数 **3 → 1**（U2 选"纯读侧裁剪"时） |

---

## §5 阶段 D：读侧切换与退役

| 步 | 动作 | 落点 | 验收 |
|---|---|---|---|
| **D1′** | 会话存在性枚举改位置（不再枚举 `ur_histories` 行） | `ConversationViewService.ListSessionsAsync` | 新旧会话均可枚举 |
| **D2′** | UI History/Task 视图切 `presentational` 读面；节点面板由**位置枚举**替代 threadId 并集（消 E9 双约定） | `ConversationViewService.cs:151-152`、`RenderTaskNodesPanelAsync` | 迁移会话与新建会话显示一致（对拍） |
| **D3′** | `ur_histories` 三态退役：**同一 writer** 收敛 `PersistHistoryBlock` 与第二通道 `MediaHandlers.HistoryStoreMediaHandler`（现违反 N3）→ `legacy`（拒写可读）→ 观察期 → `retired` | `PersistHistoryBlock.cs`、`MediaHandlers.HistoryStoreMediaHandler` | 写命中归零、读命中归零、观察期通过（CT-8） |
| **D4′** | 移除"只日志"路径（B1 后自然消失）；`AgentInvocation.Round` 退役（R5） | `ClineDiagnosticsHosting.cs`、`Agent.Contract\AgentInvocation.cs` | 死路径清零 |

---

## §6 执行顺序与门禁

```
P0-1 接线 ─► P0-2 声明 ─► P0-3 载体适配器（三实现）
                              │
A1 轮次单源 ─┬─► A3 键映射 ──► A4 边收敛 ──► A5 快照单源
             └─► A2 链双读投影  ←── A 阶段最高风险，单独一批
                              │
                              ▼
                     B1 帧物化 ──► B2 位置分离 ──► B3 Origin 叠加
                              │
                              ▼
                     C1 同键多位置 ──► C2 裁剪上移 ──► C3 去重
                              │
                              ▼
                     D1′ ──► D2′ ──► D3′ ──► D4′
```

### 6.1 每步出口命令

```powershell
# 框架侧（任一步后必跑）
dotnet build Frame\CsharpPrj\Modules\Node\Modules.Node.csproj
& Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1 -ShowDetail
& Frame\PowerShellScript\Modules\Node\Check-NodeWriteBypass.ps1 -ShowDetail
& Environment\CentralTest\Run-CSharpTests.ps1 -Test node

# 烘焙/层序（P0-1、B2、D3′ 必跑）
& Frame\PowerShellScript\Build\Check-BakeFramework.ps1        # 含 #3 引用∈manifest
& Frame\PowerShellScript\Share\Solution\Test-ProjectReferences.ps1

# Operation 侧（A2/A3/A4 触及 Operation 时）
& Environment\CentralTest\Run-CSharpTests.ps1 -Project 'WorkFlowMethod'

# Agent 侧（A/B/C/D 每步；Agent.Orchestration 无独立测试工程，
#            NodeWrite 用例入 Agent.Core.Tests，Cline 帧用例入 Agent.Modules.Tests）
& Environment\CentralTest\Run-CSharpTests.ps1 -Test ConversationPipeline -Project 'Agent\.Core'
& Environment\CentralTest\Run-CSharpTests.ps1 -Project 'Agent\.Modules'
& Environment\CentralTest\Run-CSharpTests.ps1 -Project 'Agent\.Launcher'
```

> 测试工程归属（实测）：`NodeWritePipeline` 现有覆盖在
> `Environment\CentralTest\AgentTest\Agent.Core.Tests\ConversationPipelineTests.cs`（641/643/648 行）。

---

## §7 开工前需裁决（会改变步骤形状）

| # | 问题 | 推荐 | 影响的步骤 |
|---|---|---|---|
| **U1** | 身份粒度：轮为节点 / 消息为节点 | **A 轮为节点** | P0-2 键模板、C1 载荷形状（设计 §5.3）、C3 去重幅度 |
| **U2** | `session-view`（L-B）：缓存引用+摘要 / 纯读侧裁剪 | 需定（决定 3→1 还是 3→2） | C2/C3 |
| **U4/U11** | 双轨键空间（`AxiomContext.Data`/`RoundKeys` vs `{node}.{port}`）合并时点 | **B 双轨并存至阶段 A 末** | A2/A4 的搬运代码消灭时点 |
| **U6** | 存量 `ur_histories` 行对齐方式（是否需身份对齐表） | 阶段 C 前定 | D1′/D3′ |

---

## §8 风险与不做

| 风险 | 触发点 | 缓解 / 回退 |
|---|---|---|
| **委托/投影后行为漂移**（最高） | A2 双读投影 | 先加对拍用例；旧入口保留只转发；回归即单点回退该件 |
| **实时物化写放大** | B1 | 合并窗口（毫秒级）+ `OutboxWriter` 异步落库；热路径只 append |
| **节点级/库级单写门缝** | B2 | target 口径显式登记「节点级 ⊆ 库级」（`NodeWriteGate` 装配处） |
| **`Modules.Node` 被上层绑定** | P0-1/P0-2 | 门禁 D（源码零 `using Operation.*` / `Share.*`）+ csproj 零 `ProjectReference`；DTO/适配器一律留 Agent 侧 |
| **帧级写与轮末写重叠** | B2 | writer 不重叠（`writer-stream` vs `writer-task-node`） |

**不做**（防 scope creep）：不在 `Modules.Node` 内引 `Operation`/`Share`/`Usharp` 类型或 DTO；不把表名/库名/控件写进框架；不承接执行体/状态根/终止裁决；不为未装配端口造兜底默认值；不写侧为某消费者裁剪副本；不做大爆炸式切换。

---

## §9 已落地记录（本文件配套）

| 步 | 落地事实 | 证据 |
|---|---|---|
| P0-1 | ① `Modules.Node` 进烘焙闭包（manifest 99 → 100）；② `Projects\BuildFramework\CsharpBakeRefs.props` ① 清单增 `<CsharpBakedAssembly Include="Modules.Node" />`（`Modules.Observe` 前，附闭包来源注释「经 `Modules.csproj` 的 `$(Proj_Node)` 进入本烘焙闭包，勿改为 Reference」） | §10 P0-1 |
| P0-2 | ① 声明 `Projects\Plug\Agent\Config\nodes\conversation.json`（`domain=conversation` / `identity={container}.r{round}` / `sequence=[round]` / 4 位置 / `selections` / `retentions`）；② 宿主装载点 `Projects\Plug\Agent\src\Agent.Modules\NodeSpec\AgentNodeSpec.cs`（`TryLoad` / `TryCreate`，缺失→null+可见化，**不静默兜底**）；③ 契约用例 `Environment\CentralTest\AgentTest\Agent.Modules.Tests\AgentNodeSpecDeclarationTests.cs`（5 例） | §10 P0-2 |
| P0-3 | ① `Agent.Modules\NodeCarriers\AgentSqlNodeCarrier.cs`（ur_nodes 载体，参数化 (nodeType, threadIdOf)，`afterId` 分页枚举）；② `AgentSqlKvCarrier.cs`（ur_histories 载体，载荷绑定 `ConversationLog` 既有序列化）；③ `AgentNodeCarriers.cs`（四载体唯一装配点 + 键↔ThreadId 约定）；④ 声明 `placement.carrier` 改为每位置一 id；⑤ 契约用例 `AgentNodeCarriersTests.cs`（8 例） | §10 P0-3 |
| A1 | ① `Agent.Modules\Cline\ClineArchiveRoundPolicy.cs`（轮次单源裁决 + 来源标签）；② `ClineDaemonEndpoint.TryArchiveRoundAsync` 增 `requestRound` 并优先取调用方轮次（无上下文才回退归档派生 + `ClineArchive_RoundSource` 可见化）；③ 契约用例 `ClineArchiveRoundPolicyTests.cs`（3 例） | §10 A1 |
| A5 | ① 新增 `Agent.Core\Nodes\NodeExecutionRecordFactory.cs`（**生产侧唯一构造点**：`FromExecution` / `FromStepPort` / `FromStepPorts`）；② 三处构造点改委托：`RoundStepInvoker.cs:227`（保留「产物→端口/溯源」语义）、`SignalWorkflowRunner.BuildNodes`、`FoldConversationRunner.BuildNodes`（两 runner 的私有 `BuildNodes` 收敛为一行 `FromStepPorts` 调用）；③ 用例 `NodeExecutionRecordFactoryTests.cs`（6 例：字段口径 / 跳过规则 / 两 runner 同形对拍） | §14 A5 |
| A4′ | **方案 C（真退役 + 读侧改键序）**：① `RefHead` 改按**键内序数**排序（`SessionMessageKeys.OrderComparer`，可由 `RefHeadContext.KeyOrder` 覆写），删除 `RoundSequence` 链式折叠；② `RefHeadContext` 以 `KeyOrder` 取代 `RoundSequence`；③ 退役边写原语全链：`IHistoryRelationManager.AddSequenceAsync` / `HistoryRelationManager` / `HistoryRelationGateway` / `ReferenceMaintenance.AddRoundSequenceEdgeAsync` / `ReferenceGrounding` / `NodeTaskHistoryService` / `CrossDbReferenceTypes.RoundSequence` / `ReferenceTypes.RoundSequence`；④ 删三写点：`SyncRelationsBlock`（③ 段）、`HistoryProjector.PersistRoundAsync` / `ProjectMarkedAsync`（`round_sequence` 写点 3 → **0**） | §14 A4′ |
| A3 | **分支 2（登记不改码）**：实测 `KeyProcessor.HierarchyFormat` 为 `_cN` 层次**后缀族**（`KeyManager.EnsureChainSuffix` → `_c{index}`）、`ProcessConnectionKeyMapping` 仅作用于 `Chain<string,NodeConfig>` 的 `ParameterKeys`，与 Agent 会话键族（`cline.session.{k}.round.{r}` / `session.{k}.r{r}.msg.{s}`）**不同族** ⇒ 接入会改写存量键形态（D6 坑）。**零代码改动**，结论见 §14 | §14 A3 |
| A2 | **登记不改码**：实测 `RoundSpecChainMapper`（`MapToChain`/`MapNode`/`ToJson`）**零生产消费者**（仅 global using 别名 + 测试端口夹具）⇒ 双真源风险未兑现；且 `Emits`/`Mark` 无可用透传落位（裁决 B 不可行，见 §14）⇒ Chain 路径**激活前**须重裁决落位 | §14 A2 |

---

## §10 验收记录（实测）

### P0-1 接线（已完成）

| # | 动作 | 命令 | 结果 |
|---|---|---|---|
| 1 | 确认 `Modules.Node` 进烘焙闭包 | `Generate-CsharpBakeManifest.ps1 -Configuration Debug` | ✅ **99 → 100 程序集**；`Modules.Node` = `exists=True / fresh=True / project=Modules\Node\Modules.Node.csproj`；`missing=0 / stale=0`；stamp 保留 |
| 2 | 烘焙引用清单增行 | 编辑 `Projects\BuildFramework\CsharpBakeRefs.props` | ✅ `+4` 行（1 条 `CsharpBakedAssembly` + 3 行注释） |
| 3 | 烘焙门禁 #3（引用 ∈ manifest） | `Projects\BuildFramework\Check-BakeFramework.ps1` | ✅ `3) CsharpBakeRefs.props：52 个烘焙轨引用全部在 manifest 内`（原 51） |
| 4 | Agent 侧编译可见性 | `dotnet build Projects\Plug\Agent\src\Agent.Core\Agent.Core.csproj -v q` | ✅ **0 警告 0 错误**；`Projects\Plug\Agent\BuildOutput\Debug\Modules.Node.dll`（172544 B）已生成 → 引用生效 |
| 5 | 层序/循环 | `Frame\PowerShellScript\Share\Solution\Test-ProjectReferences.ps1` | ✅ **硬违规 0 / 循环 0**，反引报告无 `Node` 新增条目 |

> **残留项（**非**本次改动引入，实测与本次 diff 无关）**：
> - `Check-BakeFramework.ps1` #5 报 `FileDriven（聚合轨/源轨）误置 RequireCsharpPrjBaked=true`（既有；本次改动面仅 `CsharpBakeRefs.props`，未触 FileDriven）。
> - `Test-ProjectReferences.ps1` 退出码 1 由既有 4 条 `FunctionExecutionFramework` 悬空引用 + 3 条未属性化相对路径导致（与 P1–P5 记录一致，与 Node 无关）。

### P0-2 声明面（已完成）

| # | 动作 | 命令 | 结果 |
|---|---|---|---|
| 1 | 落声明 + 装载点 | 新增 `Agent\Config\nodes\conversation.json`、`Agent.Modules\NodeSpec\AgentNodeSpec.cs` | ✅ 声明经 `ConfigFileLoader.Resolve` → `ConfigSourceResolver` 命中 `Projects\Plug\Agent\Config\nodes\conversation.json` |
| 2 | `Agent.Modules` 编译 | `dotnet build Projects\Plug\Agent\src\Agent.Modules\Agent.Modules.csproj -v q` | ✅ **0 警告 0 错误**（⇒ `Modules.Node` 类型在 Agent 侧可见，P0-1 端到端成立） |
| 3 | 契约用例（新增） | `Run-CSharpTests.ps1 -Test AgentNodeSpecDeclaration -Project 'Agent\.Modules'` | ✅ **通过 5 / 失败 0** —— 含 ① 0 Error 诊断；② 4 位置 + writer 一对一；③ `legacy-history` = `PlacementState.Legacy` 且拒写可读；④ `FromSpec` 装配后写侧候选 = 3（legacy 被排除）；⑤ 声明缺失 → `null`（可见化，不静默） |
| 4 | 该工程全量基线 | `Run-CSharpTests.ps1 -Project 'Agent\.Modules'` | ✅ **通过 32 / 失败 0**（27 既有 + 5 新增，零回归） |

---

### P0-3 载体适配器（已完成）

| # | 动作 | 命令 | 结果 |
|---|---|---|---|
| 1 | 四载体落码 | 新增 `Agent.Modules\NodeCarriers\{AgentSqlNodeCarrier,AgentSqlKvCarrier,AgentNodeCarriers}.cs` | ✅ `Agent.SqlNodeCarrier` 参数化 (carrierId, nodeType, threadIdOf, semantics, pageSize)；`AgentSqlKvCarrier` 绑定 `ConversationLog.Serialize/Deserialize` |
| 2 | 声明同步 | `Config\nodes\conversation.json` 的 `placement.carrier` → `sql-nodes-archive` / `sql-nodes-session` / `sql-nodes-task` / `sql-kv-legacy` | ✅ 每位置一 id（`ICarrierRegistry` 按 id 单例查询，共用 id 会退化为同一 nodeType/线程约定） |
| 3 | `Agent.Modules` 编译 | `dotnet build ...\Agent.Modules.csproj -v q` | ✅ **0 警告 0 错误** |
| 4 | 契约用例（新增 8 例） | `Run-CSharpTests.ps1 -Test AgentNodeCarriers -Project 'Agent\.Modules'` | ✅ **通过 8 / 失败 0** —— ① upsert→read 往返（JSON+摘要+ThreadId）；② 同载荷幂等（不产第二行）；③ AppendOnly 异摘要 → `Conflict` 且**不覆盖**；④ 前缀枚举 **跨分页**（pageSize=2）无漏项；⑤ sql-kv 往返 `ConversationLog`；⑥ 未装配库 → `Rejected` + `carrier.unbound` 诊断（不抛）；⑦ **声明 carrier id ↔ 注册表** 全量一致；⑧ ThreadId 提取取**末尾**标记（含容器自带 `.round.` 场景） |
| 5 | 该工程全量基线 | `Run-CSharpTests.ps1 -Project 'Agent\.Modules'` | ✅ **通过 40 / 失败 0**（32 前序 + 8 新增，零回归） |
| 6 | 框架门禁 | `Check-NodeFramework.ps1` | ✅ A/B/C/D 通过，告警 0（扫描 29 文件；框架源码未改动） |

---

### A1 轮次单源（已完成）

| # | 动作 | 命令 | 结果 |
|---|---|---|---|
| 1 | 策略件 + 接线 | 新增 `Agent.Modules\Cline\ClineArchiveRoundPolicy.cs`；`ClineDaemonEndpoint` 调用点与 `TryArchiveRoundAsync` 传入/优先 `request.Round` | ✅ 无上下文（`round≤0`）才 `NextRoundAsync` 回退，且 emit `ClineArchive_RoundSource` 标明来源 |
| 2 | `Agent.Modules` 编译 | `dotnet build ...\Agent.Modules.csproj -v q` | ✅ **0 警告 0 错误** |
| 3 | 契约用例（新增 3 例） | `Run-CSharpTests.ps1 -Test ClineArchiveRoundPolicy -Project 'Agent\.Modules'` | ✅ **通过 3 / 失败 0** —— ① 调用方轮次优先（`Resolve(7,99)=7`）；② 无上下文才回退（`Resolve(0,99)=99` 且来源=`archive-derived`）；③ **L-C 键 / L-B 键 / 调用方轮次同轮** |
| 4 | 该工程全量基线 | `Run-CSharpTests.ps1 -Project 'Agent\.Modules'` | ✅ **通过 43 / 失败 0**（40 前序 + 3 新增，零回归） |

---

## §11 声明面发现（设计 §4 草案 ↔ 框架声明契约）

| # | 设计 §4 草案 | 框架声明契约（实测） | 本批采用 |
|---|---|---|---|
| **D-1** | `session-membership` / `task-membership` 两位置（`carrier: graph-edge` + `refType`，**无 `key`**） | `ParsePlacements` 要求 `id/carrier/key/writer` **四者齐备**，缺一即 `spec.placement-incomplete`（Error）并跳过；`PlacementSpec` **无 `refType` 字段** | **不进本声明**：归属边不是"节点位置"（无键可寻址），归 P0-3 载体适配器的边写入配置（`refType` 随适配器声明）。本批声明 = 4 个可寻址位置 |
| **D-2** | `"retention": "legacy"` | `PlacementRetention` = `{Contextual, Audit, Presentational}`（**无 Legacy**）；灰度退役语义在 `PlacementState.Legacy`（`IsWritable=false` / `IsReadable=true`） | 声明改 `"state": "legacy"` + `"retention": "presentational"`（语义等价且被框架识别，已由用例③锁定） |
| **D-3** | — | `NodeFrameworkSpecLoader.ParseEnum` 对非法枚举值**静默回退**（无诊断）：声明写错 `form` / `retention` / `state` 不报错、不提示 | **登记为框架侧可见化缺口**（待治：`ParseEnum` 增 `spec.enum-invalid` 诊断）；本批不改框架（一件一验；框架侧改动须先加对拍用例） |
| **D-4** | — | `locToPlacement` / `referenceFields` 本批**留空**（未声明 ⇒ 空表：反解 ∅、改名不级联，均有可见化） | 留待 **P7 C-0**（`loc` 反转）与 **A-2.6**（引用字段声明化）补声明，避免本批凭空发明 loc 词汇 |

---

## §12 载体契约差异与落地决策（P0-3）

> P0-3 动手前核对两侧契约，发现 **1 处硬差异 + 1 处需裁决的约定**；**已按下列结论落地**（见 §10 P0-3），仅 C-4 只作登记不落码。

| # | 发现 | 实测证据 | 结论 / 建议 |
|---|---|---|---|
| **C-1** | `INodeCarrier.EnumerateKeysAsync(string? keyPrefix)` 与 `INodeStore.ListAsync(string? nodeType, string? threadId, long? afterId, int limit)` **口径不同**：载体按**键前缀**枚举，节点库按 **NodeType + ThreadId** 过滤，**无前缀枚举原语** | `Carriers\NodeCarrier.cs:51` vs `Operation\DataStoreDriven\Abstractions\NodeStore.cs:111` | **已落地**：适配器内置键 ↔ (NodeType, ThreadId) 约定 —— `EnumerateKeysAsync(prefix)` → `ListAsync(nodeType, ThreadIdOf(prefix))` + `StartsWith(prefix)` 过滤 + `afterId` 分页续取（用例④以 pageSize=2 锁定跨分页无漏项） |
| **C-2** | 该 (NodeType, ThreadId) 约定需**按位置分别给**（`archive-raw` → `cline_messages`/container；`session-view` → `conversation_message`/container；`task-node` → 业务节点类型/storage） | `NodeTypes`（`NodeStore.cs:58-94`）与 `HistoryClineMessageArchive.BuildNodeKey`（`cline.session.{k}.round.{r}`） | **已落地**：`ICarrierRegistry` 按 id 单例查询 ⇒ **每位置一个 carrier id**（`sql-nodes-archive`/`sql-nodes-session`/`sql-nodes-task`），`AgentNodeCarriers.Create` 集中声明 (nodeType, threadIdOf)；由用例⑦锁定「声明 id ↔ 注册表」一致 |
| **C-3** | `sql-kv`（`legacy-history`）走 `IHistoryStore.UpsertAsync(key, ConversationLog)`，载荷是**对象**而非 JSON 字符串 | `PersistHistoryBlock.cs:28` | **已落地**：`AgentSqlKvCarrier` 绑定 `ConversationLog.Serialize()`/`Deserialize()`（既有单源，禁第二套 JSON 库）；`ReadAsync` 摘要=对序列化文本取 `InMemoryNodeCarrier.ComputeDigest`（框架单源摘要函数） |
| **C-4** | `graph-edge`（归属边）无键可寻址，不满足 placement 契约（§11 D-1） | 同上 | 归属边**不进载体注册表**，归 `IHistoryRelationManager` / `INodeTaskLinker` 既有件（`writer-membership`），其"位置"仅作 `writer` 声明标签 |

**P0 阶段状态**：P0-1 / P0-2 / P0-3 **全部落地并验证**（§10）。**A 阶段**：A1 ✅ 已落地（§2 / §10 A1 / 附注 A-1）。

---

## §13 阶段 A 剩余步骤（**细化实施卡见** [`Agent应用篇-后续阶段细化方案.md`](./Agent应用篇-后续阶段细化方案.md)）

> 本表为索引；逐步的「前置取证 / 改动（含行号）/ 判据 / 回退」四段式实施卡、框架改造难度评估与判据命令矩阵已移入细化方案文档（本节仅留摘要）。

| 步 | 动作要点（实测落点） | 验收 | 风险 |
|---|---|---|---|
| **A3** | 键映射消费既有 `KeyHierarchyFormat` 声明，替代手拼：`HistoryClineMessageArchive.BuildNodeKey:103`、`SessionMessageKeys.Build`、`SyncRelationsBlock.cs:78` | 键改名可声明化（改声明即生效）；`Check-NodeWriteBypass.ps1` A 项 EXIT 0 | **中**：`KeyProcessor` 的层次化前后缀（`_cN`）语义须先核对，避免改写入键格式（存量键解析回归） |
| **A4** | `round_sequence` 三写者 → ≤1（`SyncRelationsBlock.cs:78`、`HistoryProjector.cs:59`、`HistoryProjector.cs:137`）；顺序改由键内 `(round, seq)` | 脚本扫描「边写点计数 ≤ 1」 | **中高**：涉及图边存量语义，须先加对拍（读侧 `SessionMessageKeys.OrderComparer` 已可替代时序边） |
| **A5** | 两 runner（`WorkflowRunner` 经 `IRoundExecutionBackend` / `SignalWorkflowRunner`）的 `NodeExecutionRecord` 快照构造收敛为单实现（消 D4） | 两 runner 共用同一实现 + 对拍用例 | 中 |
| **A2** | `RoundSpec ⇄ ChainInfo<string,NodeConfig>` 双读投影（**不改** ushar 发射器、**不新建** JSON 链） | 同一 `.ushar` 两入口产出等价（CT-U1 前置） | **高**：JSON 形态单源（N11/P8），须单独一批先行对拍 |

---

## §14 本批落地记录与验证（A 阶段收口：A5 / A4′ / A3 / A2）

> 口径：全部落点与判据**本轮实测**。A4 取**方案 C（真退役 + 读侧改键序）**（用户裁决）；A2/A3 按"取证后改写计划"收敛为**登记不改码**（依据见 14.3 / 14.4）。

### 14.1 A5 节点快照单源（已落地）

| # | 动作 | 判据 | 结果 |
|---|---|---|---|
| 1 | 新增 `Agent.Core\Nodes\NodeExecutionRecordFactory.cs`：生产侧**唯一** `new NodeExecutionRecord` 点；`FromExecution`（执行侧）/ `FromStepPort` / `FromStepPorts`（两 runner 共用形） | 构造点收敛 | ✅ 三处改委托：`RoundStepInvoker.cs:227`、`SignalWorkflowRunner.BuildNodes`、`FoldConversationRunner.BuildNodes`（后二者各收敛为**一行** `FromStepPorts` 调用） |
| 2 | 契约用例 `Agent.Core.Tests\Nodes\NodeExecutionRecordFactoryTests.cs`（6 例） | `-Test NodeExecutionRecordFactory` | ✅ **通过 6 / 失败 0**（字段口径 / `NodeKeys.DefaultWorkflowId` 非来源名 / 步骤序 / 无产出不造节点 / 无 ResultKeys 空集 / **两 runner 同形对拍逐值一致**） |
| 3 | 工程编译 | `Agent.Core` / `Agent.Orchestration` | ✅ 各 **0 警告 0 错误** |

> **取证修正（文档未列）**：`new NodeExecutionRecord` 的 grep 会命中**同名异类型** `Share.StateManagement.NodeExecutionRecord`（`Share\Foundations\StateManagement\Stages\LoopStage.cs:54`，通用循环簿记）—— 判据脚本须按**全限定类型名**限定，否则"仅工厂内 1 处"永不成立。

### 14.2 A4′ 边真退役（已落地，方案 C）

| # | 动作 | 结果 |
|---|---|---|
| 1 | **读侧改键序**：`RefHead.Project` 删除 `round_sequence` 链式拓扑折叠，改为按**键内序数**稳定排序（`SessionMessageKeys.OrderComparer`：轮次 → 轮内 seq → 键名字典序兜底）；`RefHeadContext` 以 `KeyOrder`（可选覆写）取代 `RoundSequence` | ✅ 跨模块改动零新增引用（`LoopFoldMethod` 已 `ProjectReference DataStoreDriven`） |
| 2 | **写原语全链退役**：`IHistoryRelationManager.AddSequenceAsync`、`HistoryRelationManager`、`HistoryRelationGateway`、`ReferenceMaintenance.AddRoundSequenceEdgeAsync`、`ReferenceGrounding`、`NodeTaskHistoryService.AddSequenceAsync`、`CrossDbReferenceTypes.RoundSequence`、`ReferenceTypes.RoundSequence` | ✅ 接口成员删净（"第二写点"不再可能编译通过） |
| 3 | **三写点删除**：`SyncRelationsBlock` ③ 段（`Agent` 侧）、`HistoryProjector.PersistRoundAsync` / `ProjectMarkedAsync`（`Frame` 侧） | ✅ `round_sequence` 写点 **3 → 0**；保留 `session_owns` / `cross_db_link` / `history_owns_task` 归属边 |
| 4 | 用例同步：`ProjectionTests`（改键序 4 例 + 纯度守护）、`GatewayTests`、`HistoryProjectorTests`、`HistoryMarkPolicyTests`、`HistoryRuntimeServiceTests`、`ExecutionFlagServiceTests`、`NodeTaskHistoryServiceTests`、`Agent.Persistence.Tests\HistoryRelationManagerTests`、`RecordingReferenceMaintenance` | ✅ 全部同步（假实现删成员 + 断言改键序） |

**取证关键发现（实测）**：`RefHeadContext` 全仓**生产侧零构造点**（唯一构造方为 `ProjectionTests`），`RoundSequence` **零生产者** ⇒ 方案 C 的"读侧消费者"实际只有测试，**实际风险远低于文档预估**（文档按 D4 列为中高风险）。

| 判据命令（实测） | 结果 |
|---|---|
| `dotnet build Frame\CsharpPrj\Operation\HierarchyMethod\HierarchyMethod.csproj` | ✅ 0 警告 0 错误 |
| `dotnet test ...LoopFoldMethod.Tests --filter 'RefHead\|Gateway'` | ✅ **通过 10 / 失败 0** |
| `dotnet test ...HierarchyMethod.Tests --filter 'History\|NodeTaskHistory\|ExecutionFlag'` | ✅ **通过 51 / 失败 0** |
| `dotnet test ...Agent.Persistence.Tests --filter HistoryRelationManager` | ✅ **通过 6 / 失败 0** |
| `dotnet test ...Agent.Core.Tests --filter ConversationPipeline` | ✅ **通过 35 / 失败 0** |

### 14.3 A3 键映射（登记不改码）

**实测证据**：`KeyProcessor.HierarchyFormat` = `KeyHierarchyFormat.CompatibleDefault`（`_` / `_` / 无分隔 / 十进制无填充）= 层次**后缀**族，服务 `KeyManager.EnsureChainSuffix` 的 `_c{index}` 键；`ProcessConnectionKeyMapping(connection, keyMappingJson, List<Chain<string,NodeConfig>>)` 的作用域是 **Chain 节点的 `ParameterKeys`**，其入参类型即 `WorkflowConnection<string,string>`。
**结论**：Agent 会话键族（`cline.session.{k}.round.{r}` / `session.{k}.r{r}.msg.{s}`，由 `HistoryClineMessageArchive.BuildNodeKey` / `SessionMessageKeys.Build` 单源生成）**不含 `_cN` 结构** ⇒ **两族不同族**，强行接入即改写存量键形态（D6 静默失效）。
**采用**：**分支 2** —— 零代码改动；"Agent 侧零手拼键"由门禁 `Check-NodeWriteBypass.ps1` A 项守护。**回退**：无代码即无回退需求。

### 14.4 A2 链双读（登记不改码；裁决 B 实测不可行）

**实测证据（三条，均改变计划）**
1. `RoundSpecChainMapper.MapToChain` / `MapNode` / `ToJson` **零生产消费者**：全仓命中仅 = `GlobalUsings`（两个类型别名）+ `Agent.Spec.Guard.Tests` 端口夹具 + `BuildOutput\ref\PortFixtures.Scheduler.cs` + `SpecGen-draft` ⇒ **Chain 路径尚未激活**，N11/P8 双真源风险**未兑现**。
2. `RoundStepMapping.Project` 仅投影 6 字段，`Emits`（`RoundSpec.cs:47`）/ `Mark`（`:54`）确实丢失 —— 缺口属实。
3. **裁决 B（借 `addNodeConfig.AddParameters` 透传）实测不可行**：该字段已被 `OutputMapping` JSON 占用（`RoundSpecChainMapper.MapNode:62`）；同族候选亦无空闲槽位 —— `funcPipelineConfig.Params` 被 `ParameterConfigProvider:51-54` 当参数配置读取、`nextNodeConfig.ConnectChainInfoConfig` 被 `NodeConfigFactory:134` 解析并参与 `IsInsertOperation` 判定（`Dtos.cs:104`）。
> ⇒ **`NodeConfig` 上不存在可安全借用的透传字段**；原裁决 B 的前提不成立。

**采用（登记）**：本批**不改码**（Chain 路径无消费者 ⇒ 改动无判据可验，属"不可验证的改动"）。**激活 Chain 路径的前置条件**登记为：先在 ①扩 `NodeConfig` 只读透传字段（触 `WorkFlowMethod`/`Share` 模型，需重烘焙）与 ②读侧直读 `.ushar`（双真源，不选）之间**重新裁决**，并先行落等价门用例。
**回退**：无代码即无回退需求。

### 14.5 出口门禁（实测）

| 门禁 | 结果 |
|---|---|
| `Check-NodeFramework.ps1` | ✅ A/B/C/D 通过，告警 0（扫描 29 文件） |
| `Check-NodeWriteBypass.ps1` | ✅ A/B/C 通过，告警 0（扫描 155 文件） |
| `Check-SingleWriter.ps1` | ✅ 无违规（写收敛库级通道 + 读不绑进程写门），告警 46 |
| `Test-ProjectReferences.ps1` | **硬违规 0 / 循环 0 / 悬空 0 / 未属性化 3**（退出码 1）—— 与 P0-1 记录一致（既有项；本次仅改 Frame `Operation` 源码，**未动任何 csproj**） |

> **链路说明**：本批改了 Frame 源码（`Operation`）⇒ 消费侧走**重烘焙轨**。实测先建 Frame 工程、再建 Agent 工程即取到新产物（`EnsureCsharpBaked` 兜底生效）；**新增程序集为零** ⇒ `CsharpBakeRefs.props` 无需增行。

### 14.6 顺带发现／修复的既存项（非本次引入）

| # | 位置 | 事实 | 处置 |
|---|---|---|---|
| 1 | `HierarchyMethod.Tests\TestDoubles\FakeHierarchyReferenceManager.cs` | 未实现接口成员 `IHierarchyReferenceManager.ProjectSessionNodesAsync`（接口已含、假实现滞后）⇒ 该测试工程**编译不过**（先于本次改动存在） | ✅ 补齐假实现（按键内序数序产出 `NodeProjectionItem`） |
| 2 | `HistoryMarkPolicyTests.cs:90` | 断言仍用**旧无轮次键** `session.sess-1.msg.1`，实际键已含轮次段 ⇒ 既存失败 | ✅ 改为 `session.sess-1.r2.msg.1`（`SessionMessageKeys` 权威） |
| 3 | `LoopFoldMethod.Tests.csproj:23` | 直接 `ProjectReference` 到 Frame ⇒ 触发 `ReferenceBoundary`（默认 `error`），该工程**不能**以默认属性构建 | ⏳ 登记（本次验证用 `-p:Restriction_Enforce=off` 覆盖，与 targets 自述"可在命令行以 `-p:` 覆盖"一致）；**未改**（属既有边界债） |
| 4 | `Test-ProjectReferences.ps1` | 3 条未属性化相对路径 ⇒ 退出码 1 | ⏳ 登记（与 P1–P5 记录同源） |

### 14.7 本批未做（B/C/D 阶段，按批次推进）

B0–B3（实时物化接线）、C1–C3（回写合一）、D1′–D4′（读侧切换与退役）**未动**。理由与前置：
- **B 阶段前置已解除**（P0-1/P0-2/P0-3 已落；`NodeFramework.FromSpec` / `NodeMaterializer.MaterializeAsync` / `NodeReadProjection.Overlay` 均已实测在位）—— 其中 `NodeReadProjection.Overlay`（`Live > Snapshot > Derived` 字段级叠加，非空覆盖）**已实现** ⇒ **B3 降为接线**而非实现。
- **B1 有一处计划冲突待裁决**：文档要求帧键 `seq` "由 `ISequenceAuthority` 分配"，但该权威只有 `TryNext`/`Peek`、**无按轮重置语义**（`SequenceAuthority.cs:6-13`）⇒ 与 A1 附注 A-1 同一坑（跨轮键漂移）。建议：帧键用 `{container}.r{round}.{node}.{seq}`，`seq` 取**轮内投影计数**，`ISequenceAuthority` 保持"全局单调"原义不复用。
- **C1 待评估是否触 Frame**（`HistoryProjector` 是否也改走 `NodeMaterializer`）；**D3′ 的"观察期 → retired"不可能在同一批内闭环**（CT-8 需跨批观测：写命中归零 → 观察期 → 读命中归零）。

---

## §15 B 阶段记录（B0 已落地；B1 三阻塞待裁决）

### 15.1 B0 装配（已落地）

> **取证发现（关键）**：`AgentNodeSpec` / `AgentNodeCarriers` 在 `Projects\Plug\Agent\src` 全仓**零调用点** —— P0-2/P0-3 只落了件（声明 + 四载体适配器），**框架从未被装配**，故 B 阶段消费方无框架可用。

| # | 动作 | 落点 |
|---|---|---|
| 1 | 新增槽键 | `Agent.Contract\Runtime\AgentRuntimeSlotKeys.cs`：`NodeFramework = "node-framework"` |
| 2 | 新增根分区槽（禁进程静态，消 E6） | `Agent.Launcher\Modules\AgentUiHost.cs`：`RootSlot<global::Modules.Node.NodeFramework>` + `NodeFramework`/`HasNodeFramework`/`AttachNodeFramework`/`DetachNodeFramework` |
| 3 | 端口面同步 | `Agent.Launcher\Modules\AgentUiHostAccess.cs`：`IAgentUiHostAccess.NodeFramework` + 实现（实时读取，槽与端口恒同一引用） |
| 4 | **唯一装配点** | 新增 `Agent.Launcher\Composition\NodeFrameworkHosting.cs`：`TryAttach(nodeProvider, historyProvider, overridePath)` → `AgentNodeCarriers.Create` + `AgentNodeSpec.TryCreate` → 挂槽 + 装配取证日志（identity/位置数/可写可读数/载体数/诊断分级）；声明缺失或装载失败 → `false` + 可见化，**不兜底默认** |
| 5 | 组合根接线 | `AgentComposition.RunSegmentDModulesAsync` 段 11c（`INodeStore` 注册之后、关系库之前）；载体 provider **惰性**解析（`IHistoryStore` 由 `ConversationHosting` 后段登记，调用时才取 ⇒ 装配序无关） |
| 6 | 契约用例 | 新增 `Agent.Launcher.Tests\NodeFrameworkHostingTests.cs`（5 例） |

**序数权威**：`TryAttach` **不注入** `ISequenceAuthority`（保持 `null`）—— 身份序数由调用方显式接管（帧键 `seq` 取轮内投影计数），不复用该权威的全局单调语义；框架回 `NullSequenceAuthority` + 惰性诊断（构造期 0 Error）。

**判据（实测）**

| 命令 | 结果 |
|---|---|
| `dotnet build Agent.Launcher.csproj` | ✅ 0 警告 0 错误 |
| `dotnet test Agent.Launcher.Tests`（全量） | ✅ **通过 90 / 失败 0**（含 B0 5 例：4 位置 + 4 载体 / 位置↔载体一致 / 声明缺失不装配 / 端口镜像 / 未装配库拒写可见化） |
| `dotnet test Agent.Modules.Tests`（全量） | ✅ 通过 43 / 失败 0 |

### 15.2 B1 三阻塞（实测；B1 原文口径不可直接落地）

`NodeMaterializer` 的既有契约（读码实测）：`values = TryParseValues(identity.Template, request.Key)`（`NodeMaterializer:86`）→ `placementKey = KeyTemplateEvaluator.Evaluate(spec.KeyTemplate, values)`（`:175`）。

| # | 阻塞 | 证据 | 后果 |
|---|---|---|---|
| **1** | **帧键无法被 identity 反解** | identity = `{container}.r{round}`（声明）；B1 规定帧键 `{session}.{node}.{seq}` | `materialize.key-unparsable`（`NodeMaterializer:86-94` Reject）。且 placement 键模板**只能用 identity 反解出的值**（`{container}`/`{round}`）⇒ **无法表达 per-frame 唯一性** |
| **2** | **writer 不匹配即拒写** | `:163-173` 要求 `request.WriterId == spec.WriterId`；`archive-raw` 声明 `writer: writer-archive`；B1 规定 `writer=writer-stream` | `write.writer-mismatch` → Rejected（写不进去） |
| **3** | **整域物化会抛异常** | `task-node` 键模板 `{storage}.{node}` 所需值不在 identity 中；`KeyTemplateEvaluator.Evaluate` 缺值时**抛 `InvalidOperationException`**（非诊断） | 任何按 `Domain`（未显式给 `PlacementIds`）的物化**直接崩**（P0-2 声明的潜在缺陷，属可见化缺口同族 D-3） |

### 15.3 B1 裁决选项（待定）

| 选项 | 内容 | Frame 改动 | 代价 |
|---|---|---|---|
| **B1-A** | 声明增位置 `stream-frame`（`key: cline.session.{container}.round.{round}.f{frameSeq}`、`writer: writer-stream`、`carrier: sql-nodes-archive`）；identity 扩为 `{container}.r{round}.f{frameSeq}`（`sequence` 仍 `["round"]`，`frameSeq` 由调用方显式给） | 否 | 引入 ur_nodes **新键族**（值级契约）；需同步 P0-2 声明断言与 `AgentNodeSpecDeclarationTests` |
| **B1-B** | 帧按轮聚合：`Key="{container}.r{round}"`、`placement=archive-raw`、`writer=writer-archive` | 否 | 与 `HistoryClineMessageArchive` **同键同位置同 writer**（"同事实第二写点"，与 B2「writer 不重叠」相反），只能作过渡态 |
| **B1-C** | 先治框架：`NodeMaterializer` 允许调用方提供**额外求值值**（`Extras`）+ `Evaluate` 缺值由"抛"改"诊断 Reject" | **是**（需重烘焙） | 与 §9「B 阶段 Frame 零改动」冲突，须你裁决；**但解掉阻塞 1 与 3（阻塞 3 是真实缺陷）** |
| **B1-D** | B1 暂缓，先做 C/D 阶段 | 否 | 实时物化目标延后 |

> **我的建议**：**B1-C 先行**（它是框架层正确性修复：既治 `task-node` 整域物化崩，又为 per-frame 唯一键提供正当表达），随后 **B1-A** 用 `Extras` 承载 `frameSeq`。若坚持 Frame 零改动，则只能取 **B1-B** 作过渡并显式登记"同事实第二写点"。
> **B2/B3 依赖 B1**（位置分离与读侧 Origin 都要先有帧写路径）⇒ 随 B1 裁决一并推进。

### 15.4 本批新增既存项登记（与 B 阶段无关）

| # | 位置 | 事实 | 处置 |
|---|---|---|---|
| 5 | `Agent.Spec.Guard.Tests` | **6 例失败 / 100 通过 / 106 总**（`SpecPortHostTests.AssembleHost_RegistersInlineBindingsIdempotently` 等，断言 `Unbound.Count == 0` 实际 26，属 ledger 端口绑定面） | ⏳ **登记为既存失败**。已做**对照实验**：将 B0 全部改动回退（4 文件回 HEAD + 2 新文件删除）后重跑，结果**完全相同**（6/100/106）⇒ 非本批引入。该测试工程不引用 `IAgentUiHostAccess`（全仓 grep：仅 src 3 处命中） |






