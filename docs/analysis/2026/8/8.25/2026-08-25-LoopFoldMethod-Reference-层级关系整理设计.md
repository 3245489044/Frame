# LoopFoldMethod · Reference — 层级关系整理设计（以引用组织替代散点归并）

> 版本：v1（设计稿，未动代码）
> 日期：2026-08-25
> 定位：`CsharpPrj/Operation/LoopFoldMethod/Reference`（Operation 层 · Agent 无关 · LLM-free 通用引用关系抽象）
> 阅读对象：关心 Agent 图结构（History/Task/Node 层级）收敛的开发/架构人员
> 现状源码：`CsharpPrj/Operation/HierarchyMethod/{History, Tasks, Nodes}` · `CsharpPrj/Operation/DataStoreDriven`（写边原语）
> 关联文档：[状态折叠框架设计](./2026-08-25-LoopFoldMethod-状态折叠框架设计.md)

---

## 摘要

**结论先行：折叠负责"时间"，引用负责"空间"。** `LoopFoldMethod` 已把 LLM 语境下的"循环"重建为**状态折叠**（把点展开成带、在边界截断、折叠成头）；但折叠只回答了"一步接着一步怎么推进"的**时间维度**，没有回答"这些点彼此是什么关系"的**空间维度**——后者就是本文件的主题：在 `LoopFoldMethod` 下新增 **`Reference` 第二抽象**，把散落在 `HierarchyMethod` 四处的关系管理（History / Task / Node 的三层归属 + 各类引用边）收敛为"引用关系"这一个中立面。

- **平行第二抽象**：`Fold` = 时间/推进（状态带），`Reference` = 空间/结构（层级图）。二者平行、互补，而非包含或替代。
- **单一整合点** = 折叠头复用：把"累积历史 → 当前执行上下文"视为一次 `RefHead` 投影，思想对齐 `IFoldHeadProjector`。
- **写边留 DataStoreDriven**：`Reference` 只做抽象 + 编排 + 纯推演，写边原语（`IReferenceMaintenance`/`IResourceReferenceStore`/`ReferenceGrounding`）原样保留在底层，不复制（TH-12 单一实现）。
- **本次仅落文档**：已确认先设计、后实现；本文不产生代码改动，只给出架构差异分析、映射方案与目标目录蓝图。

---

## 前言：折叠解决了"时间"，还没解决"空间"

上一份《状态折叠框架设计》把目光投向**外部驱动的状态推进**：`s₀ → s₁ → … → sₙ`，点被积分成一条只增带，在边界折叠收敛。它回答的是——**一件事怎么一步一步被展开**。

但 Agent 的真实运行里还压着第二个问题，折叠没有回答：**每一步产生的点落在哪个容器、和哪些已有实体有什么引用关系**。

- `ur_histories`（会话 class）归拢多个 `ur_tasks`，任务又归拢节点 `ur_nodes`——这是**层级/包含**。
- `round_sequence`、控制流 `Calls` 把节点排成**时序/顺序**。
- `session_owns`、`exports` 界定**归属与可见性作用域**。
- `cross_db_link`、`node_to_node`、`KeyMappingToNode` 表达**交叉数据依赖**。

这些"引用关系"当前散落在 `HierarchyMethod` 的四处，彼此共享同一套引用图底层（LiteDB graph-log）却各写各的编排门面。**本文的目标：把它们整理为 `LoopFoldMethod` 的 `Reference` 第二抽象**——一个和 `Fold` 平行的"空间/结构关系"中立面。

---

## 一、第一原则

### RP1 · Fold 是时间，Reference 是空间（平行第二抽象）

| | `Fold` | `Reference` |
|---|---|---|
| 维度 | 时间 / 推进 | 空间 / 结构 |
| 基本形 | 状态带 `FoldTrace`（只增点序列） | 层级关系图（History→Task→Node + 各类边） |
| 问题 | 这一点怎么展开出下一点 | 这些点彼此是什么关系、归属在哪 |
| 收敛 | `FoldHead`（前缀→当前判定点） | `RefHead`（累积历史→当前上下文视图） |
| 端口 | `IFoldOperator`（唯一外部驱动面） | `IRefProjector`（唯一外部投影面） |

二者是**同一个框架的两个正交面**，不是父子关系。折叠把时间铺成带，引用把带里的点组织成图；`Fold` 产出每步结论，`Reference` 说明这些结论属于谁、可见于谁、按什么顺序被消费。

### RP2 · Reference 只编排，不复制写边

参考关系推导在代码里反复出现的纪律（SingleImplementation / TH-12）：

> Reference 只做**抽象 + 编排 + 纯推演**（如何时建/挂/查、如何投影、如何推导边）；写边幂等原语（`IReferenceMaintenance` / `IResourceReferenceStore` / `ReferenceGrounding`）唯一 owner 在 `DataStoreDriven` 的图协调器，**不复制任何写边逻辑**。

