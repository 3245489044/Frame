# DataCenter · 可追踪可维护数据流图框架（设计分析与实施）

> **日期**：2026-09-12 | **定位**：Frame 平台层（Layer 3，构建在 `Frame\DataCenter` 之上） | **性质**：设计分析 + 分步实施（未动代码）
> **一句话**：中心化解决「数据在哪」，数据流图解决「数据怎么流、流过了谁、为什么」——把运行时数据统一投影为**语言无关的图**（CSG CanonicalForm），以**三条追踪链**（版本/因果/代际）与**注册制契约**保证可追踪、可维护。
> **关联**：`DataCenter-架构设计.md`（立项与红线）、`DataCenter-通用化重构与实施计划.md`（G1–G8 通用化）、`Docs\analysis\2026\6\6.28\CSG统一框架_架构设计.md`（IR 权威）、`Docs\analysis\2026\8\8.25\2026-08-25-LoopFoldMethod-Reference-层级关系整理设计.md`（时间/空间二抽象）

---

## 0. 结论先行

| # | 结论 | 依据 |
|---|---|---|
| C1 | **图是投影，不是第二真源**。状态真源唯一在 DataCenter（`StateCenter` + `IDataCell`）；图由投影器**从状态派生**并只读输出。图侧另开写口 = 双写复活，是本设计的第一红线 | 对齐 SSOT（`StateRootSingleSource`）与 ANTI-PARALLEL-RACE |
| C2 | **可追踪 = 三条链齐备**：① 版本链（谁在何时以什么序号写的）② 因果链（这一步因何而来）③ 代际链（跨插件代数是否连续）。缺任一条即不可追踪 | §5 |
| C3 | **可维护 = 注册制 + 单写口 + 门禁**。tier / nodeKind / edgeKind / 键方案全部注册而非封闭枚举；一切写入过端口；契约与中立性由脚本门禁约束 | §6 |
| C4 | **时间与空间正交**：折叠（`IFoldOperator`/`FoldTrace`）管「怎么一步步推进」，引用图（`IRefProjector`/`RefHead`）管「这些点彼此什么关系、归属谁」。图框架是后者的**载体升级**，不是折叠的替代 | LoopFold Reference 设计 RP1 |
| C5 | **落地顺序**：投影基座 → 图契约 → 追踪面 → 域接入 → 使用面（§7 的 D1–D5）。**先有投影，再谈图**；先对接既有引用图，**不重写**它 | §7/§8 |
| C6 | **诚实边界**：当前 `DataCenter.Core` 虽引用 `CSG.Unified` 但**零 CSG 代码**（无投影实现）；既有引用关系真源在 SQLite（`ur_nodes`）与 LiteDB（`graph-log.db`）；C#↔TS 往返未验证。故本框架第一阶段是「建投影 + 对接」，而非「换真源」 | §1/§10 |
| **C7** | **整体数据流由一个根实例持有**（状态 + 图 + 追踪 + 注册表 + 代际）。根是使用层唯一的持有物，也是热换代的交换点；**禁止平行根、禁止静态槽、禁止兜底造根** | §12（对齐 SSOT `StateRootSingleSource` 与通用化 G7.1–G7.3） |

---

## 1. 问题陈述：能力已备，缺的是「图」与「追踪」

### 1.1 三处已就位的既有事实（代码可查）

| # | 能力 | 现状事实 | 位置 |
|---|---|---|---|
| F1 | **中心化状态** | 唯一状态中心（scope 分区 / Config-Session-Transient 分层 / 事务 / 订阅去重），后端经端口 `IDataCell` 注入，引擎设施已下沉 Adapters | `Frame\DataCenter\DataCenter.Core`、`DataCenter.Abstractions`、`DataCenter.Adapters` |
| F2 | **语言无关 IR** | CSG `CanonicalForm{nodes,edges,metadata}` + 节点/边类型 + URI 寻址 + JSON 往返；`csg://runtime-data` 扩展档已就位（tier 注册制） | `Frame\CsharpPrj\Protocol\CSG\CSG.Unified`、`Frame\DataCenter\schemas\csg-runtime-data.schema.json` |
| F3 | **引用图与派生** | 实体真源在 SQLite `ur_nodes`；**边真源在 LiteDB `graph-log.db`**，唯一写边 owner = `ReferenceGraphCoordinator`；派生投影 `IRefProjector`/`RefHead`；跨介质由 `NodeGraphReconciler` 最终一致对账 | `Frame\CsharpPrj\Operation\DataStoreDriven\{Graph,Core,Sqlite}`、`LoopFoldMethod\Reference` |

### 1.2 五处缺口（本框架要补的）

| # | 缺口 | 后果 |
|---|---|---|
| G1 | **无运行时数据投影**：`DataCenter.Core` 零 CSG 代码，`IStateProjector` 未建 | 状态只存在于内存/存储单元，无法导出为可读、可跨语言的图 |
| G2 | **无统一追踪字段**：状态写入无 `writer/at/seq/causedBy`（CSG 元数据只有 `generation/fingerprint/schemaVersion`，粒度为文档级） | 出问题只能靠日志倒推，"这一步为什么是这样"不可答 |
| G3 | **类型与键未全注册制**：`NodeKind/EdgeKind` 无注册表；边类型常量分散（`history_owns_task`/`session_owns`/`round_sequence`/`Owns`/`Calls`/`exports`/`cross_db_link`/`node_to_node`） | 新类型靠约定，跨模块漂移，图不可长期维护 |
| G4 | **双写风险未制度化**：状态（内存/存储单元）与落盘实体（`ur_nodes`）历史上曾出现"先内存后落库"漂移 | 崩溃窗口内状态与图不一致；需一条纪律 + 门禁，而非人治 |
| G5 | **无追踪查询面**：没有"按 scope/tier 取子图""取某点祖先/后代""反向引用报告""跨代数 diff"的统一入口 | 可诊断性停留在"人读日志"，无法支撑可视化与回放 |

> **定位判断**：中心化已经把「数据」收拢了；下一步不是再造一个数据容器，而是给这个容器装上**投影出口**与**追踪坐标**。图框架 = 投影器 + 图契约 + 追踪面 + 查询面，四件套。

## 2. 第一原则（框架不变量 DF1–DF8）

> 每条都给出**可检查判据**与**对应反模式**；违反即不得进入实现（同"通用化"文档的 G1–G6 风格）。

