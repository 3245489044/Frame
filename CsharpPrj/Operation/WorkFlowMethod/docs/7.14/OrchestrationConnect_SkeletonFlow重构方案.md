# OrchestrationConnect 基于 SkeletonFlow 的重构方案

## 一、现状分析

### 1.1 OrchestrationConnect 文件结构

`OrchestrationConnect.cs` 是一个 3087 行的巨型文件，包含多个命名空间和功能模块：

| 命名空间 | 行数范围 | 功能 | 依赖关系 |
|---------|---------|------|---------|
| `OrchestrationConnect.Config` | 1-500 | 配置类型（NodeConfig、ChainInfo、WorkflowConnection 等） | 被 GraphFramework/ 中5个文件引用 |
| `OrchestrationConnect.Content` | 501-800 | 内容管理（WorkflowStorageManager、WorkflowTools 等） | 被 WrokFlow_Script.cs 引用 |
| `OrchestrationConnect.Updates` | 801-1200 | 更新管理（WorkFlowUpdate、ChainConnectionBuilder 等） | 被 WrokFlow_Script.cs 引用 |
| `OrchestrationConnect.IdManagement` | 1201-1500 | ID 管理（IdServices、NodeRegistry 等） | 内部引用 |
| `OrchestrationConnect.GraphBridge` | 1501-2000 | 图桥接（GraphFoundationBridge、GraphExtensions 等） | 被 GraphFramework/Transformers/ 引用 |
| `OrchestrationConnect.Deweight` | 2001-2500 | 去重/标准化 | 内部引用 |
| `OrchestrationConnect.Key` | 2501-2800 | 键管理 | 内部引用 |
| `OrchestrationConnect.Content.Context` | 2801-3087 | 上下文管理（WorkflowRuntimeContext、WorkflowContext 等） | 被多个文件引用 |

### 1.2 外部引用关系

```
OrchestrationConnect.cs
  ├─ 被 GraphFramework/ 中5个文件引用
  │   ├─ GraphFramework/Adapters/GraphAdapter.cs
  │   ├─ GraphFramework/Directives/GraphUpdateDirective.cs
  │   ├─ GraphFramework/Models/NodeDefinition.cs
  │   ├─ GraphFramework/Services/GraphBuilder.cs
  │   └─ GraphFramework/Transformers/GraphTransformers.cs
  ├─ 被 WrokFlow_Script.cs 引用（using static + using 别名）
  ├─ 被 Orchestration/ 中其他文件引用
  │   ├─ OrchestrationFramework.cs
  │   ├─ OrchGraph.cs
  │   └─ WorkflowCreateModules.cs
  └─ 被 ExecutionEngine/ 中多个文件引用
```

### 1.3 核心问题

1. **文件过大**：3087 行，包含 8 个命名空间，难以维护
2. **命名空间混乱**：多个命名空间定义在同一个文件中
3. **与新框架重复**：部分功能已被新框架（GraphFramework/、CentralPipeline/、Skeletons/）替代
4. **交叉引用**：旧代码之间存在复杂的依赖链

---

## 二、新框架目标架构

### 2.1 新框架目录结构

```
WorkFlowMethod/
  ├─ CentralPipeline/          # 中央管道（工作流入口）
  │   ├─ WorkflowCentralPipeline.cs  # 管道引擎
  │   ├─ WorkflowContext.cs          # 统一上下文
  │   ├─ PipelineStages.cs           # 管道阶段定义
  │   ├─ SkeletonRegistry.cs         # Skeleton 注册表
  │   └─ SkeletonFactory.cs          # Skeleton 工厂
  ├─ GraphFramework/           # 图框架（图数据模型）
  │   ├─ Models/               # 图模型（NodeDefinition、EdgeDefinition、WorkflowGraph）
  │   ├─ Services/             # 图服务（GraphBuilder、GraphValidator、GraphUpdateService）
  │   ├─ Directives/           # 图更新指令（GraphUpdateDirective 体系）
  │   ├─ Snapshots/            # 图快照（GraphSnapshot、GraphDiff）
  │   ├─ Transformers/         # 图转换器
  │   └─ Adapters/             # 图适配器
  ├─ Execution/                # 执行引擎
  │   ├─ Engine/               # 执行引擎核心
  │   ├─ Session/              # 执行会话
  │   └─ Data/                 # 数据上下文
  └─ Skeletons/                # Skeleton 定义
      ├─ NodeProcess/          # 节点处理 Skeleton
      ├─ GraphDefinition/      # 图定义 Skeleton
      ├─ UpdateCheck/          # 更新检查 Skeleton
      ├─ PostProcess/          # 后处理 Skeleton
      └─ WorkflowProcessor/    # 工作流处理器 Skeleton
```

