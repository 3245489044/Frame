# 执行函数层与数据储存层分离方案

> 文档版本：v1.2（2026-05-14）  
> 📌 **全部阶段已完成**（F-v1 ~ F-v4）：函数注册接口 + 执行级覆盖 + 参数隔离 + 旧代码清理

**更新记录**：

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-05-14 | v1.2 | 第 10 节：标记 F-v3/F-v4 完成，更新代码示例，同步文件目录和版本表 |
| 2026-05-14 | v1.1 | 第 10 节：标记第一阶段完成，同步代码示例 |
| - | v1.0 | 初始方案 |

## 1. 现状分析

### 1.1 当前架构概览

WorkFlowMethod 当前架构中，执行函数层与数据储存层高度耦合，主要通过**静态类 + 全局状态**的方式交织在一起：

```
应用程序/入口
      │
      ▼
┌─────────────────────────────────────────────────────────────┐
│                   WorkflowManager（静态门面）                 │
│  ┌──────────────────────┐  ┌──────────────────────────────┐ │
│  │   执行职责             │  │      存储职责                  │ │
│  │                       │  │                              │ │
│  │  WorkflowProcessor    │  │  WorkflowStorageManager      │ │
│  │  （静态类）            │  │  （静态门面）                  │ │
│  │                       │  │                              │ │
│  │  WorkflowExecution    │  │  ChainInfoStorageManager     │ │
│  │  Manager              │  │  （静态门面）                  │ │
│  │  （单例）              │  │                              │ │
│  │                       │  │  WebCallManager（静态门面）    │ │
│  │  FunctionExecution    │  │                              │ │
│  │  Framework            │  │  GenericWorkflowStorage-     │ │
│  │  （静态工具类）         │  │  Manager（ECS实现）           │ │
│  └──────────────────────┘  │                              │ │
│                            │  UnifiedStorageManager        │ │
│                            │  （统一存储门面）               │ │
│                            └──────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 执行函数层现状

| 组件 | 位置 | 形态 | 职责 |
|------|------|------|------|
| **WorkflowProcessor** | OrchestrationConnect.cs | 静态类 | 工作流执行核心，包含执行、暂停、恢复、终止等完整控制逻辑 |
| **WorkflowExecutionManager** | OrchestrationFramework.cs | 单例类 | 执行状态管理、任务调度、并发控制、事件处理 |
| **FunctionExecutionFramework** | ExecutionEngine.cs | 静态工具类 | 步骤执行框架，提供装饰器模式（日志、性能监控） |
| **ExecuteProcess** | ExecutionEngine.cs | 静态类 | 节点函数执行处理（参数赋值、管道运行） |
| **NextNodeProcess** | ExecutionEngine.cs | 静态类 | 下一节点连接处理、工作流链更新 |
| **AddNodeProcess** | ExecutionEngine.cs | 静态类 | 节点添加处理（输入输出数据处理） |
| **WorkflowIntegration** | Integration.cs | 实例类 | 函数注册与映射，将业务函数注册到执行框架 |
| **WorkflowFunctions** | Integration.cs | 实例类 | 内置函数实现（Plus/Concat/MergePrefix 等） |

### 1.3 数据储存层现状

| 组件 | 位置 | 形态 | 职责 |
|------|------|------|------|
| **GenericWorkflowStorageManager\<TWorkflow\>** | OrchestrationFramework.cs | 实例类（ECS） | 工作流注册/查询/删除，基于 ECS 架构的内部存储管理 |
| **ChainInfoWorkflowStorageManager** | OrchestrationConnect.cs | 静态单例 | ChainInfo 专用的存储实例管理 |
| **WorkflowStorageManager** | OrchestrationConnect.cs | 静态门面 | 提供给外部调用的工作流存储简化接口 |
| **WebCallManager** | OrchestrationConnect.cs | 静态门面 | 节点配置（WebCallOrReturnConfig）的 CRUD 管理 |
| **GenericConfigManager** | OrchestrationConnect.cs | 实例类 | 配置管理，与工作流存储联动 |
| **UnifiedStorageManager\<TWorkflow, TConfig\>** | OrchestrationFramework.cs | 实例类 | 统一存储管理器，整合工作流存储 + 配置管理 + 执行状态查询 + 缓存 |
| **ChainInfoStorageManager** | OrchestrationFramework.cs | 静态门面 | 对外暴露的统一存储简化 API |

## 2. 耦合分析

### 2.1 直接耦合点

执行代码中大量通过**静态门面**直接访问存储层，以下是关键的耦合路径：

```
WorkflowProcessor.ExecuteWorkflowCoreAsync()
  → WorkflowRuntimeContextManager.GetOrCreateContext(workflowId)      // 上下文管理（混合执行+存储）
  → runtimeCtx.GetChainIdByNode(nodeKey)                              // 查询
  → WorkflowStorageManager.GetWorkflow(chainId)                       // 🔴 静态调用存储
  → WebCallManager.GetOrCreateConfig(nodeKey, chainInfo)              // 🔴 静态调用存储
  → runtimeCtx.GetNodeConfig(nodeKey)                                 // 查询

NextNodeProcess.ProcessWorkflowAndExecuteNode()
  → WebCallManager.GetOrCreateConfig(nextNodeConfig.NextNodeKey)      // 🔴 静态调用存储
  → WorkflowStorageManager.GetWorkflow(config.ChainId)                // 🔴 静态调用存储
  → WorkflowProcessor.ExecuteFromNodeAsync(...)                       // 🔴 静态调用执行

WorkflowManager（WorkflowUseage.cs）
  → 同时代理执行和存储方法，职责未分离                              // 🔴 混合门面

ChainInfoStorageManager.Instance 初始化
  → new UnifiedStorageManager(
      new ChainInfoStorageManagerAdapter(),     // 适配 WorkflowStorageManager（存储）
      new WebCallManagerAdapter(),              // 适配 WebCallManager（存储）
      new ChainInfoNodeExtractorAdapter(),
      new WorkflowProcessorAdapter(),           // 🔴 适配 WorkflowProcessor（执行！）
      config => config.WebCallKey,
      recorder)
  → WorkflowProcessorAdapter 将执行层的状态查询适配到存储层            // 🔴 执行混入存储