| # | 不变量 | 内容 | 判据 | 反模式 |
|---|---|---|---|---|
| **DF1** | **单一真源（图是投影）** | 状态真源唯一在 DataCenter（`StateCenter` + `IDataCell`）；图由 `IStateProjector` 从状态**派生**，图侧不存在任何独立写口 | 图中任一节点/边可回溯到一条状态写记录；`grep` 图侧无 "写状态" 调用 | 图库与状态双写、`vars` 与落盘双写 |
| **DF2** | **一切经端口** | 存储单元、派发、投影、迁移、代际参与一律经 `DataCenter.Abstractions` 端口注入；Core 不出现引擎/宿主设施 | 复用 `Check-DataCenterGenericity.ps1 -Check`（G2 设施面 0 违规） | 在 Core 直接 `new StorageCell(...)`/直接引宿主 Block |
| **DF3** | **可追踪（每点可回溯）** | 每个数据点携带四要素：`writer`（写者身份）、`at`（逻辑/物理时刻）、`seq`（同 scope 单调序号）、`causedBy`（因果前驱） | 抽样 10 个节点，四要素齐备且 `causedBy` 链路可闭合 | 只记录值不记录来源；追踪信息靠日志外挂 |
| **DF4** | **只增不丢** | 历史只增；有界遥测窗口允许截断，但被弹出的条目必须**归档**到持久化历史 | 归档命中率 100%（窗口外仍可查） | 截断即丢弃（推理失忆） |
| **DF5** | **边即断言（须验证）** | 每条边是对现实的断言，必须指向存在的实体；悬空边禁止写入或即时降级为 `suspected` | 图校验：悬空边数 = 0（或全部标记 suspected） | 引用写入不校验、删除实体留悬空边 |
| **DF6** | **时间与空间正交** | 折叠（时间/推进）与图（空间/结构）是两个平行抽象，不得把图结构塞进状态带 | 图中不存在"仅为折叠服务的冗余边"；`FoldTrace` 不含图结构字段 | 把层级树塞进 `FoldVertex`，污染只增带语义 |
| **DF7** | **注册制而非封闭枚举** | tier / nodeKind / edgeKind / 键方案 / 域命名空间全部注册：默认集只是**示例**，扩展=增条目不改文法 | 新增一种节点类型只需注册表加一行，无需改框架代码 | schema 里硬编码 `(config|session|transient)`；边类型常量散落各处 |
| **DF8** | **单根持有（SSOT）** | 整体数据流（状态/图/追踪/注册表/代际）由**一个根实例**持有；根是使用层唯一持有物与换代交换点。平行根、静态槽、"兜底造根"一律禁止 | 进程内根实例 ≤ 1；产品侧 `grep "new StateCenter("` 归零；缺根时 fail-fast | 每个插件各建一个状态中心；`center ?? new StateCenter(...)` 兜底第二根 |

### 2.1 为什么 DF1 是第一位的（决策痕迹）

中心化的直接收益是"不再有第二份真源"。若图框架自带写口（例如"图库是权威，状态回填自图"），就把刚收敛的双写重新打开，且这次更难查——因为图看起来更"完整"。

**决策**：图**永远**是 `Project(state)` 的产物；图侧只读 + 只做派生的派生（如反查索引）。
**已废弃选项**：① 图库作为权威、状态作为缓存（缓存一致性问题回潮）；② 双向同步（两个写口 = 双写）。

### 2.2 DF4/DF5 的边界（诚实说明）

- DF4 不承诺"无限保留"：承诺的是 **可归档**（窗口内直查、窗口外经归档查）。
- DF5 不承诺"边永远有效"：删除实体时，边要么级联删除（结构边），要么降级标记（历史因果边必须留痕——删掉它等于删历史，违反 DF4）。

## 3. 分层架构：从框架层面到使用层面

### 3.1 五层 + 一层观测面

```
┌───────────────────────────── L5 使用层（产品） ─────────────────────────────┐
│  Agent / Engine / FrontEndDriven：只经「域 Gateway」读写，不持运行数据        │
│  观测面：dump / TraceQuery / 可视化 / 回放 / 跨语言 Adapter                    │
└───────────────▲───────────────────────────────┬─────────────────────────────┘
                │ 域视图（强类型，域语义）        │ 追踪查询（图）
┌───────────────┴───────────────────────────────▼─────────────────────────────┐
│  L4 域扩展层  DataCenter.Extensions.*                                        │
│   · 域键方案（键名唯一权威）  · 域投影（把域语义映射为节点/边）  · 域 Gateway   │
│   例：LoopDecision（G1 已建）；GraphProjection（本框架新增）                   │
└───────────────▲───────────────────────────────┬─────────────────────────────┘
                │ 通用能力（分层客户端/状态中心） │ 端口实现（投影/迁移/代际）
┌───────────────┴───────────────────────────────▼─────────────────────────────┐
│  L2 核心层 DataCenter.Core（通用，无领域词）   L3 适配层 DataCenter.Adapters   │
│   StateCenter / StateStore / Layering /        StorageCellAdapter /           │
│   InMemoryDataCell / TierStateProjector★        BlockStateInitializer /       │
│                                                AppServiceRegistration        │
└───────────────▲───────────────────────────────┬─────────────────────────────┘
                │ 只依赖端口                      │ 端口实现（引擎设施仅在此）
┌───────────────┴───────────────────────────────▼─────────────────────────────┐
│  L1 端口层 DataCenter.Abstractions（零依赖）                                   │
│   IDataCell / IDataCellFactory / IStateDispatch / IStateInitializer           │
│   ★IStateProjector / ★IStateMigration / ★IGenerationParticipant（待建）       │
└───────────────▲─────────────────────────────────────────────────────────────┘
                │ 语言无关表示
┌───────────────┴─────────────────────────────────────────────────────────────┐
│  L0 协议层 CSG（CSG.Unified / TsWorkspace.Protocol）                          │
│   CanonicalForm{nodes, edges, metadata} · URI 寻址 · 节点/边/元数据契约        │
│   Profile：csg://runtime-data（tier 注册制 / generation / fingerprint / sv）   │
└─────────────────────────────────────────────────────────────────────────────┘
```

> **根实例的位置（§12）**：以上五层之上不另设"层"，而设**一个根实例**——它是 L5 使用层唯一持有的对象，向下持有 L2 的状态中心/L1 的端口实现/L0 的投影出口，向上经端口（`IRootProvider`）被深层组件取得。整体数据流（状态 + 图 + 追踪 + 注册表 + 代际）全部挂在它身上，**不散落在各层**。

### 3.2 每层职责 / 禁止 / 判据

| 层 | 职责 | 允许 | **禁止** | 判据 |
|---|---|---|---|---|
| **L0 协议** | 定义语言无关的图表示与寻址 | 扩展 Profile（`csg://runtime-data`）；增注册条目 | 修改 CSG 主 schema；在协议里写领域语义 | 主 schema 语义冻结（R-DC1） |
| **L1 端口** | 声明 IoC 接缝 | 纯接口 + 值类型 | 引任何工程/第三方；放实现 | `<ProjectReference>` 数 = 0 |
| **L2 核心** | 通用状态能力 + 默认实现 + 通用投影 | 通用键方案/分层客户端/内存单元/结构投影 | 领域词汇；引擎设施（除契约强制白名单） | `Check-DataCenterGenericity.ps1 -Check` |
| **L3 适配** | 把引擎/宿主设施适配为端口 | 引引擎工程；实现端口 | 定义通用语义；被 L2 反向引用 | 引用方向单向（L3→L2/L1） |
| **L4 域扩展** | 域键方案 + 域投影 + 域 Gateway | 消费 L1/L2/L3；引域权威（如裁决库） | 被 L2 反向引用；自建状态存储 | 引用方向单向（L4→L2） |
| **L5 使用** | 产品装配 + 观测面 | 只经域 Gateway；注入端口实现 | 自持运行数据（`static` 槽）；插件内自建状态中心 | `grep new StateCenter(` 于产品侧归零 |

### 3.3 一次写入的完整路径（使用层看得见的部分）

```
域写者（L4/L5）
  │ ① 域语义 → 键（DomainScheme.Session(scope, key)）
  ▼
LayeredStoreClient（L2 通用通道）
  │ ② 单写口 StateCenter.Write + 事务/静默写（Transient）
  ▼
IDataCell（L1 端口）→ 具体单元（L2 InMemoryDataCell / L3 StorageCellAdapter）
  │ ③ 变更事件（单物理事件源）
  ▼
IStateDispatch（L1 端口，可空=内联）→ 订阅中枢（scope 版本去重）
  │ ④ 投影触发（轮边界/显式）
  ▼
IStateProjector（L1 端口）→ CanonicalForm（L0 协议）★本框架新增
  │ ⑤ 追踪字段装配（writer/at/seq/causedBy）
  ▼
追踪查询 / 可视化 / 回放 / 跨语言 Adapter（L5 观测面）★本框架新增
```

