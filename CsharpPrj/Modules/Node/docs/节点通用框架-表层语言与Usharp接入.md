# 节点通用框架 · 表层语言与 Usharp 接入（裁决：留端口，语言不进位置层）

> **状态**：设计稿（只出文档，未落地代码）
> **配套**：[节点通用框架设计（与位置无关）](./节点通用框架-与位置无关的设计.md) · [提取清单与复用映射](./节点通用框架-提取清单与复用映射.md) · [Agent 应用篇](./节点通用框架-Agent应用篇.md)
> **上游权威**：`Frame\Usharp\README.md`（语言规范唯一权威：语法/规则/诊断码/端口范式，本文件**不重复定义**）；`Docs\analysis\2026\9\9.21\Usharp引入Agent框架-多链收敛分析.md`、`9.22\Agent_通用逻辑架构_Usharp声明化重构方案.md`（已定「Usharp = 唯一声明语言；执行体/状态根/终止裁决不进语言面」）
> **本文件回答**：① 以 Usharp 作基础语言是否更优；② 「Node 框架不引 Usharp、只留端口」是否成立；③ 「Agent 侧用 Usharp 而不是 JSON」的确切含义与边界；④ 端口清单、校验分工、迁移路径、未决项。

---

## §0 结论先行

| # | 结论 |
|---|---|
| 1 | **方向成立**：`.ushar` 作**定义期**书写面（替代手写 `Config\workflows\*.json` / `RoundSpec` 内置常量 / `reference-schema.dsl.md` / 观测 semantics JSON）是既有共识（9.21 §0 结论 3、9.22 §2.1），且**已有等价证据**（`Usharp.Emit.Tests` 9 例 `agent-loop` 对拍、`emit.hierarchy.equivalent-to-history-task-node` 等价门） |
| 2 | **「Node 框架不引 Usharp，只留端口」正确**：位置层引语言 = 位置层被语言绑定 + 平台闭包污染（`Usharp.Emit` 已连带 `HierarchyMethod`/UR 闭包）。这**不是特例**：Usharp 自己就用同一范式（`IUsharpGraphBackend` + `NullGraphBackend` + 未装配即 `US9001` 可见化），故 Node 侧照抄该范式即可 |
| 3 | **耦合方向唯一**：**允许** `Usharp.Adapters.Node → Node 端口`（与既有 `Usharp.Adapters.UniversalReference` / `Usharp.Adapters.Observability` 同构）；**禁止** `Modules.Node → Usharp`。桥接类型只许出现在 Usharp 适配工程 |
| 4 | **「Usharp 而不是 JSON」须精化为两域切分**：`.ushar` 取代**定义期声明**（谁连谁、传什么键、什么顺序、什么观测契约）；**不取代运行期载荷**（`NodeOutput` 端口、会话正文、落库行——`NodeOutput` 仍是 JSON 文档）。依据：Usharp README R8「编译期不写库」+ 通用篇 N11（JSON 形态单源，作用于**载荷域**） |
| 5 | **顺序必须单源**：现状有三处顺序表达（`[Flow] step` 声明序 / `[Matrix] Chain` 转移表 / 通用篇 N6 键内序数）。裁决：**声明序为唯一权威**，键内序数由编译器按声明序生成，转移表降为**派生缓存**（可重算，不参与权威）——N6 因此仍然成立 |
| 6 | **校验分层，禁双实现**：语言级闸A（`US1005` 截断 / `US1001` 空间未声明 / `US1016` 端点非法 / `US4001` 环）为**定义期强校验**；Node 框架 `NodeSpecValidator` 只做**装配期兜底**（非 Usharp 来源或被外部改写时）。二者结论必须一致，冲突时以语言级为准 |
| 7 | **术语冲突须消歧**（新发现）：Usharp `[Node]`（可执行体 + 引用图资源节点，见 `UsharpNodePortReader` / README §9.2）与节点框架「节点（Key + JSON 文档）」**同名不同义**。必须 SEP 声明并用词固化，否则全库出现"两套节点概念" |
| 8 | **位置声明面反转（U12 已结案，§13）**：位置**不手写声明**，而是由 `loc`（`ResourceIdentifier.Location`，位置轴）**反解**——`.ushar` 用既有 `[Space] Loc` / `[Ref] … loc=` / `[Matrix] … over=Loc` 声明 loc 与反解表（**零新增语言单元**，绕开 README §7.5 红线），Node 框架经端口取反解产物；位置从"手写声明"变为"派生值（Origin=Derived）" |
| 9 | **`loc` 不是身份**：实测 `Id = {workflowId}.{nodeKey}`、`Location = workflowId`，且"Location 不参与键" ⇒ 反解只产位置、**不产身份**；混用即应用篇 E2 误诊复发（P21） |
| 10 | **反解表本身是声明**：框架内**禁**硬编码 `loc→carrier` 映射（P22）；反解唯一（L-2）、失败即 ∅ + 可见化（P23）、键模板须含序数占位（L-4；否则断 N6/N1） |

---

## §1 实测现状（可直接复核）

### 1.1 Usharp 是什么（一句话）

不是新运行时，而是「唯一表层语言 + 编译器」：`.ushar` → `UsharpParser`（薄包 `Plate.DslMethod` 解析器）→ `UsharpSemantics`（空间表 + 闸A + 引用图）→ `Usharp.Emit`（**落到 CsharpPrj 既有类型**：`RoundSpec` / `Chain<string,NodeConfig>` / 转移表 / `ParameterMatrix` / `HierarchySchema` / `ReferenceSchema` / `BoundaryContract`）。

