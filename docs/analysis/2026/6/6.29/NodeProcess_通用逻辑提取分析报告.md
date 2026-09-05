# NodeProcess 通用逻辑提取分析报告

> 分析日期：2026-06-28
> 分析范围：`CsharpPrj/Operation/WorkFlowMethod/NodeProcess` → `CsharpPrj/Share/WorkflowExecution`

---

## 一、已提取的通用逻辑（12项）

以下类型已从 `NodeProcess` 提取到 `WorkflowExecution`：

| # | 类型 | 源位置 | 目标位置 | 说明 |
|---|------|--------|----------|------|
| 1 | `IParameterResolver` | `NodeProcess/Abstractions` | `WorkflowExecution/Abstractions` | 参数解析器接口 |
| 2 | `ParameterResolver` | `NodeProcess/Execution` | `WorkflowExecution/Abstractions` | 参数解析器实现 |
| 3 | `PlaceholderEngine` | `NodeProcess/PlaceholderResolution` | `WorkflowExecution/Engine` | 占位符解析引擎 |
| 4 | `FuncCallRun` | `NodeProcess/Execution` | `WorkflowExecution/Engine` | 函数管线执行引擎 |
| 5 | `PipelineToStageAdapter` | `NodeProcess/Abstractions` | `WorkflowExecution/Adapters` | Pipeline→Stage 适配器 |
| 6 | `StageToPipelineAdapter` | `NodeProcess/Abstractions` | `WorkflowExecution/Adapters` | Stage→Pipeline 适配器 |
| 7 | `INodeRepository<TNode>` | `NodeProcess/Abstractions` | `WorkflowExecution/Abstractions` | 节点仓库接口 |
| 8 | `INodeConfigExtractor<TWorkflow>` | `NodeProcess/Abstractions` | `WorkflowExecution/Abstractions` | 节点配置提取器接口 |
| 9 | `FieldMappingEntry` | `NodeProcess/ReferenceRegistration` | `WorkflowExecution/Abstractions` | 字段映射条目 |
| 10 | `FieldMappingMode` | `NodeProcess/ReferenceRegistration` | `WorkflowExecution/Abstractions` | 字段映射模式枚举 |
| 11 | `RuntimeContext<TChainInfo>` | `NodeProcess/Runtime` | `WorkflowExecution/Models` | 泛型运行时上下文 |
| 12 | `RuntimeContextManager<TContext, TChainInfo>` | `NodeProcess/Runtime` | `WorkflowExecution/Models` | 泛型运行时上下文管理器 |

---

## 二、提取后引用更新情况

### 2.1 已更新 using 的文件

| 文件 | 旧引用 | 新引用 |
|------|--------|--------|
| `NodeProcess/NodeProcessPipelineBuilder.cs` | `NodeProcess.Abstractions.IParameterResolver` | `WorkflowExecution.Abstractions` |
| `NodeProcess/Execution/NodeExecutionStage.cs` | `NodeProcess.Abstractions.IParameterResolver` | `WorkflowExecution.Abstractions` |
| `NodeProcess/Execution/ParameterResolver.cs` | `NodeProcess.Abstractions.IParameterResolver` | `WorkflowExecution.Abstractions` |
| `NodeProcess/Models/ExecutionConfig.cs` | `NodeProcess.Abstractions.INodeRepository` | `WorkflowExecution.Abstractions` |
| `NodeProcess/GraphUsing/Deduplication/DeduplicationConfig.cs` | `NodeProcess.ReferenceRegistration.FieldMappingEntry` | `WorkflowExecution.Abstractions` |

### 2.2 无需更新的文件

以下文件未引用已提取的类型，无需修改：

- `NodeProcess/NodeProcessSkeleton.cs`
- `NodeProcess/NodeChainSkeleton.cs`
- `NodeProcess/Graph/GraphChainUpdateStage.cs`
- `NodeProcess/Graph/GraphDefinitionStage.cs`
- `NodeProcess/Lifecycle/NodeMetricsStage.cs`
- `NodeProcess/Execution/NodeExecutionStepSkeleton.cs`
- `NodeProcess/Models/ChainInfo.cs`
- `NodeProcess/Models/ConnectionModels.cs`
- `NodeProcess/Models/Dtos.cs`
- `NodeProcess/Models/INodeProcessor.cs`
- `NodeProcess/Models/MigrationTypes.cs`
- `NodeProcess/Models/NodeConfig.cs`
- `NodeProcess/Models/NodeDefinition.cs`
- `NodeProcess/Models/ResultTypes.cs`
- `NodeProcess/Runtime/WorkflowRuntimeContext.cs`
- `NodeProcess/Runtime/WorkflowRuntimeContextManager.cs`
- `NodeProcess/Abstractions/INodeChainService.cs`
- `NodeProcess/Abstractions/INodeExecutionCoordinator.cs`
- `NodeProcess/Abstractions/IWorkflowMediator.cs`
- `NodeProcess/Abstractions/IWorkflowRuntimeContextAccessor.cs`

---

## 三、已删除的源文件（✅ 2026-06-28 已执行）

以下 11 个文件中的类型已提取到 `WorkflowExecution`，且 `NodeProcess` 内部无其他文件引用它们，已于 2026-06-28 安全删除：