**使用层的三条"看得见"的入口**（框架对使用者的全部承诺）：
1. **写入**：`域 Gateway.Write(...)` → 框架负责"落状态 + 记追踪 + 投影"。
2. **读取**：`域 Gateway.View()/Get(...)`（强类型域视图）与 `TraceQuery.*`（图查询）。
3. **导出**：`Export(scope) → CanonicalForm`（可 dump、可跨语言、可回放）。

## 4. 数据模型：图契约（可追踪字段是本设计的核心增量）

### 4.1 节点（Node）—— 携带追踪坐标的值

```csharp
/// 追踪坐标：每个数据点的"来源证明"（DF3 四要素 + 代际）
public readonly record struct TraceStamp(
    string Writer,          // 写者身份（域/组件/actor 名，跨语言稳定，非进程 id）
    long Seq,               // 同 scope 单调序号（取自订阅中枢版本表，去重依据）
    DateTimeOffset At,      // 逻辑/物理时刻
    string? CausedBy,       // 因果前驱节点 Id（无前驱=null，如外部输入）
    int ChainDepth,         // 因果链深度（对齐终答推导的 depth 语义）
    int Generation,         // 插件代数（跨代际连续性的锚）
    string? Fingerprint);   // 内容指纹（换代去重）

public readonly record struct GraphNode(
    string Id,                                     // csg://{domain}/{tier}/{scope}/{key}
    string Kind,                                   // 注册制：state.value / decision.record / task / ...
    string Label,
    IReadOnlyDictionary<string, object?> Props,    // 业务属性（值、可见性、类型…）
    TraceStamp Trace);
```

### 4.2 边（Edge）—— 断言式，可降级但必须留痕

```csharp
public enum EdgeAssertion
{
    Asserted,   // 已断言（两端实体存在，默认）
    Suspected,  // 疑似（目标缺失/未确认；不删除，降级保留）
    Retracted,  // 已撤回（历史因果边被撤销；保留痕迹，违反 DF4 的"删除"被禁止）
}

public readonly record struct GraphEdge(
    string Id,
    string SourceId,
    string TargetId,
    string Kind,                                   // 注册制：structure.owns / causality.derives / ...
    EdgeAssertion Assertion,
    IReadOnlyDictionary<string, object?> Props);   // condition / weight / at / writer
```

### 4.3 边类型登记表（既有边 → 新 Kind 映射，避免语义漂移）

| 分类 | 新 Kind（注册制） | 语义 | 既有边（现散落常量） | 处置 |
|---|---|---|---|---|
| 结构 | `structure.owns` | 归属/包含 | `history_owns_task`、`Owns` | 映射归一 |
| 结构 | `structure.session_owns` | 会话归属 | `session_owns` | 映射归一 |
| 结构 | `structure.sequence` | 时序/顺序 | `round_sequence` | 映射归一 |
| 因果 | `causality.derives` | 因→果派生 | `node_to_node`、`causedBy` | 映射归一 |
| 因果 | `causality.invokes` | 调用 | `Calls` | 映射归一 |
| 数据 | `data.cross_store` | 跨介质数据依赖 | `cross_db_link` | 映射归一 |
| 数据 | `data.keymap` | 键映射 | `KeyMappingToNode` | 映射归一 |
| 引用 | `reference.exports` | 导出可见性 | `exports` | 映射归一 |
| 引用 | `reference.points_to` | 一般指向 | CSG `Reference` | 保留 |
| 控制 | `control.transition` | 状态/控制转移 | CSG `Transition`/`ControlFlow` | 保留 |

> **归一的收益**：边类型从"8+ 处常量"收敛为"1 张注册表"，新增类型 = 注册一行（DF7）；且分类（结构/因果/数据/引用/控制）直接决定 DF5 的删除策略（结构边可级联删、因果边只可 Retract）。

### 4.4 元数据（Metadata）—— 文档级追踪摘要

```json
{
  "csgVersion": "1.0.0",
  "schema": "csg://runtime-data",
  "kind": "mixed",
  "domain": "<域命名空间>",
  "tiers": ["config", "session", "transient"],
  "scope": "<分区标识>",
  "generation": 3,                      // 代数（§5.3）
  "fingerprint": "<内容指纹>",
  "schemaVersion": 2,                   // 结构版本（迁移触发）
  "seqRange": [120, 187],               // 本投影覆盖的序号区间（可校验连续性）
  "writers": ["<写者1>", "<写者2>"],     // 参与写者集合（可审计）
  "created": "2026-09-12T00:00:00Z"
}
```

> `seqRange` 是关键增量：它让"这张图是否覆盖了我关心的全部写入"变成**可校验**问题（区间连续 ⇒ 无缺口；有断点 ⇒ 窗口截断，须查归档，DF4）。

### 4.5 寻址：分层键 ↔ URI

| 层 | 形态 | 例 |
|---|---|---|
| 域键（写者用） | `Scheme.Session(scope, key)` → `LayeredStateKey` | `StateBlock.Decision.Session:taskA:core.token.value` |
| 全键（存储用） | `{namespace}:{scope}:{key}` | `StateBlock.Decision.Session:taskA:core.token.value` |
| **图 URI（导出用）** | `csg://{domain}/{tier}/{scope}/{key}` | `csg://StateBlock.Decision/session/taskA/core.token.value` |

映射规则：`namespace` 的 `{domain}.{tier}` 后缀展开为 URI 的 `{domain}/{tier}` 两段（`StateLayerScheme.ForDomain` 已按 `{domain}.{tier}` 生成该后缀；通用化文档 §4 计划更名为 `TierNamespaceScheme`）。**唯一映射点**放在核心层（与 G2 的 `TierMap` 同级），禁止各域自行拼 URI。

### 4.6 注册表（框架唯一"类型知识"来源）

| 注册项 | 默认示例集 | 登记位置 | 扩展方式 |
|---|---|---|---|
| `tier` | `config` / `session` / `transient` | `schemas\csg-runtime-data.schema.json → metadata.tiers` | 域声明（DF7） |
| `nodeKind` | `state.value`、`state.trace`、`decision.record`、`task`、`node.output`、`resource` | 图域注册表（L4） | 注册一行 |
| `edgeKind` | §4.3 十项 | 图域注册表（L4） | 注册一行 |
| `domain` | 域命名空间（如 `StateBlock.Decision`） | `StateLayerScheme.ForDomain` | 域声明 |
| 键名 | 域键常量（如 `core.token.value`） | 域 `Keys` 类（如 `LoopKeys`） | 域声明 |

## 5. 追踪机制：三条链 + 一个查询面

> 可追踪不是"多打日志"，而是**在数据里带上坐标**。三条链各自有唯一权威写入点，互不代偿。

### 5.1 版本链（谁、何时、第几次）

| 项 | 设计 | 权威来源 |
|---|---|---|
| `Seq` | 同 scope 单调递增序号（不要求全局唯一，只要求**可比**） | 订阅中枢版本表（`GetLastVersion(ns, scope)+1`），当前 `StateCenter.Write` 已如此取版本 |
| `Writer` | 写者身份字符串（域/组件/actor 名），跨语言稳定 | 由域 Gateway 显式传入（禁止用进程 id / 线程 id） |
| `At` | 逻辑时刻优先（事件序号），物理时刻兜底 | 写口统一盖章 |
| 去重 | 版本 ≤ lastSeen 丢弃（现状已有） | 订阅中枢 |