这与既有 `TaskNodeRelationService` / `ResourceRelationService` 的注释纪律完全一致——Reference 是这份纪律的"统一承载面"，而不是新的写边实现。

### RP3 · 与 Fold 的唯一整合点 = 折叠头复用

`Fold` 与 `Reference` 不是两条永不相交的线。它们的交点正是折叠最核心的价值：**把累积历史收敛成"此刻的判定视图"**。

- `IFoldHeadProjector.Head(trace)` 把状态带折叠成当前判定点。
- `Reference` 的 `RefHead` 把 History→Task→Node 累积的引用关系投影成"当前执行上下文"——例如"这个 taskKey 历史上挂了哪些节点、上一轮 sequential。

两者是**同一个"前缀收敛"抽象在两个维度上的投影**。Reference 复用该思想（接口形态对齐），但作用于引用图而非状态带。

---

## 二、现状盘点：需要整理的"层级关系"分散在哪

下表是"被集中整理的对象"全量清单（据此判断 Reference 必须覆盖的职责）：

| 层 | 现有位置 | 职责 / 关键边 |
|---|---|---|
| History（会话 class） | `HierarchyMethod/History/` | `HistoryProjector` 沉淀/回读；`HistoryRelationManager`（`history_owns_task` / `session_owns` / `round_sequence`）；`HistoryRecord` / `HistoryTaskLink` / `IContextProjector` |
| Task | `HierarchyMethod/Tasks/` + `Nodes/TaskGraph/` | `TaskRecord` / `TaskScope`（TaskKey / HistoryId）；`TaskGraphService`（建任务+关联节点+引用边+入队） |
| Node | `HierarchyMethod/Nodes/` | `NodeOutputStore`（ur_nodes）；`NodeOutputCodec`；`OutputScopeResolver`（可见性 Exported / WorkflowLocal） |
| 关系门面 | `HierarchyMethod/Nodes/Relations/` | `TaskNodeRelationService`（B 类执行期写 + `cross_db_link`）；`ResourceRelationService`（资源/边 CRUD）；`WorkflowReferenceStaticizer`（模板静态化 Owns/Calls）；`WorkflowResultDeriver`（链尾推导终答） |

四处的共同底层：

- 实体真源在 SQL：`ur_histories` / `ur_tasks` / `ur_nodes`。
- 引用投影在 LiteDB：`graph-log.db`（`ur_resources` / `ur_references`），由 `DataStoreDriven` 的 `ReferenceGraphCoordinator` 纯写边。

它们表达的边类型全集可用一组常量收敛：

`history_owns_task · session_owns · round_sequence · Owns · Calls · exports · cross_db_link · node_to_node · KeyMappingToNode · NodeToNode`

### 语义本质归纳

1. **层级/包含**：History ⊃ Task ⊃ Node（`history_owns_task` / 挂载）。
2. **时序/顺序**：`round_sequence`、控制流 `Calls` 链、`Owns`（工作流含步骤）。
3. **归属/作用域**：`session_owns`、`exports` 与 `Exported`/`WorkflowLocal` 可见性。
4. **交叉/数据依赖**：`cross_db_link`、`node_to_node`、`KeyMappingToNode`。

---

## 三、目标形态：Reference 目录蓝图

```
Operation/LoopFoldMethod/Reference/
├─ Model/           层级容器抽象 + 边类型常量
│   ├─ IHierarchyContainer<T>   // History/Task/Node 统一容器抽象（只读投影用）
│   └─ ReferenceTypes.cs         // 边类型常量收敛（唯一常量源）
├─ Projection/      投影端口（唯二外部投影面）
│   ├─ IRefProjector<TContext>   // 累积历史 → 当前引用视图（对齐 IFoldHeadProjector 思想）
│   └─ RefHead                      // 默认投影实现：History→Task→Node 折叠成判定视图
├─ Derivation/      纯推演（LLM-free，纯函数）
│   ├─ ScopeResolver                 // 可见性判定（Exported / WorkflowLocal）
│   ├─ TreeHeadProjector             // 链尾/终答推导（出度 0 → 终节点）
│   └─ EdgeDeriver                   // 边推导（复用既有 NodeRelation 推导门面）
└─ Gateways/        编排门面（薄，只编排不写边）
    ├─ HistoryRelationGateway        // ← HistoryRelationManager
    ├─ TaskRelationGateway           // ← TaskGraphService / TaskNodeRelationService
    └─ NodeRelationGateway           // ← ResourceRelationService / NodeRelation
```

### 契约原型（草案）