### 1.2 与节点框架已存在的接线事实

| 事实 | 位置（实测） | 对节点框架的意义 |
|---|---|---|
| `[Node] fn= inports= outports=` 已被解析为「可执行体 + 端口」 | `Usharp.Emit\UsharpNodePortReader.cs`（`UsharpNodeSpec`；端口键序 = 声明序） | **端口声明的书写面**已存在；等价于 `funcPipelineConfig.ParameterKeys/ResultKeys` |
| `[Flow]` → `RoundSpec`（步骤键 → `[Node]` 的 FuncName/ParameterKeys/ResultKeys/NodeId） | `Usharp.Emit\UsharpRoundSpecEmitter.cs` | Agent 会话链定义面已被语言覆盖（U3 等价） |
| `[Flow] connects=` → 写 `tail.nextNodeConfig.NextNodeKey = head.Id` | `Usharp.Emit\UsharpChainConnectorEmitter.cs`（注释明写"连接能力本身是 `Operation\WorkFlowMethod` 的既有真源"） | **键连接**已被语言覆盖；且**唯一映射点**复用 `RoundSpecChainMapper.MapToChain`，未新造第二份映射 |
| `Usharp.Emit` 直接 `using WorkFlowMethod.Skeletons.NodeProcess.Models` | 同上文件 | **Usharp → WorkFlowMethod 方向已存在**（`Chain`/`NodeConfig`） |
| 转移表由 `UsharpTransitionTableBuilder.Build(flow)` 产出 | `UsharpRoundSpecEmitter` 返回值 | 顺序的第二种表达（见 §5） |
| 端口范式已成惯例：`IUsharpGraphBackend` + `NullGraphBackend` + 未装配即 `US9001` 可见化 | `Usharp.Semantics\IUsharpGraphBackend.cs`（"签名只用 Usharp 自有类型 + BCL"） | Node 端口照抄此范式 |
| `.ushar` 已在产品侧跑：`Config\workflows\*.ushar` 5 份（链 1/2/3 已接线）；`RoundSpecLoader` 保留 basename→FlowId 映射 + C# 内置回退常量 | `9.22\Agent_通用逻辑架构_Usharp声明化重构方案.md` §0.1/§1.1 | 迁移已在进行，Node 框架需**现在**把端口留出，否则后续返工 |

---

## §2 三条切分（把「用 Usharp 而不是 JSON」变成可判定命题）

### 2.1 切分一：定义期 vs 运行期

| 域 | 内容 | 权威载体 | 理由 |
|---|---|---|---|
| **定义期**（结构 + 代数 + 引用） | 链/流步骤与顺序、节点可执行体引用（`fn=`）、**端口键**（`inports/outports`）、键映射、连接（`connects=` / `NextNodeKey`）、观测契约 | **`.ushar`（单一真源）** | 可编译校验（闸A）；README R1/R2/R3 |
| **运行期**（载荷 + 状态事实） | `NodeOutput.OutputPorts`、会话正文、`ur_nodes` 落库行、执行期键空间值 | **JSON（不变）** | ① README R8「编译期不写库」；② 载荷由机器写，Usharp 无运行期写入通道；③ N11 要求载荷 JSON 单源；④ 状态事实 ≠ 声明（9.22 §7 第 2/6 条） |

> 判据（可机器检查）：`.ushar` 只出现在**定义/装配**路径；端口与落库写路径的 JSON 序列化入口不变。

### 2.2 切分二：语言 vs 位置层（谁引谁）

```
允许（现状 + 新增唯一一处）：
  Usharp.Emit  ──►  WorkFlowMethod 既有类型（RoundSpec / Chain / NodeConfig）   [已存在]
  Usharp.Adapters.Node ──► Modules.Node 端口（接口定义在 Node）                [新增，唯一引 Node 处]

禁止：
  Modules.Node ──► Usharp.*                                                   [位置层被语言绑定]
  Modules.Node ──► HierarchyMethod / UR（经 Usharp 闭包引入）                  [重量污染，违 K6]
```

对称原则：`IUsharpGraphBackend` 的「签名只用 Usharp 自有类型 + BCL」在 Node 侧镜像为「**Node 端口签名禁出现 `Usharp*` 类型**」——桥接类型只在适配工程。

### 2.3 切分三：声明 vs 载荷（同一 schema 的两个面）

| 面 | 例 | 载体 |
|---|---|---|
| **声明面**（谁会用什么键、连到谁） | `[Node] cline_execute \| fn=cline \| inports=prompt \| outports=answer` | `.ushar` |
| **载荷面**（实际产出） | `{ "NodeKey":"cline_execute", "OutputPorts":{"answer":"..."} }` | JSON（`NodeOutput`） |

⇒ 「节点」不因换语言而换形态：**节点 = ⟨Key, JSON 文档⟩** 不变；变的是"谁来写它的定义"。

---

## §3 `Modules.Node` 需留的端口（不含 Usharp 类型）

> 端口 = 抽象**声明来源**，不绑定具体语言：JSON 与 Usharp 都只是它的两种装配。

