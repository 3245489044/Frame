# API参考与数学工具

## 1. API参考

### 1.1 核心类和方法

#### Application 类

**命名空间：** `FrameManage.Core`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `InitializeAsync()` | 初始化应用程序 | 无 | `Task` |
| `RegisterWorkflowAsync(string workflowId, Func<object, Task<object>> workflowFunc)` | 注册工作流函数 | workflowId: 工作流ID<br>workflowFunc: 工作流执行函数 | `Task` |
| `ExecuteWorkflowAsync(string workflowId, object input)` | 执行指定工作流 | workflowId: 工作流ID<br>input: 输入数据 | `Task<object>` |
| `GetAllWorkflowsAsync()` | 获取所有可用工作流 | 无 | `Task<IEnumerable<string>>` |
| `RegisterMetaWorkflowAsync(string metaWorkflowId, MetaWorkflowDefinition metaWorkflow)` | 注册元工作流 | metaWorkflowId: 元工作流ID<br>metaWorkflow: 元工作流定义 | `Task` |
| `ExecuteMetaWorkflowAsync(string metaWorkflowId, object input)` | 执行指定元工作流 | metaWorkflowId: 元工作流ID<br>input: 输入数据 | `Task<object>` |
| `GetAllMetaWorkflowsAsync()` | 获取所有可用元工作流 | 无 | `Task<IEnumerable<string>>` |

#### MetaWorkflowDefinition 类

**命名空间：** `FrameManage.MetaWorkflow`

**主要属性：**

| 属性名 | 类型 | 描述 |
|-------|------|------|
| `Id` | `string` | 元工作流ID |
| `Name` | `string` | 元工作流名称 |
| `Description` | `string` | 元工作流描述 |
| `Nodes` | `List<MetaWorkflowNode>` | 元工作流节点列表 |
| `DataProcessingConfig` | `DataProcessingConfig` | 数据处理配置 |
| `Parameters` | `Dictionary<string, object>` | 元工作流参数 |

#### MetaWorkflowNode 类

**命名空间：** `FrameManage.MetaWorkflow`

**主要属性：**

| 属性名 | 类型 | 描述 |
|-------|------|------|
| `Id` | `string` | 节点ID |
| `Name` | `string` | 节点名称 |
| `FrameworkName` | `string` | 框架名称 |
| `WorkflowId` | `string` | 工作流ID |
| `Parameters` | `Dictionary<string, object>` | 节点参数 |
| `Sequence` | `int` | 节点执行序列号 |
| `PreviousSequences` | `List<int>` | 前置依赖序列号列表 |
| `PreviousNodeIds` | `List<string>` | 前置依赖节点ID列表 |

#### MetaWorkflowManager 类

**命名空间：** `FrameManage.MetaWorkflow`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `RegisterMetaWorkflowAsync(string metaWorkflowId, MetaWorkflowDefinition metaWorkflow)` | 注册元工作流 | metaWorkflowId: 元工作流ID<br>metaWorkflow: 元工作流定义 | `Task` |
| `GetMetaWorkflowAsync(string metaWorkflowId)` | 获取元工作流 | metaWorkflowId: 元工作流ID | `Task<MetaWorkflowDefinition>` |
| `ExecuteMetaWorkflowAsync(string metaWorkflowId, object input)` | 执行元工作流 | metaWorkflowId: 元工作流ID<br>input: 输入数据 | `Task<object>` |
| `GetAllMetaWorkflowsAsync()` | 获取所有元工作流 | 无 | `Task<IEnumerable<string>>` |
| `CalculateReferenceDiffAsync(string moduleId)` | 计算引用差值 | moduleId: 模块ID | `Task<ReferenceDiffResult>` |
| `SyncModuleReferencesToGraphAsync(string moduleId, IEnumerable<string> references)` | 同步模块引用到图数据库 | moduleId: 模块ID<br>references: 引用列表 | `Task` |
| `TestReferenceDiffCalculationAsync(string moduleId)` | 测试引用差值计算 | moduleId: 模块ID | `Task<bool>` |

#### WorkflowRuntimeManager 类

**命名空间：** `FrameManage.Core`

**主要属性和方法：**