### 2.2 数据流架构

```
WorkflowCentralPipeline
  │
  ├─ Stage: GraphDefinition
  │   └─ GraphDefinitionSkeleton → 构建/加载图定义
  │
  ├─ Stage: NodeProcess
  │   └─ NodeProcessSkeleton → 处理单个节点
  │       ├─ Step1: GetConfig → 获取输入配置
  │       ├─ Step2: ExecuteFunc → 执行节点函数
  │       ├─ Step3: AddNode → 记录调用帧
  │       └─ Step4: NextNode → 解析下一节点
  │
  ├─ Stage: UpdateCheck
  │   └─ GraphUpdateCheckSkeleton → 检查图更新
  │
  ├─ Stage: PostProcess
  │   └─ MetricsCollectSkeleton → 收集指标
  │
  └─ WorkflowLoopSkeleton → 循环执行节点
```

---

## 三、重构方案

### 3.1 总体策略

**分拆迁移**：将 `OrchestrationConnect.cs` 中的 8 个命名空间按功能拆分迁移到新框架的对应位置。

```
OrchestrationConnect.Config       →  GraphFramework/Models/
OrchestrationConnect.Content      →  GraphFramework/Services/
OrchestrationConnect.Updates      →  GraphFramework/Directives/
OrchestrationConnect.IdManagement →  GraphFramework/Services/
OrchestrationConnect.GraphBridge  →  GraphFramework/Adapters/（部分已存在）
OrchestrationConnect.Deweight     →  GraphFramework/Services/
OrchestrationConnect.Key          →  GraphFramework/Services/
OrchestrationConnect.Content.Context → CentralPipeline/WorkflowContext.cs（已存在）
```

### 3.2 详细迁移映射

#### 3.2.1 Config 命名空间 → GraphFramework/Models/

| 旧类型 | 新位置 | 迁移方式 |
|-------|--------|---------|
| `NodeConfig` | `GraphFramework/Models/NodeConfig.cs` | 新建文件，保留核心属性 |
| `ChainInfo<TNode>` | `GraphFramework/Models/ChainInfo.cs` | 新建文件，保留核心属性 |
| `WorkflowConnection<TKey, TData>` | `GraphFramework/Models/WorkflowConnection.cs` | 新建文件 |
| `ConnectionPositionInfo<T>` | `GraphFramework/Models/ConnectionPositionInfo.cs` | 新建文件 |
| `ConnectNodeConfig` | `GraphFramework/Models/ConnectNodeConfig.cs` | 新建文件 |
| `UniversalNode` | `GraphFramework/Models/UniversalNode.cs` | 新建文件 |
| `ChainConfig` | `GraphFramework/Models/ChainConfig.cs` | 新建文件 |
| `NodeConfig.BasicConfig` | `GraphFramework/Models/NodeBasicConfig.cs` | 新建文件 |
| `NodeConfig.NextNodeConfig` | `GraphFramework/Models/NodeNextConfig.cs` | 新建文件 |
| `NodeConfig.FuncPipelineConfig` | `GraphFramework/Models/NodeFuncConfig.cs` | 新建文件 |

**依赖关系**：
- `NodeDefinition.cs` 当前引用 `NodeConfig` → 改为引用新 `NodeConfig`
- `GraphBuilder.cs` 当前引用 `ChainInfo<NodeConfig>` → 改为引用新类型
- `GraphAdapter.cs` 当前引用 `ChainInfo<NodeConfig>`、`UniversalNode` → 改为引用新类型

#### 3.2.2 Content 命名空间 → GraphFramework/Services/

