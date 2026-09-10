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

## 构建

```powershell
dotnet build d:\Project\Main\Frame\CsharpPrj\Modules\DeclarativeDataflow\DeclarativeDataflow.csproj -c Debug
```

模块经伞工程 `Modules.csproj` 递归纳入 `MainProj.sln`（叶子工程不直接进 sln）。
