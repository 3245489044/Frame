# Orchestration 代码现状分析与重构规划

> 基于 `OrchGraph.cs`、`OrchestrationConnect.cs`、`OrchestrationFramework.cs`、`WorkflowCreateModules.cs` 及 `GraphFramework/` 目录的全面分析

---

## 一、总体架构概览

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Orchestration 目录                            │
│                                                                     │
│  OrchGraph.cs (765行)         ← 已迁移6个命名空间，剩余3个命名空间     │
│  OrchestrationConnect.cs (3087行)  ← 核心连接器逻辑（最大文件）       │
│  OrchestrationFramework.cs (3707行) ← ECS框架 + 工作流存储管理       │
│  WorkflowCreateModules.cs (447行)   ← 模块创建 + 链构建              │
│  RefactoredOrchestration.cs (448行) ← 重构后的执行器（新代码）        │
│  RefactoredWorkflowCreateModules.cs (197行) ← 重构后的模块创建器      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 二、已完成的迁移工作

### 2.1 OrchGraph.cs → GraphFramework/ 迁移（已完成）

| 原命名空间 | 目标目录 | 行数 | 状态 |
|-----------|---------|------|------|
| `OrchestrationConnect.Updates` | `GraphFramework/Directives/` + `Services/` | ~540 | ✅ |
| `OrchestrationConnect.GraphBridge` | `GraphFramework/Pipeline/` | ~1140 | ✅ |
| `OrchestrationConnect.Deweight` | `GraphFramework/Deduplication/` | ~830 | ✅ |
| `WorkflowConnectorFramework.Core` | `GraphFramework/Connector/` | ~180 | ✅ |
| `OrchestrationConnect.Updates.Placeholders` | `GraphFramework/Placeholders/` | ~80 | ✅ |
| `OrchestrationConnect.Key` | `GraphFramework/KeyGeneration/` | ~450 | ✅ |

### 2.2 GraphFramework/ 目录结构（已建立）

```
GraphFramework/
├── Adapters/          ← GraphAdapter, LegacyUpdateAdapter
├── Connector/         ← IConnectableNode, ConnectorInterfaces, PipelineContext, NodeResolver, Directives
├── Deduplication/     ← ConfigDeduplicationProcessor, ConfigRelationHandler, DeduplicationConfig, DeweightReferenceRegistrar
├── Directives/        ← ChainConnectionBuilder, GraphUpdateDirective, KeyGeneratorAdapter, NodeCommandAdapter, WorkflowHelper, WorkflowMessageHelper, WorkFlowUpdate
├── KeyGeneration/     ← KeyManager, KeyProcessor
├── Models/            ← ChainInfo, DTOs, EdgeDefinition, FuncCallRun, Guard, ModularValidator, NodeConfig, NodeDefinition, UniversalNode, WorkflowConnection, WorkflowConstants, WorkflowExecutionContext, WorkflowGraph
├── Pipeline/          ← GraphAnalyzer, GraphChainBridge, GraphFoundationBridge, GraphPipelineStep, GraphPipelineSteps, GraphWorkflowConnector
├── Placeholders/      ← PlaceholderEngine
├── Services/          ← DirectiveApplier, GraphBuilder, GraphUpdateService, GraphValidator, NodeManager
├── Snapshots/         ← GraphDiff, GraphSnapshot
└── Transformers/      ← GraphTransformers
```

---

## 三、当前代码现状分析

### 3.1 OrchGraph.cs（765行）- 剩余3个命名空间

**文件状态：** 已从3218行缩减到765行，剩余3个命名空间：

| 命名空间 | 行数 | 说明 |
|---------|------|------|
| `WorkflowConnectorFramework.Core` | ~170行 | 连接器框架核心接口（已迁移到 Connector/） |
| `OrchestrationConnect.Updates.Placeholders` | ~80行 | 占位符引擎（已迁移到 Placeholders/） |
| `OrchestrationConnect.Key` | ~450行 | 键管理器+键处理器（已迁移到 KeyGeneration/） |

**问题：** 这3个命名空间在 GraphFramework/ 中已有副本，但 OrchGraph.cs 中仍保留着旧代码。需要删除旧代码并更新引用。

### 3.2 OrchestrationConnect.cs（3087行）- 最大文件

**文件状态：** 包含多个命名空间，是 Orchestration 的核心：

