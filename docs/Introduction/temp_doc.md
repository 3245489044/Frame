# WorkFlowMethod 功能介绍

## 1. 功能概述

WorkFlowMethod 是一个功能完备的工作流编排框架，专为复杂业务流程的自动化和智能化管理而设计。该框架采用模块化、可扩展的架构，提供了从工作流定义、构建到执行、监控的全生命周期管理能力。

### 核心价值与优势

- **全生命周期管理**：提供从工作流定义、构建、执行、监控到归档的完整生命周期支持，确保业务流程的端到端可追溯性。支持工作流版本管理和历史记录查询，便于审计和合规要求。
- **高度可定制**：通过灵活的节点配置系统，支持自定义节点类型、参数映射、条件分支、并行执行等复杂业务逻辑。提供丰富的配置选项，满足不同业务场景的个性化需求。
- **动态自适应**：通过 DynamicUpdate 模块实现运行时工作流结构的动态调整，支持节点的实时添加、删除和修改，使工作流能够根据业务变化自动适应。
- **实时监控与控制**：提供详细的执行状态跟踪，包括节点执行状态、执行时间、错误信息等。支持全局暂停、恢复、终止等控制操作，便于系统维护和故障处理。
- **模块化设计**：采用松耦合的组件架构，各模块职责明确，支持独立扩展和替换。提供标准化的接口，便于与外部系统集成。

### 系统架构概览

WorkFlowMethod 采用分层架构设计，各层职责明确，通过标准化接口进行交互，确保系统的可扩展性和可维护性。以下是各核心模块的具体实现和技术细节：

1. **工作流构建与定义层**：
   - **WorkflowBuilder**：
     - 提供多种工作流创建方式：字符串定义（简洁语法快速定义）、JSON文件加载（便于版本控制）、可视化构建（降低开发门槛）
     - 支持节点的批量添加和配置，提供节点间的连接关系管理
     - 实现工作流的序列化和反序列化，支持JSON格式的存储和传输
     - 核心方法：`BuildWorkflow`、`ParseFromJson`、`LoadFromFile`、`SaveToFile`等
   - **WorkflowFactory**：
     - 提供工作流模板管理，支持预定义模板的创建和使用
     - 实现配置化创建功能，通过配置参数生成定制化工作流
     - 支持工作流的快速克隆和修改，提高开发效率
     - 核心方法：`CreateEmptyWorkflow`、`CreateWorkflowTemplate`、`CreateWorkflowFromConfig`等

2. **存储与执行管理层**：
   - **GenericWorkflowStorageManager**：
     - 基于 ECS (Entity Component System) 架构，提供高效的工作流存储管理
     - 支持工作流的注册、更新、删除和查询，实现工作流的版本管理
     - 提供观察者模式，支持工作流状态变更的实时通知
     - 实现工作流与节点的关联管理，支持通过节点查找相关工作流
     - 核心方法：`RegisterOrUpdateWorkflow`、`GetWorkflow`、`RemoveWorkflow`、`RegisterObserver`等
   - **WorkflowExecutionManager**：
     - 负责执行状态的管理和跟踪，支持运行、暂停、完成、终止和故障等状态
     - 提供执行控制功能，支持全局暂停、恢复和终止操作
     - 实现执行状态的持久化，确保系统重启后能够恢复执行
     - 提供详细的执行统计信息，便于问题排查和性能分析
     - 核心方法：`TryRegisterNodeExecution`、`MarkNodeCompleted`、`PauseExecution`、`GetExecutionStatus`等
   - **TaskProcessor**：
     - 提供异步任务处理能力，基于任务队列实现并发执行
     - 支持任务优先级管理，确保关键任务优先执行
     - 实现递归深度控制，避免无限递归导致的系统问题
     - 提供任务处理的优雅停止机制，确保系统安全关闭
     - 核心方法：`Start`、`SubmitTask`、`StopAsync`等

3. **节点配置与连接层**：
   - **NodeConfig**：
     - 定义节点的配置模型，包含函数名称、参数键、结果键、后续节点配置等属性
     - 支持节点的各种配置选项，满足不同业务场景的需求
     - 提供节点配置的验证机制，确保配置的正确性和完整性
     - 核心方法：`CreateNode`、`CompleteConfig`等
   - **ChainInfo**：
     - 管理工作流节点的连接关系，支持顺序执行、条件分支、并行执行等复杂执行路径
     - 提供节点的查找和遍历功能，便于工作流的分析和优化
     - 支持工作流链的完整性验证，确保工作流的正确性
     - 核心方法：`GetAllNodeCallkeys`、`GetStartNodeCallkey`、`GetEndNodeCallkey`等
   - **ModularValidator**：
     - 提供工作流、节点、连接和配置的验证功能，确保系统的稳定性
     - 实现多层级的验证机制，包括基本验证、集合验证、工作流验证等
     - 支持自定义验证规则，满足特定业务场景的验证需求
     - 核心方法：`ValidateNotNull`、`ValidateWorkflowChain`、`ValidateChainConnectivity`等
   - **WorkflowProcessor**：
     - 提供工作流执行的核心功能，支持从任意节点开始执行
     - 实现工作流的暂停、恢复和终止操作，提供灵活的执行控制
     - 支持执行结果的收集和处理，提供详细的执行统计信息
     - 核心方法：`ExecuteWorkflowAsync`、`ExecuteFromNodeAsync`、`PauseWorkflow`、`GetExecutionStats`等

4. **脚本与模板层**：
   - **WorkFlow_Script**：
     - 提供预定义工作流模板，包括图像生成、大模型调用、数据包装存储、代码综合生成等
     - 实现提示词处理系统，支持不同类型的提示词处理策略
     - 支持工作流的快速配置和部署，提高开发效率
   - **MergeStr**：
     - 实现提示词合并策略，管理不同类型的提示词处理
     - 支持多种合并策略，包括PhotoInitiate、DataProcess、JsonProcess1、BaseProcess、CodeProcess等
     - 提供灵活的提示词处理机制，满足不同AI任务的需求
   - **Prompt**：
     - 定义提示词模板，包含各种AI任务的提示词模板
     - 支持提示词的动态生成和调整，提高AI任务的效果
     - 提供标准化的提示词格式，确保AI任务的一致性
   - **Strategy**：
     - 提供执行策略，实现不同的提示词处理方法
     - 支持策略的灵活配置和切换，适应不同的业务场景
     - 核心方法：`Execute`、`Execute_`、`Execute__`等