**判据**：对同一 scope，`Seq` 集合无重复、无回退；缺口只能是"窗口截断"且必须能经归档补齐（DF4）。

### 5.2 因果链（这一步因何而来）

- 每个节点带 `CausedBy`（前驱节点 Id）与 `ChainDepth`（深度）。
- 链的"终点裁决"复用既有语义：**取因果链最深的终点**（`argmax chain depth`），而非"最后一次写入"——这是本框架与"日志时间序"的关键区别：因果链能区分"分支试探"与"主线推进"。
- 多前驱（合并点）时：`CausedBy` 记主因，其余前驱以 `causality.derives` 边表达（图天然支持多前驱，不塞进单个字段）。

**使用层收益**：给定"最终答案"节点，可反向列出它的完整推导链（谁贡献了什么），这正是"可解释"的最小充分条件。

### 5.3 代际链（跨插件代数是否连续）

| 字段 | 语义 | 权威 |
|---|---|---|
| `generation` | 插件代数（热更新次数） | 代际管理（`IGenerationParticipant` 生命周期） |
| `fingerprint` | 内容指纹（换代去重、避免同位重放） | 内容哈希 |
| `schemaVersion` | 结构版本（触发迁移） | `IStateMigration` |

**判据（对齐既有验收）**：换代前后同一 scope 的状态**连续**（`Seq` 不重置、值不丢），旧代数 ALC 可回收。代际链断裂（如换代后 `Seq` 归零）等同"追踪失效"，必须以迁移/导入显式修复，而非容忍。

### 5.4 写入即追踪：三阶段提交（无补偿不落追踪）

```
① Commit   写状态（单写口） + 盖章 TraceStamp（writer/at/seq/causedBy）
② Record   追加追踪记录（只增；窗口外归档）
③ Observe  出队派发订阅/投影（IStateDispatch；写线程不执行回调）
```

| 阶段 | 失败处置 | 禁止 |
|---|---|---|
| Commit | 事务回滚，不产生任何追踪痕迹 | 状态写了但追踪没写（**悬空状态**） |
| Record | 追踪写失败 ⇒ 整次写入失败（追踪是写入的一部分，不是旁路日志） | "先写状态，日志失败就算了" |
| Observe | 派发失败可重试；投影可重算（幂等） | 把投影失败升级为状态写失败（投影是派生物） |

> 这条纪律把"可追踪"从**约定**变成**不变式**：状态与追踪同生共死；投影可重算，因此允许最终一致。

### 5.5 查询面（`TraceQuery`，使用层唯一图入口）

| 方法 | 语义 | 典型用途 |
|---|---|---|
| `Project(scope, tiers?)` | 状态 → `CanonicalForm`（本框架核心） | dump / 导出 / 跨语言 |
| `Node(id)` / `ByKind(kind, scope?)` | 取点 | 面板渲染 |
| `Ancestors(id, depth?)` | 因果祖先链 | "为什么是这个值" |
| `Descendants(id)` | 因果后代 | 影响面分析 |
| `OutEdges(id)` / `InEdges(id)` | 邻边 | 图遍历 / 可视化 |
| `ReverseRefs(targetId)` | 反向引用报告 | 变更影响分析（对齐 DSL 校验器的反向引用报告） |
| `Diff(scope, seqA, seqB)` | 两次序号间的图差异 | 回放 / 归因 |
| `Validate(form)` | 悬空边 / 环 / 注册项校验 | 门禁（DF5/DF7） |

**性能边界（诚实）**：图查询面向**诊断与观测**，不承诺高并发 OLTP 语义；大 scope 采用"按 kind/tier 过滤 + 分页"，投影按需触发（轮边界/显式），而非每次写入全量重算。

### 5.6 追踪失败模式对照表

| 失败模式 | 表现 | 本设计的检测点 |
|---|---|---|
| 未记录修复（改了但没说） | 值与来源不符 | 追踪记录与状态同事务（§5.4 ②） |
| 推理失忆（历史被截断且未归档） | `Ancestors` 断链 | `seqRange` 连续性校验 + 归档命中率（DF4） |
| 静默降级（失败被吞） | 图少了一块却无标记 | 边 `Assertion=Suspected`；投影报告缺口而非吞掉 |
| 追踪漂移（跨介质不一致） | SQL 实体与图边不匹配 | 复用既有对账器语义（`NodeGraphReconciler`），在 `Validate` 中暴露 |
| 支线冒充主线 | 最终答案归因错 | 因果链 `argmax depth`（§5.2），而非时间序 |

## 6. 可维护性机制：注册制、门禁、迁移、纪律

### 6.1 注册制与唯一权威（"知识"只存一份）

| 知识 | 唯一权威 | 消费方 | 违反后果 |
|---|---|---|---|
| 键名与命名空间 | 域 `Keys` 类（如 `LoopKeys`）+ `TierNamespaceScheme` | 域 Gateway、投影器 | 魔法键散点、跨模块拼错 |
| tier 集合 | schema `metadata.tiers`（注册） | 投影器、跨语言 Adapter | 硬编码 `config|session|transient` |
| nodeKind / edgeKind | 图域注册表（L4 单一位置） | 投影器、校验器、可视化 | 类型漂移、语义重载 |
| 因果深度 / 终点裁决 | 因果链投影（单点实现） | 使用层归因 | 各处各算，归因不一致 |
| 追踪字段格式 | `TraceStamp`（L1 值类型） | 所有写者 | 字段名/语义分裂 |

> 纪律：**新增类型/键 = 注册一行**；出现第二处同名知识即视为缺陷（对齐 `SingleImplementation` / `RedundancyElimination`）。

### 6.2 门禁（可执行、可回归）

沿用既有 `Check-DataCenterGenericity.ps1` 的形态（两遍扫描 + `-Check` 非零退出 + 精确白名单），扩展到图契约：

| # | 检查 | 失败判据 | 对应不变量 |
|---|---|---|---|
| K1 | 领域词汇零出现（Core） | 命中 > 0（白名单外） | DF2（已有） |
| K2 | 设施端口化（Core 不自主引引擎设施） | 违规 > 0 | DF2（已有） |
| K3 | **图契约校验**：投影产物过 CSG Profile 校验（schema/URI 格式/必填字段） | 校验失败 | DF1/DF7 |
| K4 | **悬空边**：边两端必须可解析（或标记 `Suspected`） | 悬空边数 > 0 | DF5 |
| K5 | **注册完备**：节点/边 Kind 必须出现在注册表 | 未注册 Kind > 0 | DF7 |
| K6 | **环检测**：`structure.*` 层级边不得成环 | 检出环 | 可维护性（层级有向无环） |
| K7 | **追踪完备**：节点 `TraceStamp` 四要素非空（`CausedBy` 允许 null，但必须显式声明为根） | 缺失 > 0 | DF3 |
| K8 | **序列连续**：`seqRange` 与写入记录一致（允许窗口截断，但必须能解释） | 无法解释的断点 > 0 | DF4 |
| K9 | **单根与无静态槽**：产品侧不得出现 `new StateCenter(`（白名单＝组合根/测试）；不得出现静态状态/运行时槽 | 命中 > 0 | DF8 |

### 6.3 迁移与代际（结构演进不破坏追踪）