| 命名空间 | 行数 | 内容 |
|---------|------|------|
| `OrchestrationConnect.Config` | ~820行 | NodeConfig, ChainInfo, WorkflowConnection, WorkflowExecutionContext, ModularValidator, DTOs, ECS组件 |
| `OrchestrationConnect` | ~2267行 | WorkflowRuntimeHelper, WorkflowExecutionHelper, GlobalPauseManager, WorkflowProcessor, WorkflowMediator, NodeManager, 执行引擎 |

**核心问题：**
1. **文件过大（3087行）** - 违反单一职责原则
2. **命名空间混杂** - Config 命名空间包含了数据模型、验证器、DTO、ECS组件等多种职责
3. **OrchestrationConnect 命名空间** - 包含了执行引擎、处理器、管理器等多种职责
4. **依赖外部旧命名空间** - 引用了 `OrchestrationConnect.Config`、`OrchestrationConnect.Content` 等

### 3.3 OrchestrationFramework.cs（3707行）- 最大文件

**文件状态：** 包含 ECS 框架实现和工作流存储管理：

| 区域 | 行数 | 内容 |
|-----|------|------|
| 核心数据模型 | ~10行 | WorkflowReference |
| 接口定义 | ~20行 | IWorkflowStorageManager, INodeConfigExtractor |
| ECS内部组件 | ~420行 | WorkflowRegistrationSystem, WorkflowNotificationSystem |
| GenericWorkflowStorageManager | ~370行 | 基于ECS的存储管理器 |
| WorkflowExecutionTracker | ~80行 | 执行跟踪器 |
| GenericWorkflowConfig | ~2800行 | 工作流配置抽象基类 |

**核心问题：**
1. **文件过大（3707行）** - 最大文件
2. **GenericWorkflowConfig 过于庞大（~2800行）** - 包含大量配置逻辑
3. **ECS 实现与业务逻辑耦合** - ECS 系统与工作流存储管理混在一起
4. **依赖外部旧命名空间** - 引用了 `OrchestrationConnect`、`OrchestrationConnect.Content` 等

### 3.4 WorkflowCreateModules.cs（447行）

**文件状态：** 包含模块创建和链构建逻辑：

| 区域 | 行数 | 内容 |
|-----|------|------|
| 接口定义 | ~100行 | IWorkflowDeserializer, INodeIdentityGenerator, IChainLinkProcessor, IParameterProcessor, IWorkflowRegistrar |
| 实现类 | ~200行 | WorkflowDeserializer, DefaultNodeIdentityGenerator, ChainLinkProcessor, ParameterProcessor, WorkflowRegistrar |
| WorkflowMediator | ~130行 | 链合并策略、连接器、构建器 |

**核心问题：**
1. **依赖旧命名空间** - 引用了 `OrchestrationConnect.Deweight`、`OrchestrationConnect.GraphBridge` 等
2. **DeduplicationDefaults 引用** - 第363行引用了已删除的 `DeduplicationDefaults.DefaultInstance`
3. **与 GraphFramework 的边界不清晰** - 部分逻辑应迁移到 GraphFramework

### 3.5 RefactoredOrchestration.cs（448行）

**文件状态：** 新写的重构代码，包含：
- 统一异常处理（WorkflowException, IWorkflowExceptionHandler）
- 类型安全的执行状态管理（ExecutionStateRecord, ExecutionStateManager）
- 重构后的工作流执行器（RefactoredWorkflowExecutor）

**问题：** 委托给原有的 `WorkflowProcessor.ExecuteFromNodeAsync`，但 `WorkflowProcessor` 在 `OrchestrationConnect.cs` 中，尚未重构。

### 3.6 RefactoredWorkflowCreateModules.cs（197行）

**文件状态：** 新写的重构代码，包含：
- 类型安全的模块创建接口（ModuleCreationConfig, IModuleCreator）
- 模块创建管理器（ModuleCreationManager）
- 扩展方法

**问题：** 委托给原有的 `WorkflowMediator`，但 `WorkflowMediator` 在 `WorkflowCreateModules.cs` 中，尚未重构。

---

## 四、核心问题总结