5. **动态更新层**：
   - **GraphWorkflowUpdater**：
     - 实现工作流图更新器，应用图更新指令到工作流链
     - 支持节点的添加、删除、修改等操作，实现工作流的动态调整
     - 提供更新指令的构建和处理，确保更新操作的正确性
     - 核心方法：`ApplyGraphUpdate`、`BuildDirectivesFromQueue`、`ConvertFromConnectNodeConfig`等
   - **GraphUpdateDirective**：
     - 定义图更新指令，包含更新操作的类型、目标节点、参数等信息
     - 支持多种更新操作类型，满足不同的更新需求
     - 提供指令的序列化和反序列化，便于传输和存储
   - **Transformers**：
     - 实现图转换器，执行图结构的转换和调整
     - 支持节点的记录和跟踪，确保更新操作的可追溯性
     - 提供变更的记录和管理，便于回滚和审计
     - 核心方法：`Transform`、`RecordNewNode`、`RecordModifiedNode`、`RecordDeletedNode`等
   - **Integration**：
     - 提供与现有系统的集成功能，支持传统更新方式的兼容
     - 实现更新指令的转换和应用，确保系统的平滑升级
     - 核心方法：`UpdateConnect`、`ToGraphDirective`、`ApplyGraphUpdate`等



### 技术特点

- **异步执行**：
  - 基于 TaskProcessor 的异步任务处理，提高系统吞吐量
  - 支持任务队列和并发控制，优化资源利用
  - 提供任务优先级管理，确保关键任务优先执行

- **状态持久化**：
  - 支持执行状态的持久化存储，确保系统重启后能够恢复执行
  - 提供执行历史记录，便于问题追溯和审计
  - 支持断点续传，允许从失败点重新开始执行

- **观察者模式**：
  - 实时状态变更通知机制，便于系统其他组件响应工作流状态变化
  - 支持自定义观察者，实现业务逻辑的扩展
  - 提供事件总线，实现组件间的解耦通信

- **依赖管理**：
  - 工作流间依赖关系的智能管理，确保工作流按照正确的顺序执行
  - 支持循环依赖检测和处理
  - 提供依赖图可视化，便于理解和分析

- **资源优化**：
  - 执行过程中的资源使用监控和优化
  - 支持节点执行超时控制，避免单个节点执行时间过长影响整个工作流
  - 提供资源使用统计和分析，便于系统调优

- **安全性**：
  - 支持工作流访问控制和权限管理
  - 提供敏感数据的加密存储
  - 支持审计日志，记录所有操作和变更

- **可扩展性**：
  - 模块化设计，支持组件的独立扩展和替换
  - 提供插件机制，便于功能扩展
  - 支持自定义节点类型和处理逻辑

WorkFlowMethod 不仅是一个工作流执行引擎，更是一个完整的业务流程管理平台。它通过提供强大的工作流编排能力，帮助企业实现业务流程的自动化、智能化和标准化，为企业数字化转型和自动化升级提供强大的技术支撑。无论是简单的线性流程还是复杂的并行分支流程，WorkFlowMethod 都能提供高效、可靠的执行保障。

## 2. 核心组件

### 2.1 WorkflowBuilder

**功能**：提供工作流构建、解析、加载和保存的核心功能。

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `BuildWorkflow` | 构建工作流节点列表并序列化为JSON字符串 | nodes: List<object> - 节点列表<br>quotedFields: HashSet<string> - 需要转换为引号字符串的字段列表 | string - 序列化后的JSON字符串 |
| `ParseFromJson` | 从JSON字符串解析工作流节点列表 | json: string - JSON字符串 | List<object> - 节点列表 |
| `LoadFromFile` | 从JSON文件路径加载工作流 | filePath: string - JSON文件路径<br>quotedFields: HashSet<string> - 需要去除引号的字段列表 | List<object> - 节点列表 |
| `SaveToFile` | 将工作流保存到JSON文件 | nodes: List<object> - 节点列表<br>filePath: string - 文件路径<br>quotedFields: HashSet<string> - 需要转换为引号字符串的字段列表 | void |
| `BuildSingleNode` | 构建单个节点并序列化为JSON字符串 | symbol: string - 节点符号<br>nodeId: string - 节点ID<br>funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>label: string - 节点标签<br>quotedFields: HashSet<string> - 需要转换为引号字符串的字段列表 | string - JSON字符串 |
| `AddNodesFromStrings` | 从简洁字符串定义批量添加节点 | nodes: List<object> - 节点列表<br>nodeDefinitions: string[] - 节点定义字符串数组 | void |
| `Build` | 创建包含单个节点的Workflow并返回JSON字符串 | symbol: string - 节点符号<br>nodeId: string - 节点ID<br>funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>label: string - 节点标签 | string - JSON字符串 |
| `BuildFromStrings` | 从字符串定义创建并构建工作流 | nodeDefinitions: string[] - 节点定义字符串数组 | string - JSON字符串 |
| `AddNode` | 向工作流添加节点 | nodes: List<object> - 节点列表<br>symbol: string - 节点符号<br>nodeId: string - 节点ID<br>funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>label: string - 节点标签 | void |
| `CreateNode` | 创建完整节点 | symbol: string - 节点符号<br>nodeId: string - 节点ID<br>funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>label: string - 节点标签<br>nextNodeConfig: dynamic - 后续节点配置<br>additionalProperties: Dictionary<string, object> - 额外属性 | dynamic - 节点对象 |
| `ParseFromQuotedJson` | 从带引号的JSON字符串解析工作流节点列表 | json: string - 带引号的JSON字符串<br>quotedFields: HashSet<string> - 需要去除引号的字段列表 | List<object> - 节点列表 |
| `GetSummary` | 获取工作流的摘要信息 | nodes: List<object> - 节点列表 | string - 摘要字符串 |
| `AddNodeWithNextConfig` | 向工作流添加带有后续节点配置的节点 | nodes: List<object> - 节点列表<br>symbol: string - 节点符号<br>nodeId: string - 节点ID<br>funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>nextNodeConfig: dynamic - 后续节点配置<br>label: string - 节点标签 | void |
| `CreateNextNodeConfig` | 创建后续节点配置 | configs: Dictionary<string, object> - 配置键值对 | dynamic - 后续节点配置 |
| `CreateNodeConfig` | 创建节点配置 | funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>additionalConfigs: Dictionary<string, object> - 额外配置 | dynamic - 节点配置 |

