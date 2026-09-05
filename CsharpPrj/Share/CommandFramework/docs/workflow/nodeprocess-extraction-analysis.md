# NodeProcess → WorkflowExecution 通用逻辑提取分析报告

## 概述

本文档分析 `CsharpPrj/Operation/WorkFlowMethod/NodeProcess` 中哪些逻辑应作为通用逻辑提取到 `CsharpPrj/Share/WorkflowExecution`，以及提取后的文件映射关系。

## 提取结果总览

| # | 提取项 | 源路径 | 目标路径 | 变更说明 |
|---|--------|--------|----------|----------|
| 1 | IParameterResolver | NodeProcess/Abstractions/IParameterResolver.cs | WorkflowExecution/Abstractions/IParameterResolver.cs | 命名空间改为 WorkflowExecution.Abstractions |
| 2 | ParameterResolver | NodeProcess/Execution/ParameterResolver.cs | WorkflowExecution/Engine/ParameterResolver.cs | 命名空间改为 WorkflowExecution.Engine |
| 3 | PlaceholderEngine | NodeProcess/PlaceholderResolution/PlaceholderEngine.cs | WorkflowExecution/PlaceholderResolution/PlaceholderEngine.cs | 命名空间改为 WorkflowExecution.PlaceholderResolution |
| 4 | FuncCallRun | NodeProcess/Execution/FuncCallRun.cs | WorkflowExecution/Execution/FuncCallRun.cs | WorkflowContext → FuncExecutionContext（避免命名冲突） |
| 5 | PipelineToStageAdapter | NodeProcess/Abstractions/PipelineToStageAdapter.cs | WorkflowExecution/Abstractions/PipelineToStageAdapter.cs | 命名空间改为 WorkflowExecution.Abstractions |
| 6 | StageToPipelineAdapter | NodeProcess/Abstractions/StageToPipelineAdapter.cs | WorkflowExecution/Abstractions/StageToPipelineAdapter.cs | 命名空间改为 WorkflowExecution.Abstractions |
| 7 | INodeRepository<TNode> | NodeProcess/Abstractions/INodeRepository.cs | WorkflowExecution/Abstractions/INodeRepository.cs | 命名空间改为 WorkflowExecution.Abstractions |
| 8 | INodeConfigExtractor<TWorkflow> | NodeProcess/Abstractions/INodeConfigExtractor.cs | WorkflowExecution/Abstractions/INodeConfigExtractor.cs | 命名空间改为 WorkflowExecution.Abstractions |
| 9 | FieldMappingEntry + FieldMappingMode | NodeProcess/ReferenceRegistration/FieldMappingEntry.cs | WorkflowExecution/Abstractions/FieldMappingEntry.cs | 命名空间改为 WorkflowExecution.Abstractions |
| 10 | IExecutionLifecycle | NodeProcess/Abstractions/INodeExecutionCoordinator.cs | WorkflowExecution/Abstractions/IExecutionLifecycle.cs | 从 INodeExecutionCoordinator 提取生命周期方法 |
| 11 | RuntimeContext<TChainInfo> | NodeProcess/Runtime/WorkflowRuntimeContext.cs | WorkflowExecution/Runtime/RuntimeContext.cs | 泛型化，TChainInfo 替代硬编码 ChainInfo<NodeConfig> |
| 12 | RuntimeContextManager<TContext, TChainInfo> | NodeProcess/Runtime/WorkflowRuntimeContextManager.cs | WorkflowExecution/Runtime/RuntimeContextManager.cs | 泛型化，支持工厂函数创建上下文 |

## 详细分析

### 第一优先级：直接提取（零依赖解耦）

这些类型不依赖 NodeProcess 特有的类型，可直接提取。

#### 1. IParameterResolver + ParameterResolver
- **依赖**：Share.StateManagement.NodeExecutionResult（已在共享库中）
- **提取方式**：直接复制，命名空间改为 `WorkflowExecution.Abstractions` / `WorkflowExecution.Engine`
- **文件**：
  - `WorkflowExecution/Abstractions/IParameterResolver.cs`
  - `WorkflowExecution/Engine/ParameterResolver.cs`

#### 2. PlaceholderEngine
- **依赖**：Graphkey.Core.IPlaceholder / Placeholder / IResolutionContext（已在 Share.GraphFramework 中）
- **提取方式**：直接复制，命名空间改为 `WorkflowExecution.PlaceholderResolution`
- **文件**：`WorkflowExecution/PlaceholderResolution/PlaceholderEngine.cs`

#### 3. FuncCallRun
- **依赖**：无外部依赖
- **提取方式**：直接复制，`WorkflowContext` 重命名为 `FuncExecutionContext` 避免与 `WorkflowExecution.Abstractions.WorkflowContext` 冲突
- **文件**：`WorkflowExecution/Execution/FuncCallRun.cs`

