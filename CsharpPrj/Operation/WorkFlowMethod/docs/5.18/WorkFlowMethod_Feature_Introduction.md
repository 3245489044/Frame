# WorkFlowMethod 介绍与使用手册

## 1. 概述

WorkFlowMethod 是一个功能完备的工作流编排框架，专为复杂业务流程的自动化和智能化管理而设计。该框架提供了从工作流定义、构建到执行、监控的全生命周期管理能力，适用于企业级业务流程自动化、AI 应用集成、微服务编排、数据处理管道等多种场景。

### 1.1 核心价值

- **提高业务效率**：通过自动化编排和执行复杂业务流程，减少人工干预，提高处理速度和准确性
- **降低开发成本**：提供标准化的工作流定义和执行机制，减少重复开发，加快应用构建速度
- **增强系统可靠性**：通过状态管理、错误处理和故障恢复机制，确保工作流的稳定执行
- **提升系统可维护性**：模块化设计和标准化接口，使系统更易于理解、维护和扩展
- **支持业务创新**：灵活的工作流配置和动态更新能力，使企业能够快速响应业务变化和创新需求

### 1.2 应用场景

- **企业级业务流程**：如审批流程、采购流程、报销流程等复杂业务流程的自动化管理
- **AI 应用集成**：如 AI 生图、AI 代码生成、数据分析等 AI 工作流的编排和执行
- **微服务集成**：连接和协调多个微服务，实现复杂的分布式业务逻辑
- **数据处理管道**：构建和管理数据提取、转换、加载（ETL）流程
- **测试自动化**：自动化测试流程，包括单元测试、集成测试和性能测试
- **DevOps 流程**：自动化构建、部署、监控等 DevOps 流程

### 1.3 设计理念

- **模块化设计**：各组件职责明确，松耦合，支持独立扩展和替换
- **标准化接口**：提供统一的接口定义，便于与外部系统集成
- **灵活性优先**：支持多种工作流定义方式和执行模式，适应不同业务场景
- **可靠性保障**：内置状态管理、错误处理和故障恢复机制
- **性能优化**：采用异步处理、并发控制等技术，提高执行效率

## 2. 核心功能

### 2.1 全生命周期管理

提供从工作流定义、构建、执行、监控到归档的完整生命周期支持，确保业务流程的端到端可追溯性。支持工作流版本管理和历史记录查询，便于审计和合规要求。

### 2.2 高度可定制

通过灵活的节点配置系统，支持自定义节点类型、参数映射、条件分支、并行执行等复杂业务逻辑。提供丰富的配置选项，满足不同业务场景的个性化需求。

### 2.3 动态自适应

支持运行时工作流结构的动态调整，包括节点的实时添加、删除和修改，使工作流能够根据业务变化自动适应。

### 2.4 实时监控与控制

提供详细的执行状态跟踪，包括节点执行状态、执行时间、错误信息等。支持全局暂停、恢复、终止等控制操作，便于系统维护和故障处理。

### 2.5 模块化设计

采用松耦合的组件架构，各模块职责明确，支持独立扩展和替换。提供标准化的接口，便于与外部系统集成。

### 2.6 技术特性

- **异步执行**：基于任务处理系统的异步执行，支持任务队列、并发控制和优先级管理
- **状态持久化**：支持执行状态持久化存储，确保系统重启后可恢复执行，提供断点续传能力
- **观察者模式**：实时状态变更通知机制，支持自定义观察者
- **依赖管理**：工作流间依赖关系的智能管理，支持循环依赖检测
- **资源优化**：执行过程中的资源使用监控与优化，支持节点执行超时控制
- **安全性**：支持工作流访问控制、敏感数据加密存储和审计日志
- **可扩展性**：支持组件独立扩展、插件机制及自定义节点类型

## 3. 快速开始

### 3.1 环境要求

- .NET 6.0 或更高版本
- 支持的操作系统：Windows、Linux、macOS

### 3.2 安装

**通过 NuGet 安装**：

```bash
Install-Package WorkFlowMethod
```

**通过 .NET CLI 安装**：

```bash
dotnet add package WorkFlowMethod
```

### 3.3 基本配置

在应用启动时初始化工作流配置：