| 成员名 | 类型 | 描述 |
|-------|------|------|
| `Instance` | `static WorkflowRuntimeManager` | 单例实例 |
| `RuntimeEvent` | `event EventHandler<WorkflowRuntimeEventArgs>` | 运行时事件 |
| `StartWorkflow(string workflowType, Dictionary<string, object> parameters)` | 方法 | 启动工作流 |
| `GetWorkflowStatus(Guid runId)` | 方法 | 获取工作流状态 |
| `CancelWorkflow(Guid runId)` | 方法 | 取消工作流 |

#### WorkflowBuilder 类

**命名空间：** `FrameManage.Core`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `BuildFromStrings(params string[] nodeStrings)` | 静态方法 | 从字符串定义构建工作流 | `WorkflowDefinition` |
| `GetSummary(WorkflowDefinition workflow)` | 静态方法 | 获取工作流摘要 | `string` |

### 1.2 数据处理

#### DataProcessingConfig 类

**命名空间：** `FrameManage.DataProcessing`

**主要属性：**

| 属性名 | 类型 | 描述 |
|-------|------|------|
| `EnableDataProcessing` | `bool` | 是否启用数据处理 |
| `Position` | `DataProcessingPosition` | 数据处理位置 |
| `ProcessorType` | `string` | 处理器类型 |
| `ProcessorParameters` | `Dictionary<string, object>` | 处理器参数 |

#### DataProcessingPosition 枚举

**命名空间：** `FrameManage.DataProcessing`

**枚举值：**

| 值 | 描述 |
|-----|------|
| `BeforeWorkflow` | 工作流开始前 |
| `AfterWorkflow` | 工作流结束后 |
| `BeforeNode` | 每个节点执行前 |
| `AfterNode` | 每个节点执行后 |

### 1.3 框架适配层

#### IFrameworkAdapter 接口

**命名空间：** `FrameManage.FrameworkAdapter`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `InitializeAsync()` | 初始化适配器 | 无 | `Task` |
| `ExecuteWorkflowAsync(string workflowId, object input)` | 执行工作流 | workflowId: 工作流ID<br>input: 输入数据 | `Task<object>` |
| `GetAllWorkflowsAsync()` | 获取所有工作流 | 无 | `Task<IEnumerable<string>>` |

#### FrameworkAdapterFactory 类

**命名空间：** `FrameManage.FrameworkAdapter`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `CreateAdapter(string frameworkName)` | 静态方法 | 创建框架适配器 | `IFrameworkAdapter` |

#### FrameworkAdapterManager 类

**命名空间：** `FrameManage.FrameworkAdapter`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `RegisterAdapter(string frameworkName, IFrameworkAdapter adapter)` | 方法 | 注册框架适配器 | `void` |
| `GetAdapter(string frameworkName)` | 方法 | 获取框架适配器 | `IFrameworkAdapter` |
| `GetAllAdapters()` | 方法 | 获取所有框架适配器 | `IEnumerable<string>` |

### 1.4 工作流连接器

#### IWorkflowConnector 接口

**命名空间：** `FrameManage.WorkflowConnector`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `RegisterWorkflowChainAsync(string chainId, WorkflowChainDefinition chainDefinition)` | 方法 | 注册工作流链 | `Task` |
| `ExecuteWorkflowChainAsync(string chainId, object input)` | 方法 | 执行工作流链 | `Task<object>` |
| `GetAllWorkflowChainsAsync()` | 方法 | 获取所有工作流链 | `Task<IEnumerable<string>>` |

### 1.5 模块化

#### IModuleRegistry 接口

**命名空间：** `FrameManage.Common.Core`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `RegisterModuleAsync(ModuleInfo module)` | 方法 | 注册模块 | `Task` |
| `UnregisterModuleAsync(string moduleId)` | 方法 | 注销模块 | `Task` |
| `GetModuleAsync(string moduleId)` | 方法 | 获取模块信息 | `Task<ModuleInfo>` |
| `GetAllModulesAsync()` | 方法 | 获取所有模块 | `Task<IEnumerable<ModuleInfo>>` |
| `DiscoverModulesAsync(string searchPath)` | 方法 | 发现模块 | `Task` |
| `LoadModuleAsync(string moduleId)` | 方法 | 加载模块 | `Task` |
| `UnloadModuleAsync(string moduleId)` | 方法 | 卸载模块 | `Task` |

