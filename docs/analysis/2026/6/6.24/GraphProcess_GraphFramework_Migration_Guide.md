# GraphProcess → GraphFramework 迁移使用文档

> **目标**：将 `Operation/WorkFlowMethod/GraphProcess` 中与 `Share/GraphFramework` 功能重叠的逻辑逐步委托/迁移到 GraphFramework，消除重复代码，统一图处理能力。
>
> **策略**：自底向上、逐步替换。每个阶段只改内部实现，对外接口不变，调用方无需修改代码。

---

## 目录

1. [架构总览](#一架构总览)
2. [已完成迁移（v2.0）](#二已完成迁移v20)
3. [通用框架提取（v3.0）](#三通用框架提取v30)
4. [剩余迁移计划](#四剩余迁移计划)
5. [API 映射表（更新版）](#五api-映射表更新版)
6. [迁移检查清单](#六迁移检查清单)
7. [经验总结](#七经验总结)

---

## 一、架构总览

### 1.1 当前架构（v3.0 — 通用框架提取后）

```
GraphProcess (Operation 层)          →  GraphFoundation (BasicMethod 层)
├── Pipeline/                        →  ├── Navigation/
│   ├── IGraphPipelineStep [保留接口]  │   │   └── GraphNavigationExtensions.cs [新增] ✅
│   ├── GraphWorkflowConnector [保留]  │   ├── Snapshots/
│   ├── GraphAnalyzer [已委托] ✅      │   │   ├── GraphSnapshot.cs [新增] ✅
│   └── GraphFoundationBridge [保留]   │   │   └── GraphDiff.cs [新增] ✅
├── Services/                        │   ├── Topology/
│   ├── GraphValidator [已委托] ✅    │   │   ├── Topology.cs [已有]
│   ├── GraphUpdateService [保留]     │   │   ├── CriticalPath.cs [新增] ✅
│   ├── NodeChainService [已委托] ✅   │   │   └── TopologyEngine.cs [已有]
│   ├── DirectiveApplier [保留]       │   └── Graph/
│   └── NodeManager [已废弃]          │       ├── GraphCloneExtensions.cs [新增] ✅
├── SkeletonBridge/ [已委托] ✅      │       └── ...
│   ├── ChainToGraphSkeleton [保留]   │
│   ├── GraphToChainSkeleton [保留]   │  →  GraphFramework (Share 层)
│   ├── CycleDetectionSkeleton [已委托]│  ├── Bridge/
│   ├── GraphValidationSkeleton [已委托]│  │   └── GraphFoundationBridge.cs [新增] ✅
│   └── NodeChainSkeleton [已委托] ✅  │  ├── Operations/
├── Deduplication/                   │  │   ├── GraphOperationSkeleton.cs [新增] ✅
│   └── ConfigDeduplicationProcessor  │  │   └── IDirectiveApplier.cs [新增] ✅
└── GraphCoreSkeleton.cs [保留]      │  ├── Pipeline/
                                     │  │   └── IGraphPipelineStep.cs [新增] ✅
                                     │  └── Helpers/
                                     │       └── Guard.cs [新增] ✅
```

> **图例**：✅ = 已完成迁移/提取，[保留] = 评估后保留，[已废弃] = 已删除，[新增] = 本次新增

### 1.2 依赖关系

```
GraphProcess (Operation 层)
    │  依赖
    ▼
GraphFoundation (BasicMethod 层)  ←──  GraphFramework (Share 层)
    │                                       │  依赖
    ▼                                       ▼
ECS.Core (BasicMethod 层)          ←──  GraphFoundation (BasicMethod 层)
```

### 1.3 迁移完成度总览

| 阶段 | 优先级 | 总项数 | 已完成 | 完成率 |
|------|--------|--------|--------|--------|
| 第一阶段：图分析与验证 | 🔴 高 | 6 | 6 | **100%** |
| 第二阶段：Skeleton 桥接层 | 🔴 高 | 3 | 3 | **100%** |
| 第三阶段：高级功能 | 🟡 中 | 3 | 3 | **100%** |
| 第四阶段：整体管线 | 🟢 低 | 2 | 2 | **100%** |
| **第五阶段：通用框架提取** | 🔴 高 | **10** | **10** | **100%** |
| **合计** | | **24** | **24** | **100%** |

---

## 二、已完成迁移（v2.0）

### 2.1 GraphAnalyzer.HasCycle() → GraphTopology.TopologicalSort ✅

**当前代码**（`Pipeline/GraphAnalyzer.cs`）：

```csharp
public bool HasCycle(IGraph<string, NodeConfig, object> graph)
{
    // 委托给 GraphFoundation 的拓扑排序
    var result = GraphTopology.TopologicalSort(graph);
    return result.HasCycle;
}
```

**说明**：替代了自实现的 DFS 环检测，直接委托给 `GraphFoundation.Topology.GraphTopology.TopologicalSort()`。

### 2.2 GraphAnalyzer.Analyze() → GraphStatistics<,,>.Compute() ✅

**当前代码**（`Pipeline/GraphAnalyzer.cs`）：

```csharp
public GraphStatistics Analyze(IGraph<string, NodeConfig, object> graph)
{
    var stats = new GraphStatistics<string, NodeConfig, object>(graph);
    var result = stats.Compute();
    return new GraphStatistics
    {
        NodeCount = result.NodeCount,
        EdgeCount = result.EdgeCount,
        AverageOutDegree = Math.Round(result.AvgOutDegree, 2),
        MaxDepth = result.ConnectedComponentCount, // 近似值
        HasCycle = result.HasCycle,
        ComponentCount = result.ConnectedComponentCount
    };
}
```

**说明**：替代了手动计算统计信息，委托给 `GraphFoundation.ErrorHandling.GraphStatistics<,,>.Compute()`。

### 2.3 GraphValidator.Validate() → ValidateStep ✅

**当前代码**（`Services/GraphValidator.cs`）：

```csharp
public static ValidationResult Validate(IGraph<string, NodeConfig, object> graph)
{
    // 委托给 ValidateStep
    if (graph is ICoreGraph<string, NodeConfig, object> coreGraph)
    {
        var step = new ValidateStep<string, NodeConfig, object>(coreGraph);
        var world = new EcsWorld();
        step.Execute(world);

        // 从 ECS 世界读取结果
        foreach (var entity in world.EntityManager.GetAllEntities())
        {
            if (world.EntityManager.TryGetComponent<ValidationResultComponent>(entity, out var result))
            {
                return result.IsValid
                    ? ValidationResult.Success()
                    : ValidationResult.Failure(string.Join("; ", result.Errors ?? Array.Empty<string>()));
            }
        }
    }
    return ValidationResult.Failure("验证执行失败");
}
```

**说明**：替代了自实现的验证逻辑（边引用检查、孤立节点检测等），委托给 `GraphFramework.Pipeline.ValidateStep`。

### 2.4 GraphDeduplicateStep → DedupeStep ✅

**当前代码**（`Pipeline/GraphPipelineSteps.cs`）：

```csharp
public class GraphDeduplicateStep<TKey, TNodeData, TEdgeData> : IGraphPipelineStep<TKey, TNodeData, TEdgeData>
{
    public Task ExecuteAsync(IGraph<TKey, TNodeData, TEdgeData> graph, IGraphPipelineContext context)
    {
        if (graph is ICoreGraph<TKey, TNodeData, TEdgeData> coreGraph)
        {
            var dedupeStep = new DedupeStep<TKey, TNodeData, TEdgeData>(coreGraph);
            var world = new EcsWorld();
            dedupeStep.Execute(world);
            // 从 ECS 世界读取去重结果
        }
        return Task.CompletedTask;
    }
}
```

**说明**：`IGraphPipelineStep` 接口保持不变，内部实现委托给 `GraphFramework.Pipeline.DedupeStep`。

### 2.5 GraphValidateStep → ValidateStep ✅

**当前代码**（`Pipeline/GraphPipelineSteps.cs`）：

```csharp
public class GraphValidateStep<TKey, TNodeData, TEdgeData> : IGraphPipelineStep<TKey, TNodeData, TEdgeData>
{
    public Task ExecuteAsync(IGraph<TKey, TNodeData, TEdgeData> graph, IGraphPipelineContext context)
    {
        if (graph is ICoreGraph<TKey, TNodeData, TEdgeData> coreGraph)
        {
            var validateStep = new ValidateStep<TKey, TNodeData, TEdgeData>(coreGraph);
            var world = new EcsWorld();
            validateStep.Execute(world);
            // 从 ECS 世界读取验证结果
        }
        return Task.CompletedTask;
    }
}
```

**说明**：`IGraphPipelineStep` 接口保持不变，内部实现委托给 `GraphFramework.Pipeline.ValidateStep`。

### 2.6 GraphAnalyzeStep → StatisticsSystem + TopologySystem ✅

**当前代码**（`Pipeline/GraphPipelineSteps.cs`）：

```csharp
public class GraphAnalyzeStep<TKey, TNodeData, TEdgeData> : IGraphPipelineStep<TKey, TNodeData, TEdgeData>
{
    public Task ExecuteAsync(IGraph<TKey, TNodeData, TEdgeData> graph, IGraphPipelineContext context)
    {
        if (graph is IReadOnlyGraph<TKey, TNodeData, TEdgeData> readOnlyGraph)
        {
            var world = new EcsWorld();

            // 委托给 StatisticsSystem
            var statsSystem = new StatisticsSystem<TKey, TNodeData, TEdgeData>(readOnlyGraph);
            statsSystem.World = world;
            statsSystem.Update(0);

            // 委托给 TopologySystem（环检测）
            if (graph is ICoreGraph<TKey, TNodeData, TEdgeData> coreGraph)
            {
                var entityMap = new EntityMap<TKey>();
                var topoSystem = new TopologySystem<TKey, TNodeData, TEdgeData>(coreGraph, entityMap);
                topoSystem.World = world;
                topoSystem.Update(0);
            }

            // 从 ECS 世界读取结果并设置到 context
        }
        return Task.CompletedTask;
    }
}
```

**说明**：替代了通过 `IGraphAnalyzer` 的分析方式，委托给 `GraphFramework.Systems.StatisticsSystem` + `TopologySystem`。

### 2.7 CycleDetectionSkeleton → GraphTopology.TopologicalSort ✅

**当前代码**（`SkeletonBridge/CycleDetectionSkeleton.cs`）：

```csharp
protected override Task<CycleDetectionContext> ExecuteCoreAsync(CycleDetectionContext input, SkeletonContext context)
{
    // 委托给 GraphFoundation 的拓扑排序
    var result = GraphTopology.TopologicalSort(input.Graph);
    input.HasCycle = result.HasCycle;

    if (result.HasCycle && result.CycleNodes != null)
    {
        input.Cycles = new List<List<string>>
        {
            result.CycleNodes.Select(k => k.ToString()).ToList()
        };
    }
    else
    {
        input.Cycles = new List<List<string>>();
    }
    return Task.FromResult(input);
}
```

**说明**：替代了自实现的 `DetectAllCycles` 方法，委托给 `GraphFoundation.Topology.GraphTopology.TopologicalSort()`。

### 2.8 GraphValidationSkeleton → ValidateStep ✅

**当前代码**（`SkeletonBridge/GraphValidationSkeleton.cs`）：

```csharp
protected override Task<GraphValidationContext> ExecuteCoreAsync(GraphValidationContext input, SkeletonContext context)
{
    // 委托给 ValidateStep
    if (input.Graph is ICoreGraph<string, NodeConfig, object> coreGraph)
    {
        var step = new ValidateStep<string, NodeConfig, object>(coreGraph);
        var world = new EcsWorld();
        step.Execute(world);

        // 从 ECS 世界读取结果
        foreach (var entity in world.EntityManager.GetAllEntities())
        {
            if (world.EntityManager.TryGetComponent<ValidationResultComponent>(entity, out var result))
            {
                input.Errors = result.Errors?.ToList() ?? new List<string>();
                input.Warnings = result.Warnings?.ToList() ?? new List<string>();
                input.IsValid = result.IsValid;
                break;
            }
        }
    }
    return Task.FromResult(input);
}
```

**说明**：替代了自实现的完整验证逻辑（ValidateNodes / ValidateEdges / ValidateConnectivity / ValidateCycles），委托给 `GraphFramework.Pipeline.ValidateStep`。

### 2.9 NodeChainService → OrchestrationService ✅

**当前代码**（`Services/NodeChainService.cs`）：

```csharp
public ChainInfo<TNode> CreateChainedNodes<TNode>(
    string json, int chainCount,
    WorkflowRuntimeContext context = null, string nodeCallkeyPattern = null)
    where TNode : NodeConfig
{
    // ... 反序列化、参数校验 ...

    // 委托给 OrchestrationService
    var graph = new CoreGraph<string, TNode, object>(/* context */);
    var orchestration = new OrchestrationService<string, TNode, object>(
        graph,
        keyExtractor: n => n.basicConfig?.Callkey,
        setPosition: (n, pos) => { /* 位置由 OrchestrationService 管理 */ },
        cloner: n => DeepCloneNode(n),
        keyConverter: i => i.ToString());

    var template = baseNodes.AllNodes[0];
    var result = orchestration.CreateMultipleChains(template, chainCount, baseNodes.AllNodes.Count);

    // 将结果转换回 ChainInfo
    var allNodes = new List<TNode>();
    foreach (var chainResult in result.CreatedChains)
    {
        foreach (var key in chainResult.NodeKeys)
        {
            var node = graph.GetNodeData(key);
            if (node != null)
            {
                context.RegisterNode(node);
                allNodes.Add(node);
            }
        }
    }

    return new ChainInfo<TNode>
    {
        StartNode = allNodes.FirstOrDefault(),
        EndNode = allNodes.LastOrDefault(),
        AllNodes = allNodes
    };
}
```

**说明**：替代了自实现的 `GenerateAllNodes` + `LinkCrossChain` 逻辑，委托给 `Modules.GraphProcessing.Services.OrchestrationService`。

### 2.10 ConfigDeduplicationProcessor → 新框架依赖 ✅

**当前代码**（`Deduplication/ConfigDeduplicationProcessor.cs`）：

```csharp
// 基于新框架重构：
//   - GraphFoundation.Chain.IChainNodeHandler — 链节点访问
//   - Modules.GraphProcessing.Utilities.INameGenerator — 名称生成
//   - Modules.GraphProcessing.Normalization.IdentifierExtractor — 标识符提取
//   - WorkFlowMethod.Infrastructure.Abstractions.INodeRepository — 节点存储
// 替代旧版 GraphFoundation.Normalization.ChainProcessing 依赖
```

**说明**：重构了依赖关系，从旧版 `GraphFoundation.Normalization.ChainProcessing` 迁移到新框架的接口。

### 2.11 GraphAnalyzer.FindConnectedComponents() → GraphTopology.GetConnectedComponents ✅

**当前代码**（`Pipeline/GraphAnalyzer.cs`）：

```csharp
public IReadOnlyList<IReadOnlyList<string>> FindConnectedComponents(IGraph<string, NodeConfig, object> graph)
{
    // 委托给 GraphFoundation 的 GraphTopology.GetConnectedComponents
    var components = GraphTopology.GetConnectedComponents(graph);
    return components
        .Select(c => (IReadOnlyList<string>)c.Select(k => k.ToString() ?? k.GetHashCode().ToString()).ToList().AsReadOnly())
        .ToList().AsReadOnly();
}
```

**说明**：替代了自实现的 BFS 连通分量遍历，委托给 `GraphFoundation.Topology.GraphTopology.GetConnectedComponents()`。同时删除了不再使用的 `BfsComponent` 私有方法。

### 2.12 StatisticsSystem 扩展 ConnectedComponents 字段 ✅

**当前代码**（`Systems/StatisticsSystem.cs`）：

```csharp
public struct GraphStatsComponent : IComponent
{
    // ... 现有字段 ...
    public int ConnectedComponentCount { get; set; }
    public string[][]? ConnectedComponents { get; set; } // 新增：分量列表
    public bool HasCycle { get; set; }
}
```

**说明**：在 `GraphStatsComponent` 中新增 `ConnectedComponents` 字段，`StatisticsSystem.Update()` 中通过 `GraphTopology.GetConnectedComponents()` 计算分量列表。`StatisticsStep` 同步更新以保持字段定义一致。

### 2.13 删除已废弃的 NodeManager.cs ✅

**说明**：`NodeManager` 已被 `NodeChainService` 替代，且已废弃多时。文件已删除，所有引用已清理。

### 2.14 保留项说明

以下组件经评估后**保留**，不进行迁移：

| 组件 | 保留原因 |
|------|---------|
| `GraphFoundationBridge`（GraphProcess 版本） | 提供丰富的图操作（InsertNodesAfter/Before、DeleteNode、ReplaceNode、RedirectEdge），已泛型化（移除 `NodeConfig` 约束），内部可委托给 `GraphFramework.Bridge.GraphFoundationBridge` |
| `DirectiveApplier` | GraphProcess 特有的指令体系（`GraphUpdateDirective` → `Action<IGraph>`），GraphFramework 无对应功能 |
| `GraphUpdateService` | 封装了 IGraph 事务更新的便捷方法，是 Operation 层的便捷封装 |
| `GraphToChainSkeleton` | GraphFramework 无对应功能 |
| `GraphWorkflowConnector` | 基于 `IGraphPipelineStep` 接口，与 `EcsPipeline` 抽象层级不同，不建议直接替换 |
| `GraphAnalyzer.FindCriticalPath()` | 已委托给 `GraphFoundation.Topology.CriticalPath.FindLongestPath()` |

---

## 三、通用框架提取（v3.0）

### 3.1 提取概览

本次提取将 GraphProcess 中与业务无关的通用逻辑提升到 GraphFoundation（BasicMethod 层）和 GraphFramework（Share 层），分为三个层级：

| 层级 | 目标位置 | 模块数 | 通用性 |
|------|---------|:------:|:------:|
| 🔵 **第一层** | `GraphFoundation` (BasicMethod) | 4 个 | ⭐⭐⭐ 纯图算法 |
| 🟢 **第二层** | `GraphFramework` (Share) | 4 个 | ⭐⭐ 框架能力 |
| 🟡 **第三层** | 接口抽象保留在 Operation | 2 个 | ⭐ 业务模式 |

### 3.2 第一层：GraphFoundation（BasicMethod 层）

#### 3.2.1 GraphNavigationExtensions → `GraphFoundation.Navigation`

**源文件**：`Operation/WorkFlowMethod/GraphProcess/SkeletonBridge/GraphNavigationExtensions.cs`
**目标文件**：`BasicMethod/GraphFoundation/Navigation/GraphNavigationExtensions.cs`

**提取内容**：
- `GetSuccessorKeys()` — 获取后继节点键
- `GetPredecessorKeys()` — 获取前驱节点键
- `GetEntryNodeKeys()` — 获取入口节点键
- `GetExitNodeKeys()` — 获取出口节点键
- `GetAllPaths()` — 获取从入口到出口的所有路径
- `FindAllPaths()` — 查找两节点间所有路径（DFS）
- `TopologicalSort()` — 拓扑排序（统一委托给 `GraphTopology`，消除重复实现）

**关键改进**：
- 拓扑排序不再自实现，统一委托给 `GraphFoundation.Topology.GraphTopology.TopologicalSort()`
- 完全泛型化，与 `NodeConfig` 解耦

#### 3.2.2 GraphSnapshot + GraphDiff → `GraphFoundation.Snapshots`

**源文件**：
- `Operation/WorkFlowMethod/GraphProcess/Snapshots/GraphSnapshot.cs`
- `Operation/WorkFlowMethod/GraphProcess/Snapshots/GraphDiff.cs`

**目标文件**：
- `BasicMethod/GraphFoundation/Snapshots/GraphSnapshot.cs`
- `BasicMethod/GraphFoundation/Snapshots/GraphDiff.cs`

**提取内容**：
- `GraphSnapshot<TKey, TNodeData, TEdgeData>` — 不可变图快照（执行凭证模式）
- `NodeDefinition<TKey, TNodeData>` — 节点定义
- `EdgeDefinition<TKey>` — 边定义
- `GraphDiff<TKey, TNodeData, TEdgeData>` — 图差异计算（支持回滚和审计）

**关键改进**：
- 完全泛型化，与 `NodeConfig` 解耦
- 新增 `CreateFrom(IReadOnlyGraph)` 工厂方法，直接从图创建快照
- 新增 `Validate()` 方法验证快照拓扑完整性

#### 3.2.3 CriticalPath → `GraphFoundation.Topology`

**源文件**：`Operation/WorkFlowMethod/GraphProcess/Pipeline/GraphAnalyzer.cs` 中的 `FindCriticalPath`
**目标文件**：`BasicMethod/GraphFoundation/Topology/CriticalPath.cs`

**提取内容**：
- `FindLongestPath()` — 基于 DFS 的最长路径查找（关键路径算法）

**关键改进**：
- 完全泛型化，与 `NodeConfig` 解耦
- 独立为静态类，便于单元测试

#### 3.2.4 GraphCloneExtensions → `GraphFoundation.Core`

**源文件**：`Operation/WorkFlowMethod/GraphProcess/Pipeline/GraphPipelineSteps.cs` 中的 `GraphCloneStep`
**目标文件**：`BasicMethod/GraphFoundation/Graph/GraphCloneExtensions.cs`

**提取内容**：
- `CloneTo()` — 基于事务的图深克隆（节点快照 + 边快照）
- `Clone()` — 创建图的深拷贝

**关键改进**：
- 完全泛型化，与 `NodeConfig` 解耦
- 支持可选的节点/边数据克隆函数
- 使用事务确保原子性

### 3.3 第二层：GraphFramework（Share 层）

#### 3.3.1 GraphFoundationBridge → `GraphFramework.Bridge`

**源文件**：`Operation/WorkFlowMethod/GraphProcess/Pipeline/GraphFoundationBridge.cs`
**目标文件**：`Share/GraphFramework/Bridge/GraphFoundationBridge.cs`

**提取内容**：
- `ChainInfo<TKey>` — 链信息（从图中提取的线性路径）
- `ChainToGraph()` — 链→图转换
- `GraphToChain()` — 图→链转换
- `ExtractComponents()` — 从图中提取连通分量
- `InsertNodesAfter/Before()` — 图内插入
- `DeleteNode()` — 删除节点并重连
- `ReplaceNode()` — 替换节点（保留边连接）
- `RedirectEdge()` — 重定向边
- `GetNextNodeKey()` / `GetPreviousNodeKey()` — 边查询

**关键改进**：
- 完全泛型化，与 `NodeConfig` 解耦
- 新增 `ChainInfo<TKey>` 泛型类替代 `ChainInfo<NodeConfig>`
- 所有方法通过 `IGraph<TKey, TNodeData, TEdgeData>` 操作

#### 3.3.2 GraphOperationSkeleton → `GraphFramework.Operations`

**源文件**：`Operation/WorkFlowMethod/GraphProcess/SkeletonBridge/GraphOperationSkeleton.cs`
**目标文件**：`Share/GraphFramework/Operations/GraphOperationSkeleton.cs`

**提取内容**：
- `Merge()` — 图合并（支持数据合并函数）
- `Filter()` — 节点过滤（支持谓词和克隆函数）
- `Subgraph()` — 子图提取

**关键改进**：
- 完全泛型化，与 `NodeConfig` 解耦
- 支持可选的节点/边数据合并和克隆函数

#### 3.3.3 IGraphPipelineStep → `GraphFramework.Pipeline`

**源文件**：`Operation/WorkFlowMethod/GraphProcess/Pipeline/GraphPipelineStep.cs`
**目标文件**：`Share/GraphFramework/Pipeline/IGraphPipelineStep.cs`

**提取内容**：
- `IGraphPipelineStep<TKey, TNodeData, TEdgeData>` — 图管线步骤接口
- `IPipelineContext` — 管线上下文接口
- `PipelineContext` — 默认管线上下文实现
- `GraphPipeline<TKey, TNodeData, TEdgeData>` — 图管线（按顺序执行步骤）

**关键改进**：
- 完全泛型化，与 `NodeConfig` 解耦
- 新增 `GraphPipeline` 类，提供步骤的添加/插入/移除/执行能力
- 与现有的 `EcsPipeline` 互补（直接操作 `IGraph` 而非 ECS World）

#### 3.3.4 Guard → `GraphFramework.Helpers`

**源文件**：`Operation/WorkFlowMethod/GraphProcess/Services/Guard.cs`（内部类）
**目标文件**：`Share/GraphFramework/Helpers/Guard.cs`

**提取内容**：
- `AgainstNullOrEmpty()` — 验证字符串/集合不为 null 或空
- `AgainstNull()` — 验证对象不为 null
- `AgainstNonPositive()` — 验证数值为正数
- `AgainstNegative()` — 验证数值为非负数
- `AgainstOutOfRange()` — 验证数值在范围内

### 3.4 第三层：接口抽象（保留在 Operation 层）

#### 3.4.1 IDirectiveApplier → `GraphFramework.Operations`

**源文件**：`Operation/WorkFlowMethod/GraphProcess/Services/DirectiveApplier.cs`
**目标文件**：`Share/GraphFramework/Operations/IDirectiveApplier.cs`

**提取内容**：
- `IDirectiveApplier<TDirective, TKey, TNodeData, TEdgeData>` — 指令应用器接口
- `DirectiveApplierRegistry<TKey, TNodeData, TEdgeData>` — 指令应用器注册表

**说明**：指令模式本身（`Directive → Action<IGraph>`）可以抽象为通用框架，但具体指令类型（`InsertNodeDirective`、`DeleteNodeDirective` 等）与业务强绑定，保留在 Operation 层实现。

### 3.5 文件清单

| # | 文件路径 | 层级 | 来源 |
|:-:|---------|:----:|------|
| 1 | `BasicMethod/GraphFoundation/Navigation/GraphNavigationExtensions.cs` | 🔵 | `GraphProcess/SkeletonBridge/GraphNavigationExtensions.cs` |
| 2 | `BasicMethod/GraphFoundation/Snapshots/GraphSnapshot.cs` | 🔵 | `GraphProcess/Snapshots/GraphSnapshot.cs` |
| 3 | `BasicMethod/GraphFoundation/Snapshots/GraphDiff.cs` | 🔵 | `GraphProcess/Snapshots/GraphDiff.cs` |
| 4 | `BasicMethod/GraphFoundation/Topology/CriticalPath.cs` | 🔵 | `GraphProcess/Pipeline/GraphAnalyzer.cs` |
| 5 | `BasicMethod/GraphFoundation/Graph/GraphCloneExtensions.cs` | 🔵 | `GraphProcess/Pipeline/GraphPipelineSteps.cs` |
| 6 | `Share/GraphFramework/Bridge/GraphFoundationBridge.cs` | 🟢 | `GraphProcess/Pipeline/GraphFoundationBridge.cs` |
| 7 | `Share/GraphFramework/Operations/GraphOperationSkeleton.cs` | 🟢 | `GraphProcess/SkeletonBridge/GraphOperationSkeleton.cs` |
| 8 | `Share/GraphFramework/Pipeline/IGraphPipelineStep.cs` | 🟢 | `GraphProcess/Pipeline/GraphPipelineStep.cs` |
| 9 | `Share/GraphFramework/Helpers/Guard.cs` | 🟢 | `GraphProcess/Services/Guard.cs` |
| 10 | `Share/GraphFramework/Operations/IDirectiveApplier.cs` | 🟡 | `GraphProcess/Services/DirectiveApplier.cs` |

---

## 四、剩余迁移计划

### 4.1 ChainToGraphSkeleton → ChainSystem（已评估保留 🟢）

**评估结论**：经审查 `ChainSystem` 的实现，发现其设计为"ECS 世界中的链数据 → 已有图"，而 `ChainToGraphSkeleton` 的需求是"ChainInfo → 新图"。两者抽象层级不同：

| 维度 | ChainSystem | ChainToGraphSkeleton |
|------|-------------|---------------------|
| 输入 | `ChainDataComponent<TKey>`（ECS 组件） | `ChainInfo<NodeConfig>` 列表 |
| 输出 | 写入已有 `ICoreGraph` | 创建新 `IGraph` |
| 数据源 | `_nodeDataFactory` / `_edgeDataFactory` | `ChainInfo.AllNodes` 中的完整数据 |

直接委托会导致复杂的适配代码（需要将 `ChainInfo` 拆解为 ECS 组件再重建），且 `GraphFoundationBridge.ChainToGraph()` 已在多处使用且稳定。**建议保留当前实现**。

### 4.2 GraphWorkflowConnector → EcsPipeline（已评估保留 🟢）

**评估结论**：当前 `GraphWorkflowConnector` 基于 `IGraphPipelineStep` 接口，直接操作 `IGraph`。`EcsPipeline` 基于 `IPipelineStep` 接口，操作 `EcsWorld`。两者抽象层级不同，**不建议直接替换**。

**建议**：保持 `GraphWorkflowConnector` 作为 Operation 层的管线入口，内部步骤已委托给 GraphFramework 的 Step/System。

---

## 五、API 映射表（更新版）

### 5.1 完整映射表

| GraphProcess（旧） | GraphFramework（新） | 迁移方式 | 优先级 | 状态 |
|---|---|---|---|---|
| `GraphAnalyzer.HasCycle()` | `GraphTopology.TopologicalSort().HasCycle` | 直接委托 | 🔴 高 | ✅ 已完成 |
| `GraphAnalyzer.Analyze()` | `GraphStatistics<,,>.Compute()` | 直接委托 | 🔴 高 | ✅ 已完成 |
| `GraphAnalyzer.FindConnectedComponents()` | `GraphTopology.GetConnectedComponents` | 直接委托 | 🟡 中 | ✅ 已完成 |
| `NodeManager` | 已删除 | 清理 | 🟢 低 | ✅ 已完成 |
| `GraphAnalyzer.FindCriticalPath()` | `CriticalPath.FindLongestPath()` | 提升到 GraphFoundation | 🟢 低 | ✅ 已完成 |
| `GraphFoundationBridge`（GraphProcess） | `GraphFramework.Bridge.GraphFoundationBridge` | 泛型化（移除 `NodeConfig` 约束） | 🟢 低 | ✅ 已完成 |
| `GraphOperationSkeleton`（GraphProcess） | `GraphFramework.Operations.GraphOperationSkeleton` | 内部委托 | 🟢 低 | ✅ 已完成 |
| `GraphCloneStep`（GraphProcess） | `GraphFoundation.Core.GraphCloneExtensions` | 内部委托 | 🟢 低 | ✅ 已完成 |
| `GraphNavigationExtensions`（GraphProcess） | `GraphFoundation.Navigation.GraphNavigationExtensions` | 内部委托 | 🟢 低 | ✅ 已完成 |
| `GraphValidator.Validate()` | `ValidateStep.Execute()` | 委托 | 🔴 高 | ✅ 已完成 |
| `GraphValidator.ValidationResult` | `ValidationResultComponent` | 统一格式 | 🔴 高 | ✅ 已完成 |
| `GraphDeduplicateStep` | `DedupeStep` | 内部委托 | 🔴 高 | ✅ 已完成 |
| `GraphValidateStep` | `ValidateStep` | 内部委托 | 🔴 高 | ✅ 已完成 |
| `GraphAnalyzeStep` | `StatisticsSystem` + `TopologySystem` | 内部委托 | 🔴 高 | ✅ 已完成 |
| `NodeChainService` | `OrchestrationService` | 委托 | 🔴 高 | ✅ 已完成 |
| `CycleDetectionSkeleton` | `GraphTopology.TopologicalSort` | 委托 | 🔴 高 | ✅ 已完成 |
| `GraphValidationSkeleton` | `ValidateStep` | 委托 | 🔴 高 | ✅ 已完成 |
| `ChainToGraphSkeleton` | `ChainSystem` | 已评估保留（层级不同） | 🟡 中 | ✅ 已保留 |
| `ConfigDeduplicationProcessor` | 新框架接口 | 重构依赖 | 🟡 中 | ✅ 已完成 |
| `GraphNavigationExtensions` | `GraphFoundation.Navigation` | 提升到 GraphFoundation | 🔴 高 | ✅ 已完成 |
| `GraphSnapshot` | `GraphFoundation.Snapshots` | 提升到 GraphFoundation | 🔴 高 | ✅ 已完成 |
| `GraphDiff` | `GraphFoundation.Snapshots` | 提升到 GraphFoundation | 🔴 高 | ✅ 已完成 |
| `GraphCloneStep` | `GraphFoundation.Core.GraphCloneExtensions` | 提升到 GraphFoundation | 🟡 中 | ✅ 已完成 |
| `GraphFoundationBridge` | `GraphFramework.Bridge` | 提升到 GraphFramework | 🔴 高 | ✅ 已完成 |
| `GraphOperationSkeleton` | `GraphFramework.Operations` | 提升到 GraphFramework | 🟡 中 | ✅ 已完成 |
| `IGraphPipelineStep` | `GraphFramework.Pipeline` | 提升到 GraphFramework | 🟡 中 | ✅ 已完成 |
| `Guard` | `GraphFramework.Helpers` | 提升到 GraphFramework | 🟢 低 | ✅ 已完成 |
| `DirectiveApplier` | `GraphFramework.Operations.IDirectiveApplier` | 接口抽象 | 🟢 低 | ✅ 已完成 |
| `GraphUpdateService` | 无对应 | 保留 | 🟢 低 | ✅ 已保留 |
| `GraphToChainSkeleton` | 无对应 | 保留 | 🟢 低 | ✅ 已保留 |
| `GraphWorkflowConnector` | `EcsPipeline` | 已评估保留（层级不同） | 🟢 低 | ✅ 已保留 |

### 5.2 命名空间对照

| 层 | 命名空间 |
|---|---|
| GraphProcess（旧） | `WorkFlowMethod.GraphFramework.Pipeline` |
| GraphProcess（旧） | `WorkFlowMethod.GraphFramework.Services` |
| GraphProcess（旧） | `WorkFlowMethod.GraphFramework.SkeletonBridge` |
| GraphProcess（旧） | `WorkFlowMethod.GraphFramework.Core` |
| GraphFoundation（新） | `GraphFoundation.Navigation` |
| GraphFoundation（新） | `GraphFoundation.Snapshots` |
| GraphFoundation（新） | `GraphFoundation.Topology` |
| GraphFoundation（新） | `GraphFoundation.Core` |
| GraphFoundation（新） | `GraphFoundation.Abstractions` |
| GraphFramework（新） | `GraphFramework.Bridge` |
| GraphFramework（新） | `GraphFramework.Operations` |
| GraphFramework（新） | `GraphFramework.Pipeline` |
| GraphFramework（新） | `GraphFramework.Helpers` |
| GraphFramework（新） | `GraphFramework.Systems` |
| GraphFramework（新） | `GraphFramework.Services` |
| ECS.Core | `ECS.Core` |

### 5.3 关键类型对照

| GraphProcess 类型 | GraphFramework 类型 |
|---|---|
| `IGraphPipelineStep<TKey,TNode,TEdge>` | `IPipelineStep`（ECS） |
| `GraphStatistics` | `GraphStatsComponent` |
| `ValidationResult` | `ValidationResultComponent` |
| `IGraphAnalyzer` | `StatisticsSystem` + `TopologySystem` |
| `GraphFoundationBridge` | `GraphFramework.Bridge.GraphFoundationBridge<TKey,TNode,TEdge>` |
| `GraphOperationSkeleton` | `GraphFramework.Operations.GraphOperationSkeleton<TKey,TNode,TEdge>` |
| `GraphSnapshot` | `GraphFoundation.Snapshots.GraphSnapshot<TKey,TNode,TEdge>` |
| `GraphDiff` | `GraphFoundation.Snapshots.GraphDiff<TKey,TNode,TEdge>` |
| `GraphNavigationExtensions` | `GraphFoundation.Navigation.GraphNavigationExtensions` |
| `GraphCloneStep` | `GraphFoundation.Core.GraphCloneExtensions` |
| `Guard` | `GraphFramework.Helpers.Guard` |
| `DirectiveApplier` | `GraphFramework.Operations.IDirectiveApplier<TDir,TKey,TNode,TEdge>` |
| `NodeChainService` | `OrchestrationService` |

---

## 六、迁移检查清单

### 第一阶段（高优先级）— ✅ 全部完成
- [x] `GraphAnalyzer.HasCycle()` → 委托给 `GraphTopology.TopologicalSort`
- [x] `GraphAnalyzer.Analyze()` → 委托给 `GraphStatistics<,,>.Compute()`
- [x] `GraphValidator.Validate()` → 委托给 `ValidateStep`
- [x] `GraphDeduplicateStep` → 内部委托给 `DedupeStep`
- [x] `GraphValidateStep` → 内部委托给 `ValidateStep`
- [x] `GraphAnalyzeStep` → 委托给 `StatisticsSystem` + `TopologySystem`

### 第二阶段（高优先级）— ✅ 全部完成
- [x] `CycleDetectionSkeleton` → 委托给 `GraphTopology.TopologicalSort`
- [x] `GraphValidationSkeleton` → 委托给 `ValidateStep`
- [x] `NodeChainService` → 委托给 `OrchestrationService`

### 第三阶段（中优先级）— ✅ 全部完成
- [x] `GraphAnalyzer.FindConnectedComponents()` → 委托给 `GraphTopology.GetConnectedComponents`
- [x] `ConfigDeduplicationProcessor` → 重构依赖
- [x] `NodeManager` → 删除

### 第四阶段（低优先级）— ✅ 全部完成
- [x] `StatisticsSystem` 扩展 `ConnectedComponents` 字段
- [x] `GraphWorkflowConnector` → 已评估保留

### 第五阶段：通用框架提取 — ✅ 全部完成
- [x] `GraphNavigationExtensions` → 提升到 `GraphFoundation.Navigation`
- [x] `GraphSnapshot` + `GraphDiff` → 提升到 `GraphFoundation.Snapshots`
- [x] `CriticalPath` → 提升到 `GraphFoundation.Topology`
- [x] `GraphCloneExtensions` → 提升到 `GraphFoundation.Core`
- [x] `GraphFoundationBridge` → 提升到 `GraphFramework.Bridge`
- [x] `GraphOperationSkeleton` → 提升到 `GraphFramework.Operations`
- [x] `IGraphPipelineStep` → 提升到 `GraphFramework.Pipeline`
- [x] `Guard` → 提升到 `GraphFramework.Helpers`
- [x] `IDirectiveApplier` → 提升到 `GraphFramework.Operations`

### 第六阶段：GraphProcess 内部委托 — ✅ 全部完成
- [x] `GraphAnalyzer.FindCriticalPath()` → 委托给 `CriticalPath.FindLongestPath()`
- [x] `GraphCloneStep` → 委托给 `GraphCloneExtensions.Clone()`
- [x] `GraphOperationSkeleton.ExecuteMerge/ExecuteFilter/ExecuteSubgraph` → 委托给 `GraphFramework.Operations.GraphOperationSkeleton`
- [x] `GraphNavigationExtensions` → 委托给 `GraphFoundation.Navigation.GraphNavigationExtensions`
- [x] `GraphFoundationBridge` → 泛型化（移除 `NodeConfig` 约束）
- [x] `IGraphPipelineStep` → 添加文档引用到 `GraphFramework.Pipeline.IGraphPipelineStep`
- [x] `GraphCoreSkeleton` → 添加文档说明