### P1: 文件过大
| 文件 | 行数 | 问题 |
|-----|------|------|
| OrchestrationFramework.cs | 3707 | 最大文件，ECS+存储+配置混合 |
| OrchestrationConnect.cs | 3087 | 第二大文件，Config+执行引擎混合 |
| OrchGraph.cs | 765 | 已迁移但仍有残留代码 |
| WorkflowCreateModules.cs | 447 | 模块创建+链构建混合 |

### P2: 命名空间依赖混乱
```
OrchestrationConnect.cs
  ├── OrchestrationConnect.Config     ← 数据模型
  ├── OrchestrationConnect.Content    ← ECS组件
  └── OrchestrationConnect            ← 执行引擎

OrchestrationFramework.cs
  ├── OrchestrationConnect            ← 依赖旧命名空间
  ├── OrchestrationConnect.Content    ← 依赖旧命名空间
  └── OrchestrationConnect.Deweight   ← 依赖旧命名空间

WorkflowCreateModules.cs
  ├── OrchestrationConnect.Deweight   ← 依赖旧命名空间
  ├── OrchestrationConnect.GraphBridge ← 依赖旧命名空间
  └── OrchestrationConnect.Updates    ← 依赖旧命名空间
```

### P3: 职责边界不清晰
- `OrchestrationConnect.Config` 包含了数据模型、验证器、DTO、ECS组件
- `OrchestrationConnect` 包含了执行引擎、处理器、管理器
- `OrchestrationFramework` 包含了 ECS 框架、存储管理、配置管理

### P4: 新旧代码共存
- `RefactoredOrchestration.cs` 委托给旧的 `WorkflowProcessor`
- `RefactoredWorkflowCreateModules.cs` 委托给旧的 `WorkflowMediator`
- `OrchGraph.cs` 中仍有已迁移的旧代码残留

### P5: 编译问题
- `WorkflowCreateModules.cs` 第363行引用了已删除的 `DeduplicationDefaults.DefaultInstance`

---

## 五、重构目标

### 5.1 最终架构目标

```
Orchestration/                          ← 编排层（轻量级）
├── OrchGraph.cs                       ← 仅保留 Graph 构建入口
├── OrchestrationConnect.cs            ← 拆分为多个小文件
├── OrchestrationFramework.cs          ← 拆分为多个小文件
├── WorkflowCreateModules.cs           ← 拆分为多个小文件
├── RefactoredOrchestration.cs         ← 重构后的执行器
└── RefactoredWorkflowCreateModules.cs ← 重构后的模块创建器

GraphFramework/                         ← 图框架层（基础设施）
├── Connector/                         ← 连接器框架
├── Deduplication/                     ← 去重处理
├── Directives/                        ← 指令处理
├── KeyGeneration/                     ← 键生成
├── Models/                            ← 数据模型
├── Pipeline/                          ← 管线处理
├── Placeholders/                      ← 占位符引擎
├── Services/                          ← 服务层
├── Snapshots/                         ← 快照
└── Transformers/                      ← 转换器
```

### 5.2 分层原则

```
┌─────────────────────────────────────────────┐
│              Orchestration                    │
│  编排层：工作流创建、连接、执行、管理           │
│  职责：协调 GraphFramework 完成业务流程        │
├─────────────────────────────────────────────┤
│              GraphFramework                   │
│  图框架层：图结构、管线、去重、键生成           │
│  职责：提供通用的图操作基础设施                 │
├─────────────────────────────────────────────┤
│            External Dependencies              │
│  外部依赖：ECS, GraphFoundation, SkeletonFlow  │
└─────────────────────────────────────────────┘
```

---

## 六、详细重构方案

### Step 1: 清理 OrchGraph.cs 残留代码

**目标：** 删除已迁移的3个命名空间，更新引用

**操作：**
1. 删除 `WorkflowConnectorFramework.Core` 命名空间（第55-229行）
2. 删除 `OrchestrationConnect.Updates.Placeholders` 命名空间（第234-307行）
3. 删除 `OrchestrationConnect.Key` 命名空间（第311-765行）
4. 更新 OrchGraph.cs 的 using 语句
5. 编译验证

**影响：** OrchGraph.cs 将从765行缩减到约50行（仅保留 using 和注释）

### Step 2: 拆分 OrchestrationConnect.cs（3087行）

**目标：** 按职责拆分为多个小文件

**拆分方案：**