```

### 2.2 耦合类型总结

| 耦合类型 | 具体表现 | 严重程度 |
|----------|----------|----------|
| **静态依赖** | 执行代码中直接调用 `WorkflowStorageManager.GetWorkflow()` 等静态方法 | 高 |
| **职责混合** | `WorkflowManager` 同时提供执行和存储方法 | 高 |
| **逻辑混入** | `ChainInfoStorageManager` 依赖 `WorkflowProcessorAdapter`（执行逻辑混入存储层） | 中 |
| **全局状态** | `WebCallManager`、`ChainInfoStorageManager` 等持有全局可变状态 | 高 |
| **隐式依赖** | 通过 `WorkflowRuntimeContext` 间接依赖存储上下文 | 中 |
| **测试困难** | 静态方法无法 Mock，难以单元测试 | 高 |

### 2.3 已存在的接口（未充分利用）

框架已经定义了大量接口，但实际代码仍直接使用静态类：

| 接口 | 位置 | 当前使用情况 |
|------|------|-------------|
| `IWorkflowStorageManager<TWorkflow>` | OrchestrationFramework.cs | 仅被 `GenericWorkflowStorageManager` 实现，外部仍通过静态门面调用 |
| `IWorkflowStateProvider` | OrchestrationFramework.cs | 仅被 `WorkflowProcessorAdapter` 实现 |
| `IWorkflowStateManager` | OrchestrationFramework.cs | 未被外部使用，将在后续版本中被 `IWorkflowController` 替代 |
| `IWorkflowExecutionManager` | OrchestrationFramework.cs | 仅内部使用 |
| `IWorkflowExecutionRecorder` | OrchestrationFramework.cs | 被 `UnifiedStorageManager` 使用 |
| `IStorageAccessor<TWorkflow, TConfig>` | OrchestrationFramework.cs | 仅由 `UnifiedStorageManager` 实现 |
| `ICacheManager` | OrchestrationFramework.cs | 仅由 `UnifiedStorageManager` 实现 |
| `IWorkflowNodeExtractor<TWorkflow>` | OrchestrationFramework.cs | 用于适配器模式 |
| `IConfigManager<TConfig>` | OrchestrationFramework.cs | 用于适配器模式 |
| `IWorkflowFunctions` | Integration.cs | 被 `WorkflowIntegration` 使用，业务函数接口 |
| `IWorkflowFunctionsFactory` | Integration.cs | 未充分利用 |
| `IWorkflowFunctionRegistrar` | Integration.cs | 未充分利用 |
| `IWorkflowMediator` | WorkflowCreateModules.cs | 实现已存在，接口设计良好 |
| `IWorkflowRuntime` | OrchestrationFramework.cs | 运行时数据接口，设计方向正确 |

## 3. 目标架构

### 3.1 分层设计

将系统拆分为三个明确的层，通过接口连接：

```
┌─────────────────────────────────────────────────────────────────┐
│                        应用程序 / 入口                            │
│             只依赖抽象接口，不依赖具体实现                          │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                     执行函数层（Execution Layer）                  │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │  IWorkflowExecutionManager   ← 执行控制接口                  │ │
│  │  IWorkflowFunctions          ← 业务函数注册接口              │ │
│  │  IWorkflowMediator           ← 编排中介接口                  │ │
│  │  IExecutionContext           ← 执行上下文接口                │ │
│  │  IExecutionStep              ← 执行步骤接口                  │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                │                              ▲                   │
│                │ 依赖接口                      │ 实现接口           │
│                ▼                              │                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │  执行实现（WorkflowProcessor / ExecutionEngine / ...）       │ │
│  │  只通过 IWorkflowStorageProvider 接口访问存储                │ │
│  └─────────────────────────────────────────────────────────────┘ │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            │ IWorkflowStorageProvider（存储服务接口）
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                     数据储存层（Storage Layer）                   │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │  IWorkflowStorageProvider    ← 存储服务接口（给执行层用）     │ │
│  │  IWorkflowRepository         ← 工作流仓储接口                │ │
│  │  IConfigRepository           ← 配置仓储接口                  │ │
│  │  IExecutionStateStore        ← 执行状态存储接口              │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                │                              ▲                   │
│                │ 依赖接口                      │ 实现接口           │
│                ▼                              │                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │  存储实现（GenericWorkflowStorageManager / ...）             │ │
│  │  纯数据管理，不包含任何执行逻辑                              │ │
│  └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 核心原则

| 原则 | 说明 |
|------|------|
| **单向依赖** | 执行层依赖存储层的接口，存储层不依赖执行层 |
| **接口隔离** | 每个接口职责单一，执行层只看到它需要的存储能力 |
| **依赖反转** | 高层（执行层）定义存储接口，低层（存储层）实现接口 |
| **无全局状态** | 所有依赖通过构造函数注入，消除静态门面 |
| **可测试性** | 接口可 Mock，执行层和存储层可独立测试 |

## 4. 接口设计

### 4.1 存储服务接口（给执行层使用）

执行层需要从存储层获取以下能力。设计上**推荐按职责拆分为多个小接口**（遵循接口隔离原则），同时也提供聚合接口作为便捷门面：

#### 聚合接口（便捷门面，按职责拆分才是推荐做法）

```csharp
/// <summary>
/// 聚合的存储服务接口。此为便捷门面，集合了多个职责。
/// 推荐按下方拆分为多个小接口使用，遵循接口隔离原则。
/// </summary>
public interface IWorkflowStorageProvider
{
    // ─── 工作流查询 ───
    ChainInfo GetWorkflow(string chainId);
    string GetWorkflowIdForNode(string nodeKey);
    bool IsNodeInAnyWorkflow(string nodeId);

    // ─── 工作流写入 ───
    string RegisterOrUpdateWorkflow(ChainInfo chainInfo);
    void RemoveWorkflow(string workflowId);

    // ─── 配置管理 ───
    WebCallOrReturnConfig GetOrCreateConfig(string key, ChainInfo chainInfo = null);
    bool TryGetConfig(string key, out WebCallOrReturnConfig config);
    void RemoveConfig(string key);

    // ─── 执行状态查询 ───
    WorkflowExecutionStatus GetExecutionStatus(string executionId);
    string GetCurrentNode(string executionId);
    IEnumerable<string> GetExecutionIds();

    // ─── 监控 ───
    StorageStats GetStats();
}
```

#### 按职责拆分的小接口（推荐使用）：

```csharp
/// <summary> 工作流仓储：只关心工作流定义的持久化 </summary>
public interface IWorkflowRepository
{
    ChainInfo GetWorkflow(string chainId);
    string RegisterOrUpdateWorkflow(ChainInfo chainInfo);
    void RemoveWorkflow(string workflowId);
    string GetWorkflowIdForNode(string nodeKey);
    bool IsNodeInAnyWorkflow(string nodeId);
    IEnumerable<string> GetAllWorkflowIds();
}

/// <summary> 配置仓储：只关心节点配置的持久化 </summary>
public interface IConfigRepository
{
    WebCallOrReturnConfig GetOrCreateConfig(string key, ChainInfo chainInfo = null);
    bool TryGetConfig(string key, out WebCallOrReturnConfig config);
    void RemoveConfig(string key);
    IEnumerable<string> GetAllConfigKeys();
}

/// <summary> 执行状态存储：只关心运行时执行状态的持久化 </summary>
public interface IExecutionStateStore
{
    WorkflowExecutionStatus GetExecutionStatus(string executionId);
    string GetCurrentNode(string executionId);
    IEnumerable<string> GetExecutionIds();
    ExecutionDetail GetExecutionDetail(string executionId);
}

/// <summary> 存储层监控统计信息 </summary>
public class StorageStats
{
    /// <summary> 已注册的工作流数量 </summary>
    public int WorkflowCount { get; set; }

    /// <summary> 配置项数量 </summary>
    public int ConfigCount { get; set; }

    /// <summary> 活跃执行记录数 </summary>
    public int ActiveExecutionCount { get; set; }

    /// <summary> 缓存命中率（百分比 0-100） </summary>
    public double CacheHitRate { get; set; }
}
```