```text
// 累积历史 → 当前引用视图（折叠头思想在空间维度的投影）
interface IRefProjector<in TContext> {
    IRefView Project(TContext ctx);          // History/Task/Node 累积 → 此刻判定视图
}

// 层级容器抽象（只读，供投影/推导；写走 DataStoreDriven）
interface IHierarchyContainer<out T> {
    string Id { get; }
    IReadOnlyList<T> Children { get; }        // 下一层
    IReadOnlyList<ReferenceEdge> OutEdges { get; }  // 该节点的出边
}

// 边类型唯一常量源
static class ReferenceTypes {
    public const string HistoryOwnsTask = "history_owns_task";
    public const string SessionOwns     = "session_owns";
    public const string RoundSequence   = "round_sequence";
    public const string Owns           = "Owns";
    public const string Calls          = "Calls";
    public const string Exports        = "exports";
    public const string CrossDbLink    = "cross_db_link";
    public const string NodeToNode     = "node_to_node";
    // KeyMappingToNode / NodeToNode 由 Derivation.EdgeDeriver 推导时产出
}

// 纯推演
static class ScopeResolver { // ← OutputScopeResolver 收敛
    bool IsVisibleTo(string sourceWf, Visibility vis, string requestWf);
}
```

`LoopFoldMethod/Reference` 不认识 `LLM`、不认识 `Cline/Answer`、不认识 `AgentTaskItem`——它只描述"实体与实体之间存在哪种引用关系、如何投影成当前视图"。

---

## 四、现状 → Reference 映射表（架构差异分析）

| 现有（HierarchyMethod） | Reference 目标 | 迁移性质 | 备注 |
|---|---|---|---|
| `HistoryProjector`（沉淀/回读） | `Gateways/HistoryRelationGateway` | 编排收敛 | 沉淀节奏不变，写边仍委托底层 |
| `HistoryRelationManager`（history_owns_task / session_owns / round_sequence） | `Gateways/HistoryRelationGateway` + `Projection/RefHead` | 编排收敛 + 投影 | 边类型进 `ReferenceTypes` 常量 |
| `TaskGraphService`（建任务+关联+入队） | `Gateways/TaskRelationGateway` | 编排收敛 | 入队端口 `ITaskGraphTaskStore` 不变 |
| `TaskNodeRelationService`（cross_db_link） | `Gateways/TaskRelationGateway` | 编排收敛 | 写边委托不变 |
| `ResourceRelationService`（资源/边 CRUD） | `Gateways/NodeRelationGateway` | 编排收敛 | 读写语义保持 |
| `NodeRelation`（边推导门面） | `Derivation/EdgeDeriver` | 纯推演收敛 | 保持静态纯函数门面 |
| `OutputScopeResolver`（可见性） | `Derivation/ScopeResolver` | 纯推演收敛 | 语义不动 |
| `WorkflowReferenceStaticizer`（Owns/Calls） | `Derivation/EdgeDeriver` + `ReferenceTypes` | 纯推演收敛 | 模板粒度静态化节奏不变 |
| `WorkflowResultDeriver`（链尾终答） | `Derivation/TreeHeadProjector` | 纯推演收敛 | 出度 0 → 终节点逻辑复用 |
| `IContextProjector`（历史+本轮→中立请求） | `Projection/IRefProjector`（LLM 侧适配仍在 Agent） | 契约对齐 | Reference 只定中立投影，LLM 分层仍归 Planner |

### 关键差异与权衡（决策痕迹）

1. **线性带 vs 层级树——不能硬塞进 Fold。** 折叠是 `FoldTrace` 线性只增带；History→Task→Node 是树/图。若把引用塞进 `TState`，会让"时间"与"空间"耦合在同一个带里，折叠的只增/截断语义被污染。**决策：二者平行，`Reference` 是第二抽象。**（废弃选项：把 Reference 当 `FoldVertex` 的 `TVertex`——耦合过深，折叠带会夹带图结构，破坏只增带纯度。）

2. **写边原语去留——留在 DataStoreDriven。** 引用图唯一写边 owner 底层已收敛（`IReferenceMaintenance` → `ReferenceGraphCoordinator`）。若上收 Reference，会重开写边第二实现，违反 TH-12。**决策：留底层，Reference 只编排。**（废弃选项：把写边抽象一并上收——层次更"高"但改动面大、复制语义，收益不成比例。）

3. **物理迁移 vs 抽象骨架——本次仅文档。** 已确认先设计再实现，本文件只做蓝图为证；物理迁移/新建抽象骨架留待下一阶段单独执行与验证。

---

## 五、数据流（折叠时间 + 引用空间 并行）