| 端口 | 语义 | 现状实现（JSON 路径） | Usharp 装配（适配工程） | 约束 |
|---|---|---|---|---|
| `ISpecSource` | 声明来源：产出 `DefinitionSet{Chains: Chain<string,NodeConfig>[], SourceId, SourceLocator}` | `WorkflowDeserializer` / `NodeChainService` / `RoundSpecLoader` | `.ushar` 编译 → `RoundSpec` → `RoundSpecChainMapper.MapToChain()`（**唯一映射点**，禁第二份映射） | 未装配 → Null 实现 + 可见化诊断（对齐 `US9001`） |
| `IDefinitionDiagnostics` | 定义期诊断（`文件:行` 归因，含 warn/error 分级） | 现有校验结果（`WorkflowValidator` 等） | `UsharpDiagnostic` **单向投影**（对齐既有 `UsharpDiagnosticProjector` 范式，禁反向解析） | 语言级 error 不被下游降级为 warn |
| `IOrientationAuthority` | **顺序权威**（见 §5） | 声明序（链 JSON 的 `AllNodes` 序） | `[Flow] step` 声明序（编译器生成键内序数） | 同一次装配恰一个权威 |
| `IReferenceIndexPort` | 引用字段清单 + 反向引用 + 改名级联（= 提取分册 G11） | `NodeConfigReferenceRegistrar` | `[Ref]` / `[Node]` / `[Edge]` → 引用图（`UsharpGraphBuilder`；可选经 UR 图后端） | 引用结论单一来源；报表由端口取，禁第二份清单 |
| `ISequenceAuthority`（既有，通用篇 §6.1） | 序数分配 | `IdGenerator` / 调用方 | 由 `IOrientationAuthority` 喂入声明序 | 序数单源 |
| 复用（已有/规划）：`INodeCarrier` / `ICarrierRegistry` / `NodeFormCodec` / `NodeWriteGate` | 载体、形态、写门 | — | Usharp **不参与**（不写库） | 编译期零写库 |

> 装配模式（照抄 Usharp 三档，宿主决定）：**纯声明校验**（Null 端口，语言类判据跳过并可见化）／**声明 + 引用图**（注入图后端）／**声明 + 落库**（宿主显式调用，编译期不写）。

---

## §4 校验职责分工（禁双实现）

| 校验 | 语言级（定义期，`.ushar`） | Node 框架级（装配期兜底） | 冲突时 |
|---|---|---|---|
| 引用未声明（悬空） | `US1001`（空间）/ `US1016`（端点）/ `US2001`（悬空引用） | `NodeSpecValidator.dangling-free`（仅在**非 Usharp 来源**或产物被外部改写时触发） | 语言级为准 |
| 环 | `US4001`（经图后端；未装配后端报 `US9001` 可见化） | `acyclic`（兜底；不做第二套图算法，复用既有图后端） | 语言级为准 |
| 声明力被静默截断 | `US1005` / `US1006`（`NextNodeConfig` 只取 `on_success` 类缺陷） | **不重复实现** | — |
| 重复/非法映射 | `US4002` / `US4003` / `US5005`（链尾至多一后继，可见化不静默覆盖） | 复用同一映射表校验结果 | — |
| 引用字段清单 | `[Ref]` + `[Node]`/`[Edge]` 声明（引用即边） | `NodeReferenceIndex`（读端口产出的报表，不自行扫描） | 单一来源 |
| 端口一致性 | `inports`/`outports` 键序 = 声明序 | `NodePortBinding` 按 `ResultKeys` 定向（提取分册 G1） | 键名以 `.ushar` 为准，值提取仍由框架负责 |

> 结论：提取分册的 **G10（连通性/环）与 G11（引用字段）在 Agent 侧不必由框架重复实现**，框架只保留「非语言来源」的兜底路径——这是 Usharp 接入带来的**净减工作量**。


---

## §5 顺序单源裁决（N6 如何继续成立）

现状存在**三个顺序表达**，必须收敛：

| 表达 | 来源 | 现状角色 |
|---|---|---|
| `[Flow] step` 声明序 | `.ushar`（`UsharpRoundSpecEmitter`） | 步骤序（`RoundSpec.Steps` 序 = `NodeId` 序） |
| 转移表 | `UsharpTransitionTableBuilder.Build(flow)`（`x′[out]=x[in]` 行方向） | 链推进（`ChainSignalResolver` 键约定） |
| 键内序数 | 通用篇 N6（`{container}.r{round}.msg{seq}` 形态） | 位置的**时序**承载 |

**裁决**：

1. **声明序 = 唯一权威**；`RoundSpec.Steps` 序 → 链 `AllNodes` 序 → **键内序数**，逐层由编译器/装配器**生成**，不允许任何一层手写第二份顺序清单。
2. **转移表 = 派生缓存**：要求满足 `表(键) ≡ 声明序相邻对`（既有等价门 `Agent.Axioms.Tests` T7–T12「矩阵表 ≡ 派生链逐键全等」正是此判据），漂移即报错，**不构成第二权威**。
3. N6 原文（"顺序由键内序数表达，不由边表达"）**无需修改**：键内序数现在是**声明序的编译产物**；边（含 `connects=`）仍只承载归属/依赖可见性。

> 反模式：若把转移表当独立真源手改（与 `.ushar` 声明序分叉），即 `ANTI-DUPLICATED-IMPLEMENTATION` + P2（边表顺序）。

---

## §6 术语消歧（`[Node]` vs 「节点」）