| 场景 | 机制 | 纪律 |
|---|---|---|
| 结构演进（新增字段/Kind 改名） | `schemaVersion` 递增 + `IStateMigration`（diff/apply） | **迁移失败保留旧代数**，绝不半迁 |
| 插件热更新 | `IGenerationParticipant.Export/Import`（带数据换代） | 换代前后 `Seq`/值连续；旧代数可回收 |
| 类型废弃 | `dep` 元数据 + 类型转发 + 限期退役 | 废弃类型不得被新引用（校验器强制） |
| 语义变更 | 新 Kind 注册 + 旧 Kind 映射（§4.3 表） | 禁止"同名改义"（会让历史图不可解释） |

### 6.4 演进纪律（把"经验"变成"门禁"）

1. **单一实现**：投影/追踪/裁决各只有一个入口；重复实现视为缺陷，先归一再扩展。
2. **证据优先**：任何"图正确"的论断须给出可执行检查（门禁 K1–K8 或抽样断言），不接受"看起来对"。
3. **只增不删**：历史追踪记录不可变；撤销用 `Retracted`（新事件），不用删除（DF4）。
4. **先投影后语义**：新增域接入时，先把"能投影出来"做到（结构正确），再补域语义（标签/分类），避免语义未定就上大改。
5. **每步可回退**：每个阶段独立可编译、可回滚（对标通用化文档"每阶段独立编译 + 单测通过再进下一阶段"）。

## 7. 实施路线（D0–D5：从框架层到使用层）

> 每阶段独立可编译、可回退；判据必须可执行（命令/断言），不接受"看起来对"。

### D0 · 根实例骨架（**硬前置**：整体数据流的持有者）

| 项 | 内容 |
|---|---|
| 交付 | ① `IDataCenterRoot` / `IRootProvider` 端口（L1，零依赖）② 根默认实现 `DataCenterRoot`（持有状态真源 + 投影器 + 追踪面 + 注册表 + 代际；全部经端口注入）③ 组合根装配（宿主启动构造一次）+ 缺根 fail-fast ④ `RuntimeSlot` 落位（Agent 静态槽迁入） |
| 关键决策 | **根是一个端口实现，不是新写口**（DF1）：它只**持有**已存在的组件引用，不引入第二条状态写入路径；根不含域语义（域 Gateway 仍在 L4） |
| 判据 | ① 进程内根实例 ≤ 1（构造两次 fail-fast）② 缺根时显式失败（不静默造根）③ 产品侧 `new StateCenter(` 归零（K9）④ 既有 73/73 + 396/396 零回归 |
| 回退 | 根是**装配层**新增物：回退＝恢复原装配方式，不改状态/投影实现 |

### D1 · 投影基座（框架层，最高优先）

| 项 | 内容 |
|---|---|
| 交付 | ① `IStateProjector` 端口（L1，零依赖，签名只含图契约与请求值类型）② Core `TierStateProjector`（通用结构投影：tier/scope/键 → 节点；命名空间/父链 → 结构边）③ 全键 ↔ URI 的**唯一映射点** ④ `JsonValueCodec`（值 ↔ 载荷） |
| 关键决策 | 投影是**只读派生**：`Project(state) → CanonicalForm`，绝不回写状态（DF1） |
| 判据 | ① 投影产物过 CSG Profile 校验（K3）② 新增单测：写入 → 投影 → 断言节点数/边数/URI 格式/tier 归属 ③ 往返幂等：`Project` 两次结果一致（无隐藏状态） |
| 回退 | 端口 + 实现独立，整体撤出不影响既有 73/73 |

### D2 · 图契约与追踪字段（可追踪的骨架）

| 项 | 内容 |
|---|---|
| 交付 | ① `TraceStamp` 值类型（§4.1）② `GraphNode`/`GraphEdge`/`EdgeAssertion` ③ `nodeKind`/`edgeKind` 注册表 + K5 校验 ④ schema 扩展（`seqRange`/`writers`/`assertion`）⑤ 盖章接入：`StateCenter.Write` 统一盖 `writer/at/seq`；`causedBy` 由域显式传入 |
| 关键决策 | **盖章位置**：Core 统一盖章 `writer/at/seq`（通用、不可绕过）；`causedBy/chainDepth` 属域语义，由域 Gateway 显式提供。若把因果也塞进 Core，Core 将被迫认识领域概念（违反 G1/G4） |
| 判据 | ① K5（注册完备）0 违规 ② K7（追踪完备）抽样 10 点四要素齐备 ③ 既有 73/73 + 396/396 不回归 |
| 回退 | 追踪字段为**附加**信息，未投影前不影响既有读写路径 |

### D3 · 追踪面（查询、索引、导出、归档）

| 项 | 内容 |
|---|---|
| 交付 | ① `TraceQuery`（§5.5 八项）② 反向引用索引 ③ `Export(scope) → CanonicalForm` ④ 归档策略（有界遥测窗口 + 持久化历史，保证 DF4 可归档） |
| 判据 | ① `Ancestors(终端节点)` 能闭合回根 ② `Diff(seqA, seqB)` 与写入记录逐条一致 ③ K4/K6/K8 通过 ④ 归档命中：窗口外条目可查 |
| 回退 | 查询面是只读层，撤出不影响写入与投影 |

### D4 · 域接入（对接既有引用图，**不重写**）

| 项 | 内容 |
|---|---|
| 交付 | ① 既有 `ur_nodes`（SQL 实体）与 `graph-log.db`（LiteDB 边）作为**域投影**接入：按 §4.3 映射表归一旧边类型 ② `ReferenceGraphCoordinator` 保持**唯一写边 owner**（本框架不新增写边路径）③ 图域注册表登记既有 nodeKind/edgeKind ④ 把 `NodeGraphReconciler` 的跨介质对账语义在 `Validate` 暴露 |
| 明确不做 | **不迁移真源**（实体仍以 SQL 为准、边仍以 LiteDB 为准）；不改写边实现；不动既有表结构。本阶段目标是"能一起被查询与追踪"，不是"换存储" |
| 判据 | ① 既有边 100% 可归类（无未注册 Kind）② 既有对账/持久化测试全绿 ③ 跨介质缺口在 `Validate` 中可见（`Suspected` 或报告） |
| 回退 | 映射与查询是旁路，撤出后既有链路不变 |

### D5 · 使用层（可视化、回放、跨语言、门禁）

| 项 | 内容 |
|---|---|
| 交付 | ① `DataCenter.Host --dump-state`（导出图）② 图可视化面板 / 节点分类视图 ③ 回放（按 `seqRange` 重放 + `Diff` 对比）④ 跨语言：TS Adapter（复用 `TsWorkspace\Protocol` 的 CSG 实现），同一 `CanonicalForm` 往返 ⑤ CI 门禁（K1–K8） |
| 判据 | ① C#↔TS 往返等价（同一 `csgVersion`）② 门禁纳入 CI 且可回归 ③ 热换代前后图连续（`Seq` 不重置、旧代数 ALC 可回收） |
| 回退 | 观测面完全旁路，产品主链路不受影响 |

### 7.1 阶段依赖图

```
D0 根实例骨架 ──▶ D1 投影基座 ──▶ D2 图契约/追踪字段 ──▶ D3 追踪面 ──▶ D5 使用层（可视化/回放/跨语言）
                                        │
                                        └──▶ D4 域接入（与 D3 并行可行：D2 完成后即可做映射归一）
```

> **D0 是总前置**：投影器、追踪面、注册表、代际都必须**挂在根上**（而非各自静态），否则后续每加一件都要再改一次持有关系。
> **D1 是图的前置**（没有投影就没有图）；D4 依赖 D2 的注册表与映射，但不依赖 D3 的查询面，可并行。

## 8. 与既有资产的关系（复用矩阵：谁是什么角色）