#### ModuleInfo 类

**命名空间：** `FrameManage.Common.Core`

**主要属性：**

| 属性名 | 类型 | 描述 |
|-------|------|------|
| `Id` | `string` | 模块ID |
| `Name` | `string` | 模块名称 |
| `Version` | `string` | 模块版本 |
| `Description` | `string` | 模块描述 |
| `Type` | `ModuleType` | 模块类型 |
| `Path` | `string` | 模块路径 |
| `Dependencies` | `List<ModuleDependency>` | 模块依赖 |
| `Status` | `ModuleStatus` | 模块状态 |

#### ModuleType 枚举

**命名空间：** `FrameManage.Common.Core`

**枚举值：**

| 值 | 描述 |
|-----|------|
| `Framework` | 框架模块 |
| `Workflow` | 工作流模块 |
| `DataProcessing` | 数据处理模块 |
| `Other` | 其他模块 |

#### ModuleDependency 类

**命名空间：** `FrameManage.Common.Core`

**主要属性：**

| 属性名 | 类型 | 描述 |
|-------|------|------|
| `ModuleId` | `string` | 依赖模块ID |
| `VersionRange` | `string` | 依赖版本范围 |

#### ModuleStatus 枚举

**命名空间：** `FrameManage.Common.Core`

**枚举值：**

| 值 | 描述 |
|-----|------|
| `Unloaded` | 未加载 |
| `Loading` | 加载中 |
| `Loaded` | 已加载 |
| `Failed` | 加载失败 |

#### DefaultModuleRegistry 类

**命名空间：** `FrameManage.Common.Core`

**主要方法：** 实现了 IModuleRegistry 接口的所有方法

**使用示例：**

```csharp
// 创建模块注册器
var dataStorage = new DataStorage();
var moduleRegistry = new DefaultModuleRegistry(dataStorage);

// 注册模块
var moduleInfo = new ModuleInfo
{
    Id = "module1",
    Name = "Test Module",
    Version = "1.0.0",
    Description = "Test module",
    Type = ModuleType.Workflow,
    Path = "C:\\Modules\\TestModule.dll"
};
await moduleRegistry.RegisterModuleAsync(moduleInfo);

// 加载模块
await moduleRegistry.LoadModuleAsync("module1");

// 获取所有模块
var modules = await moduleRegistry.GetAllModulesAsync();
foreach (var module in modules)
{
    Console.WriteLine($"Module: {module.Name}, Status: {module.Status}");
}

// 卸载模块
await moduleRegistry.UnloadModuleAsync("module1");

// 发现模块
await moduleRegistry.DiscoverModulesAsync("C:\\Modules");
```

### 1.6 引用关系处理

#### IReferenceManager 接口

**命名空间：** `FrameManage.Common.Core`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `AddReferenceAsync(Reference reference)` | 方法 | 添加引用 | `Task` |
| `RemoveReferenceAsync(Reference reference)` | 方法 | 移除引用 | `Task` |
| `GetReferencesAsync(string sourceId, string sourceType)` | 方法 | 获取源的引用 | `Task<IEnumerable<Reference>>` |
| `GetReferencesToAsync(string targetId, string targetType)` | 方法 | 获取指向目标的引用 | `Task<IEnumerable<Reference>>` |
| `GetAllReferencesAsync()` | 方法 | 获取所有引用 | `Task<IEnumerable<Reference>>` |

#### IWorkflowReferenceManager 接口

**命名空间：** `FrameManage.Common.Core`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `RegisterWorkflowReferencesAsync(string workflowId, string workflowType, object workflowData)` | 方法 | 注册工作流引用 | `Task` |
| `ClearWorkflowReferencesAsync(string workflowId, string workflowType)` | 方法 | 清除工作流引用 | `Task` |
| `GetWorkflowReferencesAsync(string workflowId, string workflowType)` | 方法 | 获取工作流引用 | `Task<IEnumerable<Reference>>` |
| `SubscribeToResource(string resourceType, Action<ResourceChangeEventArgs> handler)` | 方法 | 订阅资源变更 | `IDisposable` |
| `SubscribeToRelationship(string relationshipType, Action<RelationshipChangeEventArgs> handler)` | 方法 | 订阅关系变更 | `IDisposable` |