| 旧类型 | 新位置 | 迁移方式 |
|-------|--------|---------|
| `WorkflowStorageManager` | `GraphFramework/Services/WorkflowStorageManager.cs` | 新建文件 |
| `WorkflowTools` | `GraphFramework/Services/WorkflowTools.cs` | 新建文件 |
| `WorkflowMediator` | `GraphFramework/Services/WorkflowMediator.cs` | 新建文件 |
| `WorkflowRuntimeContext` | `CentralPipeline/WorkflowContext.cs` | ✅ 已存在 |
| `WorkflowRuntimeContextManager` | `CentralPipeline/WorkflowContext.cs` | ✅ 已存在 |

#### 3.2.3 Updates 命名空间 → GraphFramework/Directives/

| 旧类型 | 新位置 | 迁移方式 |
|-------|--------|---------|
| `WorkFlowUpdate` | `GraphFramework/Directives/WorkFlowUpdate.cs` | 新建文件 |
| `ChainConnectionBuilder` | `GraphFramework/Directives/ChainConnectionBuilder.cs` | 新建文件 |
| `WorkflowMessageHelper` | `GraphFramework/Directives/WorkflowMessageHelper.cs` | 新建文件 |
| `NodeManager<TNode>` | `GraphFramework/Services/NodeManager.cs` | 新建文件 |
| `GraphUpdateDirective` 体系 | `GraphFramework/Directives/GraphUpdateDirective.cs` | ✅ 已存在 |

#### 3.2.4 IdManagement 命名空间 → GraphFramework/Services/

| 旧类型 | 新位置 | 迁移方式 |
|-------|--------|---------|
| `IdServices` | `GraphFramework/Services/IdServices.cs` | 新建文件 |
| `NodeRegistry` | `GraphFramework/Services/NodeRegistry.cs` | 新建文件 |
| `INodeCallkeyGenerator` | `GraphFramework/Services/INodeCallkeyGenerator.cs` | 新建文件 |
| `DefaultNodeCallkeyGenerator` | `GraphFramework/Services/DefaultNodeCallkeyGenerator.cs` | 新建文件 |

#### 3.2.5 GraphBridge 命名空间 → GraphFramework/Adapters/

| 旧类型 | 新位置 | 迁移方式 |
|-------|--------|---------|
| `GraphFoundationBridge<TKey, TNode, TEdge>` | `GraphFramework/Adapters/GraphFoundationBridge.cs` | 新建文件 |
| `GraphExtensions` | `GraphFramework/Adapters/GraphExtensions.cs` | 新建文件 |
| `GraphWorkflowConnector` | `GraphFramework/Services/GraphWorkflowConnector.cs` | 新建文件 |
| `IGraphPipelineStep<TKey, TNode, TEdge>` | `GraphFramework/Services/IGraphPipelineStep.cs` | 新建文件 |
| `GraphChainBridge<TKey, TNode, TEdge>` | `GraphFramework/Adapters/GraphChainBridge.cs` | 新建文件 |

#### 3.2.6 Deweight 命名空间 → GraphFramework/Services/

| 旧类型 | 新位置 | 迁移方式 |
|-------|--------|---------|
| `DeduplicationProcessor` | `GraphFramework/Services/DeduplicationProcessor.cs` | 新建文件 |
| `NormalizationEngine` | `GraphFramework/Services/NormalizationEngine.cs` | 新建文件 |

#### 3.2.7 Key 命名空间 → GraphFramework/Services/

| 旧类型 | 新位置 | 迁移方式 |
|-------|--------|---------|
| `KeyManager` | `GraphFramework/Services/KeyManager.cs` | 新建文件 |

#### 3.2.8 Content.Context 命名空间 → CentralPipeline/

| 旧类型 | 新位置 | 迁移方式 |
|-------|--------|---------|
| `WorkflowContext` | `CentralPipeline/WorkflowContext.cs` | ✅ 已存在 |
| `WorkflowRuntimeContext` | `CentralPipeline/WorkflowContext.cs` | ✅ 已存在 |
| `WorkflowRuntimeContextManager` | `CentralPipeline/WorkflowContext.cs` | ✅ 已存在 |
| `EcsSkeletonContext` | `CentralPipeline/EcsSkeletonContext.cs` | 新建文件 |
| `ContextAwareSystem` | `CentralPipeline/ContextAwareSystem.cs` | 新建文件 |