| 资产 | 位置 | 角色 | 本框架如何使用 | **禁止** |
|---|---|---|---|---|
| CSG (`CSG.Unified`) | `Frame\CsharpPrj\Protocol\CSG` | L0 协议 | 图表示、URI 寻址、JSON 往返 | 改主 schema（语义冻结） |
| `csg-runtime-data.schema.json` | `Frame\DataCenter\schemas` | L0 Profile | 承载 tier 注册、代数、`schemaVersion`；本框架扩 `seqRange/writers/assertion` | 硬编码层名（已修） |
| `StateCenter`/`StateStore`/`Layering` | `DataCenter.Core` | L2 真源 | 投影的**唯一输入**；分层键 → URI | 新增第二条状态写口 |
| `IDataCell`/`IStateDispatch`/`IStateInitializer` | `DataCenter.Abstractions` | L1 端口 | 投影/派发的注入点 | 引实现或第三方 |
| `InMemoryDataCell`/`StorageCellAdapter` | Core / Adapters | L1 实现 | 投影读数来源 | 在投影里直接 `new` 具体单元 |
| `DataCenter.Adapters` | `Frame\DataCenter` | L3 适配 | 宿主设施适配 | 定义通用语义 |
| `ReferenceGraphCoordinator`/`ReferenceGrounding` | `DataStoreDriven\{Graph,Core}` | **域真源（写边 owner）** | 只读其产物；D4 按 §4.3 映射查询 | **复制任何写边逻辑** |
| `IRefProjector`/`RefHead` | `LoopFoldMethod\Reference` | 空间视图既有实现 | 与图投影**并存**：它给域视图，本框架给语言无关图 | 用图替代域视图（域视图仍需强类型） |
| `IFoldOperator`/`FoldTrace` | `LoopFoldMethod` | 时间维度 | 不动；图中仅以 `structure.sequence` 反映顺序 | 把图结构塞进状态带（DF6） |
| `SqliteNodeStore`/`ur_nodes`、`graph-log.db` | `DataStoreDriven` | **域真源（实体/边）** | D4 经域只读对接 | 在本框架内直接读写这两处 |
| `NodeGraphReconciler` | `DataStoreDriven\Core` | 跨介质对账 | 复用其语义在 `Validate` 暴露缺口 | 再造一个对账器 |
| `Check-DataCenterGenericity.ps1` | `Frame\PowerShellScript\Operation\DSL` | 门禁工具 | 扩展 K3–K8 | 整目录泛白 / 绕过白名单 |

> **一句话分工**：*状态真源*在 DataCenter，*边真源*在域（`ReferenceGraphCoordinator` 唯一写），*图*是本框架从两者派生的语言无关视图。

## 9. 验收判据

| # | 判据 | 命令 / 方法 |
|---|---|---|
| A-DF1 | **图是投影**：投影前后状态快照不变；图侧无写状态调用 | 单测（`Project` 前后状态等价）+ `grep` 图侧无状态写口 |
| A-DF2 | **一切经端口** | `Check-DataCenterGenericity.ps1 -Check` → G1/G2 违规 0 |
| A-DF3 | **追踪完备** | K7 门禁 0 缺失 + 抽样 10 点四要素齐备断言 |
| A-DF4 | **只增可归档** | 窗口外条目经归档可查的单测；`seqRange` 连续性可解释 |
| A-DF5 | **边即断言** | K4 悬空边 0（或全部 `Suspected`） |
| A-DF6 | **时间空间正交** | `FoldTrace` 无图结构字段（`grep`）+ 设计评审 |
| A-DF7 | **注册制** | K5 未注册 Kind 0 + schema 无封闭枚举（`grep` 无 `(config\|session\|transient)`） |
| A-行为等价 | 既有行为零回归 | `StateHoldDriven.Tests` 73/73、`DataCenter.Tests` 15/15(+新增)、`Agent.Core.Tests` 396/396、`Agent.Axioms.Tests` 84/84 |
| A-编译 | 全仓绿 | `DataCenter.slnx` / `MainProj.sln` / `Agent.sln` / `GameEditor.sln` / `PrjBridge` / `FileDriven.sln` |
| A-跨语言 | **C#↔TS 同一 `CanonicalForm` 往返等价**（同一 `csgVersion`） | TS Adapter 往返单测（D5） |
| A-热换 | 换代前后图连续（`Seq` 不重置、值不丢、旧代数 ALC 可回收） | 代际单测（D5） |
| A-门禁 | K1–K8 纳入 CI 且可回归 | CI 配置 + 脚本 `-Check` 非零退出 |
| **A-单根** | **整体数据流由一个根实例持有**：进程内 ≤ 1；缺根 fail-fast；产品侧无 `new StateCenter(`、无静态状态槽；换代后图连续且旧代数 ALC 可回收 | K9 门禁 + 单测（重复构造 fail-fast / 缺根抛错）+ 代际单测（D0/D5） |

## 10. 风险与诚实边界

| # | 风险 / 边界 | 说明 | 缓解 |
|---|---|---|---|
| R1 | **双写回潮** | 图好用之后，最容易被"顺手"加一个图侧写口（例如"从图回填状态"） | 门禁 K3/K4 + 代码评审硬规则；DF1 写入文档与 csproj 注释 |
| R2 | **投影成本** | 大 scope 全量投影代价高；每写必投影会拖慢热路径 | 投影**按需触发**（轮边界/显式）+ 增量 + 分页查询；不承诺实时一致，只承诺可重算 |
| R3 | **追踪数据量** | 每次写入都记追踪，长期膨胀 | 同键连续更新可按 `seqRange` 折叠为区间；窗口 + 归档；追踪只记"状态变更"，不记用户级操作 |
| R4 | **语义漂移** | 各域自行拼 URI / 自定义 Kind 语义 | URI 唯一映射点 + Kind 注册表 + K3/K5 门禁 |
| R5 | **对接期不一致** | SQL 实体与 LiteDB 边本就跨介质（无单库原子性） | 复用 `NodeGraphReconciler` 对账语义；缺口显式 `Suspected`，不掩盖 |
| R6 | **跨语言未验证** | TS 侧有 CSG 实现但两侧往返未经同一 `csgVersion` 验证 | D5 前不宣称"语言无关已达成"；A-跨语言 为门禁项 |
| R7 | **中心化的边界** | UI 控件/native/共享栈（`WorkFlowMethod`/`Share.*`/`Plate.*`）不可热换，其状态不进入"可换代"承诺 | 三域清单化；只承诺实现域换代 |
| R8 | **"可追踪" ≠ "全量审计"** | 追踪记录的是**状态变更的因果**，不是用户级审计日志（不含权限/合规语义） | 明确定位；需要审计时另建审计面，不从追踪面强行派生 |
| R9 | **域视图不可省** | 图是通用表示，不是业务 API；产品仍需要强类型域视图 | 与 `IRefProjector`/域 Gateway 并存（§8），不互相替代 |
| R10 | **性能语义** | 图查询面向诊断与观测，不承诺 OLTP 级并发/延迟 | 显式写入口径；大图分页 + 过滤 + 只读快照 |
| **R11** | **伪根 / 静态槽回潮** | 单根要求最容易被"图方便"破坏：深层组件用静态槽取根、或某插件为省事自建第二个根（现状 `AgentOrchestrator.cs:93`/`WorkflowRunner.cs:89` 的 `?? new StateCenter(...)` 即为实例） | K9 门禁（grep 归零 + 静态槽检查）+ 缺根 fail-fast（不给"兜底"机会）+ 组合根评审 |
| **R12** | **根的职责膨胀** | 根若开始承载域语义/业务逻辑，会退化为"上帝对象" | 根只**持有与装配**（引用转发），不含域逻辑；域逻辑在 L4；根的方法面尽量窄（§12.4） |