```csharp
// 初始化工作流配置
var workflowConfig = new WorkflowConfig {
    MaxExecutionTime = TimeSpan.FromMinutes(30),
    DefaultConcurrencyLimit = 10,
    EnableExecutionHistory = true
};

// 注册工作流服务
services.AddWorkFlowMethod(workflowConfig);
```

#### 存储配置

WorkFlowMethod 支持多种存储方式：

```csharp
// 使用内存存储
services.AddWorkFlowMethod()
    .AddInMemoryStorage();

// 使用 SQL Server 持久化存储
services.AddWorkFlowMethod()
    .AddSqlServerStorage("Server=localhost;Database=WorkFlowMethod;Trusted_Connection=True;");
```

#### 执行器配置

```csharp
// 配置执行器
services.AddWorkFlowMethod()
    .ConfigureExecutor(options => {
        options.MaxDegreeOfParallelism = 5;
        options.TaskTimeout = TimeSpan.FromMinutes(5);
        options.EnableDetailedLogging = true;
    });
```

### 3.4 创建第一个工作流

```csharp
// 构建工作流
var workflowJson = WorkflowBuilder.Build(
    "HelloNode",
    "node_1",
    "HelloWorld",
    new object[] { "name" },
    new object[] { "greeting" },
    "问候节点"
);

// 注册工作流
var workflowId = await workflowStorageManager.RegisterOrUpdateWorkflow(workflowJson);

// 执行工作流
var parameters = new Dictionary<string, object> { { "name", "World" } };
var result = await workflowProcessor.ExecuteWorkflowAsync(workflowId, parameters);

// 获取执行结果
var greeting = result.Outputs["greeting"];
Console.WriteLine(greeting); // 输出: Hello, World!
```

### 3.5 从字符串定义创建工作流

```csharp
// 从字符串定义创建工作流
var workflowJson = WorkflowBuilder.BuildFromStrings(
    "MergeNode:node_0:MergePrefix:DataProcess,Inputs_:Result_",
    "DataInitial:node_1:WebInvoke:WebSet_,Result_:Output_"
);
```

### 3.6 配置自定义节点

```csharp
// 注册自定义节点处理器
services.AddWorkFlowMethod()
    .AddNodeProcessor<CustomNodeProcessor>("CustomNode");

// 自定义节点处理器实现
public class CustomNodeProcessor : INodeProcessor
{
    public Task<IDictionary<string, object>> ProcessAsync(
        string nodeId,
        IDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, object>
        {
            { "result", "Custom node processed" }
        };
        return Task.FromResult<IDictionary<string, object>>(result);
    }
}
```

## 4. 工作流构建方式

### 4.1 编程式构建

通过 WorkflowBuilder 提供的 API，可以用代码方式灵活定义工作流：

```csharp
// 创建节点
var node = WorkflowBuilder.CreateNode(
    symbol: "ProcessNode",
    nodeId: "node_1",
    funcName: "DataProcessor",
    parameterKeys: new object[] { "inputData" },
    resultKeys: new object[] { "outputData" },
    label: "数据处理节点"
);
```

### 4.2 JSON 文件加载

支持从 JSON 文件加载预定义的工作流配置，便于版本控制和复用：

```csharp
var nodes = WorkflowBuilder.LoadFromFile("workflow_config.json");
```

### 4.3 简洁字符串定义

通过特定格式的字符串快速定义工作流节点和连接关系，适合快速原型开发：

```csharp
// 格式: Symbol:NodeId:FuncName:ParameterKeys:ResultKeys
WorkflowBuilder.AddNodesFromStrings(nodes,
    "StartNode:node_0:Initialize:config:status",
    "ProcessNode:node_1:Execute:status:result"
);
```

## 5. 工作流执行与控制

### 5.1 执行工作流

```csharp
// 生成执行ID
var executionId = WorkflowExecutionHelper.GenerateExecutionId("MyWorkflow");

// 执行工作流
var result = await WorkflowProcessor.ExecuteWorkflowAsync(
    executionId,
    "startNode",
    new Dictionary<string, object> { { "Input_1", "value1" } }
);
```

### 5.2 执行控制

WorkFlowMethod 提供完整的执行控制能力：

```csharp
// 暂停工作流执行
workflowProcessor.PauseWorkflow(executionId);

// 恢复工作流执行
workflowProcessor.ResumeWorkflow(executionId);

// 终止工作流执行
workflowProcessor.TerminateWorkflow(executionId);
```

