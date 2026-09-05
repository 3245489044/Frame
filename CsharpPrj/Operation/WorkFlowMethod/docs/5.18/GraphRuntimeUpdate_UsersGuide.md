WorkFlowMethod 运行时动态更新使用指南
1. 概述
WorkFlowMethod 的运行时动态更新系统允许在工作流执行过程中动态调整工作流结构，无需重新部署或重启应用。该系统将工作流链（Chain）转换为图（Graph），在图上执行更新操作，再转换回链结构，从而实现对工作流节点的实时添加、删除、替换和重定向。

1.1 核心能力
动态插入：在工作流任意位置插入新节点或整条子链

动态删除：删除指定节点并自动重连上下游

动态替换：用新节点替换现有节点，保留连接关系

边重定向：更改节点的连接目标

批量与复合：支持多条指令组合执行

1.2 架构演进：统一桥接层

```
2025年以前：两套平行指针系统
    NodeConfig.nextNodeConfig.NodeKey  ← 手动同步 →  GraphFoundation 边

2025年重构后：单一数据源
    GraphFoundation 边（EdgeComponent）—— 唯一连接关系来源
    NodeConfig.nextNodeConfig.NodeKey   —— [Obsolete]，仅序列化缓存
    GraphFoundationBridge               —— 统一的链⇄图桥接层
```

此次重构的核心变化是**消除平行指针系统**，将 **GraphFoundation 的边**作为节点连接关系的唯一真实来源（Single Source of Truth），通过统一的 `GraphFoundationBridge` 桥接层完成链与图之间的双向转换。

| 维度 | 旧架构 | 新架构 |
|---|---|---|
| **连接关系存储** | NodeConfig.nextNodeConfig.NodeKey + Edge | **仅 Edge** |
| **链→图转换** | 手动读取 NodeKey 创建 Edge | `GraphFoundationBridge.ChainToGraph()` |
| **图→链转换** | 手动从 Edge 写回 NodeKey | `GraphFoundationBridge.GraphToChain()` 自动回填 |
| **边操作** | 分散在 DynamicUpdateTransformer 各处 | `GraphFoundationBridge` 统一事务方法 |
| **桥接层数量** | 3 个（GraphAdapter / GraphChainBridge / 手动代码） | **1 个** |
| **build** 输出 | 21 个 CS0618 废弃警告（预期） | 提示迁移至桥接层 API |

1.3 定位方式

| 定位方式 | 依据 | 稳定性 | 语义 |
|---|---|---|---|
| Callkey 定位 | 节点的具体串键 | Callkey 变更后失效 | node_003 |
| Symbol 定位（推荐） | 节点的符号类型 | Symbol 不变则始终有效 | AIGenerateImage |

两种方式可共存，Symbol 定位优先级更高。

2. 系统架构
2.1 整体流程
text
用户层 API
    │
    ▼
┌──────────────────────────────────────────────────────────────┐
│  指令构建层                                                   │
│  GraphUpdateDirective 及其派生类                               │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  指令转换层                                                   │
│  ConvertToDirective() — ConnectNodeConfig → Directive        │
│  ResolveDirectiveSymbols() — Symbol → Callkey                │
│  SymbolPositionResolver — Symbol 解析引擎                    │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  图更新执行层                                                 │
│  GraphWorkflowUpdater.ApplyGraphUpdate()                     │
│      ├─ GraphFoundationBridge.ChainToGraph()  链 → 图       │
│      ├─ DynamicUpdateTransformer             执行更新指令     │
│      └─ GraphFoundationBridge.GraphToChain()  图 → 链       │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  图引擎层 (GraphFoundation)                                    │
│  CoreGraph<TKey, TNodeData, TEdgeData>                      │
│  └─ 边是连接关系的唯一真实来源                                 │
└──────────────────────────────────────────────────────────────┘
2.2 核心处理流程
text
原始链 (ChainInfo)
    │
    ▼
GraphFoundationBridge.ChainToGraph()  ───→  CoreGraph (图结构)
                                                    │
                                                    ▼
                                     DynamicUpdateTransformer
                                         ├─ ApplyInsert()
                                         │     └─ GraphFoundationBridge.InsertNodesAfter/Before
                                         ├─ ApplyDelete()
                                         │     └─ GraphFoundationBridge.DeleteNode
                                         ├─ ApplyReplace()
                                         │     └─ GraphFoundationBridge.ReplaceNode
                                         └─ ApplyRedirectEdge()
                                               └─ GraphFoundationBridge.RedirectEdge
                                                    │
                                                    ▼
                                     GraphFoundationBridge.GraphToChain()  ───→  更新后的链