### 4.2 执行层接口（给外部使用）

```csharp
/// <summary> 工作流执行器：核心执行能力 </summary>
public interface IWorkflowExecutor
{
    Task<WorkflowExecutionResult> ExecuteWorkflowAsync(
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);

    Task<WorkflowExecutionResult> ExecuteFromNodeAsync(
        string nodeKey,
        Dictionary<string, object> parameters = null,
        CancellationToken cancellationToken = default);

    Task<WorkflowExecutionResult> ResumeWorkflowFromNodeAsync(
        string existingExecutionId,
        string nodeKey,
        Dictionary<string, object> parameters = null,
        CancellationToken cancellationToken = default);
}

/// <summary> 工作流控制器：执行控制能力 </summary>
public interface IWorkflowController
{
    bool PauseWorkflow(string executionId);
    bool ResumeWorkflow(string executionId);
    bool TerminateWorkflow(string executionId);
    bool PauseAllWorkflows();
    bool ResumeAllWorkflows();
    bool TerminateAllWorkflows();
}

/// <summary> 业务函数注册中心 </summary>
public interface IFunctionRegistry
{
    void RegisterFunction(string name, Func<object[], object> function);
    void RegisterAsyncFunction(string name, Func<object[], Task<object>> function);
    object CallFunction(string name, params object[] args);
    Task<object> CallFunctionAsync(string name, params object[] args);
    bool HasFunction(string name);
}

/// <summary> 编排中介 </summary>
public interface IWorkflowMediator
{
    ChainInfo<TNode> ConnectChains<TNode>(ConnectRequest request)
        where TNode : NodeConfig;
    ChainInfo<TNode> BuildFromConfig<TNode>(ChainConfig config)
        where TNode : NodeConfig;
}
```

### 4.3 执行层入口接口（聚合）

```csharp
/// <summary>
/// WorkFlowMethod 对外暴露的统一编程模型入口。
/// 应用程序通过此接口与框架交互，无需直接操作静态类。
/// </summary>
public interface IWorkFlowMethod
{
    // ─── 执行 ───
    IWorkflowExecutor Executor { get; }
    IWorkflowController Controller { get; }

    // ─── 函数注册 ───
    IFunctionRegistry Functions { get; }

    // ─── 编排 ───
    IWorkflowMediator Mediator { get; }

    // ─── 构建 ───
    IWorkflowBuilder Builder { get; }

    // ─── 查询（只读，委托到存储层）───
    ChainInfo GetWorkflow(string workflowId);
    WorkflowExecutionStatus GetExecutionStatus(string executionId);
    StorageStats GetStats();

    // ─── 生命周期 ───
    void Dispose();
}
```

## 5. 实施步骤

### 第一阶段：接口提取与静态封装（安全过渡）

**目标**：在不修改现有代码行为的前提下，提取接口并创建封装层，使新代码可以面向接口编程。

**步骤**：

1. **提取 `IWorkflowRepository` 接口**
   - 从 `WorkflowStorageManager` 和 `GenericWorkflowStorageManager` 提取方法签名
   - 创建接口文件，放入专门的 `Contracts/` 目录

2. **提取 `IConfigRepository` 接口**
   - 从 `WebCallManager` 和 `GenericConfigManager` 提取方法签名

3. **提取 `IWorkflowExecutor` 和 `IWorkflowController` 接口**
   - 从 `WorkflowProcessor` 提取执行和控制方法签名

4. **创建静态封装适配器**（过渡期使用）
   ```csharp
   // 在过渡期，让接口实现委托给现有的静态类
   public class WorkflowRepositoryAdapter : IWorkflowRepository
   {
       public ChainInfo GetWorkflow(string chainId)
           => WorkflowStorageManager.GetWorkflow(chainId);

       public string RegisterOrUpdateWorkflow(ChainInfo chainInfo)
           => WorkflowStorageManager.RegisterOrUpdateWorkflow(chainInfo);
       // ...
   }
   ```

**验证标准**：
- 所有接口定义完成，编译通过
- 现有代码不受影响
- 新代码可以面向接口编程

### 第二阶段：消除执行→存储的直接静态依赖

**目标**：让执行层的核心代码（`WorkflowProcessor`、`NextNodeProcess` 等）通过接口访问存储，而非直接调用静态门面。

**步骤**：

1. **改造 `WorkflowProcessor` 为实例类**
   ```csharp
   public class WorkflowProcessor : IWorkflowExecutor, IWorkflowController
   {
       private readonly IWorkflowRepository _workflowRepository;
       private readonly IConfigRepository _configRepository;
       private readonly IWorkflowExecutionManager _executionManager;

       public WorkflowProcessor(
           IWorkflowRepository workflowRepository,
           IConfigRepository configRepository,
           IWorkflowExecutionManager executionManager)
       {
           _workflowRepository = workflowRepository;
           _configRepository = configRepository;
           _executionManager = executionManager;
       }
       // ... 所有存储访问改为通过 _workflowRepository / _configRepository
   }
   ```

2. **改造 `NextNodeProcess` 为实例类**
   ```csharp
   public class NextNodeProcess
   {
       private readonly IWorkflowRepository _workflowRepository;
       private readonly IConfigRepository _configRepository;
       private readonly IWorkflowExecutor _workflowExecutor;

       public NextNodeProcess(
           IWorkflowRepository workflowRepository,
           IConfigRepository configRepository,
           IWorkflowExecutor workflowExecutor)
       { ... }
   }
   ```

3. **消除 `ChainInfoStorageManager` 对 `WorkflowProcessorAdapter` 的依赖**
   - 执行状态查询功能应从执行层接口获取，而非存储层去适配执行层
   - `IExecutionStateStore` 由执行层实现并注册到存储层（如果需要持久化）

**验证标准**：
- `WorkflowProcessor` 不再直接调用任何静态存储门面
- 所有存储访问通过构造函数注入的接口
- 单元测试可通过 Mock `IWorkflowRepository`/`IConfigRepository` 进行

### 第三阶段：重构 `WorkflowManager` 为实例门面

**目标**：将 `WorkflowManager` 从静态门面改造为实例门面，通过组合方式提供统一的编程入口。

**步骤**：

1. **创建 `WorkFlowMethodService` 类**
   ```csharp
   public class WorkFlowMethodService : IWorkFlowMethod, IDisposable
   {
       public IWorkflowExecutor Executor { get; }
       public IWorkflowController Controller { get; }
       public IFunctionRegistry Functions { get; }
       public IWorkflowMediator Mediator { get; }
       public IWorkflowBuilder Builder { get; }

       private readonly IWorkflowRepository _workflowRepository;

       public WorkFlowMethodService(
           IWorkflowExecutor executor,
           IWorkflowController controller,
           IFunctionRegistry functionRegistry,
           IWorkflowMediator mediator,
           IWorkflowBuilder builder,
           IWorkflowRepository workflowRepository)
       {
           Executor = executor;
           Controller = controller;
           Functions = functionRegistry;
           Mediator = mediator;
           Builder = builder;
           _workflowRepository = workflowRepository;
       }

       public ChainInfo GetWorkflow(string workflowId)
           => _workflowRepository.GetWorkflow(workflowId);

       // ... 委托方法
   }
   ```

