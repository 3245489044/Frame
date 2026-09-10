# Modules.DeclarativeDataflow — 声明式数据流通用框架

## 概述

**层次外置的通用多层处理框架**：层数、层名、层间包含关系与"每层怎么被处理"全部由外部 `HierarchySchema` 声明，
框架只提供一套不受层数约束的解释器（递归遍历 + 按层处理 + 向上聚合）。

- **层次是数据，不是类型**：新增层 / 改层数 = 改 schema 数据，不改框架代码。
- **层图 + 自环统一**：既支持异构 N 层（如 `project→history→task→node`），也支持同构不定深度（`task→subtask→…`）。
- **零 LLM、零领域依赖**：框架本体不含任何处理逻辑，处理器与关系读取经注入端口提供。
- **不写边**：只做只读遍历与处理编排；写边/持久化由上层单一写通道负责（TH-12）。

## 目录结构

```
Modules/DeclarativeDataflow/
├── Model/                       层次元模型（层次外置的唯一外置点）
│   ├── LevelRef.cs              任意层实体定位 + 运行时唯一键（环守卫用）
│   ├── LevelPath.cs             根→当前路径（深度 / 环判定）
│   ├── HierarchyLevelSpec.cs    层声明 + 包含声明（含自环 = 不定深度）
│   ├── HierarchySchema.cs       层次 schema（层集合 + 根）
│   └── HierarchySchemaValidator.cs  装载期完整性校验（可达 / 悬空 / 层间环）
├── Topology/                    层结构 + 实例关系（只读）
│   ├── SchemaTopology.cs        纯 schema 拓扑（层序 / 父层 / 子层 / 边类型）
│   ├── RelationalHierarchyTopology.cs  实例级 父/子/祖先/后代（经窄读端口）
├── Budget/
│   └── ProcessBudget.cs         不定深度的强制执法：节点数 + 深度（信任反转）
├── Processing/                  多层处理
│   ├── ILevelProcessor.cs       层处理器端口 + 注册表（每层一个"处理代数"）
│   ├── ILevelProcessContext.cs  (在 ILevelProcessor.cs 内) 层处理上下文
│   ├── LevelProcessResult.cs    单节点结论（成败 / 结论词 / 载荷 / 截断类别）
│   ├── ChildResultAggregation.cs 子层结论聚合（默认合取）
│   ├── HierarchyProcessPlan.cs  方向 / 预算 / 处理器覆盖 / 展开止层
│   ├── IHierarchyProcessEngine.cs 引擎端口 + 观测端口 + 上下文实现
│   └── HierarchyProcessEngine.cs 递归引擎（TopDown / BottomUp + 三重护栏）
├── Builtin/
│   └── BuiltinHierarchy.cs      内置示例 schema（3 层 / 自递归）+ 流式构造器
├── Serialization/
│   └── HierarchySchemaJson.cs   层次 schema 的 JSON 装载 / 输出
└── README.md
```

## 核心概念

| 概念 | 含义 |
|------|------|
| `HierarchySchema` | 层次外置载体：根层 + N 个层声明 + 层间包含关系 |
| `HierarchyLevelSpec` | 一个层：`LevelId` / `EntityType` / `Contains` / `ProcessId` |
| `HierarchyContainment` | 一条包含声明：子层 + 边类型（`ChildLevelId == 本层` 即自环） |
| `LevelRef` | 任意层实体的定位（层 Id + 实体 Id + 可选作用域），值相等即实体同一性；`Key` 为其转义后的人类可读编码 |
| `LevelPath` | 根→当前路径；`Depth` 供深度护栏、`Contains` 供环守卫 |
| `ILevelProcessor` | "该层怎么处理"；框架本体不含实现（LLM/规则均为其实现） |
| `ProcessBudget` | 不定深度的执法层：`MaxNodes` + `MaxDepth`，绝无无界展开 |
| `LevelProcessResult` | 单节点结论；`Kind` 区分 处理/跳过/截断，`Error` 仅承载失败原因、`SkipReason` 仅承载跳过原因 |
| `HierarchyProcessEngine` | 递归引擎：遍历 + 借出处理权 + 执法（三重护栏） |

## 三重护栏（不定深度必配）

信任反转：层次数据（尤其自环）不可信、不会自觉终止，故引擎强制执法：