**WorkflowFactory 方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `CreateEmptyWorkflow` | 创建空工作流 | 无 | List<object> - 空工作流节点列表 |
| `CreateWorkflowTemplate` | 创建通用工作流模板 | templateName: string - 模板名称<br>parameters: Dictionary<string, object> - 模板参数 | List<object> - 工作流节点列表 |
| `CreateWorkflowFromConfig` | 从配置创建工作流 | workflowConfig: dynamic - 工作流配置 | List<object> - 工作流节点列表 |

### 2.2 OrchestrationFramework

**功能**：提供基于 ECS (Entity Component System) 的工作流存储和执行管理。

#### 2.2.1 GenericWorkflowStorageManager

**功能**：工作流存储管理，支持工作流的注册、更新、删除和查询。

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `RegisterOrUpdateWorkflow` | 注册或更新工作流 | workflow: TWorkflow - 工作流对象 | string - 工作流ID |
| `GetWorkflow` | 获取工作流 | workflowId: string - 工作流ID | TWorkflow - 工作流对象 |
| `RemoveWorkflow` | 移除工作流 | workflowId: string - 工作流ID | void |
| `GetAllWorkflowIds` | 获取所有工作流ID | 无 | List<string> - 工作流ID列表 |
| `UnregisterObserver` | 注册工作流观察者 | workflowId: string - 工作流ID<br>callback: Action<TWorkflow> - 回调函数 | void |
| `GetReferences` | 获取所有工作流引用 | 无 | IReadOnlyDictionary<string, WorkflowReference<TWorkflow>> - 工作流引用字典 |
| `FindWorkflowsByNode` | 根据节点查找所有关联工作流 | nodeId: string - 节点ID | List<string> - 工作流ID列表 |
| `GetWorkflowIdForNode` | 根据节点获取工作流ID | nodeId: string - 节点ID | string - 工作流ID |
| `IsNodeInAnyWorkflow` | 检查节点是否属于任何工作流 | nodeId: string - 节点ID | bool - 是否存在 |
| `GetWorkflowReference` | 获取工作流引用 | workflowId: string - 工作流ID | WorkflowReference<TWorkflow> - 工作流引用 |
| `RegisterRemovalObserver` | 注册工作流移除观察者 | workflowId: string - 工作流ID<br>callback: Action<string> - 回调函数 | void |

#### 2.2.2 WorkflowExecutionManager

**功能**：工作流执行管理，支持执行状态跟踪、节点执行控制。

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `TryRegisterNodeExecution` | 尝试注册节点执行 | executionId: string - 执行ID<br>nodeKey: string - 节点键<br>isDuplicate: out bool - 是否重复 | bool - 是否注册成功 |
| `WaitForNodeCompletionAsync` | 等待节点完成 | executionId: string - 执行ID<br>nodeKey: string - 节点键<br>cancellationToken: CancellationToken - 取消令牌 | Task - 异步任务 |
| `MarkNodeCompleted` | 标记节点完成 | executionId: string - 执行ID<br>nodeKey: string - 节点键 | void |
| `MarkNodeFailed` | 标记节点失败 | executionId: string - 执行ID<br>nodeKey: string - 节点键<br>error: Exception - 错误信息 | void |
| `PauseExecution` | 暂停执行 | executionId: string - 执行ID | bool - 是否暂停成功 |
| `ResumeExecution` | 恢复执行 | executionId: string - 执行ID | bool - 是否恢复成功 |
| `TerminateExecution` | 终止执行 | executionId: string - 执行ID | bool - 是否终止成功 |
| `GetExecutionStatus` | 获取执行状态 | executionId: string - 执行ID | WorkflowExecutionStatus - 执行状态 |
| `SetCurrentNode` | 设置当前节点 | executionId: string - 执行ID<br>nodeKey: string - 节点键 | void |
| `GetExecutionStats` | 获取执行统计信息 | executionId: string - 执行ID | WorkflowExecutionStats - 执行统计信息 |
| `GetCurrentNode` | 获取当前节点 | executionId: string - 执行ID | string - 节点键 |
| `GetActiveExecutions` | 获取所有活动执行 | 无 | IEnumerable<string> - 执行ID列表 |
| `IsNodeCompleted` | 检查节点是否已完成 | executionId: string - 执行ID<br>nodeKey: string - 节点键 | bool - 是否完成 |
| `StartExecution` | 开始执行 | executionId: string - 执行ID | bool - 是否成功 |
| `PauseAll` | 暂停所有执行 | 无 | bool - 是否成功 |
| `ResumeAll` | 恢复所有执行 | 无 | bool - 是否成功 |
| `TerminateAll` | 终止所有执行 | 无 | bool - 是否成功 |

#### 2.2.3 TaskProcessor

**功能**：任务处理器，支持异步任务处理和递归深度控制。

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `Start` | 启动处理器 | 无 | void |
| `SubmitTask` | 提交任务 | item: T - 任务项 | bool - 是否提交成功 |
| `StopAsync` | 停止处理器 | graceful: bool - 是否优雅停止 | Task - 异步任务 |

**TaskProcessorManager 方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `GetOrCreateProcessor` | 获取或创建处理器 | processorId: string - 处理器ID<br>processItemFunc: Func<T, Task> - 处理函数<br>getKeyFunc: Func<T, string> - 获取键函数 | TaskProcessor<T> - 任务处理器 |
| `StopAllProcessorsAsync` | 停止所有处理器 | 无 | Task - 异步任务 |
| `TryGetProcessor` | 尝试获取处理器 | processorId: string - 处理器ID<br>processor: out TaskProcessor<T> - 处理器 | bool - 是否获取成功 |
| `RemoveProcessor` | 移除处理器 | processorId: string - 处理器ID | void |