## 11. 待决策（建议默认）

| # | 议题 | 建议默认 | 理由 |
|---|---|---|---|
| P1 | 既有 LiteDB 引用图是否最终收敛进 DataCenter 图域 | **不收敛**：保持域真源 + 对接查询 | 迁移真源风险高、收益不明；对接已能满足"一起被查询与追踪" |
| P2 | 追踪粒度 | **每键写入记追踪**，投影按轮/显式聚合 | 追踪必须完整（DF3）；投影可重算，允许粗粒度 |
| P3 | 图的主表示载体 | **CSG 文档为主表示**（导出/跨语言/回放）；查询索引另建**可重建**派生索引 | 主表示必须语言无关且可版本化；索引可丢可重建 |
| P4 | 节点标识 | 对外 **URI 字符串**，内部结构体（domain/tier/scope/key） | 对外要可读可跨语言；内部要免解析 |
| P5 | 门禁是否纳入 CI | **纳入**（对齐既有 DSL 校验器与 G2 门禁风格） | 防回退是唯一有效手段 |
| P6 | `causedBy` 的供给方 | **域 Gateway 显式传入** + 框架校验必填性（根节点显式声明） | 因果属域语义，放进 Core 会违反 G1/G4 |
| P7 | 投影触发时机 | **轮边界 + 显式调用**（默认）；可选订阅驱动 | 避免写热路径承担投影成本（R2） |
| **P8** | 根实例实现的归属：Core vs Adapters/Host | **Core 提供 `DataCenterRoot`（通用组装，全经端口注入）+ Adapters/Host 提供宿主装配工厂**（存储后端/ALC/宿主 cell 由宿主供给） | 根需持有引擎设施时若放 Core 会破坏 G2 设施面；放 Adapters 又会让"通用组装"无法被域共享 |
| **P9** | 根的取得方式优先级 | **构造注入 > `IRootProvider` 注入 > 宿主装配期服务定位器**；`static` 槽与兜底造根一律禁止 | 静态槽已被 `ExtensionAssemblyHost`（`static _context` + `isCollectible:false`）证明不可回收；服务定位器仅装配期可用 |
| **P10** | `RuntimeSlot` 的落位形态 | 作为**根的分区**（Agent 侧静态槽迁入，挂到根下），而非新容器 | 避免再造一个"第二根型"持有物 |

---

## 12. 单根持有：整体数据流收敛到一个根实例

> **要求**：整体数据流（状态 + 图 + 追踪 + 注册表 + 代际）**由一个根实例持有**。根是使用层唯一的持有物，也是热换代的交换点。
> **依据**：SSOT `StateRootSingleSource`（任一时刻整体状态恰有一个根承载者，禁止双根并存）；通用化文档 G7.1–G7.3（删插件内 `new StateCenter(...)`、`ExtensionAssemblyHost` 加可收集代数、宿主持单实例 + 交换入口）。

### 12.1 现状：为什么"单根"不是口号而是必须

| # | 现状事实（代码可查） | 位置 | 问题 |
|---|---|---|---|
| S1 | **兜底第二根**：`_center = center ?? new StateCenter(new StorageCell("agent-default"))` | `Agent.Core\Nodes\WorkflowRunner.cs:89` | 未注入即自建根 → 同进程出现两个状态真源，状态分叉后**不可追踪** |
| S2 | **编排器自建根**：`_stateCenter = new StateCenter(new StorageCell("agent-orchestrator"))` | `Agent.Core\Composition\AgentOrchestrator.cs:93` | 与 S1 叠加即"双根并存"（SSOT 破） |
| S3 | **静态槽承载运行时**：`static` 上下文/运行时状态散落插件各处 | Agent 侧（`AgentUiHost`/`LlmRuntime`/`SkillRuntimeState`/`AgentCoreRuntime`/MCP 回环等，见 `DataCenter-架构设计.md §1`） | 数据与逻辑同命：插件卸载 = 数据丢失 |
| S4 | **扩展加载不可回收**：`public static class ExtensionAssemblyHost` + `private static ExtensionLoadContext? _context` + `isCollectible: false` | `Frame\CsharpPrj\Modules\Registry\ExtensionAssemblyHost.cs:14/20/218` | 静态持有 + 不可收集 ALC ⇒ 换代无法回收旧代数 |
| S5 | **幂等单例根（无契约）**：`StateCenterFactory` 静态 `_instance` + `Reset()` | `FrontEndDriven\State\Projection\StateCenterFactory.cs` | 已是单根雏形，但无端口契约、无生命周期/换代入口 |
| S6 | **引擎已有组合根形态**：`EngineStateCenter` 持 `StateCenter` + `EngineCompositionLifecycle` | `Projects\Plug\Engine\...\StateManagement\EngineStateCenter.cs` | 可对齐（形态对，缺端口契约） |
| S7 | **`RuntimeSlot` 尚不存在** | 架构设计 §5.2 计划名，代码中 NOT FOUND | 需要落位（P10） |

> **结论**：当前的"根"是**隐式的、可复制的、可静态化的**。单根持有要做的三件事：① 给根一个**端口契约**；② 给使用层一个**唯一取得方式**（禁止自建/兜底）；③ 把换代的**交换点**放到根上。

### 12.2 根持有什么（职责清单）

| 持有物 | 说明 | 写口 |
|---|---|---|
| 状态真源 `IDataCell` / `StateCenter` | 唯一状态写入路径（分层/scope/事务） | 单写口（既有，DF1 不变） |
| 投影器 `IStateProjector` | 图的唯一产出点（D1） | 只读派生 |
| 追踪面 `ITraceQuery` + 归档 | 三条链的查询入口（D3） | 只读；追踪写的触发在写路径（§5.4） |
| 注册表 `IStateRegistry` | nodeKind/edgeKind/键方案/tier 的**只读**消费面（D2） | 装配期注册 |
| 代际 `IStateGeneration` | `generation` / `fingerprint` / `schemaVersion` 读口 | 由换代流程写 |
| 端口实现组 | 存储后端适配、`IStateDispatch`、`IStateInitializer` | 装配期注入 |
| 交换点 `ISwapPoint` | 排空 → 换代 → 恢复（D5/G6） | 换代唯一入口 |

**不持有**（同样是纪律）：域语义（属 L4 域 Gateway）、业务对象与 UI 引用、任何"第二真源"。

### 12.3 契约形态（新增端口，L1 零依赖）

```csharp
/// 根实例的唯一外部面：整体数据流的持有者（只持有与装配，不含域语义）
public interface IDataCenterRoot : IDisposable
{
    IDataCell Root { get; }                    // 状态真源（端口）
    IStateProjector Projector { get; }         // 图投影（唯一产出点）
    ITraceQuery Trace { get; }                 // 追踪查询（三条链）
    IStateRegistry Registry { get; }           // 类型/键注册表（只读消费）
    IStateGeneration Generation { get; }       // 代际读口
}

/// 取得口：替代静态槽（组合根注入；深层组件只依赖它）
public interface IRootProvider
{
    IDataCenterRoot Root { get; }
}

/// 换代交换点
public interface ISwapPoint
{
    Task<SwapResult> SwapAsync(GenerationPlan plan, CancellationToken cancellationToken = default);
}
```

> **方法面要窄**（R12）：根只做"持有 + 转发 + 装配"，任何"根上的业务方法"都是设计错误的信号。

### 12.4 取得方式（`static` 槽与兜底造根一律禁止）

