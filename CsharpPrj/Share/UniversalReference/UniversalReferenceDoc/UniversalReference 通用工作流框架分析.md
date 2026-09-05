# UniversalReference 通用工作流框架分析

## 一、概述

**UniversalReference**（命名空间 `UniversalWorkflowFramework`）是 WorkFlowPrj 项目中的**通用工作流框架核心库**，位于 `Share/UniversalReference/` 目录下。该框架从多个业务模块（FileDriven、BatchIntegration、EventDriven 等）中提取通用逻辑，形成一套**不依赖任何业务类型的纯基础设施层**。

### 1.1 设计哲学

- **业务无关性**：核心组件不依赖任何业务类型（如 `BatchMetadata`、`LogEntry`、`ReferenceType` 等）
- **委托注入**：通过 `Func<>` 委托注入业务逻辑，而非硬编码接口依赖
- **线程安全**：大量使用 `ConcurrentDictionary`、`AsyncLocal`、`SemaphoreSlim` 等并发原语
- **渐进式重构**：代码中保留 `[Obsolete]` 标记的旧类型，逐步迁移到统一类型
- **Pipeline 编排**：支持通过 Pipeline 管道模式编排复杂业务流程

### 1.2 核心命名空间

| 命名空间 | 职责 |
|---------|------|
| `UniversalWorkflowFramework.Core` | 通用基础设施（事件总线、状态机、上下文、注册表、图验证等） |
| `UniversalWorkflowFramework.Reference` | 引用模型与接口定义 |
| `UniversalWorkflowFramework.EventDriven` | 事件驱动机制 |
| `UniversalWorkflowFramework.BatchIntegration` | 批次管理 |
| `UniversalWorkflowFramework.BranchManagement` | 分支图模型 |
| `UniversalWorkflowFramework.ActionMapping` | 操作映射 |
| `UniversalWorkflowFramework.WorkflowExecution` | 工作流执行 |
| `UniversalWorkflowFramework.Storage` | 多种存储实现 |
| `RelationshipManagement` | 关系路由与调度 |
| `UniversalWorkflowFramework.Frame` | 字段映射与引用解析框架 |

---

## 二、核心层（Core）

核心层位于 `Core/` 目录，提供 7 个通用基础设施组件，是整个框架的基石。

### 2.1 EventBus — 通用事件总线

**文件**：`Core/EventBus.cs`（153 行）

纯事件订阅/发布机制，不依赖任何外部类型。

**核心特性**：
- 基于 `ConcurrentDictionary<Type, List<Delegate>>` 实现线程安全
- 支持任意事件类型（无基类约束）
- 支持异步处理器 `Func<TEvent, Task>`
- 支持同步处理器（自动包装为异步）
- 两种发布模式：并行执行（`PublishAsync`）和串行执行（`PublishSequentialAsync`）

**关键 API**：

```csharp
public class EventBus
{
    void Subscribe<TEvent>(Func<TEvent, Task> handler);
    void SubscribeSync<TEvent>(Action<TEvent> handler);
    void Unsubscribe<TEvent>(Func<TEvent, Task> handler);
    Task PublishAsync<TEvent>(TEvent @event);           // 并行
    Task PublishSequentialAsync<TEvent>(TEvent @event); // 串行
    int GetSubscriberCount<TEvent>();
    void Clear();  // 清空所有
    void Clear<TEvent>(); // 清空指定类型
}
```

**设计亮点**：`EventManager`（EventDriven 层）内部组合 `EventBus`，而非重复实现 ConcurrentDictionary 操作，体现了**组合优于继承**原则。

---

### 2.2 StateMachine — 通用状态机

**文件**：`Core/StateMachine.cs`（154 行）

管理任意类型实体的状态转换，不依赖任何业务类型。

**核心特性**：
- 泛型约束 `where TState : struct, Enum`，支持任意枚举作为状态
- 基于 `ConcurrentDictionary<string, TState>` 实现线程安全
- 支持条件转换（通过 `Func<TState, bool>` 委托）
- 状态变更回调事件 `OnStateChanged`
- 按状态统计和查询

**关键 API**：

```csharp
public class StateMachine<TState> where TState : struct, Enum
{
    bool Register(string id, TState initialState);
    TState? GetState(string id);
    bool TryTransitionTo(string id, TState newState);
    bool TryTransitionTo(string id, TState newState, Func<TState, bool> condition);
    void SetState(string id, TState state); // 强制设置，不触发回调
    bool Remove(string id);
    int CountByState(TState state);
    IEnumerable<string> GetIdsByState(TState state);
    event Action<string, TState, TState>? OnStateChanged;
}
```

