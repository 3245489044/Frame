# Agent 应用篇 · 后续阶段细化方案（A/B/C/D 实施卡 + 框架改造难度评估）

> **定位**：本文件是 [`Agent应用篇-实施步骤.md`](./Agent应用篇-实施步骤.md) 的**下一层细化** —— 把 A/B/C/D 剩余步骤拆成"**前置取证 → 改动文件（含行号）→ 判据命令 → 回退点**"四段式实施卡，并回答「这套框架改起来难在哪、怎么把难度降下来」。
> **口径**：全部落点**本轮实测**（标行号）；与上层文档冲突处已就地更正（见 §3 的「重要更正」）。
> **前置事实**：P0-1/P0-2/P0-3/A1 已落地并验证（框架件 100 用例、Agent.Modules 43 用例、两道门禁 EXIT 0）。

---

## §1 难度解剖：机械成本（跨仓/烘焙）≠ 认知成本（隐式契约）

**结论（更精确的版本）**：**单件代码改动量很小**（A1 只动 2 文件 3 处），成本体现在两类**不同性质**的支出上 —— 必须分开看，否则会把"慢"误当成"难"：

| 成本类型 | 来源 | 性质 | 量化（实测） | 对策 |
|---|---|---|---|---|
| **机械成本（可见化=已自动化，**不列为难度**）** | 跨仓 + 烘焙二进制轨 | 反馈环长度。**不影响方案，只影响节奏** | 官方路径 = **两条命令**（`dotnet build Frame\CsharpPrj\MainProj.sln` → `dotnet build Projects\Plug\Agent\Agent.sln`），**常规增量零烘焙·零 manifest·零手工**（`CsharpBakeDeploy.targets:6-14`）；`EnsureCsharpBaked` 仅在权威目录"确实未就绪"时兜底补烘（`BeforeTargets=ResolveAssemblyReferences`，`DesignTimeBuild=true` 不动）。唯一人工点 = **新增程序集时**在另一仓登记一行（一次性） | 改 Frame 后跑命令 1；新件登记一次，**无重复成本** |
| **认知成本（"难"）** | 框架不变式是**隐式**的：键即地址、单写门、三层分离 | 消费者**不写在写点上**，写侧看不到读侧 | 本轮 4 项计划，**取证后 4/4 被改写**（见下） | §1.1 取证清单；先加对拍/等价门再改件 |

**跨仓的真实关系（更正：本条收回，不计入难度）**：不该把"不可见"当难点——**可见化本来就自动化**（见上表）。而且**不能**简单改用 `ProjectReference` 把它"换成可见"：`Projects\ReferenceBoundary.targets`（`Restriction_Enforce` 默认 **`error`**，`BeforeTargets=ResolveProjectReferences`）**全架构禁止非 Frame 工程直接 ProjectReference 到 Frame**；其注释给出的理由（原文）：直接源引用会把 Frame 子图拉进消费方构建图，并与 `EnsureCsharpBaked` 的 CLI 烘焙**重复生产同一共享权威 DLL**（`Operation.LoopFoldMethod` 即实例）⇒ 共享输出目录冲突与编译异常。⇒ 可见化的**正规途径本就是烘焙轨**（`<Reference HintPath="$(CsharpBuildOutputDir)\...dll">`），而它无需人工干预。
**取证的真实关系**：它不是流程仪式，而是**这类框架的唯一降错手段** —— 因为约束分布在运行时契约里（边谁在读、门 target 粒度、键的生成/解析单点），只能先问清再动手。

### §1.1 取证的实测价值：4 项计划，4 项被改写