| 方式 | 判定 | 说明 |
|---|---|---|
| 组合根**构造注入** | ✅ 首选 | 宿主启动时构造根，经构造参数/作用域下发；依赖显式可见 |
| `IRootProvider` **注入** | ✅ 次选 | 深层组件（无法构造注入）经端口取根；`IRootProvider` 本身仍由组合根注入 |
| 宿主装配期**服务定位器** | ⚠️ 有限 | 仅装配期注册/解析（`DataCenter.Adapters.AppServiceRegistration` 现状）；运行期不依赖 |
| `static` 槽 / 静态单例 | ❌ 禁止 | 已被 `ExtensionAssemblyHost`（`static _context` + `isCollectible:false`）证明不可回收（S4） |
| 兜底 `?? new StateCenter(...)` | ❌ 禁止 | 缺失根 = **显式失败（fail-fast）**，不给"静默造根"的机会（S1/S2 的根因） |

> 纪律：**"取不到根"是装配错误，不是运行时可恢复异常**。在启动/装配期就应暴露，而不是在业务路径里悄悄造一个根。

### 12.5 生命周期与换代

```
启动（宿主组合根，唯一一次）
  ① 构造存储后端（IDataCell / StorageCellAdapter）+ 派发（IStateDispatch?）
  ② 构造注册表（GraphRegistry）+ 代际（generation=0, fingerprint=null, schemaVersion=1）
  ③ 构造投影器（IStateProjector）+ 追踪面（ITraceQuery）
  ④ 装配根（DataCenterRoot）+ 注册到宿主装配面
运行
  写：域 Gateway → 单写口（状态 + 追踪盖章，§5.4）
  投影：轮边界 / 显式（P7）
换代（ISwapPoint.SwapAsync）
  ① Drain：暂停新写，在途任务推到边界
  ② Export：IGenerationParticipant 导出（状态/追踪/注册表）
  ③ 新代数装配：新 ALC（isCollectible: true）+ 新根
  ④ Import：承接状态，Seq 连续、generation+1、fingerprint 更新
  ⑤ Resume：新根接手；旧代数 ALC Unload（WeakReference 断言回收）
关闭：根 Dispose（Shutdown 单一释放点；顺序＝先停写→再关 Actor→再释放存储）
```

**判据**：① 换代前后同一 scope 的值与 `Seq` 连续；② 旧代数 `WeakReference.IsAlive == false`；③ 换代失败保留旧代数（不半迁）。

### 12.6 使用层看到的样子

```csharp
// ① 宿主装配（唯一一次）—— 根在这里被创建
var root = new DataCenterRoot(
    cell:       new InMemoryDataCell("root"),      // 或 new StorageCellAdapter(hostCell)
    projector:  new TierStateProjector(registry, codec),
    trace:      new TraceStore(archiver),
    registry:   GraphRegistry.CreateDefault(),
    generation: new GenerationState(0, fingerprint: null, schemaVersion: 1));

// ② 深组件取根（经端口，而非静态槽）
public sealed class SomeComponent(IRootProvider roots)
{
    private readonly IDataCenterRoot _root = roots.Root;
}

// ③ 域扩展消费（L4）：只依赖 Core 端口面（如 LoopDecision 扩展）
var view = new LoopStateStore(root.Root, scope);   // 状态读写仍走分层客户端

// ④ 观测面（L5）：投影 → 查询/导出/回放
var form = root.Projector.Project(new ProjectionRequest(scope, tiers: null));
var why  = root.Trace.Ancestors(nodeId);           // 因果链回溯
```

—— 使用层**不再出现** `new StateCenter(...)`，也**不再自持**任何运行数据。

### 12.7 判据（可执行）

| # | 判据 | 方法 |
|---|---|---|
| R1 | 进程内根实例 **≤ 1** | 单测：第二次构造 → fail-fast（或运行时计数断言） |
| R2 | 产品侧 `new StateCenter(` **归零**（白名单＝宿主组合根/测试） | K9 门禁（脚本化 grep） |
| R3 | 产品侧 `static` 状态/运行时槽归零（`ExtensionAssemblyHost` 改为**实例化代数工厂**） | K9 门禁 |
| R4 | 缺根 **fail-fast**（不静默造根） | 单测：未注入 `IRootProvider` → 构造/调用抛错 |
| R5 | 换代连续 + 旧代数 ALC 回收 | 代际单测（D5） |
| R6 | 根**不含域语义**（方法面无业务名） | 评审 + grep（根接口只含端口类型） |

### 12.8 与既有成果的衔接

| 既有成果 | 衔接点 |
|---|---|
| SSOT 公理（`StateRootSingleSource`） | 本设计是其**落地形态**：单根 = 唯一承载者；并行根（如每轮派生租约）必须降级为根下的分区 |
| 通用化 G2 端口层 | 根**只持有端口实现**（`IDataCell` 等），不新增写口；设施面门禁继续生效 |
| 通用化 G6 三件套（`ISwapPoint`/`IStateMigration`/`IGenerationParticipant`） | 根是这三者的**宿主**：换代经根完成，状态经根导入导出 |
| 通用化 G7.1–G7.3 | 与"删插件内 `new StateCenter` / `ExtensionAssemblyHost` 可收集化 / 宿主持单实例"完全同向；本设计补上**端口契约与取得纪律** |
| `EngineStateCenter`（S6）/`StateCenterFactory`（S5） | 二者是既有组合根雏形：对齐契约后收敛为"宿主装配工厂 + 根实例"，`Reset()` 改为换代/重建语义 |
| 单写口纪律（§5.4、DF1） | 根**扩大持有面**但不扩大写面：写过端口、追踪同事务 |

### 12.9 反模式清单（评审时逐条核对）

| 反模式 | 为什么禁止 |
|---|---|
| 组件内 `?? new StateCenter(...)` | 造第二根，状态分叉不可追踪（S1/S2） |
| `static IDataCenterRoot Current` | 静态槽不可回收、跨代数泄漏（S4） |
| 每轮/每任务新建根 | 根的语义是"整体数据流持有者"，不是"会话对象"；会话用 **scope 分区**表达 |
| 根上挂业务方法（`root.EvaluateLoop()`） | 职责膨胀（R12）；域语义属 L4 |
| 图/追踪各自持有静态容器 | 投影与追踪必须挂在根上，否则换代时必然漏迁 |
| 用服务定位器在运行期取根 | 隐式全局态，破坏可测试性与换代确定性（P9） |
****
## 附：一页速览（给实现者的最小清单）

1. **要建的五件套**：**根实例（D0：`IDataCenterRoot`/`IRootProvider`/`ISwapPoint` + `DataCenterRoot`）**、投影器（`IStateProjector` + `TierStateProjector`）、图契约（`GraphNode/GraphEdge/TraceStamp` + 注册表）、追踪面（`TraceQuery` + 归档）、门禁（K3–K9）。
2. **必须守的四条**：图是投影（DF1）、写即追踪（§5.4）、类型注册制（DF7）、**单根持有（DF8：整体数据流由一个根实例持有，禁止第二根/静态槽/兜底造根）**。
3. **不能碰的三处**：CSG 主 schema、域真源（SQL/LiteDB）、既有写边实现（`ReferenceGraphCoordinator`）。
4. **先做的四步**：**根实例骨架（D0，总前置）** → URI 映射点 → 投影往返单测 → schema 扩展与门禁。
5. **验收的口径**：可执行判据 + 既有测试零回归 + **单根（根实例 ≤ 1、`new StateCenter(` 归零）** + 跨语言往返（D5 起）。