2. **提供 DI 注册扩展方法**
   ```csharp
   public static class WorkFlowMethodServiceCollectionExtensions
   {
       public static IServiceCollection AddWorkFlowMethod(
           this IServiceCollection services,
           Action<WorkFlowMethodOptions> configureOptions = null)
       {
           // 注册存储层
           services.AddSingleton<IWorkflowRepository, WorkflowRepositoryAdapter>();
           services.AddSingleton<IConfigRepository, ConfigRepositoryAdapter>();

           // 注册执行层
           services.AddSingleton<IWorkflowExecutor, WorkflowProcessor>();
           services.AddSingleton<IWorkflowController, WorkflowProcessor>();

           // 注册门面
           services.AddSingleton<IWorkFlowMethod, WorkFlowMethodService>();

           return services;
       }
   }
   ```

**验证标准**：
- 应用程序可通过 DI 容器获取 `IWorkFlowMethod` 入口
- 旧的静态 `WorkflowManager` 仍然可用（向后兼容）
- 新代码通过依赖注入获取实例化服务

### 第四阶段：移除全局状态，完善持久化实现

**目标**：消除所有静态全局状态，支持多种存储后端。

**步骤**：

1. **消除 `WebCallManager` 的静态单例**
   - 将配置缓存移到 `ConfigRepository` 实现内部
   - 通过 DI 管理生命周期

2. **消除 `ChainInfoStorageManager` 的静态单例**
   - 将统一存储管理器的职责拆分到各仓储实现中
   - 缓存策略由各仓储内部管理

3. **消除 `WorkflowRuntimeContextManager` 的全局状态**
   - 运行时上下文应通过执行层管理，而非全局静态

4. **提供多种存储实现**
   ```csharp
   // 内存实现（测试用）
   public class InMemoryWorkflowRepository : IWorkflowRepository { ... }

   // SQL Server 实现（生产用）
   public class SqlServerWorkflowRepository : IWorkflowRepository { ... }
   ```

**验证标准**：
- 无任何静态类持有全局可变状态
- 可同时运行多个 `WorkFlowMethodService` 实例（隔离性）
- 支持切换存储后端

## 6. 接口依赖关系图

```
                    ┌──────────────────────┐
                    │   IWorkFlowMethod    │ ← 应用程序入口
                    └──────┬───────────────┘
                           │
              ┌────────────┼────────────────┐
              │            │                 │
              ▼            ▼                 ▼
   ┌──────────────────┐ ┌────────────┐ ┌──────────────┐
   │ IWorkflowExecutor│ │IWorkflow- │ │IFunction-    │
   │ IWorkflow-      │ │Controller │ │Registry      │
   │ Controller      │ │           │ │              │
   └──────┬───────────┘ └───────────┘ └──────────────┘
          │
          │ 依赖
          ▼
   ┌──────────────────────────────────────────────┐
   │         执行实现层（实现类）                    │
   │  WorkflowProcessor                           │
   │  FunctionExecutionFramework                   │
   │  ExecuteProcess / NextNodeProcess             │
   └──────────────┬───────────────────────────────┘
                  │ 依赖接口（不依赖实现）
                  ▼
   ┌──────────────────────────────────────────────┐
   │        IWorkflowRepository                   │
   │        IConfigRepository                     │
   │        IExecutionStateStore                  │
   └──────────────┬───────────────────────────────┘
                  │ 实现
                  ▼
   ┌──────────────────────────────────────────────┐
   │         存储实现层                              │
   │  GenericWorkflowStorageManager实现            │
   │  InMemoryWorkflowRepository                   │
   │  SqlServerWorkflowRepository                  │
   │  ...                                         │
   └──────────────────────────────────────────────┘
```

## 7. 文件目录结构建议

```
WorkFlowMethod/
├── Contracts/                      # 接口定义层（不依赖任何具体实现）
│   ├── IWorkflowExecutor.cs
│   ├── IWorkflowController.cs
│   ├── IWorkflowRepository.cs
│   ├── IConfigRepository.cs
│   ├── IExecutionStateStore.cs
│   ├── IWorkflowStorageProvider.cs
│   ├── IFunctionRegistry.cs
│   ├── IWorkflowMediator.cs
│   └── IWorkFlowMethod.cs
│
├── Contracts/                     # 接口定义
│   ├── IFunctionRegistry.cs        # 函数注册接口（新增 ✅ 已完成）
│   ├── FunctionContext.cs          # 函数执行上下文（新增 ✅ 已完成）
│   ├── FunctionNotFoundException.cs # 函数未找到异常（新增 ✅ 已完成）
│   └── ...
│
├── Functions/                      # 函数库实现
│   ├── DefaultFunctionRegistry.cs  # 默认函数注册表（新增 ✅ 已完成）
│   └── ...
│
├── ExecutionEngine/                # 执行层实现
│   ├── WorkflowProcessor.cs        # 改为实例类，实现 IWorkflowExecutor
│   ├── ExecutionEngine.cs          # FunctionExecutionFramework 等
│   ├── Integration.cs              # WorkflowIntegration + BuiltInFunctionRegistrar（✅ 已新增）
│   ├── CallFrame.cs                # AICallFrame 等
│   └── NodeProcessors/             # 节点处理器
│       ├── ExecuteProcess.cs
│       ├── NextNodeProcess.cs
│       └── AddNodeProcess.cs
│
├── Storage/                        # 存储层实现
│   ├── IWorkflowStorageManager.cs  # 现有接口
│   ├── GenericWorkflowStorageManager.cs
│   ├── WorkflowRepositoryAdapter.cs  # 过渡用适配器
│   ├── ConfigRepositoryAdapter.cs
│   ├── InMemoryWorkflowRepository.cs
│   ├── SqlServerWorkflowRepository.cs
│   └── UnifiedStorageManager.cs    # 可保留或拆分
│
├── Orchestration/                  # 编排层（现有代码）
│   ├── OrchGraph.cs
│   ├── OrchestrationConnect.cs
│   ├── OrchestrationFramework.cs
│   └── WorkflowCreateModules.cs
│
├── Di/                             # 依赖注入配置
│   └── WorkFlowMethodServiceCollectionExtensions.cs
│
└── WorkflowUseage.cs               # 保持向后兼容的静态门面
```

## 8. 向后兼容策略

### 8.1 过渡期方案

在过渡期内，旧的静态门面保持不变，新接口通过适配器委托给现有实现：