| 文件 | 提取目标 | 状态 |
|------|----------|------|
| `NodeProcess/Abstractions/IParameterResolver.cs` | `WorkflowExecution/Abstractions/IParameterResolver.cs` | ✅ 已删除 |
| `NodeProcess/Abstractions/INodeRepository.cs` | `WorkflowExecution/Abstractions/INodeRepository.cs` | ✅ 已删除 |
| `NodeProcess/Abstractions/INodeConfigExtractor.cs` | `WorkflowExecution/Abstractions/INodeConfigExtractor.cs` | ✅ 已删除 |
| `NodeProcess/Abstractions/PipelineToStageAdapter.cs` | `WorkflowExecution/Adapters/PipelineToStageAdapter.cs` | ✅ 已删除 |
| `NodeProcess/Abstractions/StageToPipelineAdapter.cs` | `WorkflowExecution/Adapters/StageToPipelineAdapter.cs` | ✅ 已删除 |
| `NodeProcess/Execution/ParameterResolver.cs` | `WorkflowExecution/Abstractions/ParameterResolver.cs` | ✅ 已删除 |
| `NodeProcess/Execution/FuncCallRun.cs` | `WorkflowExecution/Engine/FuncCallRun.cs` | ✅ 已删除 |
| `NodeProcess/PlaceholderResolution/PlaceholderEngine.cs` | `WorkflowExecution/Engine/PlaceholderEngine.cs` | ✅ 已删除 |
| `NodeProcess/ReferenceRegistration/FieldMappingEntry.cs` | `WorkflowExecution/Abstractions/FieldMappingEntry.cs` | ✅ 已删除 |
| `NodeProcess/Runtime/WorkflowRuntimeContext.cs` | `WorkflowExecution/Models/RuntimeContext.cs`（泛型化） | ✅ 已删除 |
| `NodeProcess/Runtime/WorkflowRuntimeContextManager.cs` | `WorkflowExecution/Models/RuntimeContextManager.cs`（泛型化） | ✅ 已删除 |

> 删除前已通过 `find_references` 确认无外部引用，删除后编译验证通过（仅存在预存的 NU1605 包降级警告，无编译错误）。

---

## 四、保留在 NodeProcess 中的类型（NodeProcess 特有）

以下类型是 `NodeProcess` 特有的业务逻辑，不应提取：

| 类型 | 原因 |
|------|------|
| `NodeProcessSkeleton` | 节点处理管线编排，依赖 `INodeProcessor` 和 `GraphSnapshot` |
| `NodeChainSkeleton` | 链式节点处理，依赖 `INodeChainService` |
| `GraphChainUpdateStage` | 图链更新 Stage，依赖 `GraphUpdateService` |
| `GraphDefinitionStage` | 图定义 Stage，依赖 `IGraph` 和 `GraphSnapshot` |
| `NodeExecutionStage` | 节点执行状态管理，依赖 `IExecutionStateStore` |
| `NodeExecutionStepSkeleton` | 单步节点执行体 |
| `NodeMetricsStage` | 节点指标收集 |
| `INodeChainService` | 链式节点服务接口 |
| `INodeExecutionCoordinator` | 节点执行协调器接口 |
| `IWorkflowMediator` | 工作流中介者接口 |
| `IWorkflowRuntimeContextAccessor` | 运行时上下文访问器接口 |
| `ChainInfo<TNode>` | 链信息模型 |
| `NodeConfig` | 节点配置模型 |
| `NodeDefinition` | 节点定义模型 |
| `INodeProcessor` | 节点处理器接口 |
| `ExecutionConfig` | 执行配置模型 |
| `WorkflowRuntimeContext` | 工作流运行时上下文（NodeProcess 特有版本） |
| `WorkflowRuntimeContextManager` | 工作流运行时上下文管理器（NodeProcess 特有版本） |

---

## 五、提取后架构关系

```
┌─────────────────────────────────────────────────────────────┐
│                    WorkflowExecution (Share)                  │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────────┐   │
│  │ Abstractions │  │   Adapters   │  │  Engine / Models  │   │
│  │  IParameter  │  │ PipelineTo   │  │  PlaceholderEngine│   │
│  │  Resolver    │  │ StageAdapter │  │  FuncCallRun     │   │
│  │  INodeRepo-  │  │ StageTo      │  │  RuntimeContext  │   │
│  │  sitory      │  │ Pipeline     │  │  RuntimeContext  │   │
│  │  INodeConfig │  │ Adapter      │  │  Manager         │   │
│  │  Extractor   │  └──────────────┘  └──────────────────┘   │
│  │  FieldMapping│                                            │
│  │  Entry/Mode  │                                            │
│  └─────────────┘                                            │
└─────────────────────────────────────────────────────────────┘
                           ▲ 引用
                           │
┌─────────────────────────────────────────────────────────────┐
│                    NodeProcess (Operation)                    │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────────┐   │
│  │  Pipeline    │  │  Execution   │  │  Graph / Lifecycle│   │
│  │  Builder     │  │  Stages      │  │  Stages          │   │
│  └─────────────┘  └──────────────┘  └──────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## 六、总结

1. **已提取 12 项通用逻辑**到 `WorkflowExecution`，涵盖接口抽象、适配器、引擎和模型
2. **5 个文件已更新 using** 指向新的 `WorkflowExecution` 命名空间
3. **11 个源文件可安全删除**（类型已提取且无外部引用）
4. **18 个类型保留**在 `NodeProcess` 中，属于 NodeProcess 特有的业务逻辑
5. 提取后 `WorkflowExecution` 作为共享层，`NodeProcess` 作为操作层引用共享层，符合项目分层规范