1. **环守卫**：`LevelPath.Contains` 命中即拦截（返回 `Skipped("cycle-detected")`，原因在 `SkipReason`，**不写 `Error`**）；
2. **深度预算**：`ProcessBudget.MaxDepth`（根 = 1）；
3. **节点预算**：`ProcessBudget.MaxNodes`（按实体去重后的处理节点数，含根）。

任一触发返回 `LevelProcessResult.Truncated(...)`，不再向下展开；被截断的实体保留该结论类别（重复抵达结果稳定）。
截断 / 跳过的类别与子层计数会经 `LevelProcessResult.Merge` **向上传播**到根结论：
`IsTruncated=true`（或载荷 `truncated=true`）即表示本次展开未穷尽——消费方据此区分"预算截断"与"业务失败"，
不要用 `Error != null` 判失败（`Error` 只承载失败原因）。

## 使用示例

### 1) 声明式定义任意层数（含自递归）

```csharp
using Modules.DeclarativeDataflow.Builtin;
using Modules.DeclarativeDataflow.Model;

// 异构 5 层 + 任务自递归（仅数据，无代码改动）
var schema = HierarchySchemaBuilder.Root("project")
    .Level("project",  "project",  processId: "plan")
    .Level("history",  "history")
    .Level("task",     "task",     processId: "task-run")
    .Level("node",     "node",     processId: "node-round")
    .Contains("project", "history", "project_owns_history")
    .Contains("history", "task",    "history_owns_task")
    .Contains("task",    "task",    "task_owns_task")   // 自环 = 不定深度
    .Contains("task",    "node",    "cross_db_link")
    .BuildValidated();
```

### 2) 按层处理整棵层次

```csharp
using Modules.DeclarativeDataflow.Processing;
using Modules.DeclarativeDataflow.Topology;

var topology = new RelationalHierarchyTopology(new SchemaTopology(schema), myRelationReader);
var engine = new HierarchyProcessEngine(
    topology,
    new LevelProcessorRegistry(myNodeProcessor, myTaskProcessor, myPlanProcessor));

// 自底向上：子层结论先收敛，再交给父层聚合
var result = await engine.ProcessAsync(
    LevelRef.Of("project", "demo"),
    HierarchyProcessPlan.BottomUp(new ProcessBudget(MaxNodes: 5_000, MaxDepth: 32)),
    ct);

// 自顶向下：父层结论作为下游下传给子层
var planned = await engine.ProcessAsync(
    LevelRef.Of("project", "demo"),
    HierarchyProcessPlan.TopDown(stopLevelId: "task"));
```

### 3) 从 JSON 装载

```csharp
using Modules.DeclarativeDataflow.Serialization;

var schema = HierarchySchemaJson.Parse(File.ReadAllText("hierarchy.json"));
```

## 唯一层次契约（收敛锚点 · M1）

本模块是「**包含层次**」语义域的唯一实现，其余层次（引用空间、trace、UI 树）一律**经窄端口适配器接入，不复制引擎**：

- **唯一层次模型** = `LevelRef` + `HierarchySchema`（寻址 + 层声明）；
- **唯一窄端口** = `IHierarchyRelationReader`（取子/取父的最小只读接口）；
- **唯一引擎** = `HierarchyProcessEngine` + `RelationalHierarchyTopology`（遍历 / 护栏 / 聚合）。

任何新层次消费方若需遍历，**必须**实现 `IHierarchyRelationReader` 接入本内核；**禁止**自建平行层次遍历。另两处形似抽象（`Operation.LoopFoldMethod.Reference.IHierarchyContainer<T>`、`Share.TreeTraceFramework`）已被裁决为"未接线端口 / 复活复用对象"，不得当作独立引擎（见文末"遍历/环守卫归属裁决表"）。

## 接线方式（框架零依赖）

| 端口 | 方向 | 未装配行为 |
|------|------|-----------|
| `IHierarchyRelationReader` | 上层提供实例级父/子读取（如引用图只读适配器） | 子层查询返回空集（单层直跑） |
| `ILevelProcessor` / `LevelProcessorRegistry` | 上层按层注册处理实现 | 按容器层透传（无处理） |
| `IChildResultAggregator` | 可选；自定义子结论收敛 | 默认合取 |
| `ILevelProcessObserver` | 可选；诊断 / 测试 | 无观测 |

## 与既有模块分工