### 2.3 OrchestrationConnect

**功能**：定义工作流节点配置和连接模型，提供执行上下文管理。

#### 2.3.1 NodeConfig

**功能**：节点配置模型，包含各种配置选项。

**核心属性**：
- `Id`：节点ID
- `NextNodeId`：下一个节点ID
- `Symbol`：节点符号
- `Label`：节点标签
- `funcPipelineConfig`：函数管道配置
- `nextNodeConfig`：下一个节点配置

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `CreateNode` | 创建节点 | config: NodeConfig - 节点配置 | string - 节点JSON字符串 |
| `CompleteConfig` | 完成配置 | config: NodeConfig - 节点配置<br>baseConfigType: string - 基础配置类型<br>getStrConfigType: string - 获取字符串配置类型<br>funcPipelineConfigType: string - 函数管道配置类型<br>addNodeConfigType: string - 添加节点配置类型<br>nextNodeConfigType: string - 下一个节点配置类型<br>basicConfigType: string - 基本配置类型 | void |

#### 2.3.2 ChainInfo

**功能**：工作流链信息，包含节点列表和连接关系。

**核心属性**：
- `StartNode`：开始节点
- `EndNode`：结束节点
- `AllNodes`：所有节点

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `GetAllNodeCallkeys` | 获取所有节点Callkey | 无 | List<string> - Callkey列表 |
| `GetStartNodeCallkey` | 获取开始节点Callkey | 无 | string - Callkey |
| `GetEndNodeCallkey` | 获取结束节点Callkey | 无 | string - Callkey |

#### 2.3.3 验证器 (ModularValidator)

**功能**：提供工作流、节点、连接和配置的验证功能。

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `ValidateNotNull` | BasicValidator - 验证对象不为空 | obj: object - 验证对象<br>paramName: string - 参数名 | void |
| `ValidateNotNullOrEmpty` | BasicValidator - 验证字符串不为空 | value: string - 验证字符串<br>paramName: string - 参数名 | void |
| `ValidateNotEmpty<T>` | CollectionValidator - 验证集合不为空 | collection: IEnumerable<T> - 验证集合<br>paramName: string - 参数名 | void |
| `ValidateWorkflowChain` | WorkflowValidator - 验证工作流链 | chain: ChainInfo - 工作流链 | void |
| `ValidateNodeListNotEmpty` | WorkflowValidator - 验证节点列表不为空 | nodes: List<NodeConfig> - 节点列表 | void |
| `ValidateNodeExists` | WorkflowValidator - 验证节点是否存在 | callkey: string - 节点键<br>nodeCache: Dictionary<string, NodeConfig> - 节点缓存 | void |
| `ValidateWorkflowConnections` | ConnectionValidator - 验证工作流连接 | connections: List<WorkflowConnection<string, string>> - 连接列表 | void |
| `ValidateInsertOperation` | ConnectionValidator - 验证插入操作 | pos: ConnectionPositionInfo<string> - 位置信息 | void |
| `ValidateChainConnectivity` | ConnectionValidator - 验证链连接性 | chain: ChainInfo - 工作流链 | void |
| `ValidateChainConfig` | ConfigValidator - 验证链配置 | config: ChainConfig - 链配置 | void |

#### 2.3.4 工作流处理器 (WorkflowProcessor)

**功能**：提供工作流执行的核心功能。

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `ExecuteWorkflowAsync` | 执行工作流 | parameters: Dictionary<string, object> - 执行参数<br>cancellationToken: CancellationToken - 取消令牌 | Task<WorkflowExecutionResult> - 执行结果 |
| `ExecuteFromNodeAsync` | 从指定节点执行工作流 | nodeKey: string - 节点键<br>parameters: Dictionary<string, object> - 执行参数<br>cancellationToken: CancellationToken - 取消令牌 | Task<WorkflowExecutionResult> - 执行结果 |
| `ResumeWorkflowFromNodeAsync` | 从节点恢复工作流执行 | existingExecutionId: string - 现有执行ID<br>nodeKey: string - 节点键<br>parameters: Dictionary<string, object> - 执行参数<br>cancellationToken: CancellationToken - 取消令牌 | Task<WorkflowExecutionResult> - 执行结果 |
| `PauseWorkflow` | 暂停工作流 | executionId: string - 执行ID | bool - 是否成功 |
| `ResumeWorkflow` | 恢复工作流 | executionId: string - 执行ID | bool - 是否成功 |
| `TerminateWorkflow` | 终止工作流 | executionId: string - 执行ID | bool - 是否成功 |
| `PauseAllWorkflows` | 暂停所有工作流 | 无 | bool - 是否成功 |
| `ResumeAllWorkflows` | 恢复所有工作流 | 无 | bool - 是否成功 |
| `TerminateAllWorkflows` | 终止所有工作流 | 无 | bool - 是否成功 |
| `GetExecutionStatus` | 获取执行状态 | executionId: string - 执行ID | WorkflowExecutionStatus - 执行状态 |
| `GetExecutionStats` | 获取执行统计信息 | executionId: string - 执行ID | WorkflowExecutionStats - 执行统计信息 |
| `GetCurrentNode` | 获取当前节点 | executionId: string - 执行ID | string - 节点键 |
| `GetActiveExecutions` | 获取活动执行列表 | 无 | IEnumerable<string> - 执行ID列表 |
| `GetAllExecutionsStats` | 获取所有执行统计信息 | 无 | Dictionary<string, WorkflowExecutionStats> - 执行统计字典 |
| `GetWorkflowNodes` | 获取工作流节点列表 | executionId: string - 执行ID | List<string> - 节点键列表 |
| `GetCompletedNodes` | 获取已完成节点列表 | executionId: string - 执行ID | List<string> - 已完成节点键列表 |

#### 2.3.5 工作流工具类