| 词 | 在 Usharp 中的含义 | 在节点框架中的含义 | 消歧动作 |
|---|---|---|---|
| `[Node]` | ① 引用图资源节点（`[Node]`/`[Edge]`/`[Constraint]` → `ReferenceSchema`）；② **可执行体声明**（`fn= inports= outports=`，见 `UsharpNodePortReader`） | — | 文档中固定称「**可执行体声明**（`[Node] fn=`）」，不称"节点" |
| 「节点」 | — | ⟨Key, JSON 文档, Placement*⟩：身份 + 载荷 + 位置（`NodeConfig` / `NodeOutput` / `NodeSignal`） | 保持本框架用词；与 Usharp 交互处一律限定语（"可执行体声明"/"节点端口"） |
| `inports/outports` | 端口键（声明序） | `ParameterKeys` / `ResultKeys`（`funcPipelineConfig`） | 视为**同义物**：`.ushar` 是书写面，C# 字段是载体（既有 `UsharpRoundSpecEmitter` 即此映射） |
| `[Ref] edge=` | 引用图为一条边（`RefersTo`/自定义边类型） | 键连接（`ParameterKeys ← ResultKeys`）/ 归属边 | 二者**不同域**：`[Ref]` 管引用图，键连接管数据流；命名与报表须分列 |

> 建议：在通用篇新增一条域分离声明（`SEP` 风格）——`Dom(引用图节点) ∩ Dom(节点框架节点) = ∅`，理由：前者是**被引用的资源/可执行体**，后者是**承载键与载荷的位置实体**；混称会导致"两套节点概念"与引用图端点指代漂移。

---

## §7 反模式补充（P17–P20）

> 与通用篇 §8 的 P1–P16 同一注册表；此处为语言接入新增项，冲突时以通用篇为准（单一注册表）。

| # | 反模式 | 症状 | 治 |
|---|---|---|---|
| **P17** | **载荷换语言** | 把运行期载荷（端口值、会话正文、落库行）改写成 `.ushar` 文本 | 定义期/运行期切分（§2.1）；编译期零写库（README R8）；载荷 JSON 单源（N11） |
| **P18** | **双声明无退役条件** | `.ushar` 与旧 JSON/内置常量长期并存、双向漂移 | 每类声明**恰一个退役条件**（沿用 Usharp README §9.4 迁移看板）；并存期必须有等价门 + 回退必打日志 |
| **P19** | **校验双实现** | 语言 lint 与框架装配校验各一套，结论不一致 | §4 分工表：语言级优先、框架只兜底；同一映射表/图后端复用，不新造第二套算法 |
| **P20** | **语言类型漏进位置层** | `Modules.Node` 公共签名或 csproj 出现 `Usharp*` | 端口纯净性判据（CT-U3，对齐 `U-V7` 的取数方式：csproj + 公共签名 grep 零命中） |

> **P21–P24（loc 反转引入）**：P21 loc 当身份用 · P22 双反解 · P23 反解静默兜底 · P24 键由 loc 完全推出 —— 定义与治见 §13.5，并已登记通用篇 §8。

---

## §8 迁移路径与验收判据

### 8.1 三步（对齐既有「端口 + 适配器 + 映射表 + 迁移看板」范式）

| 步 | 内容 | 验收 |
|---|---|---|
| **N1 端口** | `Modules.Node` 加 `ISpecSource` / `IDefinitionDiagnostics` / `IOrientationAuthority` / `IReferenceIndexPort` + **Null 实现** + 未装配可见化诊断（对齐 `US9001`/`US9002`） | CT-U2：未装配时行为与现状一致 + 诊断可见 |
| **N2 适配** | 新建 `Usharp.Adapters.Node`（**唯一引 Node 处**）：`.ushar` → 编译 → `RoundSpec` → `RoundSpecChainMapper` → `Chain<string,NodeConfig>`；诊断经单向投影；与 JSON 双读对拍 | CT-U1：与 JSON 定义产出逐字段等价；CT-U3：端口纯净 |
| **N3 退役** | 每类旧 JSON 定义（`Config\workflows\*.json`、C# 内置 `RoundSpec` 回退常量、键集常量）逐项给退役条件；回退路径保留期必打日志 | CT-U5：看板逐行对账，无"永续并存" |
| **N0 位置反转**（先决，见 §13.7） | L1 声明 loc 空间 → L2 `[Matrix] LocToPlacement` + 发射器（等价门对拍现状 `PlacementSpec`）→ L3 加 `IPlacementResolver` 端口 → L4 读侧同一反解 | CT-L1…CT-L5 |

### 8.2 合同测试（接通用篇 §9.2 的 CT 编号体系）

| ID | 测试 | 依据 |
|---|---|---|
| **CT-U1** | 同一工作流的 `.ushar` 定义与 JSON 定义产出**同一** `Chain<string,NodeConfig>`（逐字段等价，含键序） | 复用既有 `Usharp.Emit.Tests`（9 例 `agent-loop` 对拍）+ 通用篇 CT-1 |
| **CT-U2** | 未装配 `ISpecSource`（Null）时，Node 框架行为与现状等价，且语言类判据**跳过并可见化**（不假装通过） | 对齐 `U-V9`/`U-O2` |
| **CT-U3** | `Modules.Node` csproj 与公共签名零 `Usharp*` 命中 | 对齐 `U-V7` 取数方式 |
| **CT-U4** | 键内序数 ≡ `[Flow] step` 声明序（逐项）；转移表 ≡ 声明序相邻对 | 对齐既有 T7–T12 等价门 + 通用篇 CT-4 |
| **CT-U5** | 每类旧定义恰一个退役条件（看板对账）；并存期等价门全绿 | 对齐 Usharp §9.4 迁移看板纪律 |


---

## §9 未决项（接通用篇 U1–U5、提取分册 U7–U11）

