# GraphProcess 中 GraphFramework 的应用方案

> **目标**：指导开发者在 GraphProcess 模块中正确使用新提取的 GraphFoundation 和 GraphFramework 框架，避免重复造轮子，确保代码一致性。

---

## 1. 框架定位

```
GraphProcess (Operation 层) — 业务编排层
    ↓ 委托/使用
GraphFramework (Share 层) — 通用图框架能力
    ↓ 依赖
GraphFoundation (BasicMethod 层) — 纯图算法基础库
```

### 1.1 各层职责

| 层 | 职责 | 是否可独立使用 |
|----|------|:------------:|
| **GraphFoundation** | 纯图算法（导航、拓扑排序、关键路径、快照、克隆） | ✅ 是 |
| **GraphFramework** | 通用图框架（桥接、操作、管线、指令模式） | ✅ 是 |
| **GraphProcess** | 业务编排（链→图转换、环检测、图验证、去重） | ❌ 依赖下层 |

### 1.2 何时使用哪一层

| 场景 | 推荐使用 |
|------|---------|
| 需要遍历图（前驱/后继/入口/出口） | `GraphFoundation.Navigation.GraphNavigationExtensions` |
| 需要拓扑排序或环检测 | `GraphFoundation.Topology.GraphTopology` |
| 需要关键路径分析 | `GraphFoundation.Topology.CriticalPath` |
| 需要不可变图快照 | `GraphFoundation.Snapshots.GraphSnapshot<TKey,TNodeData,TEdgeData>` |
| 需要图差异计算 | `GraphFoundation.Snapshots.GraphDiff<TKey,TNodeData,TEdgeData>` |
| 需要图深克隆 | `GraphFoundation.Core.GraphCloneExtensions` |
| 需要链⇄图转换 | `GraphFramework.Bridge.GraphFoundationBridge<TKey,TNodeData,TEdgeData>` |
| 需要图合并/过滤/子图 | `GraphFramework.Operations.GraphOperationSkeleton<TKey,TNodeData,TEdgeData>` |
| 需要图管线编排 | `GraphFramework.Pipeline.GraphPipeline<TKey,TNodeData,TEdgeData>` |
| 需要参数校验 | `GraphFramework.Helpers.Guard` |
| 需要指令模式 | `GraphFramework.Operations.IDirectiveApplier<TDir,TKey,TNodeData,TEdgeData>` |

---

## 2. 代码编写规范

### 2.1 新代码优先使用泛型版本

```csharp
// ✅ 正确：使用泛型版本
using GraphFoundation.Navigation;

var entryNodes = graph.GetEntryNodeKeys();

// ❌ 错误：在 GraphProcess 中重新实现
var entryNodes = graph.GetAllNodeKeys()
    .Where(k => graph.GetIncomingEdges(k).Count == 0)
    .ToList();
```

### 2.2 使用 GraphFramework 而非直接操作 GraphFoundation

```csharp
// ✅ 正确：通过 GraphFramework 的桥接层
var bridge = new GraphFramework.Bridge.GraphFoundationBridge<string, NodeConfig, object>(context);
var graph = bridge.ChainToGraph(chains);

// ❌ 错误：直接操作 GraphFoundation 的底层 API
var graph = new CoreGraph<string, NodeConfig, object>(context);
// ... 手动添加节点和边 ...
```

### 2.3 保持 GraphProcess 的编排角色

GraphProcess 中的类应保持为"编排层"，内部委托给 GraphFoundation/GraphFramework：

```csharp
public class MyGraphService
{
    public IReadOnlyList<string> FindCriticalPath(IGraph<string, NodeConfig, object> graph, string start, string end)
    {
        // ✅ 委托给 GraphFoundation
        return GraphFoundation.Topology.CriticalPath.FindLongestPath(graph, start, end);
    }
}
```

---

## 3. 现有代码迁移优先级

### 3.1 已完成的委托

| 文件 | 委托目标 | 状态 |
|------|---------|:----:|
| `Pipeline/GraphAnalyzer.cs` — `FindCriticalPath` | `GraphFoundation.Topology.CriticalPath.FindLongestPath` | ✅ |
| `Pipeline/GraphPipelineSteps.cs` — `GraphCloneStep` | `GraphFoundation.Core.GraphCloneExtensions.Clone` | ✅ |
| `SkeletonBridge/GraphOperationSkeleton.cs` — `ExecuteMerge/Filter/Subgraph` | `GraphFramework.Operations.GraphOperationSkeleton` | ✅ |
| `SkeletonBridge/GraphNavigationExtensions.cs` | `GraphFoundation.Navigation.GraphNavigationExtensions` | ✅ |
| `Pipeline/GraphFoundationBridge.cs` | 泛型化（移除 `NodeConfig` 约束） | ✅ |