#### 4. PipelineToStageAdapter + StageToPipelineAdapter
- **依赖**：Modules.Pipeline（WorkflowExecution 已引用）
- **提取方式**：直接复制，命名空间改为 `WorkflowExecution.Abstractions`
- **文件**：
  - `WorkflowExecution/Abstractions/PipelineToStageAdapter.cs`
  - `WorkflowExecution/Abstractions/StageToPipelineAdapter.cs`

#### 5. INodeRepository<TNode>
- **依赖**：无
- **提取方式**：直接复制，命名空间改为 `WorkflowExecution.Abstractions`
- **文件**：`WorkflowExecution/Abstractions/INodeRepository.cs`

#### 6. INodeConfigExtractor<TWorkflow>
- **依赖**：无
- **提取方式**：直接复制，命名空间改为 `WorkflowExecution.Abstractions`
- **文件**：`WorkflowExecution/Abstractions/INodeConfigExtractor.cs`

#### 7. FieldMappingEntry + FieldMappingMode
- **依赖**：无
- **提取方式**：直接复制，命名空间改为 `WorkflowExecution.Abstractions`
- **文件**：`WorkflowExecution/Abstractions/FieldMappingEntry.cs`

### 第二优先级：提取接口（需解耦）

#### 8. IExecutionLifecycle
- **来源**：从 `INodeExecutionCoordinator` 提取生命周期管理方法
- **提取方法**：将 StartExecution/PauseExecution/ResumeExecution/TerminateExecution/GetExecutionStatus/GetActiveExecutionIds 提取为独立接口
- **文件**：`WorkflowExecution/Abstractions/IExecutionLifecycle.cs`

#### 9. INodeExecutionCoordinator
- **现状**：NodeProcess 中的 `INodeExecutionCoordinator` 与 `Share.StateManagement.INodeExecutionCoordinator` 功能高度重叠
- **建议**：NodeProcess 版本应删除，改用 Share.StateManagement 版本 + IExecutionLifecycle 的组合

### 第三优先级：泛型化后提取

#### 10. RuntimeContext<TChainInfo>
- **来源**：`WorkflowRuntimeContext`（硬编码依赖 `ChainInfo<NodeConfig>`）
- **泛型化**：`RuntimeContext<TChainInfo>` 替代，`TChainInfo` 为泛型参数
- **文件**：`WorkflowExecution/Runtime/RuntimeContext.cs`

#### 11. RuntimeContextManager<TContext, TChainInfo>
- **来源**：`WorkflowRuntimeContextManager`（硬编码依赖 `WorkflowRuntimeContext`）
- **泛型化**：`RuntimeContextManager<TContext, TChainInfo>` 替代，通过工厂函数创建上下文
- **文件**：`WorkflowExecution/Runtime/RuntimeContextManager.cs`

## 不提取的类型及原因

| 类型 | 原因 |
|------|------|
| INodeChainService | 强依赖 NodeConfig 和 WorkFlowMethod 领域逻辑 |
| IWorkflowMediator | 强依赖 NodeConfig、ChainConfig、LogicComposition |
| IWorkflowRuntimeContextAccessor | 强依赖 WorkFlowMethod.Orchestration |
| NodeChainSkeleton | 强依赖 NodeProcess 管线编排 |
| NodeProcessSkeleton | 强依赖 NodeProcess 管线编排 |
| NodeConfigFactory | 强依赖 NodeConfig 领域模型 |
| NodeProcessPipelineBuilder | 强依赖 NodeProcess 管线编排 |
| DefaultNodeProcessor | 强依赖 NodeProcess 管线编排 |
| Storage | 强依赖 NodeProcess 领域模型 |
| NodeExtractorAdapter | 强依赖 NodeConfig 领域模型 |
| NodeExecutionStage | 强依赖 NodeProcess 执行管线 |
| NodeExecutionStepSkeleton | 强依赖 NodeProcess 执行管线 |
| KeyProcessor | 强依赖 NodeProcess 键映射逻辑 |
| NodeMetricsStage | 强依赖 NodeProcess 生命周期 |
| GraphChainUpdateStage | 强依赖 Graph 领域逻辑 |
| GraphDefinitionStage | 强依赖 Graph 领域逻辑 |
| NodeConfigReferenceRegistrar | 强依赖 NodeConfig 领域模型 |
| ChainInfo / ConnectionModels / Dtos / ExecutionConfig / INodeProcessor / MigrationTypes / NodeConfig / ResultTypes | 领域模型，不属于通用逻辑 |

## 后续建议

1. **删除重复定义**：NodeProcess 中的 `INodeExecutionCoordinator` 与 `Share.StateManagement.INodeExecutionCoordinator` 重复，应删除 NodeProcess 版本
2. **更新引用**：NodeProcess 中引用提取后类型的代码应更新为引用 WorkflowExecution 版本
3. **添加 ProjectReference**：WorkFlowMethod.csproj 需添加对 WorkflowExecution.csproj 的引用
4. **WorkflowExecution.csproj 添加依赖**：需添加对 `Share.GraphFramework` 和 `Newtonsoft.Json` 的引用（PlaceholderEngine 需要）