**应用场景**：`BatchManager` 内部组合 `StateMachine<BatchStatus>` 管理批次状态转换。

---

### 2.3 ContextManager — 通用上下文管理器

**文件**：`Core/ContextManager.cs`（157 行）

提供线程安全的上下文传递机制，基于 `AsyncLocal` 实现。

**核心特性**：
- `AsyncLocal<T>` 实现，跨线程和异步流安全
- 支持 `Enter/Exit/With` 模式
- 支持嵌套上下文（自动保存和恢复上一个值）
- 支持同步和异步操作

**关键 API**：

```csharp
public class ContextManager<T> where T : class
{
    T? Current { get; }
    bool IsSet { get; }
    void Set(T value);
    void Clear();
    void Enter(T value);
    void Exit();
    void Execute(T value, Action action);           // 嵌套执行
    TResult Execute<TResult>(T value, Func<TResult> func);
    Task ExecuteAsync(T value, Func<Task> asyncAction);
    Task<TResult> ExecuteAsync<TResult>(T value, Func<Task<TResult>> asyncFunc);
}
```

**应用场景**：`BatchManager` 内部组合 `ContextManager<string>` 管理当前批次上下文。

---

### 2.4 ResolverRegistry — 通用解析器注册表

**文件**：`Core/ResolverRegistry.cs`（123 行）

提供策略模式的注册与查找机制。

**核心特性**：
- 泛型键和值，支持任意类型
- 按键注册/查找
- 按类型查找（`Get<T>()` 泛型查询）
- 线程安全的 `ConcurrentDictionary` 存储

**关键 API**：

```csharp
public class ResolverRegistry<TKey, TResolver>
    where TKey : notnull
    where TResolver : class
{
    bool Register(TKey key, TResolver resolver);
    void RegisterOrUpdate(TKey key, TResolver resolver);
    TResolver? Get(TKey key);
    T? Get<T>() where T : class, TResolver;  // 按类型查找
    bool Remove(TKey key);
    void RegisterRange(IEnumerable<KeyValuePair<TKey, TResolver>> resolvers);
}
```

---

### 2.5 CircularDependencyDetector — 循环依赖检测器

**文件**：`Core/CircularDependencyDetector.cs`（153 行）

使用栈式 DFS 算法检测引用关系中的循环依赖。

**核心特性**：
- 递归 DFS 检测单个节点是否存在循环
- `Analyze` 方法返回完整的循环路径信息
- 支持大小写不敏感的比较
- 旧版 `FindCircularNodes` 已标记 `[Obsolete]`

**关键 API**：

```csharp
public static class CircularDependencyDetector
{
    bool DetectCircular(string nodeName, Func<string, IEnumerable<string>> getDependencies, HashSet<string> visited);
    [Obsolete] List<string> FindCircularNodes(IEnumerable<string> nodeNames, Func<string, IEnumerable<string>> getDependencies);
    CircularDependencyResult Analyze(IEnumerable<string> nodeNames, Func<string, IEnumerable<string>> getDependencies);
}
```

**算法说明**：
- 使用 `visited` 集合记录当前路径上的节点
- 如果遇到已在 `visited` 中的节点，说明存在环
- `Analyze` 方法额外记录完整的循环路径（`ResourceChain`）

---

### 2.6 GraphValidator — 通用有向图验证器

**文件**：`Core/GraphValidator.cs`（279 行）

提供有向图的完整性验证和路径可达性验证。

**核心特性**：
- 通过委托注入节点/边的判断逻辑
- 完整性验证：开始/结束节点、节点ID唯一性、边引用有效性
- 路径可达性验证：DFS 查找路径
- 支持查找所有路径（带最大深度限制）

**关键 API**：

```csharp
public class GraphValidator<TNodeId> where TNodeId : notnull
{
    ValidationResult ValidateIntegrity(
        IEnumerable<TNodeId> allNodes,
        IEnumerable<(TNodeId Source, TNodeId Target)> edges,
        Func<TNodeId, bool> isStartNode,
        Func<TNodeId, bool> isEndNode);

    bool HasPath(TNodeId source, TNodeId target, Func<TNodeId, IEnumerable<TNodeId>> getOutgoingEdges);
    List<List<TNodeId>> FindAllPaths(TNodeId source, TNodeId target, Func<TNodeId, IEnumerable<TNodeId>> getOutgoingEdges, int maxDepth = 10);
    bool HasPathToAnyEnd(TNodeId startNode, IEnumerable<TNodeId> endNodes, Func<TNodeId, IEnumerable<TNodeId>> getOutgoingEdges);
    HashSet<TNodeId> GetReachableNodes(TNodeId startNode, Func<TNodeId, IEnumerable<TNodeId>> getOutgoingEdges);
}
```