### 5.3 监控与统计

```csharp
// 获取执行状态
var status = workflowExecutionManager.GetExecutionStatus(executionId);

// 获取执行统计信息
var stats = workflowExecutionManager.GetExecutionStats(executionId);
```

## 6. 动态更新

WorkFlowMethod 支持在工作流运行过程中动态调整工作流结构，无需重新部署：

```csharp
// 创建插入节点指令
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

## 7. 架构演进

### 7.1 图逻辑层统一方案

#### 背景与问题

WorkFlowMethod 框架早期存在**两套平行的节点连接指针系统**，描述同一个拓扑结构但存储在不同位置，需要手动同步：

| 系统 | 位置 | 描述 |
| --- | --- | --- |
| **GraphFoundation 边** | CoreGraph EdgeComponent | 图引擎的标准边体系 |
| **NodeConfig.nextNodeConfig.NodeKey** | NodeConfig 字段 | 旧平行指针系统（已废弃） |

核心问题包括：
- **数据不一致**：修改图引擎后忘记同步 NodeKey 导致链提取失败
- **冗余转换**：链↔图转换时必须额外读写 NodeKey，增加复杂度
- **桥接层碎片**：三个独立的转换实现各有不同的处理逻辑

#### 目标架构：单一数据源

**GraphFoundation 的边体系**是节点连接关系的单一真实来源（Single Source of Truth），`NodeConfig.nextNodeConfig.NodeKey` 被标记为 deprecated 并在所有读路径上由边查询替换。

```
   [源]                         [消费]

   GraphFoundation 边   ──→    BuildGraph()
   (EdgeComponent)             ──── 从 NodeKey 读取，自解释
        │
        │                  ──→    ExtractChains()
        │                         直接从 GetOutgoingEdges 遍历
        │
        │                  ──→    ChainInfo 序列化
        │                         nextNodeConfig.NodeKey 作为缓存
        │                         （写时由转换层填充）
        │
        └──                  ──→   运行时执行
                                    直接从 GetOutgoingEdges 找下一节点
```

| 维度 | 旧架构 | 新架构 |
| --- | --- | --- |
| 连接关系存储 | `nextNodeConfig.NodeKey` + Edge | **仅 Edge** |
| 链→图转换 | 从 `NodeKey` 创建 Edge | 直接 AddEdge |
| 图→链转换 | 从 Edge 写回 `NodeKey` | 从 Edge 直接构建链，NodeKey 只作为缓存 |
| 新节点插入 | 必须手动设置 `nextNodeConfig` | 只需 AddEdge |
| 边操作 | 分散在各处 | 统一通过 GraphFoundation 事务 |
| 桥接层数量 | 3 个 | 1 个 |

#### 统一桥接层 GraphFoundationBridge

GraphFoundationBridge 是整个架构演进的核心组件，提供统一的链⇄图桥接能力：

```
                    ┌──────────────────────────────────────┐
                    │         统一桥接层                     │
                    │  GraphFoundationBridge                │
                    │  ├─ ChainToGraph()                   │
                    │  │    从 AllNodes 创建节点，从边连接   │
                    │  ├─ GraphToChain()                   │
                    │  │    从 GetOutgoingEdges 遍历链      │
                    │  │    回写 nextNodeConfig.NodeKey     │
                    │  │    （仅作为序列化缓存）            │
                    │  ├─ InsertNodesAfter/Before()         │
                    │  │    使用事务完成插入 + 重连         │
                    │  ├─ DeleteNode()                     │
                    │  ├─ ReplaceNode()                    │
                    │  ├─ RedirectEdge()                   │
                    │  └─ GetNextNodeKey()                 │
                    │       替代 NodeKey 读路径             │
                    └──────────────────────────────────────┘