2.3 Symbol 解析流程
text
InsertChainDirective
  ├─ InsertAfterSymbol = "DataProcess"
  └─ SymbolMatchMode = Last
          │
          ▼
ResolveDirectiveSymbols()
          │
          ├─ ContextNodeRepository.GetNodeBySymbol("DataProcess")  ← 仓库查找
          └─ ChainInfo.AllNodes.Where(n => n.Symbol == "DataProcess") ← 遍历回退
          │
          ▼
解析结果: InsertAfterNodeCallkey = "node_002"
3. 更新指令体系
3.1 指令类型
指令	用途
InsertNodeDirective	插入单个或多个新节点
InsertChainDirective	从 JSON 配置插入子链
DeleteNodeDirective	删除节点（可选重连邻居）
ReplaceNodeDirective	替换节点数据
RedirectEdgeDirective	重定向边
CompositeDirective	复合多条指令
3.2 插入指令
InsertChainDirective
csharp
public class InsertChainDirective : GraphUpdateDirective
{
    // Callkey 定位
    public string InsertAfterNodeCallkey { get; set; } = string.Empty;
    public string InsertBeforeNodeCallkey { get; set; } = string.Empty;

    // Symbol 定位（推荐）
    public string InsertAfterSymbol { get; set; } = string.Empty;
    public string InsertBeforeSymbol { get; set; } = string.Empty;
    public SymbolMatchMode SymbolMatchMode { get; set; } = SymbolMatchMode.First;

    // 链配置
    public List<string> ChainJsonConfigs { get; init; } = new();
    public List<NodeTemplate> ParsedNodes { get; set; } = new();
    public int ListCount { get; set; } = 1;
}
InsertNodeDirective
csharp
public class InsertNodeDirective : GraphUpdateDirective
{
    // 定位字段
    public string SourceNodeKey { get; set; } = string.Empty;
    public string TargetNodeKey { get; set; } = string.Empty;
    public string InsertAfterSymbol { get; set; } = string.Empty;
    public string InsertBeforeSymbol { get; set; } = string.Empty;
    public SymbolMatchMode SymbolMatchMode { get; set; } = SymbolMatchMode.First;

    // 位置控制
    public EdgeDirection Direction { get; set; } = EdgeDirection.AfterSource;
    public InsertPosition Position { get; set; } = InsertPosition.AfterSource;

    // 节点定义
    public List<NodeTemplate> NodesToInsert { get; set; } = new();
    public List<EdgeTemplate>? EdgesToCreate { get; set; }
}
3.3 删除与重定向指令
csharp
public class DeleteNodeDirective : GraphUpdateDirective
{
    public string NodeKey { get; set; } = string.Empty;
    public bool ReconnectNeighbors { get; init; } = true;
    public bool DeleteOrphanedChains { get; init; } = true;
}

public class RedirectEdgeDirective : GraphUpdateDirective
{
    public string FromNodeKey { get; init; } = string.Empty;
    public string OldTargetKey { get; init; } = string.Empty;
    public string NewTargetKey { get; init; } = string.Empty;
    public bool RemoveOldEdge { get; init; } = true;
}
3.4 复合指令
csharp
public class CompositeDirective : GraphUpdateDirective
{
    public List<GraphUpdateDirective> Directives { get; set; } = new();
    public CompositeStrategy Strategy { get; set; } = CompositeStrategy.Sequential;
    public bool StopOnError { get; set; } = true;
}
4. 定位方式详解
4.1 枚举定义
csharp
public enum SymbolMatchMode
{
    First,   // 匹配第一个同名 Symbol 节点（默认）
    Last,    // 匹配最后一个同名 Symbol 节点
    All,     // 匹配所有同名 Symbol 节点（批量插入）
    Nearest  // 按前驱/后继关系匹配最近的节点
}

public enum InsertPosition
{
    AfterSource,   // 在源节点之后（默认）
    BeforeTarget,  // 在目标节点之前
    Between,       // 在源和目标之间
    AtStart,       // 在链首
    AtEnd          // 在链尾
}
4.2 Symbol 定位优势与优先级
优势：

Symbol 代表节点类型/角色，不随实例化而改变