| # | 未决项 | 影响 | 备选 |
|---|---|---|---|
| **U12** | ~~位置声明面是否也进 `.ushar`~~ **已结案（§13）**：位置声明面 = **loc 声明面**；`.ushar` 声明 loc + 反解矩阵，Node 框架由 loc **反解** placement（`IPlacementResolver` 端口）。无需新增语言单元，也不存在"两种声明语言" | 见 §13；衍生未决改为 U17–U21（loc 词集/属性面表达/方向/同源/降级路径） | — |
| **U13** | 顺序权威是否确认为"声明序"（§5），转移表降级为派生缓存 | 决定 N6 是否需要改写、等价门取数方式 | A 声明序权威（推荐）／B 转移表权威（与 N6 冲突）／C 双权威 + 一致性可见化（不推荐） |
| **U14** | 端口纯净性的检查方式与门禁归属（Node 侧脚本 vs Usharp 侧 `U-V7` 扩展） | 决定 P20 是否可自动拦截 | A Node 侧脚本（推荐，就近）／B 并入 `Usharp` 判据表／C 人工评审 |
| **U15** | 术语消歧的落点：文档用词表 vs 正式 `SEP` 声明（§6） | 决定能否机器检查"两套节点概念" | A 文档用词表 + 评审项（低成本）／B 正式 `SEP` 声明（可机器检查，推荐中） |
| **U16** | 运行期载荷是否**永不**允许 `.ushar` 表达（§2.1 的强度） | 若未来出现"用 Usharp 写载荷模板"的需求，需重新裁决 | A 永不（推荐：载荷=数据，声明=代码）／B 允许模板 → 编译期展开为 JSON（需新增发射器，成本高） |

---

## §10 明确不做什么（与 9.22 §7 对齐）

| # | 不做 | 理由 |
|---|---|---|
| 1 | 不把 **Usharp 类型**引入 `Modules.Node` | §2.2 耦合方向 + K1/K6；P20 |
| 2 | 不让 Node 框架解析 `.ushar` | 解析器零自有实现（README 结论 4）；Node 只认端口产出的既有类型 |
| 3 | 不在 Node 框架内新造第二套图算法/引用清单 | §4 分工；`IUsharpGraphBackend` 已把重图框架留在适配层 |
| 4 | 不把执行体（LLM/文件/SQLite/MCP）、状态根与写路径、终止裁决写进 `.ushar` | 9.22 §7 第 1/2/3 条；Node 侧对应 `NodeWriteGate`/`NodeCompensation` 仍在 C# |
| 5 | 不为"语言接入"新增 Node 框架组件 | 只加**端口**（§3）；组件清单仍以提取分册 §6 为准 |
| 6 | 不把位置/形态/选择语义塞进 `[Obs]` 或复用 `[Ref]` 语义 | 域分离（§6）：引用图、观测、位置三域互斥 |
| 7 | **不手写位置声明**（位置不再由 `PlacementSpec` 手写） | 位置 = `loc` 的反解产物（§13）；手写即双真源（P22） |
| 8 | **不由 loc 推身份/键** | L-1：`loc` 非身份（实测 Location 不参与键）；键由 `IdentitySpec` + 序数权威唯一生成（P21/P24） |

---

## §11 与既有文档的关系

| 文档 / 交付 | 关系 |
|---|---|
| `Frame\Usharp\README.md` | **语言规范唯一权威**（语法/诊断码/端口范式/迁移看板）；本文件不重复定义，只做 Node 侧裁决 |
| `Docs\analysis\2026\9\9.21\Usharp引入Agent框架-多链收敛分析.md` | **前置结论**：Usharp 已具备收敛多链的语义基建；决策层不纳入；三步替换 |
| `Docs\analysis\2026\9\9.22\Agent_通用逻辑架构_Usharp声明化重构方案.md` | **前置结论**：Usharp = 唯一声明语言；三层分工；§7「不做什么」= 本文件 §10 的上游 |
| 本目录 `节点通用框架-与位置无关的设计.md` | 本文件是其**语言接入分册**：位置层不变（键 + JSON + Placement），只把"定义来源"抽象为端口（§3） |
| 本目录 `节点通用框架-提取清单与复用映射.md` | G10/G11 在 Agent 侧**降级为兜底**（§4）；P17–P20 已入其反模式注册表 |
| 本目录 `节点通用框架-Agent应用篇.md` | 其阶段 A（键与 JSON 单源）与本文件 N1–N3 并行；其 U4（双轨键空间合并）与本文件 U12 互不替代 |

---

## §12 一句话答复

**是**——以 Usharp 作定义期基础语言更优（可编译校验、引用即边、单一真源），前提是三条切分（定义期/运行期、语言/位置层、声明/载荷）+ 一个耦合方向（`Usharp.Adapters.Node → Node 端口`，禁反向）+ 一份校验分工（语言级优先、框架级兜底）；**Node 框架只留端口、不引语言**，与 Usharp 自身的"端口 + Null 后端 + 可见化"范式同构。


---

## §13 位置声明面反转：`loc` → Placement（U12 结案）

> **裁决（用户确认）**：`loc` = UniversalReference 的 `ResourceIdentifier.Location`（**位置轴 / 资源归属位置**，实测 `Id = {workflowId}.{nodeKey}`、`Location = workflowId`，见 `DataStoreDriven\Core\ReferenceGrounding.cs`、`Graph\ReferenceGraphCoordinator.cs`；且"Location 不参与键"，见 `Agent.Launcher\...\AgentUiLibraryHistory.cs:263`）。
> **形态**：`.ushar` 声明 **loc 空间与坐标** → Node 框架由 loc **反解** placement（`Location → Carrier/KeyTemplate/Form/Retention/WriterId`）。**位置不再是手写声明，而是 loc 的反解产物（Origin=Derived）**。

