# NodeProcess 覆盖 Orchestration 节点功能 — 迁移计划

> **目标**：将 `Orchestration/` 目录中的节点相关逻辑逐步迁移到 `NodeProcess/`，使 `NodeProcess` 成为统一的节点处理中心，`Orchestration` 精简为纯粹的编排层。

---

## 一、现状分析

### Orchestration 中的节点相关功能

| 功能域 | 文件 | 职责 |
|--------|------|------|
| **节点模型** | `State/Models.cs` | `NodeConfig`（5 子配置）、`ChainInfo`、`ChainInfo<TNode>`、DTO 类 |
| **节点工厂** | `State/NodeConfigFactory.cs` | `CreateNode()` 序列化、`CompleteConfig()` 合并模板、`BaseConfig` 模板数据源 |
| **图操作** | `State/GraphOperations.cs` | `NodeManager`（创建链/克隆/连接）、`NodeCommandAdapter`、`IDeduplicationProcessor` |
| **运行时** | `State/WorkflowRuntime.cs` | `WorkflowRuntimeContext`（节点注册/执行/暂停/恢复/取消） |
| **状态管理** | `State/StateManager.cs` | `StateManager`（协调器、错误处理器注册表） |
| **编排 Stage** | `State/Stages/` | `OrchestrationExecutionStage`、`OrchestrationLoopStage`、`OrchestrationErrorHandlingStage` |
| **Stage 桥接** | `State/StageBridge.cs` | `StageBridge<TStage>` 工厂方法 |
| **上下文扩展** | `State/ContextExtensions.cs` | PipelineContext 扩展（调用帧记录、共享数据） |
| **ID 管理** | `State/Identity.cs` | `IdGenerator`、`NodeRegistry` |
| **存储** | `State/Storage.cs` | `ChainInfoWorkflowStorageManager`、`WebCallManager` |
| **诊断** | `State/Diagnostics.cs` | `ChainInfoStorageDiagnostics`、`ExecutionRecordEntry` |

### NodeProcess 当前能力

| 文件 | 职责 |
|------|------|
| `Models/INodeProcessor.cs` | 4 步骤接口：GetConfig → ExecuteFunction → RecordCallFrame → ResolveNextNode |
| `Models/NodeDefinition.cs` | 节点定义（不可变配置视图） |
| `Models/NodeProcessingContext.cs` | 节点处理上下文 |
| `Models/ErrorHandling.cs` | 错误处理类型 |
| `Processors/DefaultNodeProcessor.cs` | 默认组合处理器 |
| `Processors/GetConfigProcessor.cs` | 配置获取 |
| `Processors/FunctionExecutor.cs` | 函数执行 |
| `Processors/NextNodeResolver.cs` | 下一节点解析 |
| `NodeProcessSkeleton.cs` | 编排 4 步骤的 Skeleton |
| `Execution/NodeErrorHandlerStage.cs` | 节点错误处理 Stage |

**差距**：NodeProcess 目前只覆盖了"单个节点执行"这一小部分，Orchestration 中的节点模型、创建、链操作、运行时管理等均未覆盖。

---

## 二、迁移计划（分阶段）

### 第一阶段：节点模型迁移

**迁移内容**：将 `Orchestration/State/Models.cs` 中的节点模型移到 `NodeProcess/Models/`

| 源文件 | 目标文件 | 内容 |
|--------|----------|------|
| `Orchestration/State/Models.cs` | `NodeProcess/Models/NodeConfig.cs` | `NodeConfig` + 5 子配置类 |
| `Orchestration/State/Models.cs` | `NodeProcess/Models/ChainInfo.cs` | `ChainInfo`、`ChainInfo<TNode>` |
| `Orchestration/State/Models.cs` | `NodeProcess/Models/Dtos.cs` | DTO 类（`UniversalNodeDto`、`NodeDetailDto` 等） |
| `Orchestration/State/Models.cs` | `NodeProcess/Models/ConnectionModels.cs` | 连接相关模型（`ConnectionPositionInfo`、`WorkflowConnection` 等） |

**影响**：
- `Orchestration` 中保留 `using` 别名指向新位置，确保向后兼容
- 更新所有引用 `Orchestration.Config.NodeConfig` 的地方

### 第二阶段：节点工厂迁移

**迁移内容**：将 `NodeConfigFactory` 移到 `NodeProcess`

| 源文件 | 目标文件 | 内容 |
|--------|----------|------|
| `Orchestration/State/NodeConfigFactory.cs` | `NodeProcess/NodeConfigFactory.cs` | `CreateNode()`、`CompleteConfig()`、`BaseConfig` |

**影响**：
- `NodeConfigFactory` 依赖 `Share.LogicComposition`（`ObjectMerger`、`TemplateConfigProvider`），需确认依赖关系
- `Orchestration` 中保留转发调用