| 新文件 | 内容 | 预估行数 |
|-------|------|---------|
| `Config/NodeConfig.cs` | NodeConfig 及其内部类 | ~360行 |
| `Config/ChainInfo.cs` | ChainInfo, ChainInfo<TNode> | ~50行 |
| `Config/WorkflowConnection.cs` | WorkflowConnection, ConnectionPositionInfo, ChainConfig | ~50行 |
| `Config/WorkflowExecutionContext.cs` | WorkflowExecutionContext, WorkflowExecutionResult | ~30行 |
| `Config/ModularValidator.cs` | ModularValidator 及其验证器 | ~80行 |
| `Config/DTOs.cs` | ChainConfigDto, NodeDetailDto, ConnectionDto | ~40行 |
| `Config/UniversalNode.cs` | UniversalNode, GetInputStrConfig, FuncCall, ExecuteConfig, AddNodeConfig, ConnectNodeConfig | ~150行 |
| `Config/FuncCallRun.cs` | FuncCallRun, FuncPipelineParameters | ~80行 |
| `Config/WorkflowConstants.cs` | WorkflowConstants, WorkflowDefaults, WorkflowStatus | ~30行 |
| `Config/Guard.cs` | Guard 类 | ~30行 |
| `Content/ECSComponents.cs` | ECS 组件定义（NodeConfigComponent, ChainInfoComponent 等） | ~80行 |
| `Content/WorkflowConfiguration.cs` | WorkflowConfiguration 常量 | ~10行 |
| `Execution/WorkflowExecutionHelper.cs` | WorkflowExecutionHelper, GlobalPauseManager, TaskExtensions | ~100行 |
| `Execution/WorkflowProcessor.cs` | WorkflowProcessor（执行引擎核心） | ~500行 |
| `Execution/WorkflowRuntimeHelper.cs` | WorkflowRuntimeHelper | ~30行 |
| `Execution/AsyncManualResetEvent.cs` | AsyncManualResetEvent | ~40行 |
| `Connection/WorkflowMediator.cs` | WorkflowMediator（连接器核心） | ~500行 |
| `Connection/NodeManager.cs` | NodeManager（节点管理） | ~500行 |
| `Connection/WorkflowRegistrar.cs` | WorkflowRegistrar | ~50行 |

**原则：**
- 每个文件不超过500行
- 按职责分组：Config/、Content/、Execution/、Connection/
- 保持命名空间不变，避免破坏外部引用

### Step 3: 拆分 OrchestrationFramework.cs（3707行）

**目标：** 按职责拆分为多个小文件

**拆分方案：**

| 新文件 | 内容 | 预估行数 |
|-------|------|---------|
| `Framework/WorkflowReference.cs` | WorkflowReference 数据模型 | ~10行 |
| `Framework/IWorkflowStorageManager.cs` | IWorkflowStorageManager 接口 | ~15行 |
| `Framework/INodeConfigExtractor.cs` | INodeConfigExtractor 接口 | ~10行 |
| `Framework/ECS/WorkflowComponent.cs` | ECS 组件定义 | ~30行 |
| `Framework/ECS/WorkflowRegistrationSystem.cs` | WorkflowRegistrationSystem | ~170行 |
| `Framework/ECS/WorkflowNotificationSystem.cs` | WorkflowNotificationSystem | ~140行 |
| `Framework/ECS/WorkflowCommand.cs` | WorkflowCommand 类型 | ~20行 |
| `Framework/GenericWorkflowStorageManager.cs` | GenericWorkflowStorageManager | ~370行 |
| `Framework/WorkflowExecutionTracker.cs` | WorkflowExecutionTracker, ExecutionStep, ExecutionSummary | ~80行 |
| `Framework/GenericWorkflowConfig.cs` | GenericWorkflowConfig（需进一步拆分） | ~2800行 |

**GenericWorkflowConfig 进一步拆分：**