---

## 四、分步实施计划

### ✅ Step 1: 创建 GraphFramework/Models/ 中的新类型（已完成）

**目标**：将 `OrchestrationConnect.Config` 中的核心类型迁移到 `GraphFramework/Models/`

**已创建的文件**：
1. `GraphFramework/Models/NodeConfig.cs` - 节点配置 ✅
2. `GraphFramework/Models/ChainInfo.cs` - 链信息 ✅
3. `GraphFramework/Models/WorkflowConnection.cs` - 工作流连接 ✅
4. `GraphFramework/Models/ConnectionPositionInfo.cs` - 连接位置信息 ✅
5. `GraphFramework/Models/ConnectNodeConfig.cs` - 连接节点配置 ✅
6. `GraphFramework/Models/UniversalNode.cs` - 通用节点 ✅
7. `GraphFramework/Models/ChainConfig.cs` - 链配置 ✅

**验证**：`dotnet build` 通过 ✅

### Step 2: 创建 GraphFramework/Services/ 中的新类型

**目标**：将 `OrchestrationConnect.Content`、`IdManagement`、`Deweight`、`Key` 中的服务迁移

**新建文件**：
1. `GraphFramework/Services/WorkflowStorageManager.cs`
2. `GraphFramework/Services/WorkflowTools.cs`
3. `GraphFramework/Services/WorkflowMediator.cs`
4. `GraphFramework/Services/IdServices.cs`
5. `GraphFramework/Services/NodeRegistry.cs`
6. `GraphFramework/Services/INodeCallkeyGenerator.cs`
7. `GraphFramework/Services/DefaultNodeCallkeyGenerator.cs`
8. `GraphFramework/Services/DeduplicationProcessor.cs`
9. `GraphFramework/Services/NormalizationEngine.cs`
10. `GraphFramework/Services/KeyManager.cs`

**验证**：`dotnet build` 通过

### Step 3: 创建 GraphFramework/Directives/ 中的新类型

**目标**：将 `OrchestrationConnect.Updates` 中的更新逻辑迁移

**新建文件**：
1. `GraphFramework/Directives/WorkFlowUpdate.cs`
2. `GraphFramework/Directives/ChainConnectionBuilder.cs`
3. `GraphFramework/Directives/WorkflowMessageHelper.cs`
4. `GraphFramework/Services/NodeManager.cs`

**验证**：`dotnet build` 通过

### Step 4: 创建 GraphFramework/Adapters/ 中的新类型

**目标**：将 `OrchestrationConnect.GraphBridge` 中的桥接逻辑迁移

**新建文件**：
1. `GraphFramework/Adapters/GraphFoundationBridge.cs`
2. `GraphFramework/Adapters/GraphExtensions.cs`
3. `GraphFramework/Services/GraphWorkflowConnector.cs`
4. `GraphFramework/Services/IGraphPipelineStep.cs`
5. `GraphFramework/Adapters/GraphChainBridge.cs`

**验证**：`dotnet build` 通过

### Step 5: 更新所有引用到新类型

**目标**：将所有对 `OrchestrationConnect.*` 的引用改为对新框架类型的引用

**需要更新的文件**：
1. `GraphFramework/Adapters/GraphAdapter.cs` - 更新 `ChainInfo<NodeConfig>`、`NodeConfig`、`UniversalNode` 引用
2. `GraphFramework/Directives/GraphUpdateDirective.cs` - 更新 `NodeConfig` 引用
3. `GraphFramework/Models/NodeDefinition.cs` - 更新 `NodeConfig` 引用
4. `GraphFramework/Services/GraphBuilder.cs` - 更新 `ChainInfo<NodeConfig>`、`NodeConfig`、`UniversalNode` 引用
5. `GraphFramework/Transformers/GraphTransformers.cs` - 更新 `NodeConfig`、`GraphFoundationBridge` 引用
6. `WrokFlow_Script.cs` - 更新 `ChainInfo`、`ConnectionPositionInfo` 引用
7. `Orchestration/OrchestrationFramework.cs` - 更新 `ChainInfo`、`NodeConfig` 引用
8. `Orchestration/OrchGraph.cs` - 更新所有 `OrchestrationConnect.*` 引用
9. `Orchestration/WorkflowCreateModules.cs` - 更新所有 `OrchestrationConnect.*` 引用
10. `Orchestration/RefactoredOrchestration.cs` - 更新 `OrchestrationConnect` 引用
11. `Orchestration/RefactoredWorkflowCreateModules.cs` - 更新 `OrchestrationConnect.*` 引用