语义明确："在 AIGenerateImage 节点之后" 比 "在 node_003 之后" 更清晰

支持批量：SymbolMatchMode.All 可在所有同名 Symbol 后插入

优先级：Symbol 定位 > Sequence 定位 > Callkey 定位

5. 数据模型
5.1 NodeTemplate 与 EdgeTemplate
csharp
public class NodeTemplate
{
    public string? Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Callkey { get; set; }
    public object? BaseConfig { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class EdgeTemplate
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public object? Data { get; set; }
    public double Weight { get; set; } = 1.0;
}
5.2 ConnectNodeConfig
用户层与动态更新系统交互的数据模型：

csharp
public class ConnectNodeConfig
{
    public string NodeKey { get; set; }
    // Callkey 定位
    public string AfterNodeCallkey { get; set; }
    public string BeforeNodeCallkey { get; set; }
    // Symbol 定位
    public string AfterSymbol { get; set; }
    public string BeforeSymbol { get; set; }
    public string SymbolMatchMode { get; set; } = "First";
    public bool UpdateChain { get; set; }
    public string? ConnectChainInfoConfigStr { get; set; }

    public bool IsInsertOperation =>
        !string.IsNullOrEmpty(ConnectChainInfoConfigStr) ||
        !string.IsNullOrEmpty(AfterSymbol) ||
        !string.IsNullOrEmpty(BeforeSymbol);
}
5.3 ConnectionPositionInfo
配置层数据模型：

csharp
public class ConnectionPositionInfo<TWorkflowId>
{
    public int ListCount { get; set; }
    public string ChainConfigValue { get; set; }
    // Callkey 定位
    public string InsertAfterNodeCallkey { get; set; }
    public string InsertBeforeNodeCallkey { get; set; }
    // Symbol 定位
    public string InsertAfterSymbol { get; set; }
    public string InsertBeforeSymbol { get; set; }
    public string SymbolMatchMode { get; set; } = "First";
    public TWorkflowId WorkflowId { get; set; }
}
6. 核心 API 参考
6.1 GraphWorkflowUpdater
csharp
public static class GraphWorkflowUpdater
{
    public static ChainInfo ApplyGraphUpdate(
        ChainInfo originalChain,
        IEnumerable<GraphUpdateDirective> directives,
        string? workflowId = null);

    public static GraphUpdateDirective? ConvertFromConnectNodeConfig(
        ConnectNodeConfig config);

    public static UpdateStatus GetUpdateStatus(string workflowId);
    public static bool IsUpdateInProgress(string workflowId);
    public static void ClearUpdateStatus(string workflowId);
}
6.2 SymbolPositionResolver
csharp
public static class SymbolPositionResolver
{
    public static string ResolveBySymbol(
        string symbol, SymbolMatchMode mode,
        INodeRepository<NodeConfig> repository, ChainInfo chain);

    public static List<string> ResolveAllSymbols(string symbol, ChainInfo chain);

    public static (string beforeCallkey, string afterCallkey) ResolveInsertPosition(
        string insertAfterSymbol, string insertBeforeSymbol,
        SymbolMatchMode mode,
        INodeRepository<NodeConfig> repository, ChainInfo chain);
}
6.3 工厂方法与扩展
csharp
public static class WorkflowMessageHelper
{
    // Callkey 定位
    public static ConnectNodeConfig CreateInsertChainMessage(
        string currentNodeCallkey,
        IEnumerable<string> chainJson,
        string insertAfter = null,
        string insertBefore = null,
        List<object> mapKeyConfig = null,
        bool updateChain = true,
        bool handleNextNodeRun = true);