| 新文件 | 内容 | 预估行数 |
|-------|------|---------|
| `Config/GenericWorkflowConfig.cs` | 抽象基类 | ~100行 |
| `Config/WorkflowConfigInitializer.cs` | 初始化逻辑 | ~200行 |
| `Config/WorkflowConfigUpdater.cs` | 更新逻辑 | ~300行 |
| `Config/WorkflowConfigValidator.cs` | 验证逻辑 | ~200行 |
| `Config/WorkflowConfigSerializer.cs` | 序列化逻辑 | ~300行 |
| `Config/WorkflowConfigCache.cs` | 缓存逻辑 | ~200行 |
| `Config/WorkflowConfigObserver.cs` | 观察者逻辑 | ~200行 |
| `Config/WorkflowConfigTransaction.cs` | 事务逻辑 | ~300行 |
| `Config/WorkflowConfigMigration.cs` | 迁移逻辑 | ~300行 |
| `Config/WorkflowConfigExtension.cs` | 扩展方法 | ~200行 |

### Step 4: 拆分 WorkflowCreateModules.cs（447行）

**目标：** 按职责拆分为多个小文件

**拆分方案：**

| 新文件 | 内容 | 预估行数 |
|-------|------|---------|
| `Creation/IWorkflowDeserializer.cs` | IWorkflowDeserializer, WorkflowDeserializer | ~50行 |
| `Creation/INodeIdentityGenerator.cs` | INodeIdentityGenerator, DefaultNodeIdentityGenerator | ~30行 |
| `Creation/IChainLinkProcessor.cs` | IChainLinkProcessor, ChainLinkProcessor | ~40行 |
| `Creation/IParameterProcessor.cs` | IParameterProcessor, ParameterProcessor | ~80行 |
| `Creation/IWorkflowRegistrar.cs` | IWorkflowRegistrar, WorkflowRegistrar | ~20行 |
| `Creation/MergeOptions.cs` | MergeOptions, ConnectRequest | ~15行 |
| `Creation/IChainMergeStrategy.cs` | IChainMergeStrategy, WorkflowConnectorMergeStrategy | ~60行 |
| `Creation/IWorkflowMediator.cs` | IWorkflowMediator, WorkflowMediator | ~130行 |

### Step 5: 修复编译问题

**问题1：** `WorkflowCreateModules.cs` 第363行引用了已删除的 `DeduplicationDefaults.DefaultInstance`

**解决方案：** 替换为 `GraphFramework.Deduplication.ConfigDeduplicationProcessor` 或 `NodeManager.DeduplicationProcessor`

```csharp
// 旧代码
var deduped = DeduplicationDefaults.DefaultInstance.Deduplicate(workflows, null, false, request.WorkflowId);

// 新代码
var deduped = NodeManager.DeduplicationProcessor.Deduplicate(workflows, null, false, request.WorkflowId);
```

**问题2：** `NodeManager.cs` 第XX行引用了 `DeduplicationDefaults.DefaultInstance`

**解决方案：** 同上，替换为 `ConfigDeduplicationProcessor`

### Step 6: 整合重构后的代码

**目标：** 将 RefactoredOrchestration.cs 和 RefactoredWorkflowCreateModules.cs 整合到主流程

**操作：**
1. 将 `RefactoredWorkflowExecutor` 作为 `WorkflowProcessor` 的替代入口
2. 将 `DefaultModuleCreator` 作为 `WorkflowMediator` 的替代入口
3. 逐步替换旧调用点

### Step 7: 更新外部引用

**需要更新的文件：**
- `WorkFlowMethod/ExecutionEngine/Integration.cs`
- `WorkFlowMethod/CentralPipeline/WorkflowCentralPipeline.cs`
- `WorkFlowMethod/Skeletons/` 下的所有文件
- `WorkFlowMethod/WorkflowUseage.cs`
- `WorkFlowMethod/WrokFlow_Script.cs`

---

## 七、执行计划

### 阶段1：清理（1-2天）
```
Step 1: 清理 OrchGraph.cs 残留代码
  ├── 删除已迁移的3个命名空间
  ├── 更新 using 语句
  └── 编译验证
```

### 阶段2：拆分 OrchestrationConnect.cs（2-3天）
```
Step 2: 拆分 OrchestrationConnect.cs
  ├── 创建 Config/ 子目录（10个文件）
  ├── 创建 Content/ 子目录（2个文件）
  ├── 创建 Execution/ 子目录（4个文件）
  ├── 创建 Connection/ 子目录（3个文件）
  ├── 保留原文件作为转发入口（可选）
  └── 编译验证
```