### 13.1 反转了什么（正/反对比）

| | 正向（原设计） | **反转（本裁决）** |
|---|---|---|
| 声明对象 | 声明**位置**（`PlacementSpec{LevelId, CarrierId, KeyTemplate, Form, Retention, WriterId}`） | 声明**loc**（位置轴坐标，`[Space] Loc: kind=keys` + `[Ref] … loc=`） |
| 位置来源 | 手写 JSON 声明（Node 侧第二套声明语言） | **反解**：`Placement = Resolve(loc, keyContext)`；反解表本身是声明（`[Matrix] … over=Loc`） |
| 语言面代价 | 需新增位置前缀/字段 ⇒ 触 README §7.5 红线（U12 备选 B） | **零新增语言单元**：复用既有 `[Space]`/`[Ref]`/`[Matrix]` |
| 与"引用即边" | 位置与引用图无关 | loc 即一条边（`[Ref] … loc=`），反向引用/影响域免费可得（R1） |
| U12 状态 | 未决（"搬 or 不搬"两难） | **结案**：位置声明面 = loc 声明面，语言唯一、无第二套声明语言 |

### 13.2 反解链（可判定形式）

```
[Space] Loc: kind=keys        ← loc 受控词集（闸A：未声明即 error，US1001 同族）
  wf.agent-round
  session.{container}
  cline.session.{container}       （示例；实测现状只有 workflowId 一值，需先扩成受控词集 → U17）

[Ref] <name>: Loc:<coord> | src=<资源> | tgt=<资源> | intent=…    ← loc 挂载（引用即边）

[Matrix] LocToPlacement: over=Loc                                ← 反解表（声明，非代码常量）
  <属性行> = <loc 坐标 / 常量>…

          ▼ 编译（Usharp.Emit，新增发射器）
   PlacementSpec 集合（Node 侧契约，JSON/DTO 形态不变）

          ▼ 运行
   NodeMaterializer(Key, json, placementId)   ← 仍是唯一写入口
```

**反解产出的属性面**（与通用篇 §1.3 对齐）：`LevelId`（层级）/ `CarrierId`（载体）/ `KeyTemplate`（键模板 + **序数占位**）/ `Form` / `Retention` / `WriterId`。

### 13.3 硬性约束（否则反转会破坏既有不变式）

| # | 约束 | 理由 / 依据 |
|---|---|---|
| **L-1** | **loc 不是身份**：反解只产位置，**不得**由 loc 推出或拼装 Key | N1（身份唯一）+ 实测"Location 不参与键"；混用即应用篇 **E2 误诊**复发（P21） |
| **L-2** | **反解唯一**：同一 `(loc, keyContext)` 恰一个 placement；多解 → 报错（对齐闸A"未声明即 error"，禁择一静默） | N1/N3；对齐 `US1002` 家族 |
| **L-3** | **反解表是声明的唯一来源**：框架内**禁**硬编码 `loc → carrier` 映射表 | N8；否则双真源（P22） |
| **L-4** | **键模板含序数占位**，序数由 `ISequenceAuthority` 填；反解不得产出"完整键" | N6 + 通用篇 §1.1（否则序数丢失/手写，P24） |
| **L-5** | **失败即 ∅ + 可见化**：loc 未声明 / 反解无命中 → 不写、报诊断，**禁回退默认 carrier** | N9（来源可辨）+ P9/P15；对齐 `US9001` |
| **L-6** | **读写同一反解实现**：`NodeReadProjection` 与写侧共用同一 `IPlacementResolver` | 消除"写侧按 A 位置、读侧按 B 位置"的漂移（G2 同族） |
| **L-7** | **loc 受控词集与 UR 共享**（同源或显式映射表） | 避免 `ResourceIdentifier.Location` 出现两个词汇表（U20） |

### 13.4 Node 框架侧改动（只加端口，不加语言依赖）

| 件 | 变化 | 说明 |
|---|---|---|
| `IPlacementResolver`（**新增端口**） | `Resolve(loc, keyContext) → PlacementSet ∪ ∅` | 实现来源：语言产出（`Usharp.Adapters.Node`）或现状 JSON 声明；未装配 → Null + 可见化 |
| `PlacementSpec` | 从"手写声明"降为**编译产物/派生**（Origin=Derived 可标注） | 与 loc 反解表的等价门锁定（CT-L2） |
| `NodeMaterializer` | **不变**（仍是唯一写入口） | 反解只决定"写哪些位置" |
| `NodeReadProjection` | 改经 `IPlacementResolver` 取位置集合 | L-6 单源 |
| `PortKeySpace` / `NodeWriteGate` / `NodeFormCodec` | **不变** | 反转不触碰键空间与写门 |

### 13.5 反模式补充（P21–P24）

| # | 反模式 | 症状 | 治 |
|---|---|---|---|
| **P21** | **loc 当身份用** | 键里混 loc / 由 loc 推 nodeKey；把 loc 变化当"同一实体新身份" | L-1；N1；应用篇 E2 历史误诊 |
| **P22** | **双反解** | 框架内硬编码 `loc→carrier`，与 `.ushar` 反解表并存 | L-3；单一真源 |
| **P23** | **反解静默兜底** | 未声明 loc 时回退默认载体继续写 | L-5；N9；对齐 `US9001` 可见化 |
| **P24** | **键由 loc 完全推出** | 反解直接产出完整键（丢序数/手写序号） | L-4；N6 + `ISequenceAuthority` |