#### IIntegratedReferenceManager 接口

**命名空间：** `FrameManage.Common.Core`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `AddResourceAsync(ResourceIdentifier resource, Dictionary<string, object> properties)` | 方法 | 添加资源 | `Task` |
| `UpdateResourceAsync(ResourceIdentifier resource, Dictionary<string, object> properties)` | 方法 | 更新资源 | `Task` |
| `RemoveResourceAsync(ResourceIdentifier resource)` | 方法 | 移除资源 | `Task` |
| `GetResourceAsync(ResourceIdentifier resource)` | 方法 | 获取资源 | `Task<Dictionary<string, object>>` |
| `GetAllResourcesAsync(string resourceType)` | 方法 | 获取所有资源 | `Task<IEnumerable<ResourceIdentifier>>` |

#### IReferenceProcessManager 接口

**命名空间：** `FrameManage.Common.Core`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `ExecuteBatchAsync(IEnumerable<ReferenceOperation> operations)` | 方法 | 执行批量操作 | `Task<BatchOperationResult>` |
| `GetCachedReferencesAsync(string workflowId, string workflowType)` | 方法 | 获取缓存的引用 | `Task<IEnumerable<Reference>>` |
| `InvalidateCacheAsync(string workflowId, string workflowType)` | 方法 | 使缓存失效 | `Task` |

#### Reference 类

**命名空间：** `FrameManage.Common.Core`

**主要属性：**

| 属性名 | 类型 | 描述 |
|-------|------|------|
| `Id` | `string` | 引用ID |
| `SourceId` | `string` | 源ID |
| `SourceType` | `string` | 源类型 |
| `TargetId` | `string` | 目标ID |
| `TargetType` | `string` | 目标类型 |
| `RelationshipType` | `string` | 关系类型 |
| `Properties` | `Dictionary<string, object>` | 引用属性 |

#### ResourceIdentifier 类

**命名空间：** `FrameManage.Common.Core`

**主要属性：**

| 属性名 | 类型 | 描述 |
|-------|------|------|
| `Id` | `string` | 资源ID |
| `ResourceType` | `string` | 资源类型 |

#### ReferenceOperation 类

**命名空间：** `FrameManage.Common.Core`

**主要属性：**

| 属性名 | 类型 | 描述 |
|-------|------|------|
| `WorkflowId` | `string` | 工作流ID |
| `WorkflowType` | `string` | 工作流类型 |
| `OperationType` | `ReferenceOperationType` | 操作类型 |
| `WorkflowData` | `object` | 工作流数据 |

#### ReferenceOperationType 枚举

**命名空间：** `FrameManage.Common.Core`

**枚举值：**

| 值 | 描述 |
|-----|------|
| `Register` | 注册引用 |
| `Clear` | 清除引用 |
| `Get` | 获取引用 |

#### 使用示例

**工作流引用管理**

```csharp
// 创建引用管理器
var referenceManager = new WorkflowReferenceManager(universalReferenceManager, eventBus);

// 注册工作流引用
await referenceManager.RegisterWorkflowReferencesAsync(workflowId, "MetaWorkflow", workflowData);

// 获取工作流引用
var references = await referenceManager.GetWorkflowReferencesAsync(workflowId, "MetaWorkflow");

// 清除工作流引用
await referenceManager.ClearWorkflowReferencesAsync(workflowId, "MetaWorkflow");
```

**批量引用处理**

```csharp
// 创建引用处理管理器
var processManager = new ReferenceProcessManager(workflowReferenceManager, universalReferenceManager);

// 执行批量操作
var operations = new List<ReferenceOperation>
{
    new ReferenceOperation
    {
        WorkflowId = "workflow1",
        WorkflowType = "MetaWorkflow",
        OperationType = ReferenceOperationType.Register,
        WorkflowData = workflowData
    }
};

var result = await processManager.ExecuteBatchAsync(operations);
```

### 1.7 引用工具

#### ReferenceUtils 类