```

#### 实施状态

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| **第一阶段**：标记废弃 + 创建统一桥接层 | ✅ 已完成 | `[Obsolete]` 标记已添加，`GraphFoundationBridge` 已创建 |
| **第二阶段**：替换 DynamicUpdateTransformer | ✅ 已完成 | 所有 Apply 方法已改为桥接层调用 |
| **第三阶段**：删除冗余桥接层实现 | ✅ 已完成 | `GraphAdapter` 已删除，`GraphChainBridge` 已简化 |
| **第四阶段**：边操作事务化 | ✅ 已完成 | 所有边操作通过桥接层事务方法完成 |

#### 运行时查询模式对照

| 场景 | 旧模式（平行指针） | 新模式（图边查询） |
| --- | --- | --- |
| 获取下一节点 | `node.nextNodeConfig.NodeKey` | `graph.GetOutgoingEdges(key)[0].Target` |
| 获取上一节点 | 无直接支持 | `graph.GetIncomingEdges(key)[0].Source` |
| 判断是否为尾节点 | `nextNodeConfig.HandleNextNodeRun == "false"` | `graph.GetOutgoingEdges(key).Count == 0` |
| 判断是否存在后继 | `!string.IsNullOrEmpty(nextNodeConfig.NextNodeKey)` | `graph.GetOutgoingEdges(key).Count > 0` |
| 序列化 ChainInfo | 手动设置 | `SyncNextNodeKeys()` 自动回填 |
| 新链插入 | 手动拼 nextNodeConfig + AddEdge | `InsertNodesAfter/Before` 自动完成 |
| 链遍历 | 从 StartNode 沿 NodeKey 走 | 从入度为 0 的节点沿出边走 |

### 7.2 NodeKey 废弃迁移指南

#### 概述

`NodeConfig.nextNodeConfig.NodeKey` 已被标记为 `[Obsolete]`，项目正从**平行指针系统**迁移到以 **GraphFoundation 边**作为节点连接关系唯一真实来源的架构。

```
旧模式（平行指针）：                       新模式（图边查询）：
node.nextNodeConfig.NodeKey       →       graph.GetOutgoingEdges(key)[0].Target
NodeKey 手动同步                             GraphFoundationBridge.GetNextNodeKey(graph, key)
```

`NodeKey` 现在仅作为**序列化缓存**保留，由 `GraphFoundationBridge.GraphToChain()` 中的 `SyncNextNodeKeys()` 自动回填。所有**读路径**都应替换为图边查询。

#### 废弃定义位置

[OrchestrationConnect.cs](file:///d:/Project/Program/WorkFlow_/WorkFlowMethod/Orchestration/OrchestrationConnect.cs)：

```csharp
[Obsolete("Use GraphFoundation edges instead. " +
          "Will be removed in future version. " +
          "Use GraphFoundationBridge.GetNextNodeKey() to read.")]
public string NodeKey { get; set; }
```

#### 迁移分类与策略

| 类别 | 计数 | 策略 |
| --- | --- | --- |
| **写入序列化缓存**（有意为之） | 3 处 | `#pragma warning disable` |
| **序列化/渲染读取**（无图上下文） | 2 处 | `#pragma warning disable` |
| **更新引用跟踪**（无图上下文） | 1 处 | `#pragma warning disable` |
| **旧桥接层过渡代码**（待删除） | 1 处 | `#pragma warning disable` |
| **过渡读路径**（可查图边） | 1 处 | 改为图边查询 |
| **运行时读路径**（有图上下文） | 2 处 | 改为 `Callkey` 查询 |

#### 推荐实施步骤

**第 1 步：压制无法避免的警告（安全）**

将安全写入和无图上下文位置的警告压制，每个压制处添加注释说明原因：

```csharp
// ── 序列化缓存：NodeKey 已标记 [Obsolete] ──
// GraphFoundation 边是连接关系的唯一真实来源
#pragma warning disable CS0618
node.nextNodeConfig.NodeKey = targetKey;
#pragma warning restore CS0618
```

**第 2 步：改造有图上下文的读路径**

优先使用 `graph.GetOutgoingEdges(key)` 查询，回退 `NodeKey`（兼容旧数据）：

```csharp
var nextKey = graph.GetOutgoingEdges(key).Count > 0
    ? graph.GetOutgoingEdges(key)[0].Target.ToString()
    : node?.nextNodeConfig?.NodeKey; // ← 回退（兼容旧数据）
```

**第 3 步：验证构建**

```bash
dotnet build
```

目标：**CS0618 零警告**，所有 `#pragma warning disable CS0618` 均有注释说明。

#### 未来清理计划