```
旧代码路径（不变）:
  WorkflowManager.ExecuteAsync(...)
    → WorkflowProcessor.ExecuteWorkflowAsync(...)         // 静态
      → WorkflowStorageManager.GetWorkflow(...)           // 静态

新代码路径（面向接口）:
  IWorkFlowMethod workflow = serviceProvider.GetService<IWorkFlowMethod>();
  await workflow.Executor.ExecuteWorkflowAsync(parameters);
    → WorkflowProcessor(实例).ExecuteWorkflowAsync(...)
      → _workflowRepository.GetWorkflow(...)              // 接口
        → WorkflowRepositoryAdapter
          → WorkflowStorageManager.GetWorkflow(...)       // 静态（过渡）
```

### 8.2 版本演进计划（存储分离）

| 版本 | 变更 | API 兼容性 |
|------|------|-----------|
| 当前 | 全部静态类，无接口分离 | - |
| S-v1（接口提取） | 创建接口 + 适配器，静态类不变 | ✅ 完全向后兼容 |
| S-v2（执行层改造） | WorkflowProcessor 改为实例类，旧静态门面保留委托 | ✅ 向后兼容 |
| S-v3（存储层改造） | 存储层改为接口实现，可切换后端 | ⚠️ 配置接口可能微调 |
| S-v4（清理） | 删除废弃的静态门面 | ❌ 破坏性变更（需大版本） |

## 9. 预期收益

| 维度 | 当前 | 改造后 |
|------|------|--------|
| **可测试性** | 存储和执行的静态依赖难以 Mock，几乎无法单元测试 | 全部接口化，可轻松 Mock，支持纯单元测试 |
| **可扩展性** | 存储实现固定（ECS），切换或扩展困难 | 支持多种存储后端（内存/SQL/NoSQL），可自定义 |
| **可维护性** | 职责混合，静态全局状态导致隐式耦合 | 职责清晰，依赖可见，修改影响范围可控 |
| **并发安全性** | 全局静态字典 + 读写锁，风险较高 | 实例隔离，每个服务实例有独立状态 |
| **部署灵活性** | 单例模式，无法在同一个进程中运行多个隔离实例 | 支持多实例，适用于多租户场景 |
| **框架集成** | 不支持 DI 容器，需手动管理初始化 | 原生支持 Microsoft.Extensions.DependencyInjection |

---

## 10. 函数库外迁方案

### 10.1 函数库耦合现状

除了执行层与存储层的耦合外，框架中的**业务函数库**也存在严重的耦合问题。函数库的调用链路如下：

```
ExecuteProcess.ExecuteProcessFuncAsync()                    // ExecutionEngine.cs
  → new DictionaryMapFuncs().MappingFunction(new ...)()     // 每次执行都新建空的 FunctionMap
  → uNode.ExeConfig.FuncPipeline.FunctionMap = ...          // 设给管道的 FunctionMap
  → FuncParameterSet(uNode)                                 // 从 GraphUnit.dic 读参数
  → FuncCallRun.PipelineRunAsync(funcPipeline)              // OrchestrationConnect.cs
    → funcPipeline.FunctionMap[call.FuncName]               // 查函数名 → 委托
      → func.DynamicInvoke(params)                          // 反射调用
```

### 10.2 函数调用链路角色梳理

| 角色 | 组件 | 位置 | 问题 |
|------|------|------|------|
| **函数定义** | 工作流节点 JSON 中的 `FuncName` | 配置文件 / WorkflowBuilder | 字符串引用函数名，无编译期检查 |
| **参数来源** | `GraphUnit.dic`（全局 `Dictionary<string, object>`） | ExecutionEngine.cs / 外部全局 | 全局可变静态字典，测试困难 |
| **函数映射** | `FuncPipelineParameters.FunctionMap` | OrchestrationConnect.cs | 每个执行周期新建，无全局注册机制 |
| **函数查找** | `DictionaryMapFuncs.MappingFunction()` | 外部包 `Common.GraphMindStudio.Function` | 黑盒反射扫描，行为不可控 |
| **函数调用** | `FuncCallRun.PipelineRunAsync()` | OrchestrationConnect.cs | `DynamicInvoke` 弱类型调用 |
| **内置函数** | `WorkflowFunctions`（Plus/Int/Concat等） | Integration.cs | 硬编码嵌入框架，不可替换 |
| **流程函数** | `FlowAction`（AI生图/代码生成等） | WrokFlow_Script.cs | 工作流模板耦合在使用脚本中 |
| **外部注册** | `FunctionRegistry.Default` | 外部包 `UniversalWorkflowFramework` | 框架间依赖不透明 |

### 10.3 核心耦合问题

#### 问题 1：FunctionMap 每次执行都重建

```csharp
// ExecuteProcess.ExecuteProcessFuncAsync() - ExecutionEngine.cs:917
uNode.ExeConfig.FuncPipeline.FunctionMap =
    new DictionaryMapFuncs().MappingFunction(new Dictionary<string, object>());
```

- 每次执行节点时都创建新的空 `FunctionMap`
- `DictionaryMapFuncs.MappingFunction` 接收空字典，通过反射自动扫描程序集中的 `Action`/`Func` 来填充
- 执行者无法控制实际注册了哪些函数

#### 问题 2：参数通过全局静态字典传递

```csharp
// ExecuteProcess.FuncParameterSet() - ExecutionEngine.cs:933
var value = executeoperations.GetPathGetValue<object>(GraphUnit.dic, parameterKey);
```

- `GraphUnit.dic` 是 `Common.GraphMindStudio.DataSet.globalvariableset.dataStorage` 的别名
- 所有工作流执行共享同一个全局字典
- 并发执行时存在数据竞争风险
- 无法隔离不同执行的参数空间

#### 问题 3：函数调用的弱类型和不可见性

```csharp
// FuncCallRun.PipelineRunAsync() - OrchestrationConnect.cs:424
if (action is Delegate func)
{
    parameter = func.DynamicInvoke((call.Parameters ?? new List<object>()).ToArray());
}
```

- 使用 `Delegate.DynamicInvoke`，无编译期类型安全
- 函数签名约定为 `object[] → object`，所有类型转换在运行时
- 外部调用者无法知道有哪些函数可用

#### 问题 4：内置函数硬编码

`WorkflowFunctions` 类包含了 Plus、Int、Float、Double、String、MergePrefix、Concat、GetPathValue、SetPathValue、ExecuteWorkflow 等函数，直接硬编码在 `Integration.cs` 中，外部无法替换或扩展这些实现。

#### 问题 5：工作流级函数不可配置

`WrokFlow_Script.cs` 中的 `FlowAction` 字典映射了具体的工作流执行函数（AI生图工作流、AI代码工作流等），这些是工作流模板级别而非节点级别，外部无法在调用时配置行为。

### 10.4 目标架构：函数库外迁