---

### 2.7 ReferenceDiagnostics — 引用诊断服务

**文件**：`Core/ReferenceDiagnostics.cs`（189 行）

检查引用配置的完整性和一致性。

**诊断项**：
1. **循环引用检查** — 委托 `CircularDependencyDetector.Analyze`
2. **孤立引用检查** — 未被任何其他引用引用的引用
3. **未使用引用检查** — 通过 `getUsedReferences` 委托判断
4. **缺失引用检查** — 引用了不存在的目标

**关键 API**：

```csharp
public static class ReferenceDiagnostics
{
    DiagnosticResult RunFullDiagnostics(
        IDictionary<string, IEnumerable<string>> allReferences,
        Func<string, IEnumerable<string>> getDependencies,
        Func<HashSet<string>> getUsedReferences);
}
```

`DiagnosticResult` 提供 `GenerateReport()` 方法生成格式化诊断报告。

---

## 三、引用管理层（Reference）

位于 `Reference/` 目录，定义引用相关的模型和接口。

### 3.1 引用模型（ReferenceModels.cs）

```csharp
public static class ReferenceConfig
{
    static ReferenceConfiguration GenerateConfiguration(); // 生成默认配置
    void AddNodeType(NodeType nodeType, ReferenceConfiguration? config = null);
    void AddEdgeType(EdgeType edgeType, ReferenceConfiguration? config = null);
    void AddValidationRule(ValidationRule rule, ReferenceConfiguration? config = null);
}
```

默认配置包含：
- **10 种节点类型**：workflow、module、service、repository、model、data、code、tool、api、database
- **5 种边类型**：depends_on、contains、uses、calls、generates
- **4 条验证规则**：工作流完整性、服务依赖、模型数据源、引用完整性

### 3.2 引用接口（ReferenceInterfaces.cs）

| 接口 | 职责 |
|------|------|
| `IReferenceManager` | ★ 已废弃，请使用 `IUniversalReferenceManager` |
| `IReferenceable` | 可引用对象接口 |
| `IReferenceEventBus` | 引用事件总线 |
| `IReferenceAnalysisService` | 引用分析服务 |
| `IReferenceValidationService` | 引用验证服务 |
| `IUnifiedCacheService<T>` | 统一缓存服务 |
| `IUnifiedEventPublisherService` | 统一事件发布服务 |
| `IUnifiedResourceIdentifierService` | 统一资源标识符服务 |

### 3.3 引用模型（Reference/Models/）

定义在 `Reference/Models/ReferenceModels.cs` 中的核心模型：

| 模型 | 说明 |
|------|------|
| `Reference` | 引用（Source → Target + RelationshipType） |
| `ReferenceChange` | 引用变更记录 |
| `ReferenceIssue` | 引用问题 |
| `ReferenceOperation` | 引用操作 |
| `ProcessedReference` | 已处理的引用 |
| `ReferenceAnalysisItem` | 引用分析项 |
| `ResourceReference` | 资源引用 |
| `ResourceData` | 资源数据 |

---

## 四、事件驱动层（EventDriven）

位于 `EventDriven/` 目录，在 Core.EventBus 基础上封装业务事件。

### 4.1 EventManager（EventManager.cs）

内部组合 `Core.EventBus`，添加日志和异常处理。

```csharp
public class EventManager : IEventManager
{
    void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : EventBase;
    void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : EventBase;
    Task PublishAsync<TEvent>(TEvent @event) where TEvent : EventBase;
    int GetSubscriberCount<TEvent>() where TEvent : EventBase;
}
```

### 4.2 事件模型（Events.cs）

| 类型 | 说明 |
|------|------|
| `ReferenceChangeEventArgs` | 引用变更事件参数 |
| `ReferenceRelationshipChangedEventArgs` | 引用关系变更事件参数 |
| `BulkReferenceChangeEventArgs` | 批量引用变更事件参数 |

---

## 五、批次集成层（BatchIntegration）

位于 `BatchIntegration/` 目录，管理批次的完整生命周期。

### 5.1 BatchManager（BatchManager.cs，419 行）

单例模式，内部组合 `Core.StateMachine<BatchStatus>` 和 `Core.ContextManager<string>`。

**核心功能**：