当所有持久化数据完成迁移后：
- 删除 `[Obsolete]` 属性和字段定义
- 删除所有 `#pragma warning disable CS0618`
- 删除 `SyncNextNodeKeys` 方法
- 清理 JSON 序列化模板中的 `NodeKey` 输出

## 8. 预定义工作流模板

WorkFlowMethod 提供了丰富的预定义工作流模板，涵盖以下领域：

- **AI 图像生成**：从输入提示词生成图像的工作流
- **AI 代码生成**：根据需求自动生成代码的工作流
- **数据分析**：数据处理和分析的工作流
- **数据包装存储**：数据格式转换和持久化的工作流
- **提示词处理**：提示词合并、模板定义与执行策略

## 9. API 参考

### 9.1 工作流构建 API

| API | 说明 |
| --- | --- |
| WorkflowBuilder | 工作流构建、解析、加载和保存的核心工具 |
| WorkflowFactory | 工作流模板管理和快速创建 |

### 9.2 存储和执行 API

| API | 说明 |
| --- | --- |
| GenericWorkflowStorageManager | 工作流注册、更新、删除和查询的存储管理 |
| WorkflowExecutionManager | 执行状态跟踪、节点执行控制 |
| TaskProcessor | 异步任务处理、并发控制和优先级管理 |

### 9.3 节点和连接 API

| API | 说明 |
| --- | --- |
| NodeConfig / INodeConfigProvider | 节点配置定义和管理 |
| ChainInfo | 工作流链信息管理，支持顺序、条件分支、并行执行路径 |
| ModularValidator | 工作流、节点、连接的多层级验证 |
| WorkflowProcessor | 工作流执行核心逻辑 |

### 9.4 动态更新 API

| API | 说明 |
| --- | --- |
| GraphWorkflowUpdater | 工作流图运行时更新 |
| GraphUpdateDirective | 更新指令定义（插入、删除、替换、重定向） |

### 9.5 接口与扩展点

| 接口 | 用途 |
| --- | --- |
| IWorkflowDeserializer | 自定义工作流反序列化 |
| INodeIdentityGenerator | 自定义节点标识生成 |
| IParameterProcessor | 自定义参数处理逻辑 |
| IWorkflowMediator | 自定义编排中介逻辑 |
| INodeProcessor | 自定义节点处理逻辑 |

## 10. 应用示例

### 10.1 AI 生图工作流

```csharp
// 使用预定义的 AI 图像生成工作流模板
var workflowJson = WorkflowBuilder.BuildFromStrings(
    "PromptGen:node_0:GeneratePrompt:input:prompt",
    "ImageGen:node_1:AIGenerateImage:prompt:imageUrl"
);
```

### 10.2 数据处理管道

```csharp
// 构建数据处理工作流
var workflowJson = WorkflowBuilder.BuildFromStrings(
    "Extract:node_0:DataExtract:source:rawData",
    "Transform:node_1:DataTransform:rawData:transformedData",
    "Load:node_2:DataLoad:transformedData:status"
);
```

## 11. 版本历史

| 版本 | 日期 | 主要特性 |
| --- | --- | --- |
| 3.0.0 | 2026-05-13 | 全项目架构重构：模块化工作流创建管线、节点配置与执行解耦、图引擎统一、编排中介层与全局状态管理优化 |
| 2.2.0 | 2025-04-10 | 优化 API 设计，提供更简洁的接口；增加工作流可视化构建工具 |
| 2.1.0 | 2025-01-20 | 增加多种数据库存储支持；工作流版本管理功能 |
| 2.0.0 | 2024-10-15 | 重构核心架构，优化执行引擎，增加更多预定义工作流模板 |
| 1.0.0 | 2024-01-15 | 初始版本，包含基本工作流构建、执行和管理功能 |

## 12. 总结

WorkFlowMethod 是一个功能强大、设计灵活的工作流编排框架，提供了完整的工作流定义、构建、执行和管理功能。无论是简单的线性流程还是复杂的并行分支流程，WorkFlowMethod 都能提供高效、可靠的执行保障。

该框架适用于各种复杂的业务场景，特别是需要灵活编排和管理多步骤流程的应用。通过预定义的工作流模板和可扩展的架构，它可以快速适应不同的业务需求，提高开发效率和系统可靠性。