**命名空间：** `FrameManage.MetaWorkflow`

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `Parse(string reference)` | 静态方法 | 解析引用字符串 | `ResourceIdentifier` |
| `Format(ResourceIdentifier resource)` | 静态方法 | 格式化资源标识符 | `string` |
| `IsValid(string reference)` | 静态方法 | 验证引用格式 | `bool` |
| `GetType(string reference)` | 静态方法 | 获取引用类型 | `string` |
| `CompareLists(IEnumerable<string> list1, IEnumerable<string> list2)` | 静态方法 | 比较两个引用列表 | `ReferenceComparisonResult` |

## 2. 数学计算工具

### 2.1 概述

MathCalculation 模块提供了一系列数学计算工具，用于分析工作流和系统的各种属性，包括边界稳定性、系统复杂度、循环依赖检测和图算法等。这些工具类为 FrameManage 框架提供了强大的数学分析能力，帮助开发者更好地理解和优化工作流系统。

### 2.2 目录结构

```
MathCalculation/
├── BoundaryAnalysisMath.cs      # 边界分析数学工具类
├── CircularDependencyDetector.cs # 循环依赖检测器
└── GraphAlgorithmMath.cs        # 图算法数学工具类
```

### 2.3 核心类

#### BoundaryAnalysisMath

**命名空间：** `FrameManage.MathCalculation`

**功能：** 提供边界分析相关的数学计算方法，用于评估工作流边界的稳定性和系统复杂度。

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `CalculateStabilityScore` | 计算边界稳定性评分 | codeConnections: 代码连接数<br>dataConnections: 数据连接数<br>totalDependencies: 总依赖数 | `double` - 稳定性评分（0-1之间） |
| `CalculateSystemComplexity` | 计算系统复杂度 | analyses: 工作流边界分析列表 | `double` - 系统复杂度评分 |
| `CalculateDegreeCentrality` | 计算节点度中心性 | nodes: 节点字典<br>edges: 边列表 | `Dictionary<string, int>` - 节点度中心性字典 |
| `CalculateGraphIntegrityScore` | 计算图的完整性评分 | nodes: 节点字典<br>edges: 边列表 | `double` - 完整性评分（0-100之间） |
| `CalculateAveragePathLength` | 计算平均路径长度 | adjacencyList: 邻接表 | `double` - 平均路径长度 |
| `FindShortestPathLength` | 寻找最短路径长度（私有方法） | adjacencyList: 邻接表<br>start: 起始节点<br>end: 结束节点 | `int?` - 最短路径长度 |

**使用场景：**
- **边界稳定性评估**：评估工作流边界的稳定性，帮助开发者识别不稳定的边界。
- **系统复杂度分析**：分析系统的整体复杂度，为系统优化提供参考。
- **节点重要性分析**：通过度中心性计算，识别系统中重要的节点。
- **图完整性评估**：评估系统图的完整性，确保系统各部分之间的连接性。
- **路径分析**：分析系统中节点之间的路径长度，优化系统结构。

**示例代码：**

```csharp
// 计算边界稳定性评分
double stabilityScore = BoundaryAnalysisMath.CalculateStabilityScore(5, 3, 10);
Console.WriteLine($"边界稳定性评分: {stabilityScore}");

// 计算系统复杂度
List<WorkflowBoundaryAnalysis> analyses = GetWorkflowBoundaryAnalyses();
double complexity = BoundaryAnalysisMath.CalculateSystemComplexity(analyses);
Console.WriteLine($"系统复杂度: {complexity}");

// 计算节点度中心性
Dictionary<string, object> nodes = GetNodes();
List<object> edges = GetEdges();
Dictionary<string, int> centrality = BoundaryAnalysisMath.CalculateDegreeCentrality(nodes, edges);
foreach (var item in centrality)
{
    Console.WriteLine($"节点 {item.Key} 的度中心性: {item.Value}");
}

// 计算图的完整性评分
double integrityScore = BoundaryAnalysisMath.CalculateGraphIntegrityScore(nodes, edges);
Console.WriteLine($"图的完整性评分: {integrityScore}%");

// 计算平均路径长度
Dictionary<string, List<string>> adjacencyList = BuildAdjacencyList();
double avgPathLength = BoundaryAnalysisMath.CalculateAveragePathLength(adjacencyList);
Console.WriteLine($"平均路径长度: {avgPathLength}");
```