| 功能 | 方法 |
|------|------|
| 批次注册 | `RegisterBatch(batchId, name, source, ...)` |
| 状态管理 | `CompleteBatch` / `FailBatch` / `CancelBatch` |
| 当前批次 | `SetCurrentBatch(batchId)` |
| 查询 | `GetBatch` / `GetAllBatches` / `GetSummary` |
| 清理 | `CleanupBatch` / `CleanupOldBatches(maxAgeDays)` |
| Pipeline 编排 | `ExecutePipelineAsync` / `CreateBatchPipeline` |

**Pipeline 支持**：`CreateBatchPipeline<TData>` 预置三个步骤：
1. 注册批次（默认自动注册到 BatchManager）
2. 处理批次数据（必须自定义）
3. 完成批次（默认根据错误状态自动 Complete/Fail）

### 5.2 辅助组件

| 文件 | 职责 |
|------|------|
| `BatchModels.cs` | 批次元数据模型（`BatchMetadata`、`BatchStatus` 枚举） |
| `BatchRegistry.cs` | 批次注册器 |
| `BatchLoggerBridge.cs` | 批次与日志的桥接 |
| `BatchChangeTracker.cs` | 批次变更追踪 |
| `IBatchAware.cs` | 批次感知接口 |

---

## 六、分支管理层（BranchManagement）

位于 `BranchManagement/` 目录，定义工作流的分支图结构。

### 6.1 BranchGraph 模型（BranchGraph.cs）

```csharp
public class BranchGraph
{
    string BranchId, Name, Description;
    List<BranchNode> Nodes;
    List<BranchEdge> Edges;
}

public class BranchNode
{
    string NodeId, Name;
    NodeType Type;  // Start / Action / Decision / End / Subflow
    Dictionary<string, object> Data;
}

public class BranchEdge
{
    string EdgeId;
    string SourceNodeId, TargetNodeId;
    EdgeType Type;  // Default / Normal / Conditional / Error
    Dictionary<string, object> Data;
}
```

---

## 七、操作映射层（ActionMapping）

位于 `ActionMapping/` 目录，实现输入操作到输出操作的映射。

### 7.1 ActionMapper（ActionMapper.cs，328 行）

```csharp
public class ActionMapper<TInput, TOutput> : IActionMapper<TInput, TOutput>
    where TInput : ActionBase
    where TOutput : ActionBase, new()
```

**核心功能**：
- `MapAsync(TInput input)` — 映射输入操作到输出操作
- `AddMappingAsync(inputType, outputType, parameterMap)` — 添加映射关系
- `GetAllMappingsAsync()` — 获取所有映射
- `RemoveMappingAsync(inputType)` — 删除映射

**映射存储**：使用 `GraphDatabaseStorage`（图数据库）存储映射关系。

**参数映射**：支持两种方式：
1. 传统字典遍历（`MapParameters`）
2. **FieldMapping 框架**（`MapParametersWithFieldMapping`）— 通过 `FieldMappingResolverBuilder` 构建映射规则

---

## 八、工作流执行层（WorkflowExecution）

位于 `WorkflowExecution/` 目录，提供工作流执行能力。

### 8.1 WorkflowExecutor（WorkflowExecutor.cs，317 行）

```csharp
public class WorkflowExecutor : IWorkflowExecutor
```

**核心功能**：
- `ExecuteAsync(object action, bool isUIAction)` — 执行操作（支持 UI/AI 两种来源）
- `ExecuteBranchAsync(string branchId, Dictionary<string, object> parameters)` — 执行分支
- `ExecuteWorkflowPipelineAsync<TData>(pipeline, context)` — Pipeline 编排执行
- `CreateWorkflowPipeline<TData>(validateStep, executeStep, resultStep)` — 创建执行管道
- `ValidateExecutionAsync(workflowId, executionResults)` — 验证执行结果

**执行流程**：
1. 从图数据库获取操作逻辑
2. 模拟执行（`Task.Delay(100ms)`）
3. 构建执行结果（包含来源、类型、时间等信息）

---

## 九、存储层（Storage）

位于 `Storage/` 目录，提供多种存储实现。

### 9.1 存储接口

```csharp
// 通用工作流存储
public interface IWorkflowStorage<T>
{
    Task StoreAsync(string workflowId, T workflow);
    Task<T?> RetrieveAsync(string workflowId);
    Task DeleteAsync(string workflowId);
    Task<bool> ExistsAsync(string workflowId);
    Task<List<string>> GetAllWorkflowIdsAsync();
}

// 图数据库存储（只负责资源存储，不处理引用关系）
public interface IGraphDatabaseStorage
{
    Task InitializeAsync();
    Task StoreResourceAsync(ResourceIdentifier resourceId, Dictionary<string, object> metadata, string? symbol = null);
    Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId);
    Task DeleteResourceAsync(ResourceIdentifier resourceId);
    Task<List<Resource>> GetResourcesByTypeAsync(string resourceType);
}
```