| 计划原口径 | 取证后事实（实测） | 改写结果 | **成因归因** |
|---|---|---|---|
| A2「**新增**投影层 `NodeChainProjection`」 | `RoundSpecChainMapper.MapToChain/MapNode/ToJson` **已存在**（`RoundSpecChainMapper.cs:25/39/68`） | 从"写新代码"→"补 2 个丢失字段（`Emits`/`Mark`）+ 锁等价" | **文档失真**（设计文档口径过时），与框架设计无关 |
| A3「键构造接入 `KeyProcessor`」 | `KeyHierarchyFormat` 是 `_cN` 层次**后缀族**（`KeyHierarchyFormat.cs:12-19`），与会话键族不同构 | 从"改码接入"→"先判族；不同族则**不改码**只登记" | **两子系统各自演进**（键族约定不一致），与框架设计无关 |
| A4「`round_sequence` **退役**」 | 有读侧消费者 `RefHead.Project`（链式拓扑折叠，`RefHead.cs:33-60`） | 从"退役"→"**单写者化**"（真退役降为 A4′） | **框架设计**（语义落在运行时契约：写点旁不声明"谁在读"）—— **4 项中仅此 1 项** |
| A5「**两** runner 快照收敛」 | 构造点实为**三**处（`RoundStepInvoker.cs:226` + 两个 `BuildNodes`） | 范围 +1 处，共享点落 `Agent.Core\Nodes\` | **文档失真**（清单不全），与框架设计无关 |

> **归因结论**：4 处返工中 **3 处是"文档与代码不同步/清单不全"，只有 1 处（A4）真正源于框架设计**（把不变式放在运行时契约里，而不在写点旁声明消费者）。⇒ **不能**说"框架设计导致修改困难"；准确说法是「**改动前的调查成本偏高**，且该成本的 3/4 来自配套文档缺失」。

> 这 4 项全是**纯读代码**得出的结论 —— 没有一项是"设计文档写清楚了的"。**跨仓影响的是这几步要跑多久；取证影响的是这几步会不会做错**。若把三仓并成单仓 + ProjectReference，机械成本会显著下降，但上表 4 处返工**依然会发生**（它们来自隐式契约，不来自仓库布局）。
>
> **关键区分**：这 4 处的对象**在源码里全都是"可见"的**（`RoundSpecChainMapper`、`KeyHierarchyFormat`、`RefHead`、三处 `new NodeExecutionRecord` 都公开可读）——问题**不是**访问性（能不能看到），而是**契约分散 + 没人会主动去查**：写点旁边不写"谁在读"，设计文档也没列消费者清单。所以"把不可见的换成可见的"**解决不了**这类返工；它只能靠**一次性的取证动作**（查清后写进文档，后续复用）。

### §1.2 七条结构性原因（D1 = 机械成本；D2–D7 = 认知成本）

| # | 难点 | 实测证据 | 影响 | 对策 |
|---|---|---|---|---|
| **D1** | **三仓分立 + 烘焙二进制轨（节奏项，非难度项）** | 三仓（`Frame` / `Projects` / `Environment`）；Agent 经 **Reference HintPath** 消费 CsharpPrj 产物（**不是 ProjectReference**，且 `ReferenceBoundary.targets` 以 `error` 级禁止非 Frame 工程源码引用 Frame） | 改 Frame 后需重建权威目录；**但已自动化**（`CsharpBakeDeploy.targets:6-14`：两条命令、常规增量零烘焙；`EnsureCsharpBaked` 兜底） | 命令 1 = `dotnet build Frame\CsharpPrj\MainProj.sln`；**仅新增程序集时**需在 `Projects\BuildFramework\CsharpBakeRefs.props` 登记一行（一次性） |
| **D2** | **契约边界（D-1 案 A）** | `Modules.Node.csproj` **零 `ProjectReference`**；门禁 D 禁源码 `using Operation.*` / `Share.*` | 想在框架里直接用 `NodeOutput`/`Chain<>`/`INodeStore` ⇒ 违 K1/K6/P20，必须改写为「端口 + 上层适配器」 | DTO/序列化/库访问一律落上层（本轮 `NodeCarriers`/`NodeSpec` 即此形）；框架只留「键 + JSON + BCL」 |
| **D3** | **单写门与写点唯一性** | 门禁 C：`WriteOutputAsync` 生产调用点仅 `NodeOutputPersistStage`；`NodeWriteGate` 同 target 平行写 **fail-fast**（不排队） | 新写点必须登记，否则门禁失败或运行时抛 | 新写点先在装配处登记 target 语义（「节点级 ⊆ 库级」）；禁旁路 |
| **D4** | **隐性读侧消费者（最易误判）** | `round_sequence` 看似"写侧边表"，实为 `RefHead.Project` **读**输入：沿时序边做链式拓扑折叠（入度 0 链头 → 后继链，`RefHead.cs:33-60`），`RefHeadContext.RoundSequence`（`RefHeadContext.cs:16`）、`IRefView.cs:17` 均依赖 | "退役某边/某写点"可能**静默打断折叠投影** | 任何删/退役前先做**反向引用扫描**（§2 取证清单第 1 条） |
| **D5** | **测试归属分散** | `Agent.Orchestration` **无独立测试工程**；`NodeWritePipeline` 现覆盖在 `Agent.Core.Tests\ConversationPipelineTests.cs` | 用例放错工程 ⇒ 跑不到/重复 ROI | 按落点选命令（§5）：Agent.Core/Core.Tests、Modules/Modules.Tests、Frame 侧 `Modules.Node.Tests`/`WorkFlowMethod.Tests` |
| **D6** | **存量键与数据兼容** | `SessionMessageKeys` 明写「键格式只此一处生成/解析，禁各处手拼」；`KeyHierarchyFormat` 默认 `_`/`_`/无分隔（`KeyHierarchyFormat.cs:12-19`） | 键格式一改 ⇒ 存量会话解析静默失效 | 键相关改动必须带**双向解析回归**（Build→Parse 往返） |
| **D7** | **门禁三件套 + 一件一验** | `Check-NodeFramework.ps1`(A/B/C/D)、`Check-NodeWriteBypass.ps1`(A/B/C)、`Test-ProjectReferences.ps1`（悬空/层序/环）；crit:micro 要求微步验证 | 一次大改后失败点难定位 | 出口命令矩阵（§5）+ 单点回退 |

> **一句话**：这套框架**不是"难改"，而是"改之前必须先问清谁在读写它"**。把取证做成清单后，每件的实际代码改动通常是 1–3 处。

---

## §2 改动前必做的取证清单（每件一张卡）

```
① 反向引用扫描：被改点有谁写、有谁读（grep 符号 + 语义族名，如 round_sequence / SessionMessageKeys）
② 键格式核对：是否改到键的形态（须双向解析回归；不改则免）
③ 层序与边界：是否越层（Frame→Agent 反向）、是否新增写点/旁路
④ 测试落点：用例放哪个工程（Agent.Orchestration 无测试工程）
⑤ 回退点：单点回退手段（删新增即回落 / 保留旧入口只转发）
⑥ 门禁影响：三件套中哪几件必跑（§5 矩阵）
```

**实施卡模板**（§3/§4 逐件按此填写）：

| 段 | 内容 |
|---|---|
| 前置取证 | 要问清的对象 + 已实测结论 |
| 改动 | 文件:行 → 动作（新增/改签名/改调用点/删写点） |
| 判据 | 用例名 / 脚本 / 计数上限 |
| 回退 | 单点回退手段 |

---

## §3 A 阶段实施卡（键与 JSON 单源）

### A2 链双读投影（`RoundSpec ⇄ ChainInfo<string,NodeConfig>`）

> **重要更正（本轮实测）**：映射**不是待建**——`RoundSpecChainMapper.MapToChain / MapNode / ToJson` **已存在**（`Operation\HierarchyMethod\Nodes\Relations\RoundSpecChainMapper.cs:25 / 39 / 68`）。A2 的真实内容是「**补字段 + 锁等价**」。

| 段 | 内容 |
|---|---|
| **前置取证** | 映射契约表（`RoundSpecChainMapper.cs:21-24`）：`FuncName→funcPipelineConfig.FuncName + basicConfig.Callkey`；`EffectiveNodeId→Id/Symbol`；`ParameterKeys/ResultKeys→JSON 串`；`InputMapping→nextNodeConfig.MapKeyConfig`；`OutputMapping→addNodeConfig.AddParameters`；`NextNodeKey=下一 EffectiveNodeId`（尾步空） |
| **实证缺口（对拍必暴露）** | `RoundStepMapping.Project`（`RoundStepMapping.cs:17-24`）只投影 **6 字段**，**未含** `Emits`（观测事件，`RoundSpec.cs:47`）与 `Mark`（入史标记，`RoundSpec.cs:54`）⇒ **Chain 路径丢失 2 个声明面字段 ⇒ 双读不等价** |
| **改动** | ① 先加等价门用例（不接口线）；② 补 `Emits`/`Mark` 承载（见下方裁决 A2-D1）；③ `MapNode` 同步补映射 |
| **判据** | 等价门逐字段比对全绿（含 `Emits`/`Mark`）；`RoundSpecChainMapper` 是**唯一** `RoundSpec→NodeConfig` 映射（禁第二处手写） |
| **回退** | 只加用例阶段零风险；接线后若回归 → 停止接线（映射未被消费即零影响） |
| **裁决 A2-D1** | `NodeConfig` 无 `Emits`/`Mark` 字段 ⇒ 落位三选：**A** 扩 `NodeConfig`（加只读透传字段，需动 `Share`/`WorkFlowMethod` 模型）｜**B** 经 `addNodeConfig.AddParameters` 以保留键名透传（零模型改动，但语义借用）｜**C** Chain 侧不承载、由读侧从 `.ushar` 直读（N11 双真源，不选）。**推荐 B**，并在文档登记"借用端口"语义 |

### A3 键映射（接入 `KeyProcessor` / 消费键格式声明）

> **重要更正（本轮实测）**：`KeyHierarchyFormat`（`KeyHierarchyFormat.cs:12-19`）= 层次化**后缀**格式 `{Prefix}{层次}{Suffix}`（默认 `_` / `_` / 无分隔 / 十进制无填充），服务 `KeyProcessor` 的 **`_cN` 家族**键；而 Agent 会话键族（`cline.session.{k}.round.{r}`、`session.{k}.r{r}.msg.{s}`）**不含 `_cN` 结构** ⇒ **强行接入会改写键形态**（正是 D6 的坑）。

| 段 | 内容 |
|---|---|
| **前置取证** | 读 `Operation\WorkFlowMethod\NodeProcess\KeyMapping\KeyProcessor.cs` 的 `ProcessConnectionKeyMapping` 键形态判定；确认两族键能否共存（同族判定条件） |
| **改动（二分支，先取证后定）** | **分支 1（同族可共存）**：Agent 侧手拼键改为经 `KeyProcessor`（`HistoryClineMessageArchive.BuildNodeKey:103`、`SessionMessageKeys.Build`、`SyncRelationsBlock.cs:78`）；**分支 2（不同族）**：A3 改写为「**键格式登记**」——两族键已由声明 `identity` + placement `key` 模板承载（P0-2 已落），断言"Agent 侧零手拼键"由门禁守护，并**记录"不接 `KeyProcessor`"的理由**（避免为一致性而破坏存量键） |
| **判据** | 分支 1：改声明即改键 + **双向解析回归**（Build→Parse 往返）+ 门禁 A 项 EXIT 0；分支 2：门禁 `Check-NodeWriteBypass.ps1` A 项 EXIT 0 + 文档登记结论 |
| **回退** | 分支 1 单点回退（键构造回退到 `SessionMessageKeys`/`BuildNodeKey`）；分支 2 无代码改动 |

### A4 边收敛（`round_sequence` 三写者 → ≤1）

> **重要更正（本轮实测）**：**"退役"不可行** —— `round_sequence` 有**读侧消费者**：`RefHead.Project`（`Operation\LoopFoldMethod\Reference\Projection\RefHead.cs:33-60`）沿时序边做链式拓扑折叠（入度 0 链头 → 后继链 → 当前判定视图；孤立/成环按 `NodeKeys` 兜底），`RefHeadContext.RoundSequence`（`RefHeadContext.cs:16`）与 `IRefView.cs:17` 均为其契约。⇒ A4 只能做「**单写者化**」，不能删边。

| 段 | 内容 |
|---|---|
| **前置取证** | ① 写点：`SyncRelationsBlock.cs:78`（Agent 侧）、`HistoryProjector.cs:59`（`PersistRoundAsync`）、`HistoryProjector.cs:137`（`ProjectMarkedAsync`）；② 读点：`RefHead`（折叠）、`IRefView`；③ 转发层：`HistoryRelationGateway.cs:79-81`、`NodeTaskHistoryService.cs:101`、`ReferenceGrounding.AddRoundSequenceEdgeAsync:92-99`（**边写原语唯一 owner 在 DataStoreDriven**） |
| **方案（三选）** | **A** 维持三写者（违 N3/N6，不选）｜**B（推荐）单写者化**：唯一写者 = `HistoryProjector`（轮粒度、含 mark 过滤后的时序），删除 `SyncRelationsBlock.cs:78` 与 `PersistRoundAsync` 里的重复时序写（保留 `session_owns`/`cross_db_link` 归属边）；**C（A4′，后续）** 真退役 + 读侧改键序：`RefHead` 改从 `SessionMessageKeys.OrderComparer` 排序（需 `RefHeadContext` 增键序能力，跨 `LoopFoldMethod`，改动面更大） |
| **改动（方案 B）** | ① 先加**读侧对拍**（同一 `RefHeadContext`：收敛前/后 `RefHead.Project` 的 `Sequence` 结果一致）；② 删 `SyncRelationsBlock.cs:71-81` 的 `AddSequenceAsync` 段与 `HistoryProjector.cs:58-60`；③ 新增脚本扫描「`AddSequenceAsync` 生产调用点 ≤ 1」 |
| **判据** | `round_sequence` **写点 ≤ 1**；`RefHead` 折叠结果对拍一致（时序序不变）；`Agent.Core.Tests` / `WorkFlowMethod.Tests` 零回归 |
| **回退** | 单点回退其中一个写点（保留另一写者即可恢复三写语义，无数据损坏——边写幂等） |

### A5 快照单源（消 D4：`NodeExecutionRecord` 构造归一）

> **实测更正（比设计文档多一处）**：构造点**三处**，非"两 runner"—— `RoundStepInvoker.cs:226`（执行侧）、`SignalWorkflowRunner.cs:203-210`（`BuildNodes`）、`FoldConversationRunner.cs:178-189`（`BuildNodes`，其注释自述"与 `SignalWorkflowRunner.BuildNodes` 同构"，`FoldConversationRunner.cs:160/173`）。

| 段 | 内容 |
|---|---|
| **前置取证** | 三处构造的字段口径差异（`NodeKey/WorkflowId/OutputPorts/Provenance/AtUtc`）；`Agent.Orchestration` **引用 `Agent.Core`**（`Agent.Orchestration.csproj:43`）⇒ 共享点可放 `Agent.Core\Nodes\` |
| **改动** | 新增 `Agent.Core\Nodes\NodeExecutionRecordFactory.cs`（唯一构造点：`FromSteps(spec, outputs, atUtc)`）；三处改委托；`RoundStepInvoker` 保留"执行产物→字典"的语义，只把记录构造交工厂 |
| **判据** | 对拍用例（同 `spec`+`outputs` 下三处产出记录集合逐字段一致）；grep `new NodeExecutionRecord` **仅工厂内 1 处** |
| **回退** | 工厂纯构造无 IO；三处可逐点回退到内联构造 |
| **风险** | **低**（无 IO、无键、无门禁交互），建议作为 A 阶段下一步的**首选** |

---

## §4 B/C/D 阶段实施卡（要点 + 落点）

### B 阶段：实时物化（前置已解除：P0-1/P0-3 已落地）

| 步 | 改动（文件:行） | 判据 |
|---|---|---|
| **B0 装配** | 在 Agent 组合根（宿主装配处）接线：`AgentNodeSpec.TryCreate(overridePath, AgentNodeCarriers.Create(nodes, history, diagnostics), diagnostics, sequenceAuthority)` → 持有 `NodeFramework` 实例（**禁进程静态**，消 E6） | 装配日志可见 4 载体 + 4 位置；框架门禁 D 通过 |
| **B1 帧→物化** | `Agent.Modules\Cline\ClineDiagnosticsHosting.cs:57-64`（`OnPersistFrame`）由 `static` 改**实例级**并注入物化端口；帧 → 节点 JSON（`{NodeKey, WorkflowId, OutputPorts}`，键 `{session}.{node}.{seq}`，seq 由 `ISequenceAuthority` 分配）→ 合并窗口（毫秒级背压）→ `NodeMaterializer.MaterializeAsync(...)`（`placement=archive-raw`，port=stream，writer=`writer-stream`）；热路径 append 复用 `OutboxWriter` 模式（参照 `NodeStepPersistCoordinator.cs:58`） | 新增 `Agent.Modules.Tests\PersistFrameMaterializationTests.cs`：① 帧到达 N ms 内读侧可读；② 重放不重复写（幂等）；③ 崩溃恢复（已物化不丢） |
| **B2 位置分离** | 帧写 `writer-stream`（`archive-raw`/port=stream）与轮末写 `writer-task-node`（`PersistNodesBlock`→`NodeStepPersistCoordinator`）**不重叠**；结清 P1–P5 §6.3 遗留：`NodeWriteGate` target 口径（`carrier\|placementKey`）与 `NodeStepPersistCoordinator`（`workflowId`）粒度不一 → 装配处显式登记「节点级 ⊆ 库级」 | `Check-SingleWriter.ps1` + 新增零冲突断言 |
| **B3 读侧 Origin** | `NodeReadProjection.Overlay` 字段级叠加（`Live > Snapshot > Derived`）接面板读路径；`RunStateProjection` 单入口 | 帧前后读一致（CT-5 风格） |

### C 阶段：回写合一（三路 → 一次物化 · 多位置）

| 步 | 改动 | 判据 |
|---|---|---|
| **C1** | 归档（L-C）与入史（L-B）**同 Key（轮粒度）**多 placement 物化：`HistoryClineMessageArchive.ArchiveAsync:47/103` 与 `HistoryProjector.ProjectMarkedAsync:100` 改为经 `NodeMaterializer` 写声明位置（`archive-raw` / `session-view`），`HistoryProjector` 保留 mark 判定（C2 前）；`PersistHistoryBlock`（`ur_histories`）转 `legacy` 读面 | `(container, round)` 一次枚举出全部位置（CT-1 风格） |
| **C2** | `HistoryMarkPolicy`（`HistoryProjector.cs:119`）从**写侧**判据改为**读侧** `selection: marked` 谓词（声明在 `conversation.json` 的 `selections.marked`，P0-2 已占位） | 写侧不再按标记另存副本（N7）；`selection` 未识别表达式须**可见化**（既有 `selection.unparsed` 诊断） |
| **C3** | `session-view` 去重（U2 裁决）：纯读侧裁剪 ⇒ 同轮正文库内出现 **3 → 1**；缓存方案 ⇒ 3 → 2 | 正文出现次数达目标；`SessionMessageKeys` 读侧排序回归 |

### D 阶段：读侧切换与退役

| 步 | 改动 | 判据 |
|---|---|---|
| **D1′** | `Agent.Launcher\Modules\Libraries\ConversationViewService.cs`（`ListSessionsAsync:200-206`）改枚举 `session.*` 位置 | 新旧会话均可枚举 |
| **D2′** | `ConversationViewService.cs:151-152`（History/Task 视图）切 `presentational` 读面；`RenderTaskNodesPanelAsync:490` 的 threadId 并集改**位置枚举**（消 E9） | 迁移会话与新建会话显示一致（对拍） |
| **D3′** | `ur_histories` 三态：`PersistHistoryBlock` 与第二通道 `MediaHandlers.HistoryStoreMediaHandler:73` **收敛同一 writer** → `legacy`（拒写可读）→ 观察期 → `retired` | 写命中归零、读命中归零（CT-8） |
| **D4′** | 删"只日志"路径（B1 后自然消失）；`AgentInvocation.Round`（`Agent.Contract\AgentInvocation.cs:45`）退役（R5） | 死路径清零 |

---

## §5 判据命令矩阵（按改动面选跑）

| 改动面 | 必跑命令 |
|---|---|
| `Modules.Node`（Frame） | `dotnet build Frame\CsharpPrj\Modules\Node\Modules.Node.csproj` → `& Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1 -ShowDetail` → `& Environment\CentralTest\Run-CSharpTests.ps1 -Test node` |
| **改了 Frame 且需 Agent 消费** | 追加：`& Frame\CsharpPrj\Generate-CsharpBakeManifest.ps1 -Configuration Debug`（须"全部就绪且新鲜"）→ `& Projects\BuildFramework\Check-BakeFramework.ps1`（#3 引用 ∈ manifest） |
| `Operation`（Frame） | `& Environment\CentralTest\Run-CSharpTests.ps1 -Project 'WorkFlowMethod'` + `& Frame\PowerShellScript\Modules\Node\Check-NodeWriteBypass.ps1 -ShowDetail` |
| Agent（Core/Modules/Launcher） | `& Environment\CentralTest\Run-CSharpTests.ps1 -Project 'Agent\.Core'` / `'Agent\.Modules'` / `'Agent\.Launcher'` |
| 层序 / 单写门 | `& Frame\PowerShellScript\Share\Solution\Test-ProjectReferences.ps1` + `& Frame\PowerShellScript\DataStore\Check-SingleWriter.ps1`（路径以实际为准） |
| 键格式改动 | 追加双向解析回归（`Build → Parse` 往返用例），`Check-NodeWriteBypass.ps1` A 项 EXIT 0 |

---

## §6 推荐执行顺序（按「风险递增 × 前置松弛度」排）

```
【立即可做，风险低】
 A5 快照单源（3 处构造 → 1 工厂；无 IO/无键/无门禁交互）
   │