**验证**：`dotnet build` 通过

### Step 6: 删除旧文件

**目标**：删除已迁移完毕的旧文件

**删除文件**：
1. `Orchestration/OrchestrationConnect.cs` - 所有命名空间已迁移
2. `Orchestration/OrchestrationFramework.cs` - 功能已迁移到 CentralPipeline/
3. `Orchestration/OrchGraph.cs` - 功能已迁移到 GraphFramework/
4. `Orchestration/RefactoredOrchestration.cs` - 功能已迁移到 CentralPipeline/
5. `Orchestration/RefactoredWorkflowCreateModules.cs` - 功能已迁移
6. `Orchestration/WorkflowCreateModules.cs` - 功能已迁移

**验证**：`dotnet build` 通过

---

## 五、关键类型迁移示例

### 5.1 NodeConfig 迁移

**旧代码**（`OrchestrationConnect.cs` 中）：
```csharp
namespace OrchestrationConnect.Config
{
    public class NodeConfig
    {
        public string Symbol { get; set; }
        public string Label { get; set; }
        public BasicConfig basicConfig { get; set; }
        public NextNodeConfig nextNodeConfig { get; set; }
        public FuncPipelineConfig funcPipelineConfig { get; set; }
        public GetStrConfig getStrConfig { get; set; }
        public AddNodeConfig addNodeConfig { get; set; }
        
        public class BasicConfig { public string Callkey { get; set; } }
        public class NextNodeConfig { public string NextNodeKey { get; set; } }
        public class FuncPipelineConfig { public string FuncName { get; set; } }
    }
}
```

**新代码**（`GraphFramework/Models/NodeConfig.cs`）：
```csharp
namespace WorkFlowMethod.GraphFramework.Models
{
    /// <summary>
    /// 节点配置 - 从 OrchestrationConnect.Config 迁移
    /// </summary>
    public class NodeConfig
    {
        public string Symbol { get; set; }
        public string Label { get; set; }
        public NodeBasicConfig basicConfig { get; set; }
        public NodeNextConfig nextNodeConfig { get; set; }
        public NodeFuncConfig funcPipelineConfig { get; set; }
        public GetStrConfig getStrConfig { get; set; }
        public AddNodeConfig addNodeConfig { get; set; }
    }

    public class NodeBasicConfig { public string Callkey { get; set; } }
    public class NodeNextConfig { public string NextNodeKey { get; set; } }
    public class NodeFuncConfig { public string FuncName { get; set; } }
}
```

### 5.2 ChainInfo 迁移

**旧代码**（`OrchestrationConnect.cs` 中）：
```csharp
namespace OrchestrationConnect.Config
{
    public class ChainInfo<TNode> where TNode : NodeConfig
    {
        public TNode StartNode { get; set; }
        public TNode EndNode { get; set; }
        public List<TNode> AllNodes { get; set; }
    }
}
```

**新代码**（`GraphFramework/Models/ChainInfo.cs`）：
```csharp
namespace WorkFlowMethod.GraphFramework.Models
{
    /// <summary>
    /// 链信息 - 从 OrchestrationConnect.Config 迁移
    /// </summary>
    public class ChainInfo<TNode> where TNode : NodeConfig
    {
        public TNode StartNode { get; set; }
        public TNode EndNode { get; set; }
        public List<TNode> AllNodes { get; set; }
    }
}
```

### 5.3 GraphFoundationBridge 迁移