### 9.2 存储实现

| 实现 | 文件 | 说明 |
|------|------|------|
| `LocalStorage<T>` | `LocalStorage.cs` | JSON 文件存储，基于项目根目录 |
| `LiteDBPersistence<TKey, TNodeData, TEdgeData>` | `LiteDBRelate.cs` | LiteDB 嵌入式 NoSQL 数据库，支持节点和边的持久化 |
| `SqliteRelate` | `SqliteRelate.cs` | SQLite 关系型数据库，包含 DataUniversal、DataCreate、DataSave、DataRead、AutoStorage 五个子模块 |
| `GraphDatabaseStorage` | `GraphDatabaseStorage.cs` | 图数据库存储，内部组合 `IUniversalReferenceManager`，额外提供查询、搜索、统计、批量操作 |

### 9.3 LiteDB 存储模型

```csharp
public class NodeDocument<TKey, TNodeData>
{
    [BsonId] TKey Key;
    TNodeData Data;
    string? Symbol;
    DateTime CreatedAt, UpdatedAt;
}

public class EdgeDocument<TKey, TEdgeData>
{
    [BsonId] string EdgeId;
    TKey Source, Target;
    bool IsDirected;
    TEdgeData? Data;
    double Weight;
    DateTime CreatedAt, UpdatedAt;
}
```

---

## 十、关系管理层（RelationshipManagement）

位于 `UniversalReferenceLayer.cs` 后半部分（第 3062-3771 行），提供基于规则的关系路由与调度。

### 10.1 架构组件

| 组件 | 接口/类 | 职责 |
|------|---------|------|
| 路由上下文 | `IRoutingContext` / `RoutingContext` | 封装决策所需信息（PrimaryKey、Tags、Payload） |
| 规则 | `IRelationshipRule` / `ExpressionRule` | 基于表达式的规则，支持比较和逻辑运算 |
| 规则集 | `IRuleSet` / `InMemoryRuleSet` | 规则集合管理 |
| 匹配策略 | `IMatchingStrategy` / `BestMatchStrategy` | 最佳匹配/全匹配 |
| 目标解析器 | `ITargetResolver<TTarget>` / `DelegateTargetResolver<TTarget>` | 目标解析 |
| 调度器 | `IRelationshipDispatcher<TInput, TOutput>` / `RelationshipDispatcher` | 单播/广播调度 |

### 10.2 ExpressionRule 条件解析器

内置递归下降表达式解析器（`ConditionParser`），支持：

- **逻辑运算**：`&&`、`||`、`!`
- **比较运算**：`==`、`!=`、`>`、`>=`、`<`、`<=`
- **值类型**：数字、字符串（单引号/双引号）、布尔、null
- **变量访问**：`PrimaryKey`、`Tags["key"]`、`Payload.Property`
- **数值类型提升**：自动将 int/long/double 统一提升为 double 比较

### 10.3 配置与热加载

```csharp
public class FileWatcherRuleSet : IRuleSet, IDisposable
```

- 支持从 JSON 文件加载规则
- 使用 `FileSystemWatcher` 监视文件变化，自动重新加载
- 使用 `SemaphoreSlim` 确保重入安全

### 10.4 异常定义

| 异常 | 说明 |
|------|------|
| `RelationshipException` | 关系异常基类 |
| `NoMatchingRuleException` | 无匹配规则 |
| `TargetResolutionException` | 目标解析失败 |
| `RuleEvaluationException` | 规则评估失败 |

---

## 十一、字段映射层（Frame）

位于 `Frame/` 目录，提供字段级引用解析和 Pipeline 编排能力。

### 11.1 FieldMapping（FieldMapping.cs）

字段映射解析器，支持多种映射模式：

| 模式 | 说明 |
|------|------|
| `ByValueDirectRename` | 直接重命名 |
| `ByValueDirect` | 保持原名 |
| `ByValueWithPrefix` | 添加前缀 |
| `ByValueWithSuffix` | 添加后缀 |
| `ByValueWithFormat` | 格式化 |
| `ByValueWithTransform` | 值转换 |
| `Custom` | 自定义解析 |

### 11.2 Pipeline（Pipeline.cs）

通用 Pipeline 管道模式，支持：
- 步骤注册（`AddStep`）
- 异步执行（`ExecuteAsync`）
- 构建器模式（`CreateBuilder`）

### 11.3 ReferenceResolution（ReferenceResolution.cs）