【需先取证，中等风险】
 A3 键映射（先判两族键是否同族；不同族则只登记不改码）
   │
 A4 边单写者化（先加 RefHead 读侧对拍 → 再删两个写点）
   │
 A2 链双读（先补等价门用例；Emits/Mark 落位需裁决 A2-D1）
   │
【阶段 B：实时物化（前置已解除）】
 B0 装配 → B1 帧物化 → B2 位置分离 → B3 读侧 Origin
   │
【阶段 C：回写合一】C1 同键多位置 → C2 裁剪上移 → C3 去重
   │
【阶段 D：读侧切换与退役】D1′ → D2′ → D3′ → D4′
```

**每件固定四步**：① 取证（§2 清单）→ ② 先加对拍/等价门用例 → ③ 改件（单点）→ ④ 出口命令（§5）+ 单点回退验证。

---

## §7 风险与回退（汇总）

| 风险 | 触发步 | 缓解 / 回退 |
|---|---|---|
| **Frame 改动未重烘 ⇒ Agent 侧"看不见"**（最高频） | 任何 Frame 改动 | 每改必跑「建 → 重烘 → `Check-BakeFramework` #3」；manifest 数不变即视为未生效 |
| **隐性读侧消费者被打断** | A4（`round_sequence` → `RefHead`） | 删/退役前做反向引用扫描；先加读侧对拍（折叠结果不变）再删写点 |
| **键格式漂移打断存量会话** | A3、C1 | 只改声明模板，不改生成/解析实现；带 Build→Parse 往返回归 |
| **双真源（新增映射/第二套实现）** | A2、A5 | 唯一入口原则：A2 收口到 `RoundSpecChainMapper`；A5 收口到 `NodeExecutionRecordFactory`；门禁/脚本扫"第二处" |
| **写点/门缝违规** | B1、B2、C1 | 新写点先在装配处登记 target；`Check-NodeWriteBypass.ps1` + `Check-SingleWriter.ps1` |
| **进程静态回归**（E6） | B1 | 组合根持有实例，`Interlocked` 进程静态禁用（门禁 E 类会扫静态字段/属性） |
| **回退成本** | 全部 | 每件保持"单点回退"：新增件删除即回落；改签名的件保留旧入口只转发 |

---

## §8 待裁决（进入对应步之前必须定）

| # | 问题 | 影响步 | 推荐 |
|---|---|---|---|
| **U1** | 身份粒度：轮为节点 / 消息为节点 | C1/C3、P0-2 键模板 | **A 轮为节点** |
| **U2** | `session-view` 保留为缓存 / 纯读侧裁剪 | C2/C3（去重 3→1 或 3→2） | 需定 |
| **U4/U11** | `AxiomContext.Data`(RoundKeys) 与 `{node}.{port}` 合并时点 | A3/A4、B 阶段 | **B 双轨并存至阶段 A 末** |
| **U6** | 存量 `ur_histories` 行与节点位置对齐方式 | D1′/D3′ | 阶段 C 前定 |
| **A2-D1（本轮新增）** | `Emits`/`Mark` 在 `NodeConfig` 的落位（扩字段 / 借用 `AddParameters` / 读侧直读） | A2 | **借用 `AddParameters` 透传（B）** |
| **A3-新（本轮新增）** | 两族键（`_cN` 家族 vs 会话键族）是否同族；不同族则 A3 降为"登记不改码" | A3 | 先取证 |

---

## §9 边界规则：Frame 零改动优先（本轮补充）

> **规则**：Agent 侧应用改造**默认不改 `Frame\CsharpPrj` 源码**。（点明一句事实：Agent 与 Frame 并非无关——Agent 单向消费 Frame 的二进制闭包：`Share.Conversation` / `Operation.HierarchyMethod` / `Operation.DataStoreDriven` / `WorkFlowMethod` / `GraphFramework` 等，见 `Projects\BuildFramework\CsharpBakeRefs.props`；但**消费 ≠ 可在其中改造**。）

### 9.1 剩余各项的 Frame 改动面（实测判定）

| 步 | 是否必须改 Frame | Frame 零改动方案 |
|---|---|---|
| **A5** 快照单源 | **否** —— 三处构造点**全在 Agent 侧**（`Agent.Core\Nodes\RoundStepInvoker.cs:226`、`Agent.Orchestration\...\SignalWorkflowRunner.cs:203`、`...\FoldConversationRunner.cs:178`）；`NodeExecutionRecord` 只是**类型**来自 Frame | 共享工厂放 `Agent.Core\Nodes\NodeExecutionRecordFactory.cs`（Orchestration 已引用 Core）✅ |
| **A2** 链双读 | **否** —— `RoundSpecChainMapper` 在 Frame，但**不必改它** | Agent 侧包一层：先调 Frame 的 `MapToChain`，再把 `Emits`/`Mark` 从 `RoundStep` 注入 Chain 的透传字段（`Chain`/`NodeConfig` 类型由烘焙轨提供，可消费）✅ |
| **A3** 键映射 | **否** —— 本就是"Agent 侧消费声明/登记族" | 判族 + 登记；Frame 零改动 ✅ |
| **A4** 边收敛 | **是** —— 3 个写点中 **2 个在 Frame**（`Operation\HierarchyMethod\History\HistoryProjector.cs:59`、`:137`），仅 `SyncRelationsBlock.cs:78` 在 Agent 侧 | **两条路**：① 接受改 Frame 2 处 → 达"写点 ≤1"；② **Frame 零改动** → 仅收敛 Agent 侧写点，写点 **3 → 2**（Agent 侧单写者），并把"Frame 侧两写者"登记为已知边界（它们服务不同调用路径：`PersistRoundAsync`=执行侧沉淀、`ProjectMarkedAsync`=入史投影） |
| **B 阶段** | **否** —— `ClineDiagnosticsHosting` / `NodeCarriers` / 组合根均在 Agent 侧 | Frame 零改动 ✅ |
| **C1** | **待评估** —— 若要求 `HistoryProjector` 也改走 `NodeMaterializer`，则触 Frame | 先做 Agent 侧两路（`HistoryClineMessageArchive` + `PersistNodesBlock`）经 `NodeMaterializer`；Frame 侧 `HistoryProjector` 保持为**读侧/投影**职责（原本就是它的定位）✅ |
| **C2/C3、D1′–D4′** | **否** —— 落点均在 Agent 侧（`StandardContextBuilder` / `ConversationViewService` / `PersistHistoryBlock`） | Frame 零改动 ✅ |

**结论**：A/B/C/D 剩余项中，**只有 A4 存在"必须改 Frame"的形态**；其余全部可在 Agent 侧完成（Frame 仅作为**读取对象**与**类型来源**）。⇒ 默认口径 = **Frame 零改动**；A4 按 §9.1 两路之一裁决。

### 9.2 取证的边界（避免"全量影响面分析"）

只查**被改点自身**的读写方，不做全仓影响面推演：

| 需要查 | 不需要查 |
|---|---|
| 我要动的那一处，**谁在写、谁在读**（A4 只查 `round_sequence`；A5 只查 `new NodeExecutionRecord`；A2 只查"映射是否已存在"） | 沿调用链向外推演 n 层影响 |
| **是否改到键的形态**（改键才需要双向解析回归） | 不涉及的模块内部实现 |
| 层序/门禁/回退 —— **交给脚本**（`Check-NodeFramework` / `Check-NodeWriteBypass` / `Test-ProjectReferences` 自动兜） | 人工逐一核对门禁规则 |

> 定点取证的经验值：**一项 = 2–4 次 grep/read，5 分钟量级**；换来的是"计划不被改写"（本轮 4/4 被改写，即不做这一步的代价）。

---

## §10 错误引导源清单（本轮实测定位；与"跨仓"无关）

> **共同机制**：这些注释/文档都是**写侧视角的自我说明** —— 只说"我做了什么"，**不说"谁在依赖我"**。于是写侧读起来是"权威 / 唯一 / 同形"，一旦要动它，没有任何一行提示"还有谁在读"。
> **处置口径**：Frame 侧**只登记、不落码**（§9 边界规则）；Agent 侧随对应步消解。

| # | 位置 | 原文（节选） | 实际事实 | 被误导的结论 | 处置 |
|---|---|---|---|---|---|
| **M1** | `Agent.Modules\Cline\ClineDaemonEndpoint.cs:265-266` | 「归档轮次权威来源 = 框架历史容器已归档 raw round 最大 + 1…**取代依赖编排器 request.Round**（与子 Cline 实际轮次可能错位）。引擎回带 r.Round 仅作诊断观测。」 | 该口径与设计文档「轮次单源」相反；`request.Round` 链路（`RoundKeys.Round`→`cline_execute`→`AgentExecutorRequest.Round`）**是通的** | 把"归档自推"当作权威，A1 的收敛目标一度口径相反 | ✅ **已修**（本批 A1 改写为"优先调用方轮次 + 无上下文才回退并可见化"） |
| **M2** | `Operation\HierarchyMethod\Nodes\Relations\RoundStepMapping.cs:11-13` | 「运行期 `NodeDefinition` 与声明期 `NodeConfig` **两处映射共用同一字段口径**，避免…漂移」 | 投影仅 6 字段，**静默丢弃 `Emits`/`Mark`**（`RoundSpec.cs:47/54`） | 读起来"已对齐" ⇒ A2 的等价缺口不暴露，只能靠对拍发现 | ⏳ Frame 侧**登记不落码**；A2 时以 Agent 侧补字段 + 等价门暴露 |
| **M3** | `Agent.Orchestration\...\SignalWorkflowRunner.cs:201-202` 与 `...\FoldConversationRunner.cs:171-176` | 两处近乎逐字重复：「按 spec.Steps（EffectiveNodeId）+ 折叠 Outputs（ResultKeys[0]）重建节点执行快照…」；后者又称「与 `SignalWorkflowRunner.BuildNodes` 同形」，并把 `RoundStepInvoker` 仅作**参照系**提及（`:174-175`） | 构造点实为**三处**：`RoundStepInvoker.cs:226` 与这两处并列 | 形成"只有两个构造点"的闭环证据 ⇒ A5 写成"两 runner" | ⏳ Agent 侧；**随 A5 收敛为单一工厂后自然消解**（注释改为指向工厂） |
| **M4** | `Operation\HierarchyMethod\History\HistoryProjector.cs:96-97` | 「时序权威 = 键内 `(round, seq)`…`round_sequence` 为轮内相邻边…**跨轮续接随「边单写」批收敛**」 | 边有**读侧消费者**：`RefHead.Project` 沿边链式折叠（`RefHead.cs:33-60`）+ `RefHeadContext.RoundSequence`（`:16`） | 只谈"写侧待收敛" ⇒ 暗示边可退役 ⇒ A4 写成"退役" | ⏳ Frame 侧**登记不落码**；A4 按 §9.1 两路裁决 |

**另有设计文档口径过时（比代码注释影响更大）**：`节点通用框架-Agent应用篇.md` §1.2 表把 `Chain<TKey,TNode>` 等标「❌ 未用」（未列 `RoundSpecChainMapper`）、§7.2 写「`round_sequence`（3 写者）**退役**」（未核查读侧）、§3.2 写「两 runner 共用实现」（实为三处）。

### 10.1 纠正方式：给被改点加"反向声明"（只写事实，不做机制）

| 误导点 | 应加的 1–2 行（示例） |
|---|---|
| M2 | `// 未覆盖字段：Emits / Mark（RoundSpec.cs:47/54）—— Chain 侧需另路承载` |
| M3 | `// 构造点共 3 处（并列）：RoundStepInvoker.cs:226 / 本处 / FoldConversationRunner.cs:178` |
| M4 | `// 读方：RefHead.Project（RefHead.cs:33-60 链式折叠）+ RefHeadContext.RoundSequence —— 退役/改语义前须先迁移读侧` |