### 阶段3：拆分 OrchestrationFramework.cs（3-4天）
```
Step 3: 拆分 OrchestrationFramework.cs
  ├── 创建 Framework/ 子目录（4个文件）
  ├── 创建 Framework/ECS/ 子目录（3个文件）
  ├── 创建 Config/ 子目录（10个文件）
  ├── 保留原文件作为转发入口（可选）
  └── 编译验证
```

### 阶段4：拆分 WorkflowCreateModules.cs（1天）
```
Step 4: 拆分 WorkflowCreateModules.cs
  ├── 创建 Creation/ 子目录（8个文件）
  ├── 保留原文件作为转发入口（可选）
  └── 编译验证
```

### 阶段5：修复编译问题（1天）
```
Step 5: 修复编译问题
  ├── 修复 DeduplicationDefaults 引用
  ├── 修复其他编译错误
  └── 编译验证
```

### 阶段6：整合重构代码（2-3天）
```
Step 6: 整合重构代码
  ├── 将 RefactoredWorkflowExecutor 集成到主流程
  ├── 将 DefaultModuleCreator 集成到主流程
  ├── 逐步替换旧调用点
  └── 集成测试
```

### 阶段7：更新外部引用（1-2天）
```
Step 7: 更新外部引用
  ├── 更新 ExecutionEngine/Integration.cs
  ├── 更新 CentralPipeline/WorkflowCentralPipeline.cs
  ├── 更新 Skeletons/ 下的文件
  ├── 更新 WorkflowUseage.cs
  ├── 更新 WrokFlow_Script.cs
  └── 全面编译验证
```

---

## 八、风险与注意事项

### 8.1 风险点

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 命名空间变更导致大量文件需要更新 | 高 | 保留旧命名空间的转发类 |
| ECS 框架依赖复杂 | 中 | 保持 ECS 相关代码不变，仅拆分文件 |
| GenericWorkflowConfig 过于庞大 | 高 | 分步拆分，先拆出独立功能 |
| 外部项目引用 Orchestration 命名空间 | 中 | 保留旧命名空间的别名 |
| 编译期间功能不可用 | 中 | 分步进行，每步编译验证 |

### 8.2 注意事项

1. **保持命名空间不变** - 拆分文件时保持命名空间不变，避免破坏外部引用
2. **逐步进行** - 每完成一步都进行编译验证
3. **保留转发入口** - 在旧文件中保留 `using` 别名或转发类，确保旧代码能编译
4. **先拆分后重构** - 先拆分大文件，再进行逻辑重构
5. **测试先行** - 确保有足够的测试覆盖

---

## 九、附录：文件依赖关系图

```
OrchGraph.cs
  ├── GraphFramework/Models/*
  ├── GraphFramework/Services/*
  ├── GraphFramework/Snapshots/*
  └── Execution/Engine/*

OrchestrationConnect.cs
  ├── OrchestrationConnect.Config     ← 自包含
  ├── OrchestrationConnect.Content    ← 自包含
  └── OrchestrationConnect            ← 依赖 Config + Content

OrchestrationFramework.cs
  ├── OrchestrationConnect            ← 依赖旧命名空间
  ├── OrchestrationConnect.Content    ← 依赖旧命名空间
  ├── OrchestrationConnect.Deweight   ← 依赖旧命名空间
  └── ECS.Core                        ← 外部依赖

WorkflowCreateModules.cs
  ├── OrchestrationConnect.Config     ← 依赖旧命名空间
  ├── OrchestrationConnect.Deweight   ← 依赖旧命名空间
  ├── OrchestrationConnect.GraphBridge ← 依赖旧命名空间
  ├── OrchestrationConnect.Updates    ← 依赖旧命名空间
  └── GraphFramework/Pipeline/*       ← 新依赖
```

---

## 十、总结

当前 Orchestration 目录的核心问题是**文件过大**和**职责边界不清晰**。经过分析：

1. **OrchGraph.cs** 已完成大部分迁移，只需清理残留代码
2. **OrchestrationConnect.cs**（3087行）需要按 Config/Content/Execution/Connection 拆分
3. **OrchestrationFramework.cs**（3707行）需要按 Framework/ECS/Config 拆分
4. **WorkflowCreateModules.cs**（447行）需要按 Creation/ 拆分
5. **编译问题**需要修复 DeduplicationDefaults 引用
6. **重构代码**需要整合到主流程

建议按 **7个阶段** 逐步执行，每步编译验证，确保平稳过渡。