```
┌─────────────────────────────────────────────────────────────────┐
│                     外部调用者（你的代码）                        │
│                                                                  │
│  IWorkFlowMethod  service = ...                                 │
│  service.Functions.Register("MyFunc", args => { ... })          │
│  service.Functions.Register("HttpCall", async args => { ... })  │
│                                                                  │
│  await service.Executor.ExecuteWorkflowAsync(                   │
│      new ExecutionRequest("startNode")                          │
│      {                                                          │
│          Parameters = new { input = "hello" },                  │
│          FunctionOverrides = { "WebInvoke" = myWebInvoke }      │
│      });                                                        │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                   WorkFlowMethod 服务实例                        │
│                                                                  │
│  ┌──────────────────────┐  ┌──────────────────────────────────┐ │
│  │  IFunctionRegistry   │  │  执行引擎                         │ │
│  │  （全局函数注册表）    │  │                                  │ │
│  │                      │  │  WorkflowProcessor               │ │
│  │  ├─ 内置函数          │  │    → FuncCallRun                 │ │
│  │  │  (Plus/Concat/...)│  │      → registry.CallFunction()   │ │
│  │  │                   │  │        (不再查 FunctionMap)       │ │
│  │  ├─ 用户注册函数      │  │                                  │ │
│  │  │  (MyFunc/HttpCall)│  │  ExecutionRequest                 │ │
│  │  │                   │  │    → Parameters（隔离参数字典）    │ │
│  │  └─ 执行级覆盖函数    │  │    → FunctionOverrides           │ │
│  │     (per-request)    │  │    → ContextData（执行上下文）     │ │
│  └──────────────────────┘  └──────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

#### 核心原则

| 原则 | 说明 |
|------|------|
| **全局注册，执行隔离** | 函数在服务初始化时全局注册，但每次执行的参数和覆盖函数隔离 |
| **类型安全** | 强类型函数签名替代 `DynamicInvoke` |
| **可替换** | 内置函数可按名覆盖，外部可注册自定义函数 |
| **参数隔离** | 每次执行有独立的参数字典，不共享全局 `GraphUnit.dic` |
| **执行级注入** | 外部可在调用时注入覆盖函数或上下文数据 |

### 10.5 接口设计

#### 函数注册接口

```csharp
/// <summary> 工作流节点函数的统一注册与调用入口 </summary>
public interface IFunctionRegistry
{
    /// <summary> 注册同步函数 </summary>
    void Register(string name, Func<FunctionContext, object> function);

    /// <summary> 注册异步函数 </summary>
    void RegisterAsync(string name, Func<FunctionContext, Task<object>> function);

    /// <summary> 调用函数（由执行引擎内部使用） </summary>
    object Call(string name, FunctionContext context);

    /// <summary> 异步调用函数 </summary>
    Task<object> CallAsync(string name, FunctionContext context);

    /// <summary> 是否存在指定名称的函数 </summary>
    bool HasFunction(string name);

    /// <summary> 获取所有已注册的函数名 </summary>
    IEnumerable<string> GetFunctionNames();
}

/// <summary> 函数执行上下文（替代全局 GraphUnit.dic） </summary>
public class FunctionContext
{
    /// <summary> 当前执行的参数（隔离的参数字典，不共享全局） </summary>
    public Dictionary<string, object> Parameters { get; }

    /// <summary> 执行级别的上下文数据（可读写） </summary>
    public Dictionary<string, object> Data { get; }

    /// <summary> 执行 ID </summary>
    public string ExecutionId { get; }

    /// <summary> 当前节点 key </summary>
    public string NodeKey { get; }

    /// <summary> 工作流 ID </summary>
    public string WorkflowId { get; }

    // 强类型参数读取
    T GetParameter<T>(int index);
    T GetParameter<T>(string key);
    T GetData<T>(string key);
}
```

#### 执行请求接口（外部调用时传入）

```csharp
/// <summary> 执行请求（外部调用的入参） </summary>
public class ExecutionRequest
{
    /// <summary> 起始节点 Key </summary>
    public string StartNodeKey { get; set; }

    /// <summary> 参数（替代 GraphUnit.dic） </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary> 执行级函数覆盖（按名覆盖已注册的函数） </summary>
    public Dictionary<string, Func<FunctionContext, object>> FunctionOverrides { get; set; }

    /// <summary> 异步函数覆盖 </summary>
    public Dictionary<string, Func<FunctionContext, Task<object>>> AsyncFunctionOverrides { get; set; }

    /// <summary> 执行上下文数据（传递给 FunctionContext.Data） </summary>
    public Dictionary<string, object> ContextData { get; set; } = new();
}

/// <summary> 工作流执行器扩展 </summary>
public interface IWorkflowExecutor
{
    Task<WorkflowExecutionResult> ExecuteAsync(ExecutionRequest request,
        CancellationToken cancellationToken = default);
}
```

### 10.6 函数调用链路对比

#### 当前链路（✅ 已完成迁移至 F-v4）

```
ExecuteProcessFuncAsync()
  → FuncParameterSet()                                     // 从 GraphUnit.dic 读参数（旧路径保留）
  → FuncCallRun.PipelineRunAsync(funcPipeline)              // 原始重载
    → 检测 AsyncLocal<IFunctionRegistry>
    → 如有注册表 → 委托到 PipelineRunAsync(funcPipeline, registry, context)
    → 如无注册表 → 仅处理 PipelineGroup 递归，不调用函数

// 新入口（F-v3/F-v4 推荐路径）：
WorkflowProcessor.ExecuteWorkflowWithRegistryAsync(...)
  → 设置 AsyncLocal<IFunctionRegistry> + AsyncLocal<FunctionContext>
  → ExecuteProcessFuncAsync(uNode, debug, parameters, registry)
    → FuncParameterSet(uNode, parameters)                  // 从隔离参数字典读取
    → FuncExecute(uNode, output, registry)
      → FuncCallRun.PipelineRunAsync(funcPipeline, registry, context)
        → registry.Call(funcName, callContext)             // 强类型调用
          → 先查 FunctionOverrides（执行级覆盖）
          → 再查 IFunctionRegistry（全局注册）
          → func.Invoke(context)

// 外部调用入口（F-v3）：
var executor = new WorkflowExecutor(registry);
await executor.ExecuteAsync(new ExecutionRequest
{
    StartNodeKey = "node_0",
    Parameters = new Dictionary<string, object> { { "key", "value" } },
    FunctionOverrides = { { "MyFunc", ctx => ... } },
    ContextData = { { "traceId", "abc" } }
});
```

### 10.7 函数作用域层次

函数查找优先级从高到低：

```
1. 执行级覆盖（ExecutionRequest.FunctionOverrides）
    └─ 只对单次执行生效，调用者可在外部调用时临时替换

2. 全局注册（IFunctionRegistry.Register）
    └─ 服务生命周期内注册，所有执行共享

3. 内置默认函数（Built-in Functions）
    └─ 框架自带的 Plus/Int/Concat 等，可被 1 和 2 覆盖