### 3.2 建议后续迁移

| 文件 | 建议 | 优先级 |
|------|------|:------:|
| `Pipeline/GraphAnalyzer.cs` — `Analyze` | 已委托给 `GraphStatistics<,,>.Compute()` | 🟢 已完成 |
| `Pipeline/GraphAnalyzer.cs` — `HasCycle` | 已委托给 `GraphTopology.TopologicalSort` | 🟢 已完成 |
| `Services/GraphValidator.cs` | 已委托给 `ValidateStep` | 🟢 已完成 |
| `Services/NodeChainService.cs` | 已委托给 `OrchestrationService` | 🟢 已完成 |
| `SkeletonBridge/CycleDetectionSkeleton.cs` | 已委托给 `GraphTopology.TopologicalSort` | 🟢 已完成 |
| `SkeletonBridge/GraphValidationSkeleton.cs` | 已委托给 `ValidateStep` | 🟢 已完成 |

### 3.3 保留不迁移的组件

| 组件 | 保留原因 |
|------|---------|
| `GraphFoundationBridge`（GraphProcess 版本） | 提供丰富的图操作（InsertNodesAfter/Before、DeleteNode、ReplaceNode、RedirectEdge），已泛型化 |
| `DirectiveApplier` | GraphProcess 特有的指令体系 |
| `GraphUpdateService` | Operation 层的便捷封装 |
| `GraphToChainSkeleton` | GraphFramework 无对应功能 |
| `GraphWorkflowConnector` | 与 EcsPipeline 抽象层级不同 |

---

## 4. 新功能开发指南

### 4.1 添加新的图算法

1. 判断算法是否与业务无关（纯图算法）
2. 如果是 → 添加到 `BasicMethod/GraphFoundation/` 对应子目录
3. 如果否 → 添加到 `Operation/WorkFlowMethod/GraphProcess/` 对应子目录

### 4.2 添加新的图框架能力

1. 判断能力是否通用（可被多个模块复用）
2. 如果是 → 添加到 `Share/GraphFramework/` 对应子目录
3. 如果否 → 保留在 `Operation/WorkFlowMethod/GraphProcess/`

### 4.3 添加新的业务逻辑

1. 直接添加到 `Operation/WorkFlowMethod/GraphProcess/`
2. 内部委托给 GraphFoundation/GraphFramework 的通用能力

---

## 5. 架构决策记录

### 5.1 为什么 GraphFoundation 在 BasicMethod 层而非 Share 层？

**决策**：GraphFoundation 放在 `BasicMethod/GraphFoundation/` 而非 `Share/GraphFoundation/`。

**原因**：
- GraphFoundation 是纯图算法库，与业务无关
- BasicMethod 层是项目的基础工具层，GraphFoundation 作为基础工具之一
- Share 层依赖 BasicMethod 层，如果 GraphFoundation 在 Share 层，会导致 Share 层自引用

### 5.2 为什么 GraphFramework 在 Share 层？

**决策**：GraphFramework 放在 `Share/GraphFramework/`。

**原因**：
- GraphFramework 提供的是框架能力（桥接、操作、管线），而非纯算法
- Share 层是共享复用层，GraphFramework 作为共享框架能力
- GraphFramework 依赖 GraphFoundation（BasicMethod 层），符合 Share → BasicMethod 的依赖方向

### 5.3 为什么保留 GraphProcess 中的重复委托文件？

**决策**：保留 `SkeletonBridge/GraphNavigationExtensions.cs` 等委托文件。

**原因**：
- 保持 GraphProcess 对外接口不变，调用方无需修改代码
- 委托文件作为"适配层"，桥接 GraphProcess 的具体类型和 GraphFoundation 的泛型类型
- 未来可逐步淘汰，但当前阶段保持兼容性更重要

---

## 6. 代码审查清单

在审查 GraphProcess 相关代码时，检查以下要点：

- [ ] 是否使用了 `GraphFoundation.Navigation` 而非自实现导航逻辑？
- [ ] 是否使用了 `GraphFoundation.Topology.GraphTopology` 而非自实现拓扑排序？
- [ ] 是否使用了 `GraphFoundation.Topology.CriticalPath` 而非自实现关键路径？
- [ ] 是否使用了 `GraphFoundation.Core.GraphCloneExtensions` 而非自实现克隆？
- [ ] 是否使用了 `GraphFramework.Operations.GraphOperationSkeleton` 而非自实现合并/过滤/子图？
- [ ] 是否使用了 `GraphFramework.Helpers.Guard` 而非自实现参数校验？
- [ ] 新算法是否放在了正确的层级（纯算法→GraphFoundation，框架能力→GraphFramework，业务→GraphProcess）？