**WorkflowExecutionHelper 方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `GenerateExecutionId` | 生成执行ID | symbol: string - 符号前缀 | string - 执行ID |
| `GetChildExecutions` | 获取子执行列表 | parentId: string - 父执行ID<br>contexts: ConcurrentDictionary<string, WorkflowExecutionContext> - 执行上下文字典<br>parentToChildren: ConcurrentDictionary<string, List<string>> - 父子关系字典 | List<string> - 子执行ID列表 |
| `UpdateExecutionContext` | 更新执行上下文 | contexts: ConcurrentDictionary<string, WorkflowExecutionContext> - 执行上下文字典<br>id: string - 执行ID<br>node: string - 节点键 | void |
| `CleanupExecutionContext` | 清理执行上下文 | contexts: ConcurrentDictionary<string, WorkflowExecutionContext> - 执行上下文字典<br>parentToChildren: ConcurrentDictionary<string, List<string>> - 父子关系字典<br>id: string - 执行ID | void |
| `CreateExecutionResult` | 创建执行结果 | id: string - 执行ID<br>success: bool - 是否成功<br>msg: string - 消息<br>err: Exception - 错误<br>start: DateTime - 开始时间 | WorkflowExecutionResult - 执行结果 |

**WorkflowChainConfigRetriever 方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `GetChainConfig` | 获取链配置 | workflowId: string - 工作流ID<br>chainId: string - 链ID | ChainConfigDto - 链配置DTO |
| `GetChainConfigByNode` | 根据节点获取链配置 | nodeKey: string - 节点键<br>workflowId: string - 工作流ID | ChainConfigDto - 链配置DTO |
| `GetAllChainConfigs` | 获取所有链配置 | workflowId: string - 工作流ID | List<ChainConfigDto> - 链配置DTO列表 |

**WorkflowTools 方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `EnsureWorkflowRuntimeContext` | 确保工作流运行时上下文 | workflowId: string - 工作流ID | WorkflowRuntimeContext - 运行时上下文 |
| `CreateChainInfo` | 创建链信息 | json: string - JSON字符串<br>chainCount: int - 链数量<br>workflowId: string - 工作流ID | ChainInfo - 链信息 |

### 2.4 WorkFlow_Script

**功能**：提供工作流脚本相关的功能，包括提示词模板和工作流配置。

**核心组件**：
- `WorkFlowMethod.MergeStr`：提示词合并策略，管理不同类型的提示词处理策略
- `WorkFlowMethod.Prompt`：提示词模板定义，包含各种AI任务的提示词模板
- `WorkFlowMethod.Strategy`：执行策略，提供不同的提示词处理方法
- `WorkFlowsSet`：工作流配置集合，包含各种预定义工作流模板
- `WorkFlowConfig`：工作流配置和具体工作流实现

**提示词处理系统**：
- **MergeStr 策略**：PhotoInitiate、DataProcess、JsonProcess1、BaseProcess、CodeProcess、PipelineInputTestFunction、PipelineInvokeJson
- **Prompt 模板**：PhotoInitiateTemplate、DataProcessTemplate、JsonProcessTemplate、BaseProcessTemplate、CodeProcessTemplate、PipelineInputTestTemplate、PipelineInvokeJsonTemplate
- **Strategy 执行方法**：Execute、Execute_、Execute__

**预定义工作流模板**：
- 图像生成：AI生图工作流
- 大模型调用生成：大模型调用工作流
- 数据包装存储：数据存储工作流
- 代码综合生成：代码生成工作流
- 代码混合生成：混合代码生成工作流
- 编译测试：编译测试工作流

**具体工作流实现**：
- AI生图工作流：从输入提示词生成图像
- AI代码工作流：根据需求生成代码
- 数据分析工作流：处理和分析数据
- 编译测试工作流：自动编译和测试代码
- 数据库测试工作流：测试数据库操作

### 2.5 DynamicUpdate

**功能**：提供工作流的动态更新功能，支持运行时修改工作流结构。

**核心组件**：
- `GraphWorkflowUpdater`：工作流图更新器，应用图更新指令
- `GraphUpdateDirective`：图更新指令，定义更新操作
- `Transformers`：图转换器，执行图结构转换

**主要方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `ApplyGraphUpdate` | 应用图更新指令到工作流链 | originalChain: ChainInfo - 原始工作流链<br>directives: IEnumerable<GraphUpdateDirective> - 更新指令<br>workflowId: string - 工作流ID | ChainInfo - 更新后的工作流链 |
| `BuildDirectivesFromQueue` | 从消息队列构建更新指令 | queue: SimpleMessageQueue<ConnectNodeConfig> - 消息队列<br>timeoutMs: int - 超时时间 | List<GraphUpdateDirective> - 更新指令列表 |
| `ConvertFromConnectNodeConfig` | 从连接节点配置转换为更新指令 | config: ConnectNodeConfig - 连接节点配置 | GraphUpdateDirective - 更新指令 |

**Transformers 方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `Transform` | DynamicUpdateTransformer - 转换图结构 | graph: object - 图对象 | object - 转换后的图 |
| `RecordNewNode` | UpdateTransformContext - 记录新节点 | nodeKey: string - 节点键 | void |
| `RecordModifiedNode` | UpdateTransformContext - 记录修改节点 | nodeKey: string - 节点键 | void |
| `RecordDeletedNode` | UpdateTransformContext - 记录删除节点 | nodeKey: string - 节点键 | void |
| `RecordChange` | UpdateTransformContext - 记录变更 | change: object - 变更对象 | void |

**Integration 方法**：

| 方法名 | 功能描述 | 参数 | 返回值 |
|-------|---------|------|--------|
| `UpdateConnect` | LegacyUpdateAdapter - 更新连接 | config: ConnectNodeConfig - 节点配置<br>chainInfo: ChainInfo - 链信息<br>output: object - 输出<br>dic: Dictionary<string, object> - 字典数据 | ChainInfo - 更新后的链信息 |
| `ToGraphDirective` | GraphUpdateExtensions - 转换为图指令 | config: ConnectNodeConfig - 连接节点配置 | GraphUpdateDirective - 图更新指令 |
| `ApplyGraphUpdate` | GraphUpdateExtensions - 应用图更新 | chain: ChainInfo - 工作流链<br>directive: GraphUpdateDirective - 更新指令 | ChainInfo - 更新后的链 |
| `IsGraphUpdateSupported` | GraphUpdateExtensions - 检查是否支持图更新 | config: ConnectNodeConfig - 连接节点配置 | bool - 是否支持 |