引用解析引擎，与 FieldMapping 配合使用：
- `ReferenceUpdateEngine` — 引用更新引擎
- `FieldMappingReferenceResolver` — 字段映射引用解析器

---

## 十二、架构总结

### 12.1 模块依赖关系

```
Core（基础设施）
  ├── EventBus ──────────→ EventDriven.EventManager
  ├── StateMachine ──────→ BatchIntegration.BatchManager
  ├── ContextManager ────→ BatchIntegration.BatchManager
  ├── ResolverRegistry ──→ 策略注册
  ├── CircularDependencyDetector → ReferenceDiagnostics
  └── GraphValidator ────→ 图验证

Reference（引用模型）
  └── 被 Storage、EventDriven、ActionMapping 等引用

Storage（存储层）
  ├── LocalStorage ──────→ JSON 文件
  ├── LiteDBPersistence ─→ LiteDB 嵌入式数据库
  ├── SqliteRelate ──────→ SQLite 关系数据库
  └── GraphDatabaseStorage → IUniversalReferenceManager

Frame（字段映射 + Pipeline）
  ├── FieldMapping ──────→ ActionMapper
  ├── Pipeline ──────────→ BatchManager、WorkflowExecutor
  └── ReferenceResolution → 引用解析

RelationshipManagement（关系路由）
  └── 独立模块，依赖 Core 模型
```

### 12.2 设计模式总结

| 模式 | 使用位置 |
|------|---------|
| **单例模式** | `BatchManager.Instance` |
| **策略模式** | `ResolverRegistry<TKey, TResolver>`、`IMatchingStrategy` |
| **观察者模式** | `EventBus` 订阅/发布、`StateMachine.OnStateChanged` |
| **管道模式** | `Pipeline<T>` 步骤编排 |
| **构建器模式** | `FieldMappingResolverBuilder`、`PipelineBuilder` |
| **工厂模式** | `EventManagerFactory`、`ActionMapperFactory`、`WorkflowExecutorFactory` |
| **组合模式** | `EventManager` 组合 `EventBus`、`BatchManager` 组合 `StateMachine` + `ContextManager` |
| **模板方法** | `ExecuteWithLogAsync` / `ExecuteWithLogOrDefaultAsync` 通用异常处理模板 |
| **桥接模式** | `BatchLoggerBridge` 桥接批次和日志系统 |

### 12.3 重构演进路线

代码中清晰展示了渐进式重构的路径：

```
第一阶段：业务耦合
  └── BatchRouter（日志相关）、BranchValidator（分支相关）

第二阶段：提取通用核心
  └── Core.EventBus、Core.StateMachine、Core.ContextManager、Core.GraphValidator

第三阶段：统一类型
  └── Reference.Models.CircularDependencyResult（替代 Core.CircularDependencyResult）
  └── Core.Interfaces.IUniversalReferenceManager（替代 Reference.IReferenceManager）

第四阶段：Pipeline 编排
  └── BatchManager.CreateBatchPipeline<TData>
  └── WorkflowExecutor.CreateWorkflowPipeline<TData>
```

### 12.4 代码规模统计

| 模块 | 文件数 | 核心行数 |
|------|--------|---------|
| Core（核心层） | 12+ | ~1200 |
| Reference（引用层） | 4+ | ~400 |
| EventDriven（事件驱动） | 4+ | ~300 |
| BatchIntegration（批次集成） | 6 | ~700 |
| BranchManagement（分支管理） | 3 | ~250 |
| ActionMapping（操作映射） | 3 | ~400 |
| WorkflowExecution（工作流执行） | 2 | ~350 |
| Storage（存储层） | 5 | ~800 |
| RelationshipManagement（关系路由） | 1 | ~700 |
| Frame（字段映射+Pipeline） | 4 | ~500 |
| **总计** | **~44** | **~5600** |

---

## 十三、使用方分析

UniversalReference 作为通用基础设施层，被整个 WorkFlowPrj 项目中的多个模块引用。以下分析基于项目引用（`.csproj`）和代码中的 `using` 语句。

### 13.1 项目引用关系

| 引用方项目 | 引用方式 | 主要使用的模块 |
|-----------|---------|---------------|
| `Share/WorkFlowMethod` | 项目引用（Aliases="ShareAlias"） | FunctionExecution |
| `Share/Diagnostics` | 项目引用 | BatchIntegration（BatchManager、BatchLoggerBridge） |
| `Share/PlaceholderFramework` | 项目引用 | 间接依赖（通过 BasicMethod 中转，代码中无直接 using） |
| `BasicMethod/DataManage` | 项目引用 | Core.Models（Resource、ResourceIdentifier）、Storage |
| `FileDriven/FileDriven.Logging` | 项目引用 | BatchIntegration |
| `CentralConsole/Share.Tsets/UniversalReference.Tests` | 项目引用 | 全部模块（完整测试覆盖） |
| `CentralConsole/BasicMethod.Tests` | 项目引用 | BatchIntegration |
| `CentralConsole/FileDriven.Tests` | 项目引用 | BatchIntegration |
| `CentralConsole/BasicMethod.Tests/Logging.Tests` | 项目引用 | BatchIntegration |