**旧代码**（`OrchestrationConnect.GraphBridge` 中）：
```csharp
namespace OrchestrationConnect.GraphBridge
{
    public class GraphFoundationBridge<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : NodeConfig
    {
        private readonly IContext _context;
        
        public GraphFoundationBridge(IContext context) { _context = context; }
        
        public IGraph<string, TNodeData, object> ChainToGraph(ChainInfo<TNodeData> chain) { ... }
        public IReadOnlyList<ChainInfo<TNodeData>> ExtractComponents(IGraph<string, TNodeData, object> graph) { ... }
        public void InsertNodesAfter(IGraph<string, TNodeData, object> graph, string sourceKey, List<NodeTemplate> nodes) { ... }
        public void InsertNodesBefore(IGraph<string, TNodeData, object> graph, string targetKey, List<NodeTemplate> nodes) { ... }
        public void DeleteNode(IGraph<string, TNodeData, object> graph, string nodeKey, bool reconnect) { ... }
        public void ReplaceNode(IGraph<string, TNodeData, object> graph, string oldKey, NodeTemplate newNode, bool preserveSymbol) { ... }
        public void RedirectEdge(IGraph<string, TNodeData, object> graph, string fromKey, string newTargetKey) { ... }
    }
}
```

**新代码**（`GraphFramework/Adapters/GraphFoundationBridge.cs`）：
```csharp
namespace WorkFlowMethod.GraphFramework.Adapters
{
    /// <summary>
    /// 图基础桥接器 - 从 OrchestrationConnect.GraphBridge 迁移
    /// 提供 ChainInfo ↔ IGraph 的双向转换和增量更新操作
    /// </summary>
    public class GraphFoundationBridge<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : Models.NodeConfig
    {
        private readonly IContext _context;
        
        public GraphFoundationBridge(IContext context) { _context = context; }
        
        // ... 保留所有方法，更新类型引用
    }
}
```

---

## 六、依赖关系与风险

### 6.1 依赖关系图

```
Step 1 (Models) ──→ Step 2 (Services) ──→ Step 3 (Directives)
       │                                        │
       └──────────→ Step 4 (Adapters) ←─────────┘
                            │
                            ↓
                    Step 5 (更新引用)
                            │
                            ↓
                    Step 6 (删除旧文件)
```

### 6.2 风险分析

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| `NodeConfig` 类型被大量文件引用，改名可能导致编译错误 | 高 | 保留旧类型名，只改命名空间 |
| `GraphFoundationBridge` 被 `GraphTransformers.cs` 直接使用 | 中 | 先迁移桥接器，再更新引用 |
| `OrchestrationConnect.cs` 中类型之间存在内部交叉引用 | 高 | 按命名空间分批迁移，每次确保构建通过 |
| `WrokFlow_Script.cs` 中 `using static` 导入的静态方法 | 低 | 搜索确认无反射引用后删除 |
| `OrchestrationFramework.cs` 中 ECS 相关代码 | 中 | 保留 ECS 代码，只更新命名空间引用 |

### 6.3 验证清单

- [x] Step 1 完成后：`dotnet build` 通过 ✅
- [ ] Step 2 完成后：`dotnet build` 通过
- [ ] Step 3 完成后：`dotnet build` 通过
- [ ] Step 4 完成后：`dotnet build` 通过
- [ ] Step 5 完成后：`dotnet build` 通过
- [ ] Step 6 完成后：`dotnet build` 通过
- [ ] 最终：无 `OrchestrationConnect` 命名空间引用残留
- [ ] 最终：完整解决方案构建通过

---

## 七、总结

本方案将 `OrchestrationConnect.cs`（3087 行）按功能拆分为 6 个步骤迁移到新框架：

1. **Models**（~500 行）→ `GraphFramework/Models/`
2. **Services**（~600 行）→ `GraphFramework/Services/`
3. **Directives**（~400 行）→ `GraphFramework/Directives/`
4. **Adapters**（~500 行）→ `GraphFramework/Adapters/`
5. **更新引用**（更新 11 个文件）
6. **删除旧文件**（删除 6 个文件）

每个步骤都确保 `dotnet build` 通过，最终消除所有对 `OrchestrationConnect` 命名空间的引用。
