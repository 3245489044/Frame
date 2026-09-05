# LayoutAnalysis 复用 Map（GraphFoundation）树/图能力重构方案

> **日期**：2026-07-31
> **目标**：复用 `CsharpPrj/BasicMethod/Map/GraphFoundation` 的树遍历、路径查找、索引、图存储与图分析能力，消除 `CsharpPrj/Share/LayoutAnalysis` 中手写的重复实现，并新增布局图分析能力。
> **对应公理**：`[AX-FRAME] StratifiedReflectionSchema`（分层复用）、`[AX] StateSnapshot/StateMerge`（只读图快照一致性）

---

## 一、现状与问题

### 1.1 LayoutAnalysis 手写实现（与 GraphFoundation 重叠）

| LayoutAnalysis 中的手工实现 | 对应 GraphFoundation 能力 | 重复点 |
|---|---|---|
| `ControlTreeWalker.Walk/WalkBreadthFirst` 手写递归 DFS + BFS | `TreeWalker<TNode>`（DFS/BFS/后序 + 深度限制 + WalkAsync） | 遍历逻辑重复 |
| `OverlapCalculator.IsAncestorOf` 手写沿 ParentName 链向上追溯 | `TreePathFinder<TNode>`（FindNearestAncestor / IsDescendantOf / LCA） | 祖先查找重复 |
| `UIElementTracker.FindByName/FindByType`、`UIInspector.FindControl/FindControlsByType` 手写 Where/FirstOrDefault | `TreeIndex<TNode, TKey>`（按 ID 建索引 + TryGet/Contains） | 查找逻辑重复 |
| `RelationCalculator.CalculateHierarchyRelations` 手写字典遍历 | `CoreGraph<string,...>` Parent→Child 有向边 | 层级关系即树的边集 |
| `RelationCalculator.CalculateSpatialRelations/SemanticRelations` 扁平 List | `CoreGraph` 有向带权边存储 | 关系数据无图查询能力 |

### 1.2 依赖约束

- `LayoutAnalysis.csproj` 原仅依赖 `UniversalLayoutMath`（纯数学库，不依赖 BasicMethod 上层）
- `GraphFoundation.csproj` 目标框架 `net10.0-windows`，与 LayoutAnalysis 兼容
- 下游引用方（`FrontEndDriven.csproj`、`Share.csproj`、`BasicMethod.csproj`）公开 API 必须保持兼容

---

## 二、重构方案（四阶段）

### 阶段一：消除树遍历/祖先查找/查找 API 重复

**变更文件**：

1. `LayoutAnalysis.csproj`：新增对 `..\..\BasicMethod\Map\GraphFoundation\GraphFoundation.csproj` 的 ProjectReference
2. 新增 `LayoutTreeTraversal.cs`：
   - `CreateWalker(controls)` → `new TreeWalker<ControlMetadata>(c => 子节点委托)`
   - `CreatePathFinder(controls)` → `new TreePathFinder<ControlMetadata>(c => 父节点委托)`
   - `CreateIndex(controls)` → `new TreeIndex<ControlMetadata, string>(Name 索引)`
   - `GetRoots(controls)` → 无父节点的根集合
3. `UIInspector/ControlTreeWalker.cs`：手写递归 `WalkRecursive` 与 BFS 队列删除，改用 `TreeWalker<TraversalState>`（坐标累加逻辑封装在 `getChildren` 委托内）
4. `UIOverlapDetector/Calculator/OverlapCalculator.cs`：`IsAncestorOf` 手写循环删除，改用 `TreePathFinder<ControlMetadata>.IsDescendantOf`
5. `UIInspector/UIInspector.cs` + `UIElementTracker.cs`：`FindByName/FindControl/FindByType/FindControlsByType` 改用 `LayoutTreeTraversal.CreateIndex` O(1) 查找

### 阶段二：布局数据图化（核心）

**新增文件**：

1. `Models/LayoutGraphModels.cs`：
   - `LayoutNodeData`：包装 `NormalizedEntity`
   - `LayoutEdgeData`：Kind（hierarchy/spatial/semantic）+ RelationType + Description + Evidence
   - `LayoutCluster`：视觉簇（连通分量）
   - `LayoutGraphContext`：Graph + Clusters + EntryNodeIds + ExitNodeIds + HasCycle