## 3. 工作流执行流程

1. **工作流定义**：通过 WorkflowBuilder 创建或加载工作流定义
2. **工作流注册**：将工作流注册到 GenericWorkflowStorageManager
3. **执行初始化**：创建 WorkflowExecutionContext，生成执行ID
4. **节点执行**：按顺序执行工作流中的节点，处理输入参数，生成输出结果
5. **状态管理**：通过 WorkflowExecutionManager 跟踪执行状态
6. **结果处理**：收集执行结果，处理错误情况

## 4. 核心功能特性

### 4.1 动态工作流构建
- **简洁字符串定义**：通过特定格式的字符串快速定义工作流节点和连接关系，无需编写复杂的JSON配置
- **JSON文件加载**：支持从JSON文件加载预定义的工作流配置，便于工作流的版本控制和复用
- **运行时动态修改**：通过DynamicUpdate模块支持在工作流运行过程中动态添加、删除或修改节点，实现工作流的自适应调整
- **可视化构建**：支持通过可视化界面构建工作流，降低开发门槛

**相关函数**：

| 函数名 | 所属类 | 功能描述 | 参数 | 返回值 |
|-------|--------|---------|------|--------|
| `BuildWorkflow` | WorkflowBuilder | 构建工作流节点列表并序列化为JSON字符串 | nodes: List<object> - 节点列表<br>quotedFields: HashSet<string> - 需要转换为引号字符串的字段列表 | string - 序列化后的JSON字符串 |
| `ParseFromJson` | WorkflowBuilder | 从JSON字符串解析工作流节点列表 | json: string - JSON字符串 | List<object> - 节点列表 |
| `LoadFromFile` | WorkflowBuilder | 从JSON文件路径加载工作流 | filePath: string - JSON文件路径<br>quotedFields: HashSet<string> - 需要去除引号的字段列表 | List<object> - 节点列表 |
| `SaveToFile` | WorkflowBuilder | 将工作流保存到JSON文件 | nodes: List<object> - 节点列表<br>filePath: string - 文件路径<br>quotedFields: HashSet<string> - 需要转换为引号字符串的字段列表 | void |
| `BuildFromStrings` | WorkflowBuilder | 从字符串定义创建并构建工作流 | nodeDefinitions: string[] - 节点定义字符串数组 | string - JSON字符串 |
| `AddNodesFromStrings` | WorkflowBuilder | 从简洁字符串定义批量添加节点 | nodes: List<object> - 节点列表<br>nodeDefinitions: string[] - 节点定义字符串数组 | void |
| `ApplyGraphUpdate` | GraphWorkflowUpdater | 应用图更新指令到工作流链 | originalChain: ChainInfo - 原始工作流链<br>directives: IEnumerable<GraphUpdateDirective> - 更新指令<br>workflowId: string - 工作流ID | ChainInfo - 更新后的工作流链 |
| `BuildDirectivesFromQueue` | GraphWorkflowUpdater | 从消息队列构建更新指令 | queue: SimpleMessageQueue<ConnectNodeConfig> - 消息队列<br>timeoutMs: int - 超时时间 | List<GraphUpdateDirective> - 更新指令列表 |
| `ConvertFromConnectNodeConfig` | GraphWorkflowUpdater | 从连接节点配置转换为更新指令 | config: ConnectNodeConfig - 连接节点配置 | GraphUpdateDirective - 更新指令 |
| `UpdateConnect` | LegacyUpdateAdapter | 将ConnectNodeConfig转换为图指令并应用更新 | config: ConnectNodeConfig - 节点配置<br>chainInfo: ChainInfo - 链信息<br>output: object - 输出<br>dic: Dictionary<string, object> - 字典数据 | ChainInfo - 更新后的链信息 |

### 4.2 强大的执行管理
- **执行状态跟踪**：支持工作流执行状态的实时跟踪，包括运行、暂停、完成、终止和故障等状态
- **节点执行控制**：通过TaskProcessor实现节点的异步执行和并发控制，提高工作流执行效率
- **全局暂停和恢复**：支持全局暂停和恢复工作流执行，便于系统维护和故障处理
- **执行统计信息**：提供详细的执行统计信息，包括执行时间、节点执行状态、错误信息等，便于问题排查
- **超时控制**：支持设置节点执行超时时间，避免单个节点执行时间过长影响整个工作流

**相关函数**：

| 函数名 | 所属类 | 功能描述 | 参数 | 返回值 |
|-------|--------|---------|------|--------|
| `TryRegisterNodeExecution` | WorkflowExecutionManager | 尝试注册节点执行 | executionId: string - 执行ID<br>nodeKey: string - 节点键<br>isDuplicate: out bool - 是否重复 | bool - 是否注册成功 |
| `WaitForNodeCompletionAsync` | WorkflowExecutionManager | 等待节点完成 | executionId: string - 执行ID<br>nodeKey: string - 节点键<br>cancellationToken: CancellationToken - 取消令牌 | Task - 异步任务 |
| `MarkNodeCompleted` | WorkflowExecutionManager | 标记节点完成 | executionId: string - 执行ID<br>nodeKey: string - 节点键 | void |
| `MarkNodeFailed` | WorkflowExecutionManager | 标记节点失败 | executionId: string - 执行ID<br>nodeKey: string - 节点键<br>error: Exception - 错误信息 | void |
| `PauseExecution` | WorkflowExecutionManager | 暂停执行 | executionId: string - 执行ID | bool - 是否暂停成功 |
| `ResumeExecution` | WorkflowExecutionManager | 恢复执行 | executionId: string - 执行ID | bool - 是否恢复成功 |
| `TerminateExecution` | WorkflowExecutionManager | 终止执行 | executionId: string - 执行ID | bool - 是否终止成功 |
| `GetExecutionStatus` | WorkflowExecutionManager | 获取执行状态 | executionId: string - 执行ID | WorkflowExecutionStatus - 执行状态 |
| `GetExecutionStats` | WorkflowExecutionManager | 获取执行统计信息 | executionId: string - 执行ID | WorkflowExecutionStats - 执行统计信息 |
| `PauseAll` | WorkflowExecutionManager | 暂停所有执行 | 无 | bool - 是否成功 |
| `ResumeAll` | WorkflowExecutionManager | 恢复所有执行 | 无 | bool - 是否成功 |
| `TerminateAll` | WorkflowExecutionManager | 终止所有执行 | 无 | bool - 是否成功 |
| `Start` | TaskProcessor<T> | 启动处理器 | 无 | void |
| `SubmitTask` | TaskProcessor<T> | 提交任务 | item: T - 任务项 | bool - 是否提交成功 |
| `StopAsync` | TaskProcessor<T> | 停止处理器 | graceful: bool - 是否优雅停止 | Task - 异步任务 |
| `GetOrCreateProcessor` | TaskProcessorManager<T> | 获取或创建处理器 | processorId: string - 处理器ID<br>processItemFunc: Func<T, Task> - 处理函数<br>getKeyFunc: Func<T, string> - 获取键函数 | TaskProcessor<T> - 任务处理器 |
| `StopAllProcessorsAsync` | TaskProcessorManager<T> | 停止所有处理器 | 无 | Task - 异步任务 |
| `WaitIfPausedOrCheckTerminationAsync` | WorkflowExecutionManager | 等待如果暂停或检查终止 | executionId: string - 执行ID<br>cancellationToken: CancellationToken - 取消令牌<br>timeout: TimeSpan - 超时时间 | Task<bool> - 是否继续执行 |