```
         ┌──────────────────── Fold（时间维度）────────────────────┐
         │  s₀ ─ op₁ ─▶ s₁ ─ op₂ ─▶ s₂ ─ … ─▶ sₙ   FoldTrace（只增带）│
         └───────────────────────────────────────────────────┘
                          │ 每步产出节点 + 引用边（落 DataStoreDriven）
                          ▼
         ┌──────────── Reference（空间维度）────────────┐
         │  ur_histories ─(history_owns_task)─▶ ur_tasks ─(挂载)─▶ ur_nodes
         │  round_sequence ▸ session_owns ▸ Owns/Calls ▸ exports     │
         │  RefHead.Project(累积) ──▶ 当前引用视图（→ 下轮上下文）      │
         └──────────────────────────────────────────────────┘
```

1. **折叠推进**：`FoldEngine` 每轮经 `IFoldOperator.Expand` 展开，产出下一状态点 + 每步顶点（含本轮节点产出）。
2. **沉淀（既有，写边委托）**：工作流完成一轮后由历史投影器把节点挂进历史容器，建 `history_owns_task` / `session_owns` / `round_sequence`。
3. **投影（Reference 新增）**：`RefHead.Project` 读取累积引用关系，投影成"当前执行上下文"（某 taskKey 历史上挂了哪些节点、上轮序列）；该视图供下轮 `provide_prompt`/`cline_execute` 使用——思想正是 `FoldHead` 在空间维度的复刻。
4. **纯推演**：`ScopeResolver` / `TreeHeadProjector` 在引用图上做可见性判定与终答推导，零 LLM。

---

## 六、与既有框架的分工

| 模块 | 回答的问题 |
|---|---|
| `LoopFoldMethod/Fold` | **时间**：把点展开成带、在边界截断、折叠成头 |
| `LoopFoldMethod/Reference`（本次） | **空间**：把散点组织成层级图、投影成当前引用视图 |
| `HierarchyMethod` | **容器语义**：数据/关系/历史存哪、怎么归（其关系部分逐步收敛进 Reference） |
| `DataStoreDriven` | **存储真源**：ur_* 表 + 引用图 gold 写边原语（唯一 owner，不动） |
| `IFoldOperator` / `IRefProjector` | **唯一外部面**：一步展开 / 一面投影（LLM 只是其中一个实现） |
| `Agent.Core` | **装配组合根**：把折叠引擎 + 引用投影 + Agent 节点执行粘在一起 |

---

## 七、边界与风险（诚实声明）

1. **本次未动代码**：本文件是设计蓝图，Reference 目录尚不存在；所有"收敛"均为映射描述，落地需下一阶段单独执行与编译/测试验证。
2. **与 `Fold` 的边界是纪律而非物理隔离**：`Reference` 与 `Fold` 同处 `LoopFoldMethod`，但语义正交；防止"顺手把图逻辑塞进算子"的隐性耦合，是落地时的第一纪律。
3. **写边不复制**：Reference 的门面若擅自直接调 `IReferenceStore.AddReferenceAsync`（绕过协调器守卫），将重演既往的写边双实现；落地时必须只经 `IReferenceMaintenance` 单一守卫。
4. **纯推演是最大收益**：`ScopeResolver` / `TreeHeadProjector` / `EdgeDeriver` 保持静态纯函数，使引用关系的查询与终答推导可在**零 AI 额度**的纯 CI 中测试——这是 Reference 与 Fold 共享的"LLM-free 关键收益"。

---

## 八、落地验收路径（待确认，非本次执行）

若用户确认，下一阶段按以下顺序执行并以此验收：

1. `Reference/Model`：落地 `ReferenceTypes` 常量 + `IHierarchyContainer`（纯类型，不接线）。
2. `Reference/Derivation`：把 `OutputScopeResolver` → `ScopeResolver`、`WorkflowResultDeriver` → `TreeHeadProjector` 平移为纯函数（编译绿 + 单测通过为验收）。
3. `Reference/Gateways`：把四处编排门面收敛（不改写边、不改沉淀节奏；消费方引用逐一改指）。
4. `Reference/Projection`：落地 `IRefProjector` + `RefHead`（先给默认投影；LLM 分层适配留在 Agent/Planner）。
5. `HierarchyMethod` 原文件标记为"已被 Reference 收敛"并决定去留（保守：保留门面转发，避免物理破坏既有消费方）。

---

## 结语

折叠回答了"时间"，引用回答"空间"。`Reference` 是 `LoopFoldMethod` 的第二抽象：它把散落在 `HierarchyMethod` 四处的 History/Task/Node 层级与引用边整理为一个中立面——**只编排、只投影、只推演，不复制写边**。它与 `Fold` 平行互补，唯一的交点"累积历史 → 当前判定视图"正是折叠头思想在空间维度的复刻。引用关系是智能体架构的第一性原理，而 `Reference` 让这份原理第一次有了一个**独立的、LLM-free 的、可测试的家**。