| 模块 | 回答的问题 |
|------|-----------|
| `Modules.DeclarativeDataflow`（本模块） | **层次**：有多少层、逐层怎么处理、子结论怎么向上收敛（LLM-free） |
| `Operation.LoopFoldMethod/Fold` | **时间**：单个状态点如何逐步推进（线性折叠） |
| `Operation.LoopFoldMethod/Reference` | **空间**：实体之间的引用关系 |
| `Operation.HierarchyMethod` | **容器语义**：数据/关系/历史存哪、怎么归（本框架的一个消费方） |
| `Operation.DataStoreDriven` | **存储真源**：引用图写边原语（唯一 owner，本框架不触碰） |

## 消费矩阵（谁在用、怎么用）

| 消费方 | 复用能力 | 装配方式 |
|--------|----------|----------|
| `Operation.HierarchyMethod`（`Hierarchy/Dataflow`） | 全部四项（schema / 拓扑 / 引擎 / 护栏） | `HierarchySchemas` 声明层次（支持 JSON 外置）、`ReferenceHierarchyRelationReader` 对接引用图、三个 `ILevelProcessor` 按层投影；经 `HierarchyProcessFacade` 组合，由 `IHierarchyManager` 门面暴露 |
| `Modules.Tests/DeclarativeDataflow.Tests` | 全部四项 | 直接以 `FakeHierarchyRelationReader` + 记录型处理器做零 IO 验证 |

## 边界（不得越界）

| 不做 | 原因 |
|------|------|
| ❌ 替换控制流 / 顺序执行编排（如 `NodeGraphOrchestrator`、`RoundSpec.Steps`） | 那是**执行序**（控制流），不是**包含层次**（part-of） |
| ❌ 合并时间折叠预算（`LoopFoldMethod.Fold.FoldBudget`）与层次展开预算（`ProcessBudget`） | 二者语义正交（时间 vs 空间），只统一"护栏模式"不统一"预算语义" |
| ❌ 承接一般图算法（最短路径 / 任意边路径枚举，见 `Modules.GraphProcessing`） | 本内核只表达 containment，保持零依赖与单一职责 |
| ❌ 承接写边 / 持久化 | TH-12：写边唯一 owner = `DataStoreDriven.ReferenceGraphCoordinator` |

## 遍历 / 环守卫归属裁决表（R2 决策记录）

> 全仓存在多套「遍历 + 环守卫 + 深度/节点预算」实现。逐项裁决：**只收敛「包含层次」**；一般图算法与领域树各自保留（把一般图并进零依赖内核会同时破坏零依赖与单一职责）。

| 现有实现 | 域 | 裁决 | 理由 |
|----------|----|------|------|
| `RelationalHierarchyTopology.DescendantsAsync`（BFS + `seen` + `maxDepth`） | 包含层次（本内核） | ✅ 内核算权威 | 引擎处理与成员集查询共用同一遍历 |
| `HierarchyProcessEngine`（递归 + 路径环守卫 + `ProcessBudget`） | 包含层次（本内核） | ✅ 内核算权威 | 处理代数 + 结论聚合 + 三重护栏 |
| `Modules.GraphProcessing.Algorithms`（`ICycleDetector` DFS 着色 / `IPathFinder` 全路径 / `IShortestPathFinder` BFS） | **一般图**（任意边、最短路径、环路径输出） | ⛔ 保留，不并入 | 内核只表达 containment；并入即破坏零依赖 |
| `Share.UniversalReference`（`GraphValidator.FindAllPaths` / `ReferenceManagement._visited` DFS） | 一般引用图（诊断） | ⛔ 保留 | 同上；属引用图诊断，非层次展开 |
| `Share.TreeTraceFramework`（`TraceQueryEngine` + `TraceQueryStep`） | UI/JSON 树查询 | ⛔ 保留（无生产消费方，另行评估废弃） | 域不同（元素树/参数树），无实体层次语义 |
| `Share.ControlLookup` / `Share.LayoutAnalysis` / `Share.PlaceholderFramework` / `BasicMethod` 的 `TreeWalker` 系列 | UI 控件树 / JSON 树 | ⛔ 保留 | 同上 |
| `LoopFoldMethod.Reference`（`IHierarchyContainer<T>` / `RefHead` / `IRefProjector`） | 引用空间（投影 + 时序折叠） | ⛔ 不并入，且**不造适配器** | 见下「R1 结论」 |
| `HierarchyMethod` 的 `WorkflowResultDeriver` / `OutputScopeResolver` | 纯推演 | ✅ 已收敛（无需动作） | 二者已薄委托 `LoopFoldMethod.Reference.Derivation`（`TreeHeadProjector` / `ScopeResolver`），无重复实现 |