### 4.3 灵活的节点配置
- **丰富的配置选项**：支持节点的各种配置选项，包括函数名称、参数键、结果键、后续节点配置等
- **复杂连接关系**：支持节点间的复杂连接关系，包括顺序执行、条件分支、并行执行等
- **参数和结果映射**：支持节点间的参数传递和结果映射，实现数据在工作流中的流动
- **自定义节点类型**：支持自定义节点类型和处理逻辑，扩展工作流的功能
- **配置验证**：提供节点配置的验证机制，确保配置的正确性和完整性

**相关函数**：

| 函数名 | 所属类 | 功能描述 | 参数 | 返回值 |
|-------|--------|---------|------|--------|
| `CreateNode` | NodeConfig | 创建节点并生成JSON字符串 | config: NodeConfig - 节点配置 | string - 节点JSON字符串 |
| `CompleteConfig` | NodeConfig | 完成节点配置，合并默认配置 | config: NodeConfig - 节点配置<br>baseConfigType: string - 基础配置类型<br>getStrConfigType: string - 获取字符串配置类型<br>funcPipelineConfigType: string - 函数管道配置类型<br>addNodeConfigType: string - 添加节点配置类型<br>nextNodeConfigType: string - 下一个节点配置类型<br>basicConfigType: string - 基本配置类型 | void |
| `BuildSingleNode` | WorkflowBuilder | 构建单个节点并序列化为JSON字符串 | symbol: string - 节点符号<br>nodeId: string - 节点ID<br>funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>label: string - 节点标签<br>quotedFields: HashSet<string> - 需要转换为引号字符串的字段列表 | string - JSON字符串 |
| `CreateNode` | WorkflowBuilder | 创建完整节点 | symbol: string - 节点符号<br>nodeId: string - 节点ID<br>funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>label: string - 节点标签<br>nextNodeConfig: dynamic - 后续节点配置<br>additionalProperties: Dictionary<string, object> - 额外属性 | dynamic - 节点对象 |
| `AddNode` | WorkflowBuilder | 向工作流添加节点 | nodes: List<object> - 节点列表<br>symbol: string - 节点符号<br>nodeId: string - 节点ID<br>funcName: string - 函数名称<br>parameterKeys: object[] - 参数键列表<br>resultKeys: object[] - 结果键列表<br>label: string - 节点标签 | void |
| `GetAllNodeCallkeys` | ChainInfo<TNode> | 获取所有节点Callkey | 无 | List<string> - Callkey列表 |
| `GetStartNodeCallkey` | ChainInfo<TNode> | 获取开始节点Callkey | 无 | string - Callkey |
| `GetEndNodeCallkey` | ChainInfo<TNode> | 获取结束节点Callkey | 无 | string - Callkey |
| `TryGet` | NodeCache<TNode> | 尝试从缓存获取节点 | key: string - 节点键 | TNode - 节点对象或null |
| `GetOrThrow` | NodeCache<TNode> | 从缓存获取节点，不存在则抛出异常 | key: string - 节点键 | TNode - 节点对象 |
| `Rebuild` | NodeCache<TNode> | 重建节点缓存 | workflows: IEnumerable<ChainInfo> - 工作流集合 | void |
| `ValidateWorkflowChain` | ModularValidator.WorkflowValidator | 验证工作流链 | chain: ChainInfo - 工作流链 | void |
| `ValidateNodeExists` | ModularValidator.WorkflowValidator | 验证节点是否存在 | callkey: string - 节点键<br>nodeCache: Dictionary<string, NodeConfig> - 节点缓存 | void |
| `ValidateChainConnectivity` | ModularValidator.ConnectionValidator | 验证链连接性 | chain: ChainInfo - 工作流链 | void |

### 4.4 完整的生命周期管理
- **工作流注册和管理**：支持工作流的注册、更新和删除，实现工作流的版本管理
- **执行状态持久化**：支持执行状态的持久化和恢复，确保工作流在系统重启后能够继续执行
- **观察者模式**：提供观察者模式，支持工作流状态变更通知，便于系统其他组件响应工作流状态变化
- **工作流依赖管理**：支持工作流之间的依赖关系管理，确保工作流按照正确的顺序执行
- **资源管理**：提供工作流执行过程中的资源管理，包括内存、CPU等资源的合理分配

**相关函数**：

