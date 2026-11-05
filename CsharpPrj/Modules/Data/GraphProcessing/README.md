# Dispose.GraphProcessing — 图处理框架

## 概述

基于 **Graph**（`ICoreGraph`）和 **Transformers**（`ITransformer`、`GroupReducer` 等）框架重构的图处理库，完全覆盖 `BasicMethod/GraphFoundation/Other` 中 21 个文件的所有逻辑。

## 架构

```
Dispose/GraphProcessing/
├── Algorithms/             # 通用图算法（泛型，不依赖具体图模型）
│   ├── ICycleDetector.cs              # 环检测（DFS 着色，返回环路径 + 自环）
│   ├── IPathFinder.cs                 # 路径遍历（HasPath/FindAllPaths/HasPathToAnyEnd/GetReachableNodes）
│   └── IShortestPathFinder.cs         # BFS 最短路径（无权图最优解）
├── Diagnostics/           # 引用诊断
│   └── IReferenceDiagnostics.cs       # 循环/孤立/未用/缺失引用诊断（组合 ICycleDetector）
├── Normalization/          # 归一化模块（含去重子步骤）
│   ├── DedupeStrategies.cs             # 4 种匹配策略 + 5 种归约策略（基于 IMatchStrategy / IReduceStrategy）
│   ├── NormalizationConfig.cs          # 归一化配置（含去重策略配置 + 4 种工厂方法）
│   ├── NormalizationTransformer.cs     # 归一化转换器（去重使用 GroupReducer 实现）
│   ├── NormalizationExtensions.cs      # 2 个归一化扩展 + 4 个去重扩展方法
│   ├── SmartDeduplicationEngine.cs     # [迁移] 智能去重引擎（来自 Normalization/SmartDeduplicationEngine.cs）
│   ├── IdentifierExtractor.cs          # [迁移] 标识符提取器（来自 Normalization/IdentifierExtractor.cs）
│   ├── NormalizationPlanner.cs         # [迁移] 重命名规划器（来自 Normalization/NormalizationPlanner.cs + FullRenameContext.cs）
│   └── UniqueIdGenerator.cs            # [迁移] 唯一 ID 生成器（来自 Normalization/UniqueIdGenerator.cs）
├── Snapshots/              # 快照与差异
│   ├── GraphSnapshot.cs                # 图快照（节点/边定义 + 入口节点推导）
│   └── GraphDiff.cs                    # 快照差异计算（增删节点/边 + 修改节点）
├── Infrastructure/         # 基础设施（Events/Graph）
├── Utilities/              # 工具类
│   ├── BaseHandlers.cs                 # 链 ID 生成器 + 名称生成器
│   ├── GraphAccessor.cs                # 图访问器 + 命令工厂
│   └── Utilities.cs                    # JSON 操作 + 引用解析
├── OrchestrationService.cs             # 链编排（CreateChainFromTemplate/ConnectChains/RebuildSequentialRelations）
├── ReferenceService.cs                 # 跨链引用（ReferenceRewriter/DeepReferenceUpdater）
├── GraphProcessing.csproj              # 项目文件
└── README.md
```

## 依赖

- `BasicMethod.GraphFoundation` — 提供 `ICoreGraph`、`CoreGraph`、`Connection` 等
- `BasicMethod.Transformers` — 提供 `ITransformer`、`GroupReducer`、`IMatchStrategy`、`IReduceStrategy` 等

## 核心设计

### 去重 → Normalization 的子步骤

去重（Deduplication）不再是独立模块，而是作为归一化（Normalization）的一个可配置子步骤。`NormalizationTransformer` 的 `MergeDuplicateEdges` 方法内部使用 Transformers 的 `GroupReducer<Connection>` 实现：

```csharp
// 归一化配置中指定去重策略
var config = new NormalizationConfig
{
    MergeDuplicateEdges = true,
    DedupeMatchStrategy = new SourceTargetDirectionMatch().AsObjectMatch(),
    DedupeReduceStrategy = new KeepHigherWeightReduce(),
    RemoveIsolatedNodes = true
};

// 执行归一化（含去重）
var result = graph.Normalize(config);
```

### 匹配策略映射

| Other (IDedupeMatchStrategy) | 新 (IMatchStrategy) |
|---|---|
| `SourceTargetMatch` | `SourceTargetMatch` → `(Source, Target)` |
| `SourceTargetDirectionMatch` | `SourceTargetDirectionMatch` → `(Source, Target, IsDirected)` |
| `SourceTargetDirectionWeightMatch` | `SourceTargetDirectionWeightMatch` → `(Source, Target, IsDirected, Weight)` |
| `FullMatch` | `FullMatch` → `(Source, Target, IsDirected, Weight, Id)` |

### 冲突策略映射

| Other (IDedupeConflictStrategy) | 新 (IReduceStrategy) |
|---|---|
| `KeepFirstStrategy` | `KeepFirstReduce` |
| `KeepNewerStrategy` | `KeepLastReduce` |
| `KeepHigherWeightStrategy` | `KeepHigherWeightReduce` |
| `KeepLowerWeightStrategy` | `KeepLowerWeightReduce` |

### 配置工厂方法

`NormalizationConfig` 提供 4 种静态工厂方法，覆盖原 `DeduplicatorFactory` 的全部场景：

| 工厂方法 | 匹配策略 | 归约策略 | 移除孤立节点 |
|---|---|---|---|
| `CreateBasic()` | Source+Target+Direction | KeepFirst | ❌ |
| `CreateWeightAware()` | Source+Target+Direction | KeepHigherWeight | ✅ |
| `CreateStrict()` | 全匹配 | KeepFirst | ❌ |
| `CreateUndirected()` | Source+Target | KeepHigherWeight | ✅ |

## 归一化与快照协同

`Normalize()`/`Deduplicate()` 就地改写图后，可立即产出快照并与历史快照比对差异（归一化 → 快照 → diff 链路）：

```csharp
var before = GraphSnapshot<object, object>.CreateFrom(graph, "g");
graph.Normalize();                                   // 去重/移除孤立/合并
var after = GraphSnapshot<object, object>.CreateFrom(graph, "g", version: 2);
var diff = GraphDiff<object, object>.Calculate(before, after);
// diff.AddedNodeKeys / diff.RemovedEdgeIds / diff.ModifiedNodes ...
```

## 使用示例

```csharp
// 1. 创建图
var graph = new CoreGraph<string, object, object>();

// 2. 添加数据
graph.AddNode("A", new object());
graph.AddNode("B", new object());
graph.AddEdge("e1", "A", "B", true, null, 1.0);
graph.AddEdge("e2", "A", "B", true, null, 2.0); // 重复边

// 3. 仅去重（使用默认配置）
var dedupeResult = graph.DeduplicateEdges();
Console.WriteLine($"Merged: {dedupeResult.MergedDuplicateEdges}");

// 4. 完整归一化（去重 + 移除孤立节点 + 验证）
var normalizeResult = graph.Normalize();
Console.WriteLine($"Isolated removed: {normalizeResult.RemovedIsolatedNodes}");

// 5. 使用权重感知去重
var weightResult = graph.DeduplicateByWeight();
Console.WriteLine($"Merged: {weightResult.MergedDuplicateEdges}");

// 6. 管线编排
var pipeline = ProcessingPipeline.CreateFull();
var pipelineResult = pipeline.Execute(graph);
Console.WriteLine($"Pipeline: {pipelineResult.SuccessfulSteps}/{pipelineResult.TotalSteps}");
```