2. `LayoutGraphBuilder.cs`：
   - `CoreGraph<string, LayoutNodeData, LayoutEdgeData>` 作为唯一图存储
   - 节点 = NormalizedEntities；层级边（Parent→Child）、空间边（From→To）、语义边（From→To）统一入图
   - 复用 `GraphFoundation.Topology.GraphTopology`（连通分量 GetConnectedComponents / 环检测 HasCycle / 拓扑排序 TopologicalSort）
   - 复用 `GraphFoundation.Navigation.GraphNavigationExtensions`（入口节点 GetEntryNodeKeys / 出口节点 GetExitNodeKeys）

**变更文件**：

3. `Models/DataModels.cs`：`LayoutCollectionResult` 新增 `GraphContext` 属性
4. `LayoutDataCollector.cs`：
   - `Collect(...)` 内部自动调用 `LayoutGraphBuilder.Build(result)` 填充 `GraphContext`
   - 新增 `CollectGraph(...)` 便捷入口（直接返回 `LayoutGraphContext`）

### 阶段三：图导出能力

**新增文件**：

5. `Formatting/LayoutGraphFormatter.cs`：
   - `GenerateSummary(ctx)`：自然语言摘要（节点/边数量、环、入口/出口、视觉簇）
   - `GenerateJson(ctx)`：结构化 JSON（节点计数、边详情、簇、入口出口）

**变更文件**：

6. `LayoutDataCollector.cs`：新增 `ExportGraph(result, format)`（text/json）

### 阶段四：API 收敛与兼容

7. 所有公开 API 签名保持不变 → 下游零破坏
8. `UIElementTracker.cs`：新增第 7 节「布局图分析」便捷入口（`BuildGraph` / `ExportGraph`）

---

## 三、变更清单

| 操作 | 文件 |
|------|------|
| 修改 | `CsharpPrj/Share/LayoutAnalysis/LayoutAnalysis.csproj`（新增 GraphFoundation 引用） |
| 新增 | `CsharpPrj/Share/LayoutAnalysis/LayoutTreeTraversal.cs` |
| 重构 | `CsharpPrj/Share/LayoutAnalysis/UIInspector/ControlTreeWalker.cs`（TreeWalker 替代手写递归/BFS） |
| 重构 | `CsharpPrj/Share/LayoutAnalysis/UIOverlapDetector/Calculator/OverlapCalculator.cs`（TreePathFinder 替代手写祖先链） |
| 重构 | `CsharpPrj/Share/LayoutAnalysis/UIInspector/UIInspector.cs`、`UIElementTracker.cs`（TreeIndex 替代手写查找） |
| 新增 | `CsharpPrj/Share/LayoutAnalysis/Models/LayoutGraphModels.cs` |
| 新增 | `CsharpPrj/Share/LayoutAnalysis/LayoutGraphBuilder.cs` |
| 修改 | `CsharpPrj/Share/LayoutAnalysis/Models/DataModels.cs`（LayoutCollectionResult.GraphContext） |
| 修改 | `CsharpPrj/Share/LayoutAnalysis/LayoutDataCollector.cs`（Collect 自动构图 + CollectGraph/ExportGraph） |
| 新增 | `CsharpPrj/Share/LayoutAnalysis/Formatting/LayoutGraphFormatter.cs` |

---

## 四、收益

1. **消除重复**：树遍历、祖先查找、查找 API 三处手写实现全部删除，统一委托 GraphFoundation
2. **获得图分析**：布局关系具备连通簇（视觉分组）、入口/出口（根容器/叶子控件）、环检测（关系异常）能力
3. **统一数据模型**：层级/空间/语义关系统一存入一张 `CoreGraph`，查询与导出一致
4. **零破坏演进**：公开 API 签名不变，三个下游项目编译零警告零错误

---

## 五、验证结果

```
dotnet build CsharpPrj/Share/LayoutAnalysis/LayoutAnalysis.csproj  → 0 警告 0 错误
dotnet build CsharpPrj/Operation/FrontEndDriven/FrontEndDriven.csproj → 0 警告 0 错误
dotnet build CsharpPrj/BasicMethod/BasicMethod.csproj              → 0 警告 0 错误
dotnet build CsharpPrj/Share/Share.csproj                          → 0 警告 0 错误
```

---

## 六、后续可扩展方向

- `OverlapCalculator` 可进一步复用 `LayoutTreeTraversal.CreateWalker` 提供树序遍历式重叠检测（当前为两两比较）
- `LayoutGraphBuilder` 可扩展 `LayoutCluster` 元数据（簇内包围盒、簇间空间关系）
- 图事件（`GraphEvent`）可接入 UI 布局变更订阅（增量布局分析）