### 13.6 合同测试（CT-L1…CT-L5）

| ID | 测试 | 依据 |
|---|---|---|
| **CT-L1** | 同一 `(loc, keyContext)` 反解恰一个 placement；构造多解输入 → 报错 | L-2；对齐闸A |
| **CT-L2** | **等价门**：`[Matrix] LocToPlacement` 产出 ≡ 现状 `PlacementSpec` 声明（逐字段，含载体/形态/保留/writer） | 迁移安全（对齐 §8 CT-U1 范式） |
| **CT-L3** | 仅改 loc ⇒ **Key 不变**、placement 变（身份与位置解耦） | N2 + L-1 |
| **CT-L4** | 未声明 loc / 无命中 ⇒ ∅ + 诊断（不写、不兜底） | L-5；N9 |
| **CT-L5** | 反解产出键模板的序数占位与实写键一致（键序数 = 声明序，§5） | N6 + §5 顺序单源 |
| **CT-L6** | **读写一致**：读侧投影位置集合 ≡ 写侧物化位置集合（同一 `IPlacementResolver` 配置） | L-6；通用篇 CT-4/CT-5 |

### 13.7 迁移（L1–L4）

| 步 | 内容 | 验收 |
|---|---|---|
| **L1 loc 空间** | 枚举并在 `.ushar` 声明现有 loc 取值（实测仅 `workflowId` 一值 → 先扩受控词集，见 U17）；闸A 生效 | 未声明 loc 报错；现有写入不受影响 |
| **L2 反解表** | `[Matrix] LocToPlacement` + 新发射器 → `PlacementSpec`；与现状声明**等价门**对拍 | CT-L2 全绿 |
| **L3 端口接入** | Node 侧加 `IPlacementResolver`（Null 实现 + 可见化）；`PlacementSpec` 声明退役（逐项退役条件） | CT-L1/CT-L3/CT-L4 |
| **L4 读侧切换** | `NodeReadProjection` 经同一反解取位置 | CT-L6（读写一致）+ 通用篇 CT-4/CT-5 |

### 13.8 未决项（接 §9 U16）

| # | 未决项 | 影响 | 备选 |
|---|---|---|---|
| **U17** | loc 受控词集与层级：现状 `Location = workflowId` **单值**，是否升为分层坐标（`session.*` / `cline.session.*` / `archive.*`）+ 是否允许多值 loc | 决定反解表规模与"一轮多位置"能力 | A 单值→多值 + 分层（推荐，与应用篇 placement 清单一致）／B 保持单值（表达力不足）／C 多值不分层 |
| **U18** | 反解表的**属性面表达**：`[Matrix] … over=Loc` 内如何写 `carrier/form/retention/writer`（一矩阵多属性 vs 每属性一矩阵）？是否需 P1–P7 之外的新字段 | 触 README §7.5 红线与否；决定发射器复杂度 | A 多矩阵（每属性一矩阵，零新字段，推荐）／B 单矩阵 + 属性行约定（需评审）／C 新增 `carrier=` 字段（走语法扩张评审） |
| **U19** | 反解方向是否单向（loc → placement） | 是否允许 placement 回写 loc（双写风险） | A 单向（推荐）／B 双向（禁，P22） |
| **U20** | loc 受控词集是否与 UR `ResourceIdentifier.Location` **同源** | 决定是否出现两个 Location 词汇表 | A 同源（推荐，L-7）／B 各自命名空间 + 映射表（弱耦合） |
| **U21** | `PlacementSpec` 退役后，Node 侧是否保留"位置声明 JSON"作为**无语言环境**的降级路径 | 影响纯 C# 宿主（不接 Usharp）可用性 | A 保留（Null resolver + JSON 声明，推荐）／B 仅语言路径（收缩宿主面） |

---

## §14 表达形态实测（2026-09-26；修正 §13.2 的 `[Matrix]` 写法）

> **结论**：§13.2 画的 `[Matrix] LocToPlacement: over=Loc`（行 = 位置属性 = loc 坐标）**在现有语言面不可表达**；可行形态见 §14.2。本节为**实测修正**，不改 §13 的裁决方向（位置 = loc 反解、不手写声明）。

### 14.1 实测（`Usharp.Cli build`，可复现）

| 形态 | 实测诊断 |
|---|---|
| §13.2 原样：`[Space] Loc` + `[Space] Placement: … \| contains=Loc` + `[Matrix] … over=Loc` | ❌ **6 错**：`US1003`×3（行输出不在 `over=` 空间）+ `US1009`×3（子空间坐标不在父空间） |
| 反解矩阵 `over=` 子空间（读祖先 loc、写自身位置坐标） | ❌ **3 错**：`US1009`×3 |
| 单一键空间（loc 值 ∪ 位置 id）+ `[Matrix] over=该空间`，行 = `⟨位置 id = loc 值⟩` | ✅ **0 错 0 警告** |

**根因**：`[Matrix]` 行的输出坐标须 ∈ `over=` 空间自身（`US1003`）；`contains=` 子空间的坐标须 ⊆ 父空间（`US1009`）。位置属性（carrier / keyTemplate / form / retention / writer）**不是 Loc 坐标**，故不能作 `over=Loc` 的行输出；反过来把位置另立子空间又触 `US1009`。两条门同时关上，§13.2 的写法无解。