> 成本 1–2 行/处；**Frame 侧两处（M2/M4）按 §9 只登记本表**，等 A2/A4 决策落地时一并处理（若届时选择"Frame 零改动"，则改为在 Agent 侧包一层并在包装处写全反向声明）。

---

## §11 为什么"谁依赖我"与改代码直接相关（判定表）

**答案取决于改动落在哪一类契约上** —— 类型契约有编译器当依赖清单，值级契约没有：

| 改动落在 | 编译器能否兜 | 运行时能否兜 | 需要查"谁依赖我" |
|---|---|---|---|
| **类型契约**（签名 / 接口 / DTO 字段类型） | ✅ 编译报错即依赖清单 | — | **否**（编译器就是清单） |
| **内部实现**（签名不变） | — | ✅ 既有测试 | **否** |
| **纯新增**（新文件 / 新方法 / 新判据，无人调用） | — | — | **否** |
| **值级契约**：键格式、JSON 字段名、图边、事件名、轮次/序数口径、"同一事实的第二个构造点" | **❌ 静默** | **❌ 静默或降级** | **✅ 是唯一来源** |

### 11.1 实测反例（最强证据）：删边写点不会报错，只会静默降级

删 `round_sequence` 写点（A4 的"退役"口径）会发生什么：

1. **编译通过**：边是**图数据**（`ReferenceGrounding.AddRoundSequenceEdgeAsync` → `_graph.AddReferenceAsync(from, to, ...)`），不是类型引用 ⇒ 无编译期连接；
2. **无告警**：`RefHead.Project` 在**无边时静默降级** —— `RefHead.cs:48-51`「链头（入度 0）按 NodeKeys 序取」；边全缺 ⇒ 所有节点入度 0 ⇒ 每个节点自成链头 ⇒ 按 `NodeKeys` 顺序逐条追加，`sequence` 依旧"看起来正常"；
3. **测试也可能过**：既有用例若不显式断言"折叠序 = 时序边序"，就断言不到；而 `NodeKeys` 顺序与真实时序**恰好相同时**（单链、无并发）连行为差异都没有。

⇒ 这就是"删除一条边的写点"能**静默**改变投影语义的完整路径。**只有"谁在读这条边"能提前拦住它**。

### 11.2 根因：本框架把连接从"类型"降级为"值"

框架的核心主张是**位置无关**（N1）：节点 = ⟨键 + JSON + Placement⟩，连接 = 键引用 / 图边。这带来两个后果：

- **收益**：位置层不绑定上层 DTO（D-1 案 A）、键即地址、可多位置物化；
- **代价**：**连接不在编译期** —— 编译器不再为"键格式、JSON 字段、图边、轮次口径"提供依赖检查。

所以在这套框架里，**"谁依赖我" ≈ 编译器本该提供、但被降级掉的依赖清单**。这不是额外的仪式，而是**补上编译器的缺口**。

### 11.3 边界（不必全查）

- 只对**值级契约**查：要动的键 / 字段 / 边 / 事件 / 口径；
- 只查**被改点自身**的读写方（§9.2），不推演调用链；
- 一次命中即止：本轮 A1 的 `TryArchiveRoundAsync` 全库 1 处调用 ⇒ grep 一次即证"无第二调用方"，5 秒级。