    // Symbol 定位（推荐）
    public static ConnectNodeConfig CreateInsertChainMessageBySymbol(
        string currentNodeCallkey,
        IEnumerable<string> chainJson,
        string insertAfterSymbol = null,
        string insertBeforeSymbol = null,
        SymbolMatchMode matchMode = SymbolMatchMode.First,
        List<object> mapKeyConfig = null,
        bool updateChain = true,
        bool handleNextNodeRun = true);
}

public static class GraphUpdateExtensions
{
    public static GraphUpdateDirective? ToGraphDirective(this ConnectNodeConfig config);
    public static ChainInfo ApplyGraphUpdate(this ChainInfo chain, GraphUpdateDirective directive);
    public static bool IsGraphUpdateSupported(this ConnectNodeConfig config);
}
7. 使用示例
7.1 Symbol 定位插入子链
csharp
var directive = new InsertChainDirective
{
    InsertAfterSymbol = "AIGenerateImage",
    SymbolMatchMode = SymbolMatchMode.First,
    ChainJsonConfigs = new List<string>
    {
        "[{\"Symbol\":\"ImagePostProcess\",\"funcPipelineConfig\":{\"FuncName\":\"PostProcessImage\"}}]"
    },
    ListCount = 1
};

var updatedChain = GraphWorkflowUpdater.ApplyGraphUpdate(originalChain, new[] { directive });
7.2 工厂方法创建
csharp
var message = WorkflowMessageHelper.CreateInsertChainMessageBySymbol(
    currentNodeCallkey: "current_001",
    chainJson: new[] { "[{\"Symbol\":\"NewStep\"}]" },
    insertAfterSymbol: "DataProcess",
    matchMode: SymbolMatchMode.All
);

var directive = GraphWorkflowUpdater.ConvertFromConnectNodeConfig(message);
var result = GraphWorkflowUpdater.ApplyGraphUpdate(chain, new[] { directive });
7.3 删除与自动重连
csharp
var directive = new DeleteNodeDirective
{
    NodeKey = "node_to_delete",
    ReconnectNeighbors = true
};

var result = GraphWorkflowUpdater.ApplyGraphUpdate(chain, new[] { directive });
7.4 复合更新
csharp
var composite = new CompositeDirective
{
    Strategy = CompositeStrategy.Sequential,
    Directives = new List<GraphUpdateDirective>
    {
        new InsertChainDirective { InsertAfterSymbol = "Setup", ... },
        new InsertNodeDirective { InsertAfterSymbol = "MidProcess", ... },
        new DeleteNodeDirective { NodeKey = "obsolete", ReconnectNeighbors = true }
    }
};

var result = GraphWorkflowUpdater.ApplyGraphUpdate(chain, new[] { composite });
7.5 JSON 配置方式
json
{
  "ConfigKey": "Workflow_Insertion",
  "MapKey": "AIGenerateImage",
  "ListCount": 1,
  "InsertAfterSymbol": "AIGenerateImage",
  "SymbolMatchMode": "First"
}
csharp
var list = ConnectionPositionInfoFactory.CreateListFromJson(jsonConfig);
8. GraphFoundationBridge 统一桥接层

8.1 设计原则

1. **GraphFoundation 的边是节点连接关系的唯一真实来源**
2. `nextNodeConfig.NodeKey` 仅作为序列化缓存，由 `GraphToChain` 自动填充
3. 所有边操作通过 GraphFoundation 事务执行，保证原子性

8.2 GraphFoundationBridge API

```csharp
public class GraphFoundationBridge<TKey, TNodeData, TEdgeData>
    where TKey : notnull
    where TNodeData : NodeConfig
    where TEdgeData : class
{
    // 链 → 图
    public IGraph<string, TNodeData, object> ChainToGraph(
        IReadOnlyList<ChainInfo<TNodeData>> chains);
    public IGraph<string, TNodeData, object> ChainToGraph(
        ChainInfo<TNodeData> chain);

    // 图 → 链
    public ChainInfo<TNodeData> GraphToChain(
        IGraph<string, TNodeData, object> graph,
        ChainInfo<TNodeData>? originalChain = null);
    public List<ChainInfo<TNodeData>> ExtractComponents(
        IGraph<string, TNodeData, object> graph);

    // 边查询（替代 nextNodeConfig.NodeKey 读路径）
    public static string? GetNextNodeKey(
        IGraph<string, TNodeData, object> graph, string nodeKey);
    public static string? GetPreviousNodeKey(
        IGraph<string, TNodeData, object> graph, string nodeKey);

    // 插入操作（事务）
    public List<string> InsertNodesAfter(
        IGraph<string, TNodeData, object> graph,
        string sourceKey, List<NodeTemplate> nodesToInsert);
    public List<string> InsertNodesBefore(
        IGraph<string, TNodeData, object> graph,
        string targetKey, List<NodeTemplate> nodesToInsert);

    // 删除操作（事务）
    public void DeleteNode(
        IGraph<string, TNodeData, object> graph,
        string nodeKey, bool reconnectNeighbors = true);

    // 替换操作（事务）
    public void ReplaceNode(
        IGraph<string, TNodeData, object> graph,
        string oldNodeKey, NodeTemplate newNode,
        bool preserveConnections = true);

    // 重定向操作（事务）
    public void RedirectEdge(
        IGraph<string, TNodeData, object> graph,
        string fromNodeKey, string newTargetKey);
}
```

8.3 链 → 图（ChainToGraph）

```csharp
var bridge = new GraphFoundationBridge<string, NodeConfig, object>(context);
var graph = bridge.ChainToGraph(new[] { chainInfo });
```

转换逻辑：
1. 从 `AllNodes` 列表顺序添加所有节点
2. 优先从 `nextNodeConfig.NodeKey` 创建边（兼容已持久化的数据）
3. 若 NodeKey 缺失，回退到 `AllNodes` 列表中的下一个节点的 Callkey

8.4 图 → 链（GraphToChain）

```csharp
var result = bridge.GraphToChain(updatedGraph, originalChain);
```

转换逻辑：
1. 从图中找到入度为 0 的节点（链头）
2. 沿 `GetOutgoingEdges` 遍历到链尾
3. 自动回填 `nextNodeConfig.NodeKey` 作为序列化缓存
4. 处理孤立节点（补到结果末尾）

8.5 运行时查询替代

所有读取 `node.nextNodeConfig.NodeKey` 的地方改为查询图：

```csharp
// 旧代码（读取平行指针 — 已标记 [Obsolete]）
var nextKey = node.nextNodeConfig.NodeKey;

// 新代码（通过桥接层查询图边）
var nextKey = GraphFoundationBridge<string, NodeConfig, object>
    .GetNextNodeKey(graph, node.basicConfig.Callkey);

// 或直接查询图 API
var nextKey = graph.GetOutgoingEdges(node.basicConfig.Callkey)
    .FirstOrDefault().Target?.ToString();
```

8.6 事务保证

所有复合操作（插入+重连/删除+桥接/替换+边重建）均使用**单事务**提交：

```csharp
using var tx = graph.BeginTransaction();
tx.Enqueue(new AddNodeCommand(...));
tx.Enqueue(new AddEdgeCommand(...));
tx.Enqueue(new RemoveEdgeCommand(...));
tx.Commit();  // 自动发布 GraphEvent
```

事务保证：
- **原子性**：所有操作全部成功或全部回滚
- **事件驱动**：`Commit()` 自动发布 `GraphEvent`，其他组件可订阅 `EdgeAddedEvent` / `EdgeRemovedEvent` 等
- **读锁安全**：边信息读取在事务外完成，避免读写锁递归
9. 最佳实践
9.1 定位方式选择
场景	推荐定位	原因
原型开发	Callkey	快速直接
生产环境	Symbol	抗变更、语义清晰
批量操作	Symbol + All	一次匹配全部
精细控制	Callkey	精确定位到特定实例
9.2 Symbol 命名规范
使用帕斯卡命名法：AIGenerateImage、DataProcess

反映节点业务角色而非具体实例

同一类型节点使用相同 Symbol

9.3 更新前后验证
csharp
var validation = DirectiveValidator.Validate(directive);
if (!validation.IsValid)
    throw new InvalidOperationException(validation.ErrorMessage);

var result = GraphWorkflowUpdater.ApplyGraphUpdate(chain, new[] { directive });
10. 实现文件索引
组件	文件	命名空间
更新指令定义	Integration.cs	WorkFlow.Porgram.WorkFlowMethod.DynamicUpdate
图转换器	Integration.cs	WorkFlow.Porgram.WorkFlowMethod.DynamicUpdate.Transformers
图更新执行器	Integration.cs	WorkFlow.Porgram.WorkFlowMethod.DynamicUpdate
节点配置定义	OrchestrationConnect.cs	OrchestrationConnect.Config
统一桥接层 (GraphFoundationBridge)	OrchGraph.cs	OrchestrationConnect.GraphBridge
Symbol 仓库	OrchGraph.cs	OrchestrationConnect.Deweight
用户层工厂	OrchGraph.cs	OrchestrationConnect.Updates
配置工厂	WrokFlow_Script.cs	WorkFlow
Placeholder 系统	GraphKey.cs	Graphkey.Core

> **注意**：`GraphAdapter` 和 `IGraphAdapter` 已删除，统一由 `GraphFoundationBridge` 替代。`GraphChainBridge` 仍然存在但不再继承 `IGraphAdapter`。