| 函数名 | 所属类 | 功能描述 | 参数 | 返回值 |
|-------|--------|---------|------|--------|
| `RegisterOrUpdateWorkflow` | GenericWorkflowStorageManager<TWorkflow> | 注册或更新工作流 | workflow: TWorkflow - 工作流对象 | string - 工作流ID |
| `GetWorkflow` | GenericWorkflowStorageManager<TWorkflow> | 获取工作流 | workflowId: string - 工作流ID | TWorkflow - 工作流对象 |
| `RemoveWorkflow` | GenericWorkflowStorageManager<TWorkflow> | 移除工作流 | workflowId: string - 工作流ID | void |
| `GetAllWorkflowIds` | GenericWorkflowStorageManager<TWorkflow> | 获取所有工作流ID | 无 | List<string> - 工作流ID列表 |
| `RegisterObserver` | GenericWorkflowStorageManager<TWorkflow> | 注册工作流观察者 | workflowId: string - 工作流ID<br>callback: Action<TWorkflow> - 回调函数 | void |
| `UnregisterObserver` | GenericWorkflowStorageManager<TWorkflow> | 注销工作流观察者 | workflowId: string - 工作流ID<br>callback: Action<TWorkflow> - 回调函数 | void |
| `RegisterRemovalObserver` | GenericWorkflowStorageManager<TWorkflow> | 注册工作流移除观察者 | workflowId: string - 工作流ID<br>callback: Action<string> - 回调函数 | void |
| `GetWorkflowIdForNode` | GenericWorkflowStorageManager<TWorkflow> | 根据节点获取工作流ID | nodeId: string - 节点ID | string - 工作流ID |
| `FindWorkflowsByNode` | GenericWorkflowStorageManager<TWorkflow> | 根据节点查找所有关联工作流 | nodeId: string - 节点ID | List<string> - 工作流ID列表 |
| `IsNodeInAnyWorkflow` | GenericWorkflowStorageStorageManager<TWorkflow> | 检查节点是否属于任何工作流 | nodeId: string - 节点ID | bool - 是否存在 |
| `GetWorkflowReference` | GenericWorkflowStorageManager<TWorkflow> | 获取工作流引用 | workflowId: string - 工作流ID | WorkflowReference<TWorkflow> - 工作流引用 |
| `Refresh` | GenericWorkflowConfig<TWorkflow> | 刷新工作流配置 | 无 | void |
| `UpdateWorkflowId` | GenericWorkflowConfig<TWorkflow> | 更新工作流ID | newWorkflowId: string - 新工作流ID | void |
| `GetOrCreateConfig` | GenericConfigManager<TConfig, TWorkflow> | 获取或创建配置 | key: string - 配置键<br>workflow: TWorkflow - 工作流对象 | TConfig - 配置对象 |
| `RemoveConfig` | GenericConfigManager<TConfig, TWorkflow> | 移除配置 | key: string - 配置键 | void |
| `ClearAllConfigs` | GenericConfigManager<TConfig, TWorkflow> | 清除所有配置 | 无 | void |
| `Dispose` | GenericWorkflowStorageManager<TWorkflow> | 释放资源 | 无 | void |
| `Dispose` | GenericWorkflowConfig<TWorkflow> | 释放资源 | 无 | void |
| `Dispose` | GenericConfigManager<TConfig, TWorkflow> | 释放资源 | 无 | void |


## 5. 技术架构

### 5.1 模块化设计
- 采用模块化设计，各组件职责明确
- 支持组件的独立扩展和替换
- 提供统一的接口定义

### 5.2 ECS 架构
- 采用 Entity Component System 架构管理工作流
- 提供高效的实体管理和组件更新机制
- 支持并发操作和状态管理

### 5.3 异步处理
- 支持异步任务处理
- 提供任务队列和处理器
- 支持取消和超时控制

### 5.4 可扩展性
- 支持自定义节点类型和处理逻辑
- 支持自定义工作流模板
- 支持插件式扩展

## 6. 使用示例

### 6.1 创建简单工作流

```csharp
// 创建一个包含单个节点的工作流
var workflowJson = WorkflowBuilder.Build(
    "TestNode",
    "node_0",
    "TestFunction",
    new object[] { "Input_1", "Input_2" },
    new object[] { "Output_1" },
    "测试节点"
);
```

### 6.2 从字符串定义创建工作流

```csharp
// 从字符串定义创建工作流
var workflowJson = WorkflowBuilder.BuildFromStrings(
    "MerageNode:node_0:MergePrefix:DataProcess,Inputs_:Result_",
    "DataInitial:node_1:WebInvoke:WebSet_,Result_:Output_"
);
```

### 6.3 执行工作流

```csharp
// 生成执行ID
var executionId = WorkflowExecutionHelper.GenerateExecutionId("TestWorkflow");

// 执行工作流
var result = await WorkflowProcessor.ExecuteWorkflowAsync(
    executionId,
    "startNode",
    new Dictionary<string, object> { { "Input_1", "value1" }, { "Input_2", "value2" } }
);
```

### 6.4 动态更新工作流

```csharp
// 创建更新指令
var directive = new InsertChainDirective
{
    InsertAfterNodeCallkey = "existingNode",
    ChainJsonConfigs = new List<string> { "{...}" },
    ListCount = 1
};

// 应用更新
var updatedChain = GraphWorkflowUpdater.ApplyGraphUpdate(
    originalChain,
    new List<GraphUpdateDirective> { directive }
);
```

## 7. 应用场景

### 7.1 AI 应用工作流
- **AI生图工作流**：从输入提示词生成图像
- **AI代码生成工作流**：根据需求生成代码
- **数据分析工作流**：处理和分析数据

### 7.2 业务流程自动化
- **审批流程**：自动化处理审批流程
- **数据处理流程**：批量处理数据
- **集成流程**：连接不同系统的集成流程

### 7.3 测试和验证
- **编译测试工作流**：自动编译和测试代码
- **数据库测试工作流**：测试数据库操作
- **性能测试工作流**：执行性能测试

## 8. 总结

WorkFlowMethod 是一个功能强大、设计灵活的工作流编排框架，提供了完整的工作流定义、构建、执行和管理功能。它采用模块化设计和 ECS 架构，支持动态工作流构建、执行状态管理、节点连接和动态更新等核心功能。

该框架适用于各种复杂的业务场景，特别是需要灵活编排和管理多步骤流程的应用。通过预定义的工作流模板和可扩展的架构，它可以快速适应不同的业务需求，提高开发效率和系统可靠性。

WorkFlowMethod 不仅提供了完整的工作流功能，还通过简洁的 API 和丰富的文档，使开发者能够快速上手和使用，为复杂系统的流程管理提供了有力的工具支持。