### 第三阶段：节点管理器迁移

**迁移内容**：将 `NodeManager` 移到 `NodeProcess`

| 源文件 | 目标文件 | 内容 |
|--------|----------|------|
| `Orchestration/State/GraphOperations.cs` | `NodeProcess/NodeManager.cs` | `CreateChainInfo`、`CreateChainedNodes`、`LinkCrossChain` |
| `Orchestration/State/GraphOperations.cs` | `NodeProcess/NodeCommandAdapter.cs` | `NodeCommandAdapter` |
| `Orchestration/State/GraphOperations.cs` | `NodeProcess/DeduplicationProcessor.cs` | `IDeduplicationProcessor`、`DefaultDeduplicationProcessor` |

**影响**：
- `NodeManager` 依赖 `WorkflowRuntimeContext`，需解耦或一起迁移
- 依赖 `KeyManager`（`Infrastructure/KeyMapping`）

### 第四阶段：运行时管理迁移

**迁移内容**：将 `WorkflowRuntimeContext` 移到 `NodeProcess`

| 源文件 | 目标文件 | 内容 |
|--------|----------|------|
| `Orchestration/Content/Context/WorkflowRuntime.cs` | `NodeProcess/Runtime/WorkflowRuntimeContext.cs` | 运行时上下文 |
| `Orchestration/Content/Context/WorkflowRuntime.cs` | `NodeProcess/Runtime/WorkflowRuntimeContextManager.cs` | 上下文管理器 |

**影响**：
- 这是最大的迁移项，涉及 `ConcurrentDictionary` 管理、异步执行、暂停/恢复机制
- 依赖 `INodeExecutorFactory`、`WorkflowConfiguration` 等

### 第五阶段：编排 Stage 适配

**迁移内容**：调整 `Orchestration/State/Stages/` 中的 Stage，使其通过 `NodeProcess` 新 API 工作

| 当前 | 目标 |
|------|------|
| `OrchestrationLoopStage` 直接引用 `NodeProcessSkeleton` | 保持不变（已正确） |
| `OrchestrationExecutionStage` 使用 `GraphSnapshot` | 通过 `NodeProcess` 获取节点信息 |
| `OrchestrationErrorHandlingStage` 检查节点状态 | 通过 `NodeProcess` 的错误处理 API |

---

## 三、依赖关系图

```
NodeProcess (目标中心)
├── Models/              ← 第一阶段：从 Orchestration 迁移
│   ├── NodeConfig.cs    (原 Orchestration/State/Models.cs)
│   ├── ChainInfo.cs     (原 Orchestration/State/Models.cs)
│   ├── Dtos.cs          (原 Orchestration/State/Models.cs)
│   ├── INodeProcessor.cs (已有)
│   ├── NodeDefinition.cs (已有)
│   └── ErrorHandling.cs  (已有)
├── NodeConfigFactory.cs  ← 第二阶段：从 Orchestration 迁移
├── NodeManager.cs        ← 第三阶段：从 Orchestration 迁移
├── Runtime/              ← 第四阶段：从 Orchestration 迁移
│   ├── WorkflowRuntimeContext.cs
│   └── WorkflowRuntimeContextManager.cs
├── Processors/           (已有)
├── NodeProcessSkeleton.cs (已有)
└── Execution/            (已有)

Orchestration (精简后)
├── State/
│   ├── Stages/           ← 保留，通过 NodeProcess API 工作
│   ├── StageBridge.cs    ← 保留
│   ├── StateManager.cs   ← 保留
│   ├── ContextExtensions.cs ← 保留
│   └── ...               ← 删除已迁移的模型/工厂/管理器
├── Content/
│   └── Context/
│       └── WorkflowRuntime.cs ← 删除（已迁移）
└── ...
```

---

## 四、迁移优先级

| 优先级 | 阶段 | 原因 |
|--------|------|------|
| **P0** | 第一阶段（模型） | 基础依赖，其他迁移都依赖模型定义 |
| **P1** | 第二阶段（工厂） | 节点创建是核心功能，独立性强 |
| **P2** | 第三阶段（管理器） | 链操作依赖模型和工厂 |
| **P3** | 第五阶段（Stage 适配） | 可并行，影响面小 |
| **P4** | 第四阶段（运行时） | 影响最大，需谨慎处理 |

---

## 五、向后兼容策略

1. **保留 using 别名**：在 `Orchestration` 中保留 `using NodeConfig = NodeProcess.Models.NodeConfig` 等别名
2. **保留静态转发方法**：在原始位置保留 `[Obsolete]` 标记的转发方法
3. **逐步删除**：每个阶段完成后，通过编译警告确认无引用后再删除旧代码
4. **测试覆盖**：每个阶段迁移后运行完整编译验证