### 14.2 落定形态（零新增语言单元）

```
[Space] Loc: kind=keys            ← loc 受控词集（单源；闸A 未声明即 error）＝ loc **值**的下划线形
  agent_round

[Space] LocPlacement: kind=keys    ← loc 值 ∪ 位置 id（同空间的代价见下）
  agent_round
  archive_raw
  …

[Matrix] LocToPlacement: over=LocPlacement
  archive_raw = agent_round        ← 行 = ⟨位置 id = loc 值⟩
  …
```

> 映射键 = **loc 值**（非坐标名）：运行期 `IPlacementResolver.Resolve(loc, …)` 按值查表；现状 loc 单值 =
> 节点 ThreadId 缺省 `agent-round`（`ReferenceGrounding` 的 `Location = workflowId`；`NodeKeys.DefaultWorkflowId`）。

**两条代价（须登记，不得静默）**：

1. **两词表同居一空间**：`LocPlacement` 同持 loc 值与位置 id ⇒ 需机器判据区分两子集（= `Check-NodeLocPlacement` C2：坐标集恰 = 位置 id ∪ Loc 词表，双向无游离）。这是「零新增语言单元」的代价，替代方案是 `[Matrix]` 行新增属性字段（U18 选项 C，走 README §7.5 语法扩张评审）。
2. **坐标名禁 `-`**（`UsharpIdentifier`：`[A-Za-z_][A-Za-z0-9_]*`，因 `-` 与线性表达式减号同形）⇒ 位置 id 在声明面用**下划线形**（`archive_raw`），与 kebab 形（`archive-raw`）之归一由 C 侧反解表装配承担（唯一映射点，禁第二处换算）。

### 14.3 消费点与门禁（治 P23：声明不得无消费）

| 项 | 落点 |
|---|---|
| 声明面 | `Projects/Plug/Agent/Config/nodes/placement.ushar` |
| 发射器 | `Frame/Usharp/src/Usharp.Emit/UsharpLocPlacementEmitter.cs`（中立 JSON；两侧 `_`→`-` 归一唯一映射点 = `ToRuntimeId`） |
| 装配消费 | `Agent.Modules/NodeSpec/NodePlacementDeclaration.cs`（**无降级**：缺失/编译失败/发射失败 ⇒ 抛）→ `NodeFrameworkSpecLoader`（映射**工厂**形态，按域裁剪）→ `DeclaredPlacementResolver` |
| 禁手写 | JSON 声明面出现 `locToPlacement` ⇒ Error `spec.loc-to-placement-forbidden`（手写即双真源 P22） |
| 门禁 | `Check-NodeLocPlacement.ps1`（C1 闸A 编译 0 错 / C2 反解域完备 / C3 反解唯一 · 每位置恰一行 / C4 正对照 / C5 负对照锁死 §14.1 不可表达形态）→ `GateManifests.psd1` 的 `NodeLocPlacement` |
| 用例 | `usharp.emit` 套件 2 例（声明产出逐字段 + 缺反解域不产出 `US5006`）；`Agent.Modules.Tests` 1 例（每域解析器由声明面支撑且按域裁剪） |
| 待接（U12 L4） | 读/写路径经**同一** `IPlacementResolver` 取位置（L-6）；`PlacementSpec` 手写声明逐项退役（U21=B：不留 JSON 降级路径） |

> 排序增量（I0–I7）见 `Projects\Plug\Agent\doc\节点键关系彻底重构-坐标声明化与框架算键方案.md` §0.5 修订 4；其中 I3 起涉 `Usharp.Emit` 改动，须**重烘焙 Frame 二进制**方可被 Agent 装配面消费。

### 14.4 `[Ref]` 承载「边类型 + loc 挂载」的字段面实测（2026-09-26）

§13.2 写的 `[Ref] … loc=` **不可直接书写**：`[Ref]` 的字段面 = `space` / `key` / `src` / `tgt` / `intent` / `edge` / `scope`
（`UsharpLanguage.Field*`；另有 `[Edge]` 的 `ur=`），**无 `loc=`**。实测（`Usharp.Cli build`，夹具含三形态）：

| 形态 | 实测 |
|---|---|
| `[Ref] RefA: History:history_id \| edge=history_owns_task \| src=task_id \| tgt=history_id \| intent=consume` | ✅ 通过 |
| `[Ref] RefB: Task:task_id \| edge=cross_db_link \| … \| scope=agent-run \| …`（`agent-run` 已声明为 `[Obs] kind=scope`） | ✅ 通过 |
| `[Ref] RefC: History:history_id \| loc=agent-round \| intent=consume` | ⚠️ **0 错 0 警告**（字段被**静默忽略**） |

合计输出：`空间 2 / 坐标 3 / 引用 3 / 边 3 / 错误 0 / 警告 0`。

**两条结论**：

1. **`edge=` / `scope=` 可用** ⇒ 归属边与挂载面可由既有 `[Ref]` 承载（零新增语言单元）。
2. **未知字段静默忽略是判据缺口**：`loc=` 既不报错也不生效 ⇒ 按 U12 §13.2 原样书写会产出
   **假声明**（"看起来声明了、实际无作用"），与 `ANTI-DEAD-GATE` / 静默丢弃声明同族。
   ⇒ 任何依赖 `[Ref]` 承载新语义的方案，必须同时提供**字段白名单门禁**（见
   `节点三转换-应用工作流化-下一步实施规划.md` §4 I9-⑤），否则"声明了"无法与"生效了"区分。