```

### 10.8 实施步骤

> ✅ **第一阶段已完成**（对应 F-v1 + F-v2），具体代码位置见下方。

#### 第一阶段：提取函数上下文与函数注册接口（✅ 已完成）

**目标**：定义函数执行上下文和统一注册接口，消除 `GraphUnit.dic` 全局依赖和 `FunctionMap` 字典。

**新增文件**：
- [ExecutionEngine/FunctionContext.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/FunctionContext.cs) — 函数执行上下文
- [ExecutionEngine/IFunctionRegistry.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/IFunctionRegistry.cs) — 函数注册接口
- [ExecutionEngine/DefaultFunctionRegistry.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/DefaultFunctionRegistry.cs) — 默认实现
- [ExecutionEngine/FunctionNotFoundException.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/FunctionNotFoundException.cs) — 自定义异常

**修改文件**：
- [Orchestration/OrchestrationConnect.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/Orchestration/OrchestrationConnect.cs) — 新增 `PipelineRunAsync` 重载 + `CreateCallContext`
- [ExecutionEngine/Integration.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/Integration.cs) — 新增 `BuiltInFunctionRegistrar`

**步骤**：

1. **创建 `FunctionContext` 类**（作为 `IFunctionRegistry` 的依赖类型）
   ```csharp
   public class FunctionContext
   {
       public Dictionary<string, object> Parameters { get; }
       public Dictionary<string, object> Data { get; }
       public string ExecutionId { get; }
       public string NodeKey { get; }
       public string WorkflowId { get; }

       public T GetParameter<T>(int index) { ... }
       public T GetParameter<T>(string key) { ... }
       public T GetData<T>(string key) { ... }
   }
   ```

2. **创建 `IFunctionRegistry` 接口**，包含 Register/Call/CallAsync/HasFunction/GetFunctionNames 方法

3. **创建 `DefaultFunctionRegistry` 实现**
   ```csharp
   public class DefaultFunctionRegistry : IFunctionRegistry
   {
       private readonly Dictionary<string, Func<FunctionContext, object>> _sync = new();
       private readonly Dictionary<string, Func<FunctionContext, Task<object>>> _async = new();

       public void Register(string name, Func<FunctionContext, object> func)
           => _sync[name] = func ?? throw new ArgumentNullException(nameof(func));

       public void RegisterAsync(string name, Func<FunctionContext, Task<object>> func)
           => _async[name] = func ?? throw new ArgumentNullException(nameof(func));

       public object Call(string name, FunctionContext context)
       {
           if (_sync.TryGetValue(name, out var func))
               return func(context);
           if (_async.TryGetValue(name, out var asyncFunc))
               return asyncFunc(context).GetAwaiter().GetResult();
           throw new FunctionNotFoundException(name);
       }

       public async Task<object> CallAsync(string name, FunctionContext context)
       {
           if (_async.TryGetValue(name, out var asyncFunc))
               return await asyncFunc(context);
           if (_sync.TryGetValue(name, out var func))
               return func(context);
           throw new FunctionNotFoundException(name);
       }

       public bool HasFunction(string name)
           => _sync.ContainsKey(name) || _async.ContainsKey(name);

       public IEnumerable<string> GetFunctionNames()
           => _sync.Keys.Concat(_async.Keys).Distinct();
   }
   ```

3. **改造 `FuncCallRun.PipelineRunAsync`**，新增通过 `IFunctionRegistry` 查找函数的重载，`FunctionMap` 降级为回退
   ```csharp
   // 原始重载（向后兼容，保留不动）
   if (funcPipeline.FunctionMap.TryGetValue(call.FuncName, out var action))
       if (action is Delegate func)
           parameter = func.DynamicInvoke(params);

   // 新增重载（F-v2）——优先通过 IFunctionRegistry
   var callContext = CreateCallContext(call, context);
   if (functionRegistry.HasFunction(call.FuncName))
       parameter = functionRegistry.Call(call.FuncName, callContext);
   else if (funcPipeline.FunctionMap.TryGetValue(call.FuncName, out var action))
       parameter = func.DynamicInvoke(params);  // 降级回退
   ```

4. **将内置函数迁移到 `BuiltInFunctionRegistrar`**
   ```csharp
   public static class BuiltInFunctionRegistrar
   {
       public static void RegisterTo(IFunctionRegistry registry)
       {
           registry.Register("Plus", ctx => {
               var a = ctx.GetParameter<object>("0");
               var b = ctx.GetParameter<object>("1");
               return Arithmetics.Plus(new List<object> { a, b });
           });
           registry.Register("Concat", ctx => {
               return string.Join("", ctx.Parameters.Values.Select(v => v?.ToString()));
           });
           // ... 其他内置函数
       }
   }
   ```

**验证标准**：
- ✅ `FuncCallRun.PipelineRunAsync` 新增通过 `IFunctionRegistry` 调用的重载
- ✅ 所有内置函数通过 `BuiltInFunctionRegistrar.RegisterTo()` 注册
- ✅ 外部可通过 `registry.Register("MyFunc", ...)` 添加自定义函数
- ✅ `FunctionMap` 降级为回退路径，旧代码无需修改
- ✅ 编译通过

#### 第二阶段：支持执行级函数覆盖和参数隔离（✅ 已完成）

**目标**：外部调用时可通过 `ExecutionRequest` 注入覆盖函数和参数。

**新增文件**：
- [ExecutionEngine/ExecutionRequest.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/ExecutionRequest.cs) — 执行请求参数
- [ExecutionEngine/IWorkflowExecutor.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/IWorkflowExecutor.cs) — 执行器接口
- [ExecutionEngine/WorkflowExecutor.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/WorkflowExecutor.cs) — 执行器实现
- [ExecutionEngine/OverridableFunctionRegistry.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/OverridableFunctionRegistry.cs) — 函数覆盖包装器
- [ExecutionEngine/FunctionRegistryExtensions.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/FunctionRegistryExtensions.cs) — CallWithOverrides 扩展

**修改文件**：
- [Orchestration/OrchestrationConnect.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/Orchestration/OrchestrationConnect.cs) — 新增 `ExecuteWorkflowWithRegistryAsync` + `AsyncLocal` 注册表
- [ExecutionEngine/ExecutionEngine.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/ExecutionEngine.cs) — 新增 `ExecuteProcessFuncAsync` 重载（参数隔离）

**步骤**：

1. **创建 `ExecutionRequest` 类**
2. **创建 `IWorkflowExecutor` 接口** + `WorkflowExecutor` 实现
3. **实现执行级覆盖逻辑**：通过 `OverridableFunctionRegistry` 包装器，在 `Call` 中先查 `FunctionOverrides`，再查全局注册
4. **参数隔离**：`ExecuteProcessFuncAsync` 新增重载接受独立 `Dictionary<string, object> parameters`，不再依赖 `GraphUnit.dic`

**验证标准**：
- ✅ 外部调用者可传入 `Parameters` 和 `FunctionOverrides`
- ✅ `OverridableFunctionRegistry` 支持执行级函数覆盖
- ✅ 新 `ExecuteProcessFuncAsync` 重载使用隔离参数字典
- ✅ `WorkflowExecutor` 通过 `ExecuteWorkflowWithRegistryAsync` 设置 `AsyncLocal` 注册表
- ✅ 编译通过

#### 第三阶段：移除 DictionaryMapFuncs 和旧 FunctionMap（✅ 已完成）

**目标**：清除旧的函数映射机制，完全迁移到 `IFunctionRegistry`。

**修改文件**：
- [Orchestration/OrchestrationConnect.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/Orchestration/OrchestrationConnect.cs) — 移除 `FunctionMap` 属性，清理 `PipelineRunAsync`
- [ExecutionEngine/ExecutionEngine.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/ExecutionEngine.cs) — 移除 `DictionaryMapFuncs().MappingFunction(...)` 调用
- [ExecutionEngine/Integration.cs](file:///d:/Project/Program/Workflow_/WorkFlowMethod/ExecutionEngine/Integration.cs) — 移除 `WorkflowIntegration`、`WorkflowFunctions`、`IWorkflowFunctions` 等类

**步骤**：

1. ✅ **移除 `ExecuteProcess` 中的 `new DictionaryMapFuncs().MappingFunction(...)` 调用**
2. ✅ **移除 `FuncPipelineParameters.FunctionMap` 属性**
3. ✅ **移除 `DictionaryMapFuncs` 依赖**（核心执行链路不再引用）
4. ✅ **移除 `WorkflowFunctions` 和 `WorkflowIntegration` 类**（功能由 `BuiltInFunctionRegistrar` 替代）
5. ✅ **移除 `IWorkflowFunctions`、`IWorkflowFunctionsFactory`、`IWorkflowFunctionRegistrar` 接口**

**验证标准**：
- ✅ 代码中不存在对 `DictionaryMapFuncs` 的引用
- ✅ `FuncPipelineParameters` 不再有 `FunctionMap` 属性
- ✅ `WorkflowIntegration` 和 `WorkflowFunctions` 已移除
- ✅ 编译通过

### 10.9 外部调用示例

```csharp
// ─── 初始化 ───
var service = new WorkFlowMethodServiceBuilder()
    .UseInMemoryStorage()
    .Build();