#### CircularDependencyDetector

**命名空间：** `FrameManage.MathCalculation`

**功能：** 提供循环依赖检测功能，用于检测系统中的循环依赖关系。

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `Detect<T>` | 检测循环依赖 | nodes: 节点集合<br>getDependencies: 获取依赖的函数 | `CircularDependencyResult` - 循环依赖检测结果 |
| `DetectById` | 根据ID检测循环依赖 | nodeIds: 节点ID集合<br>getDependencies: 获取依赖的函数 | `CircularDependencyResult` - 循环依赖检测结果 |
| `DetectFromAdjacencyList` | 从邻接表检测循环依赖 | adjacencyList: 邻接表 | `CircularDependencyResult` - 循环依赖检测结果 |
| `DetectCycle<T>` | 检测循环（私有方法） | node: 当前节点<br>visited: 已访问节点集合<br>recursionStack: 递归栈<br>path: 当前路径<br>cyclePath: 循环路径<br>getDependencies: 获取依赖的函数 | `bool` - 是否检测到循环 |
| `ExtractCyclePath<T>` | 提取循环路径（私有方法） | path: 当前路径<br>cyclePath: 循环路径<br>dependency: 依赖节点 | `void` |
| `BuildAdjacencyList` | 构建邻接表 | nodes: 节点字典<br>edges: 边列表<br>relationType: 关系类型（可选） | `Dictionary<string, List<string>>` - 邻接表 |

**使用场景：**
- **循环依赖检测**：检测系统中的循环依赖，避免系统出现死锁或无限循环。
- **依赖关系分析**：分析系统中各组件之间的依赖关系，优化系统结构。
- **邻接表构建**：根据节点和边构建邻接表，为其他图算法提供数据结构。

**示例代码：**

```csharp
// 从邻接表检测循环依赖
Dictionary<string, List<string>> adjacencyList = new Dictionary<string, List<string>>
{
    { "A", new List<string> { "B" } },
    { "B", new List<string> { "C" } },
    { "C", new List<string> { "A" } } // 循环依赖
};

CircularDependencyResult result = CircularDependencyDetector.DetectFromAdjacencyList(adjacencyList);

if (result.HasCircularDependency)
{
    Console.WriteLine("检测到循环依赖:");
    foreach (var cycle in result.Cycles)
    {
        Console.WriteLine(cycle.Description);
    }
}
else
{
    Console.WriteLine("未检测到循环依赖");
}

// 构建邻接表
Dictionary<string, object> nodes = GetNodes();
List<object> edges = GetEdges();
Dictionary<string, List<string>> builtAdjacencyList = CircularDependencyDetector.BuildAdjacencyList(nodes, edges);
```

#### GraphAlgorithmMath

**命名空间：** `FrameManage.MathCalculation`

**功能：** 提供图算法相关的数学计算方法，用于分析图的各种属性和关系。

**主要方法：**

| 方法名 | 描述 | 参数 | 返回值 |
|-------|------|------|-------|
| `FindShortestPath` | 寻找最短路径 | adjacencyList: 邻接表<br>start: 起始节点<br>end: 结束节点 | `List<string>` - 最短路径节点列表 |
| `FindAllPaths` | 寻找所有路径 | adjacencyList: 邻接表<br>start: 起始节点<br>end: 结束节点<br>maxPaths: 最大路径数 | `List<List<string>>` - 路径列表 |
| `FindPathsRecursive` | 递归寻找路径（私有方法） | adjacencyList: 邻接表<br>current: 当前节点<br>end: 结束节点<br>currentPath: 当前路径<br>visited: 已访问节点集合<br>paths: 路径列表<br>maxPaths: 最大路径数 | `void` |
| `GetDependencies` | 获取依赖关系 | adjacencyList: 邻接表<br>node: 目标节点<br>maxDepth: 最大深度 | `List<string>` - 依赖节点列表 |
| `GetDependenciesRecursive` | 递归获取依赖关系（私有方法） | adjacencyList: 邻接表<br>current: 当前节点<br>dependencies: 依赖集合<br>currentDepth: 当前深度<br>maxDepth: 最大深度 | `void` |
| `GetDependents` | 获取依赖者 | adjacencyList: 邻接表<br>node: 目标节点<br>maxDepth: 最大深度 | `List<string>` - 依赖者节点列表 |
| `BuildReverseAdjacencyList` | 构建反向邻接表（私有方法） | adjacencyList: 邻接表 | `Dictionary<string, List<string>>` - 反向邻接表 |
| `ReconstructPath` | 重建路径（私有方法） | previous: 前驱节点字典<br>start: 起始节点<br>end: 结束节点 | `List<string>` - 重建的路径 |
| `CalculateGraphDiameter` | 计算图的直径 | adjacencyList: 邻接表 | `int` - 图的直径 |
| `IsGraphConnected` | 检测图是否连通 | adjacencyList: 邻接表 | `bool` - 是否连通 |