### 13.2 各模块使用场景详解

#### 13.2.1 BatchIntegration — 最广泛使用的模块

**BatchIntegration** 是 UniversalReference 中被引用最多的模块，主要用于日志系统和批次管理。

**使用方**：`Share/Diagnostics`、`FileDriven/FileDriven.Logging`、`CentralConsole/BasicMethod.Tests` 等

**典型使用模式**：

```csharp
// 1. 批次注册与状态管理（Share/Diagnostics/LogBatch.cs）

using UniversalWorkflowFramework.BatchIntegration;

// BatchManager 管理批次生命周期
BatchManager.Instance.RegisterBatch(batchId, name, source);
BatchManager.Instance.CompleteBatch(batchId);
BatchManager.Instance.FailBatch(batchId, errorDescription);

// 2. 批次上下文桥接（Share/Diagnostics/BatchSelector.cs）
using UniversalWorkflowFramework.BatchIntegration;

// BatchLoggerBridge 桥接批次和日志系统
BatchLoggerBridge.EnterBatch(batchId);
var currentBatchId = BatchLoggerBridge.CurrentBatchId;

// 3. Pipeline 编排（FileDriven/FileDriven.Logging/LogPipeline.cs）
using UniversalWorkflowFramework.BatchIntegration;

// 创建批次处理管道
var pipeline = BatchManager.CreateBatchPipeline(
    registerStep: async (ctx, ct) => { /* 自定义注册 */ },
    processStep: async (ctx, ct) => { /* 处理逻辑 */ },
    completeStep: async (ctx, ct) => { /* 完成处理 */ }
);
```

#### 13.2.2 Core.Models — 资源模型共享

**Core.Models** 中的 `Resource` 和 `ResourceIdentifier` 是跨模块共享的核心数据模型。

**使用方**：`BasicMethod/DataManage` 及其子模块

**典型使用模式**：

```csharp
// BasicMethod/DataManage/DataManage.Usage.cs
using UniversalWorkflowFramework.Core.Models;

// 创建资源标识符
var resourceId = new ResourceIdentifier
{
    ResourceType = "workflow",
    Id = "order-processing"
};

// 存储资源
await storage.StoreResourceAsync(resourceId, new Dictionary<string, object>
{
    { "name", "订单处理工作流" },
    { "version", "1.0.0" }
}, "WORKFLOW");

// 查询资源
var resource = await storage.GetResourceAsync(resourceId);
// resource.Identifier.ResourceType, resource.Identifier.Id, resource.Metadata, resource.Symbol

// 按类型查询
var workflows = await storage.GetResourcesByTypeAsync("workflow");
```

**DataManage 的图存储体系**（`CachedGraphStorage`、`GraphIndexManager`、`GraphTransaction` 等）全部基于 `UniversalWorkflowFramework.Core.Models.Resource` 和 `ResourceIdentifier` 构建，形成了完整的图存储生态：

```
IGraphStorage（接口）
  ├── CachedGraphStorage（缓存装饰器）
  ├── GraphIndexManager（索引管理器）
  └── GraphTransaction（事务管理器）
       └── 全部基于 Core.Models.Resource / ResourceIdentifier
```

#### 13.2.3 FunctionExecution — 工作流函数执行

**FunctionExecution** 是 UniversalReference 中另一个重要的子命名空间，被 `Share/WorkFlowMethod` 和 `FileDriven` 使用。

**使用方**：`Share/WorkFlowMethod`、`FileDriven/FileDriven.UI`

**典型使用模式**：

```csharp
// Share/WorkFlowMethod/WorkflowUseage.cs
using UniversalWorkflowFramework.FunctionExecution;
using UniversalWorkflowFramework.FunctionExecution.Core;

// 创建执行上下文
var context = new ExecutionContext
{
    WorkflowId = workflowId,
    Parameters = parameters
};

// 注册函数
registry.RegisterFunction("myFunction", async (ctx, ct) =>
{
    // 执行逻辑
    return new ExecutionResult { Status = ExecutionStatus.Success };
});

// 执行管道
var result = await pipelineExecutor.ExecuteAsync(pipeline, context);
```