### R1 结论（层次抽象收敛）

核查事实：
- `LoopFoldMethod.Reference.Model.IHierarchyContainer<T>` **零代码消费者**（仅自身文件 + `ReferenceEdge` 文档引用），设计文档标注"纯类型，不接线"；
- `RefHead` / `RefView` / `IRefProjector` 是**投影**（累积历史 → 判定视图），与 `LevelRef` + schema 的**遍历**语义正交，不是重复实现；
- `HierarchyMethod` 侧两处"收敛自 Reference"的纯推演**已实际委托**，无重复。

裁决：**不新增适配器**——新适配器将无消费者，即为死代码，违反 `Strategy.RedundancyElimination` / `@ ANTI-BLOAT` / `@ ANTI-OVER-DESIGN`。
待 `IHierarchyContainer` 出现**首个真实接线需求**时，再于 `Operation.HierarchyMethod`（同时可见 `LoopFoldMethod` 与 `Modules.DeclarativeDataflow`）落地 `IHierarchyRelationReader` 适配器。

### 层次适配器候选契约（M2 登记 · 按需落地，不预先造）

| 源层次模型 | 适配器 | 建议落点 | 触发条件 |
|-----------|--------|---------|----------|
| `Operation.LoopFoldMethod.Reference.IHierarchyContainer<T>` | `ContainerHierarchyReader` | `Operation.HierarchyMethod.Hierarchy.Dataflow` | `LoopFoldMethod.Reference` 出现首个需要层次遍历的消费方 |
| `Share.TreeTraceFramework.TraceNode` | `TraceHierarchyReader` | 同上（或 `Share.TreeTraceFramework` 若获准引用内核） | `TreeTraceFramework` 被**复活**且需要带护栏遍历（**当前已定复活复用，见 M3**，用时按此接入） |
| `ResourceIdentifier` 引用图 | `ReferenceHierarchyRelationReader` | `Operation.HierarchyMethod.Hierarchy.Dataflow` | ✅ 已落地 |

> 触发前一律**不写实现**（零消费者即死代码，`@ ANTI-OVER-DESIGN`）；出现真实消费方后再按上表落地。

### 域内结构树归属（M7 收口确认）

「结构树」为**域内单点原语**，各域自持、**不并入本内核**（无实体层次语义，见 §4 边界）：

| 域 | 唯一原语 | 位置 | 状态 |
|----|----------|------|------|
| UI 控件树 | `ControlTraverser`（显式栈 DFS + maxDepth） | `Share.ControlLookup` | ✅ 收敛点 |
| UI 布局树 | `ControlTreeWalker` → 状态抽取**经** `Framework.Tree.TreeWalker` | `Share.LayoutAnalysis` | ✅ 消费者（合规） |
| JSON 参数树 | `JsonTreeWalker`（值+路径） | `Modules.ParameterResolver` | ✅ 单点保留 |
| 对象图 | `FullCollectionTraverser`（`ReferenceEqualityComparer` + 回溯） | `Share.UniversalReference` | ✅ 单点保留 |
| 通用结构树 | `Framework.Tree.TreeWalker<TNode>` | `BasicMethod.Map.GraphFoundation` | ✅ 唯一通用原语 |

**约束**：新增代码**禁止**再声明第 2 个通用 `TreeWalker` / 平行 walker，一律复用上表原语或经其委托。

## 公理对齐（与 `.clinerules` 的语义衔接）

| 内核机制 | 对应公理 | 说明 |
|----------|----------|------|
| `ProcessBudget` 到界即截断、不可被上层忽略 | `AX.TerminationBipartition`（Budgetary 分支） | 层次展开在**空间维度**的可判定终止；外置/不可判定的探索终止仍由宿主承担 |
| 引擎只读遍历、写边经注入端口 | `SEP.DeterministicCoreDisjoint` / `Strategy.SingleImplementation` | 确定性核心写路径单一；内核不引入第二写者 |
| `LevelProcessResult.IsTruncated` 与 `Error` 分离 | `AX.EvaluationOutcomeSet` | "未穷尽"≠"业务失败"，消费方可区分 |

## 构建

```powershell
dotnet build d:\Project\Main\Frame\CsharpPrj\Modules\DeclarativeDataflow\DeclarativeDataflow.csproj -c Debug
```

模块经伞工程 `Modules.csproj` 递归纳入 `MainProj.sln`（叶子工程不直接进 sln）。