**使用场景：**
- **路径分析**：寻找节点之间的最短路径或所有路径，优化工作流执行路径。
- **依赖关系分析**：分析节点的依赖关系和被依赖关系，了解系统的依赖结构。
- **图属性分析**：计算图的直径和连通性，评估系统的整体结构。
- **系统结构优化**：基于图算法分析结果，优化系统结构，提高系统性能。

**示例代码：**

```csharp
// 寻找最短路径
Dictionary<string, List<string>> adjacencyList = new Dictionary<string, List<string>>
{
    { "A", new List<string> { "B", "C" } },
    { "B", new List<string> { "D" } },
    { "C", new List<string> { "D" } },
    { "D", new List<string> { "E" } }
};

List<string> shortestPath = GraphAlgorithmMath.FindShortestPath(adjacencyList, "A", "E");
Console.WriteLine("最短路径: " + string.Join(" -> ", shortestPath));

// 寻找所有路径
List<List<string>> allPaths = GraphAlgorithmMath.FindAllPaths(adjacencyList, "A", "E");
Console.WriteLine("所有路径:");
foreach (var path in allPaths)
{
    Console.WriteLine(string.Join(" -> ", path));
}

// 获取依赖关系
List<string> dependencies = GraphAlgorithmMath.GetDependencies(adjacencyList, "E");
Console.WriteLine("E的依赖关系: " + string.Join(", ", dependencies));

// 获取依赖者
List<string> dependents = GraphAlgorithmMath.GetDependents(adjacencyList, "D");
Console.WriteLine("D的依赖者: " + string.Join(", ", dependents));

// 计算图的直径
int diameter = GraphAlgorithmMath.CalculateGraphDiameter(adjacencyList);
Console.WriteLine("图的直径: " + diameter);

// 检测图是否连通
bool isConnected = GraphAlgorithmMath.IsGraphConnected(adjacencyList);
Console.WriteLine("图是否连通: " + isConnected);
```

### 2.4 数据结构

#### CircularDependencyResult

**功能：** 循环依赖检测结果。

**主要属性：**
- `HasCircularDependency`：是否存在循环依赖
- `Cycles`：循环依赖信息列表

#### CircularDependencyInfo

**功能：** 循环依赖信息。

**主要属性：**
- `NodeIds`：循环依赖中的节点ID列表
- `Description`：循环依赖描述

### 2.5 应用场景

1. **工作流分析**：分析工作流的边界稳定性、复杂度和依赖关系，优化工作流设计。

2. **系统架构评估**：评估系统的整体结构，识别循环依赖和潜在的性能瓶颈。

3. **路径优化**：寻找工作流执行的最优路径，提高系统性能。

4. **依赖管理**：管理系统各组件之间的依赖关系，避免循环依赖和过度依赖。

5. **系统监控**：监控系统的结构变化，及时发现和解决问题。

### 2.6 总结

MathCalculation 模块提供了一系列强大的数学计算工具，为 FrameManage 框架提供了深入的系统分析能力。这些工具可以帮助开发者更好地理解和优化工作流系统，提高系统的稳定性、性能和可维护性。通过使用这些工具，开发者可以：

- 评估工作流边界的稳定性
- 分析系统的复杂度
- 检测和解决循环依赖
- 优化工作流执行路径
- 管理系统依赖关系

这些功能使得 FrameManage 框架能够更好地支持复杂工作流的设计和执行，为开发者提供了更强大的工具和更深入的分析能力。