#### 13.2.4 PlaceholderFramework — 间接依赖的特殊情况

**PlaceholderFramework** 虽然项目引用了 UniversalReference，但代码中**没有任何直接使用 `using UniversalWorkflowFramework` 的语句**。其引用关系是通过 `BasicMethod` 间接传递的：

```
PlaceholderFramework → BasicMethod（项目引用）→ UniversalReference（项目引用）
```

**引用路径分析**：

| 层级 | 项目 | 引用方式 | 说明 |
|------|------|---------|------|
| 1 | `Share/PlaceholderFramework` | 项目引用 `BasicMethod` | 使用 BasicMethod 的 Pipeline、ErrorCollector 等 |
| 2 | `BasicMethod` | 项目引用 `Share/UniversalReference` | 使用 UniversalReference 的 BatchIntegration、Core.Models 等 |
| 3 | `Share/UniversalReference` | — | 被引用方，提供基础设施 |

**PlaceholderFramework 实际使用的 BasicMethod 类型**：

```csharp
// PlaceholderFramework 代码中实际使用的 BasicMethod 命名空间
using BasicMethod.Pipeline;           // ErrorCollector, PipelineContext
using BasicMethod.Pipeline.ErrorLevel; // 错误级别枚举
```

**结论**：PlaceholderFramework 对 UniversalReference 的引用是**间接的、非直接的**。它通过 BasicMethod 的 Pipeline 管道系统与 UniversalReference 产生间接关联。这种设计体现了**分层依赖**原则——PlaceholderFramework 只依赖 BasicMethod 的 Pipeline 抽象，而不直接耦合 UniversalReference 的具体实现。

#### 13.2.5 完整集成测试示例

`CentralConsole/Share.Tsets/UniversalReference.Tests/CompleteUsageExample.cs` 提供了一个**端到端的使用示例**，展示了所有模块的协同工作方式：

```csharp
// 1. 操作映射：UI操作 → AI操作
var actionMapper = ActionMapperFactory.Create<UIAction, AIAction>();

await actionMapper.AddMappingAsync("click", "ai_click",
    new Dictionary<string, string> { { "buttonId", "elementId" } });
var aiAction = await actionMapper.MapAsync(uiAction);

// 2. 分支管理：创建工作流分支图
var branchManager = BranchManagerFactory.Create<BranchGraph>(storage);
var branchId = await branchManager.RegisterBranchAsync("user-registration", branchGraph);

// 3. 工作流执行
var executor = WorkflowExecutorFactory.Create();
var result = await executor.ExecuteAsync(uiAction, isUIAction: true);

// 4. 引用管理：建立资源引用关系
var referenceManager = new GraphReferenceManager(innerManager);
await referenceManager.EstablishReferenceAsync(workflowRes, actionRes, "uses");

// 5. 事件驱动：响应式资源管理
var observableManager = new ObservableReferenceManager(innerManager);
observableManager.OnResourceAdded += (sender, args) => { /* 处理事件 */ };

// 6. LiteDB 持久化
using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();
await persistence.InitializeAsync(databasePath);
await persistence.SaveNodeAsync("node-001", nodeData, "START");

// 7. 图数据库存储
var graphStorage = GraphDatabaseStorageFactory.Create(logger);
await graphStorage.StoreResourceAsync(resourceId, metadata, "SYMBOL");
```

### 13.3 使用模式总结

| 使用模式 | 典型场景 | 涉及模块 |
|---------|---------|---------|
| **批次管理** | 日志系统、CLI 执行流程 | BatchIntegration（BatchManager、BatchLoggerBridge） |
| **资源存储** | 工作流数据持久化 | Storage（LocalStorage、LiteDB、SQLite、GraphDatabase） |
| **图数据操作** | 资源关系管理 | Core.Models（Resource、ResourceIdentifier） |
| **操作映射** | UI→AI 动作转换 | ActionMapping（ActionMapper） |
| **工作流执行** | 自动化流程执行 | WorkflowExecution（WorkflowExecutor） |
| **事件驱动** | 资源变更通知 | EventDriven（ObservableReferenceManager） |
| **分支管理** | 工作流分支图 | BranchManagement（BranchManager、BranchGraph） |
| **引用管理** | 资源依赖关系 | ReferenceManagement（GraphReferenceManager） |
| **Pipeline 编排** | 复杂业务流程 | Frame.Pipeline + BatchManager/WorkflowExecutor |

---

> **文档生成日期**：2026-06-03  
> **分析范围**：`Share/UniversalReference/` 目录下所有 C# 源文件  
> **核心命名空间**：`UniversalWorkflowFramework`