// ─── 注册自定义函数 ───
service.Functions.Register("SendEmail", ctx =>
{
    var to = ctx.GetParameter<string>("to");
    var subject = ctx.GetParameter<string>("subject");
    EmailSender.Send(to, subject, ctx.Data["body"].ToString());
    return "sent";
});

service.Functions.RegisterAsync("HttpPost", async ctx =>
{
    var url = ctx.GetParameter<string>("url");
    var body = ctx.GetParameter<string>("body");
    using var client = new HttpClient();
    var response = await client.PostAsync(url, new StringContent(body));
    return await response.Content.ReadAsStringAsync();
});

// ─── 执行工作流（传入参数 + 覆盖函数） ───
var result = await service.Executor.ExecuteAsync(new ExecutionRequest
{
    StartNodeKey = "node_0",
    Parameters = new Dictionary<string, object>
    {
        ["input"] = "Hello",
        ["to"] = "user@example.com"
    },
    FunctionOverrides = new Dictionary<string, Func<FunctionContext, object>>
    {
        // 在本次执行中，覆盖内置的 "WebInvoke" 函数
        ["WebInvoke"] = ctx => MyCustomWebInvoke(ctx.Parameters)
    },
    ContextData = new Dictionary<string, object>
    {
        ["body"] = "Workflow execution completed"
    }
});

Console.WriteLine($"执行状态: {result.Status}");
```

### 10.10 函数库分离后的文件目录（当前实际状态 ✅ F-v4）

```
WorkFlowMethod/
├── ExecutionEngine/
│   ├── IFunctionRegistry.cs         # 函数注册接口（新增 ✅）
│   ├── FunctionContext.cs           # 函数执行上下文（新增 ✅）
│   ├── DefaultFunctionRegistry.cs   # 默认实现（新增 ✅）
│   ├── FunctionNotFoundException.cs # 异常类型（新增 ✅）
│   ├── OverridableFunctionRegistry.cs # 函数覆盖包装器（新增 ✅ F-v3）
│   ├── FunctionRegistryExtensions.cs # CallWithOverrides 扩展（新增 ✅ F-v3）
│   ├── ExecutionRequest.cs          # 执行请求参数（新增 ✅ F-v3）
│   ├── IWorkflowExecutor.cs         # 执行器接口（新增 ✅ F-v3）
│   ├── WorkflowExecutor.cs          # 执行器实现（新增 ✅ F-v3）
│   ├── Integration.cs              # BuiltInFunctionRegistrar（✅ F-v4 已清理旧类）
│   ├── ExecutionEngine.cs          # ExecuteProcess — 参数隔离重载（✅ F-v3）
│   └── ...
│
├── Orchestration/
│   ├── OrchestrationConnect.cs      # FuncCallRun AsyncLocal + PipelineRunAsync 重载（✅ F-v2/F-v3/F-v4）
│   └── ...
│
├── Storage/
│   └── ...
│
└── WorkflowUseage.cs                # 保持向后兼容的静态门面
```

### 10.11 版本演进计划（函数库外迁）

| 版本 | 函数库变更 | API 兼容性 | 状态 |
|------|-----------|-----------|------|
| 当前 | `FunctionMap` 字典 + `DynamicInvoke` + `GraphUnit.dic` | - | - |
| F-v1 | 提取 `FunctionContext` + `IFunctionRegistry` + `DefaultFunctionRegistry` | ✅ 完全向后兼容 | ✅ **已完成** |
| F-v2 | `FuncCallRun` 新增 `IFunctionRegistry` 重载 + `BuiltInFunctionRegistrar` | ✅ 向后兼容 | ✅ **已完成** |
| F-v3 | `ExecutionRequest` + `IWorkflowExecutor` + `OverridableFunctionRegistry` + 参数隔离 | ⚠️ 新增接口 | ✅ **已完成** |
| F-v4 | 移除 `DictionaryMapFuncs` / `FunctionMap` / `WorkflowIntegration` / `WorkflowFunctions` | ❌ 破坏性变更 | ✅ **已完成** |

### 10.12 预期收益

| 维度 | 当前 | 改造后 | 最终状态 |
|------|------|--------|---------|
| **函数扩展性** | 只能通过 `WorkflowFunctions` 派生或 `FunctionRegistry.Default`（外部包） | 原生 `IFunctionRegistry.Register`，任何 `Func<FunctionContext, object>` 均可 | ✅ **全部实现** |
| **参数隔离性** | 全局 `GraphUnit.dic`，并发执行有数据竞争 | 每次执行隔离的 `Parameters` 字典 | ✅ **F-v3 实现** |
| **函数覆盖能力** | 无法在单次执行中临时替换函数 | 支持 `ExecutionRequest.FunctionOverrides` 执行级覆盖 | ✅ **F-v3 实现** |
| **类型安全性** | `DynamicInvoke(object[])` 运行时绑定 | `Func<FunctionContext, object>` 编译期类型约束 | ✅ **全部实现** |
| **外部可配置性** | 函数绑定在代码中，外部无法传入自定义逻辑 | 外部可在调用时传参、覆写函数、注入上下文 | ✅ **全部实现** |
| **依赖可见性** | `DictionaryMapFuncs` 来自外部包，行为不透明 | 纯接口依赖，实现可见可控 | ✅ **F-v4 实现** |
