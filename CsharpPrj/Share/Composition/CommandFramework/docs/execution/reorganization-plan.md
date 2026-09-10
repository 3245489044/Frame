# CommandFramework 执行逻辑整理规划

## 一、概述

本文档详细规划了将 CommandFramework 中分散的执行逻辑代码归拢到统一的 `Execution/` 文件夹下的实施方案。整理后的结构将以**执行路径**为主线，消除当前按技术分类（Abstractions/、Execution/、Models/、Pipeline/）带来的混乱，使代码组织结构更加清晰。

> **参考文档**：本规划基于 [integration-analysis.md](../integration/integration-analysis.md) 的架构分析结论。

---

## 二、核心策略：分离物理搬迁与架构重构

本规划将任务分为**两个独立轨道**，并行推进：

| 轨道 | 内容 | 风险 | 时间 |
|------|------|------|------|
| **Move First** | 仅物理搬迁 + 命名空间变更，无代码逻辑修改 | 低 | 1-2 PR |
| **Refactor Then Move** | 需要架构重构后才能搬迁的文件 | 中 | 多个 PR |

**设计理念**：许多文件可以立即搬迁，只需更新命名空间，无需等待架构重构完成。这能快速改善代码组织结构，降低后续重构的复杂度。

---

## 三、当前文件分布分析

### 3.1 现有执行相关文件清单

| 目录 | 文件 | 所属执行路径 | 核心职责 |
|------|------|-------------|---------|
| `Execution/` | `CommandEngine.cs` | 命令管线 | 命令注册、执行、撤销/重做 |
| `Execution/` | `FunctionExecution.cs` | 共享 | 函数执行器，通过注册表执行函数 |
| `Execution/` | `InMemoryStateStore.cs` | 共享 | 内存状态存储实现 |
| `Execution/` | `StateStoreExtensions.cs` | 共享 | 状态存储扩展方法 |
| `Execution/` | `ControlValueAccessorBase.cs` | 命令管线 | 控件值访问器基类 |
| `Execution/` | `EnvironmentAdapterBase.cs` | 命令管线 | 环境适配器基类 |
| `Execution/` | `CommandResults.cs` | 命令管线 | 命令执行结果模型 |
| `Pipeline/` | `PipelineEntry.cs` | 命令管线 | 管线入口，管理共享状态和执行历史 |
| `Pipeline/` | `PipelineStageBase.cs` | 命令管线 | 管线阶段基类 |
| `Pipeline/Stages/` | `ParseCommandStage.cs` | 命令管线 | 命令解析阶段 |
| `Pipeline/Stages/` | `ValidationStage.cs` | 命令管线 | 命令验证阶段 |
| `Pipeline/Stages/` | `ExecuteCommandStage.cs` | 命令管线 | 命令执行阶段（核心） |
| `Pipeline/Stages/` | `ResultCollectStage.cs` | 命令管线 | 结果收集阶段 |
| `Pipeline/Stages/` | `LoggingStage.cs` | 共享 | 日志记录阶段 |
| `Pipeline/Stages/` | `RetryStage.cs` | 共享 | 重试阶段 |
| `Pipeline/Stages/` | `ScriptLoadStage.cs` | 命令管线 | 脚本加载阶段 |
| `WorkflowExecution/Engine/` | `WorkflowEngine.cs` | 工作流 | 工作流引擎，管理生命周期 |
| `WorkflowExecution/Engine/` | `NodeExecutor.cs` | 工作流 | 节点执行器，执行单个节点 |
| `WorkflowExecution/Execution/` | `FuncCallRun.cs` | 工作流 | 函数调用链执行 |
| `WorkflowExecution/Pipeline/` | `CentralPipeline.cs` | 工作流 | 中央管线，管理工作流生命周期 |
| `WorkflowExecution/Pipeline/` | `PipelineStages.cs` | 工作流 | 管线阶段定义 |
| `WorkflowExecution/Pipeline/` | `WorkflowContext.cs` | 工作流 | 工作流上下文 |
| `WorkflowExecution/Runtime/` | `RuntimeContext.cs` | 工作流 | 运行时上下文 |
| `WorkflowExecution/Runtime/` | `RuntimeContextManager.cs` | 工作流 | 运行时上下文管理器 |
| `WorkflowExecution/Skeletons/` | `NodeChainSkeleton.cs` | 工作流 | 节点链骨架 |
| `WorkflowExecution/Skeletons/` | `NodeProcessSkeleton.cs` | 工作流 | 节点处理骨架 |
| `WorkflowExecution/PlaceholderResolution/` | `PlaceholderEngine.cs` | 工作流 | 占位符解析引擎 |
| `Parameters/` | `ArgumentBuilder.cs` | 共享 | 参数构建器 |
| `Parameters/` | `ParameterResolver.cs` | 共享 | 参数解析器 |
| `Parsing/` | `CommandParser.cs` | 命令管线 | 命令解析器 |
| `Parsing/` | `ParsedCommand.cs` | 命令管线 | 解析后的命令模型 |
| `Parsing/` | `StringUtils.cs` | 共享 | 字符串处理工具（统一字符串拆分） |
| `Models/` | `CommandContext.cs` | 命令管线 | 命令执行上下文 |
| `Models/` | `CommandExecutionContext.cs` | 命令管线 | 命令执行上下文（扩展） |
| `Models/` | `CommandExecutionReport.cs` | 命令管线 | 命令执行报告 |
| `Models/` | `JsonCommandResult.cs` | 命令管线 | JSON 命令结果 |
| `Models/` | `LoadResult.cs` | 命令管线 | 加载结果 |
| `Abstractions/` | `IExecutionContext.cs` | 共享 | 执行上下文接口 |
| `Abstractions/` | `IControlFinder.cs` | 命令管线 | 控件查找器接口 |
| `Abstractions/` | `IControlValueWriter.cs` | 命令管线 | 控件值写入器接口 |
| `Abstractions/` | `IEventBindingResolver.cs` | 命令管线 | 事件绑定解析器接口 |
| `Abstractions/` | `IEventTypeMapper.cs` | 命令管线 | 事件类型映射器接口 |
| `Abstractions/` | `INonEventCommandLookup.cs` | 命令管线 | 非事件命令查找接口 |
| `Abstractions/` | `INonEventCommandRegistry.cs` | 命令管线 | 非事件命令注册接口 |
| `Abstractions/` | `IScriptConverter.cs` | 命令管线 | 脚本转换器接口 |
| `Abstractions/` | `IStateQuery.cs` | 共享 | 状态查询接口 |
| `Abstractions/` | `IUIController.cs` | 命令管线 | UI 控制器接口 |
| `Libraries/` | `NonEventCommands.cs` | 命令管线 | 非事件命令实现 |
| `Validators/` | `CommandContextValidator.cs` | 命令管线 | 命令上下文验证器 |
| `Validators/` | `ParsedCommandValidator.cs` | 命令管线 | 解析命令验证器 |
| `WorkflowExecution/Abstractions/` | `INodeRepository.cs` | 工作流 | 节点仓库接口 |
| `WorkflowExecution/Abstractions/` | `IPipelineStage.cs` | 工作流 | 管线阶段接口 |
| `WorkflowExecution/Models/` | `NodeDefinition.cs` | 工作流 | 节点定义模型 |
| `WorkflowExecution/Models/` | `WorkflowDefinition.cs` | 工作流 | 工作流定义模型 |
| `/` | `CommandFrameworkHost.cs` | 入口 | 统一入口（外观模式） |
| `/` | `ICommand.cs` | 共享 | 命令接口 |

### 3.2 当前结构问题

```
CommandFramework/
├── Abstractions/          ← 接口分散，部分与执行无关（如 IUIController）
├── Execution/             ← 执行逻辑但包含非执行类（如 StateStore）
├── Pipeline/              ← 命令管线（独立于工作流管线）
├── WorkflowExecution/     ← 工作流执行（层级过深，与 Pipeline 重复）
│   ├── Engine/
│   ├── Execution/         ← 与根目录 Execution/ 命名冲突
│   ├── Pipeline/          ← 与根目录 Pipeline/ 功能重叠
│   ├── Runtime/
│   ├── Skeletons/
│   ├── Models/
│   └── Abstractions/      ← 与根目录 Abstractions/ 功能重叠
├── Parameters/            ← 参数解析（执行基础设施）
├── Parsing/               ← 命令解析（命令管线专用）
├── Models/                ← 模型分散（命令上下文 vs 工作流上下文）
├── Libraries/             ← 非事件命令（命令管线专用）
└── Validators/            ← 验证器（命令管线专用）
```

**主要问题：**
1. **命名冲突**：`Execution/` 在根目录和 `WorkflowExecution/` 下重复出现
2. **职责不清**：`Abstractions/` 混合了命令管线和工作流的接口
3. **层级过深**：`WorkflowExecution/Engine/`、`WorkflowExecution/Pipeline/` 等嵌套过深
4. **重复实现**：两套管线框架（`Pipeline/` 和 `WorkflowExecution/Pipeline/`）
5. **模型分散**：上下文模型分散在 `Models/` 和 `WorkflowExecution/Models/`

---

## 四、目标文件夹结构

### 4.1 整理后的目录结构

```
CommandFramework/
├── Execution/                              ← 统一执行逻辑根目录
│   ├── CommandPipeline/                    ← 命令管线执行路径
│   │   ├── PipelineEntry.cs               ← 管线入口（待移除）
│   │   ├── PipelineStageBase.cs           ← 管线阶段基类（待移除）
│   │   ├── Stages/                        ← 命令管线阶段
│   │   │   ├── ParseCommandStage.cs
│   │   │   ├── ValidationStage.cs
│   │   │   ├── ExecuteCommandStage.cs
│   │   │   ├── ResultCollectStage.cs
│   │   │   ├── LoggingStage.cs
│   │   │   ├── RetryStage.cs
│   │   │   └── ScriptLoadStage.cs
│   │   ├── Parsing/                       ← 命令解析
│   │   │   ├── CommandParser.cs
│   │   │   └── ParsedCommand.cs
│   │   ├── Models/                        ← 命令管线模型
│   │   │   ├── CommandContext.cs
│   │   │   ├── CommandExecutionContext.cs
│   │   │   ├── CommandExecutionReport.cs
│   │   │   ├── JsonCommandResult.cs
│   │   │   └── LoadResult.cs
│   │   ├── Libraries/                     ← 非事件命令库
│   │   │   └── NonEventCommands.cs
│   │   └── Validators/                    ← 命令管线验证器
│   │       ├── CommandContextValidator.cs
│   │       └── ParsedCommandValidator.cs
│   ├── WorkflowEngine/                    ← 工作流执行路径
│   │   ├── WorkflowEngine.cs              ← 工作流引擎
│   │   ├── NodeExecutor.cs                ← 节点执行器
│   │   ├── FuncCallRun.cs                 ← 函数调用链执行
│   │   ├── CentralPipeline.cs             ← 中央管线（统一管线核心）
│   │   ├── PipelineStages.cs              ← 管线阶段定义
│   │   ├── Runtime/                       ← 运行时管理
│   │   │   ├── RuntimeContext.cs
│   │   │   └── RuntimeContextManager.cs
│   │   ├── Skeletons/                     ← 骨架定义
│   │   │   ├── NodeChainSkeleton.cs
│   │   │   └── NodeProcessSkeleton.cs
│   │   ├── PlaceholderResolution/         ← 占位符解析
│   │   │   └── PlaceholderEngine.cs
│   │   └── Models/                        ← 工作流模型
│   │       ├── NodeDefinition.cs
│   │       └── WorkflowDefinition.cs
│   ├── Shared/                            ← 共享基础设施
│   │   ├── CommandEngine.cs               ← 命令引擎（注册、执行、撤销）
│   │   ├── FunctionExecution.cs           ← 函数执行器
│   │   ├── StateManagement/               ← 状态管理
│   │   │   ├── InMemoryStateStore.cs
│   │   │   └── StateStoreExtensions.cs
│   │   ├── Parameters/                    ← 参数解析
│   │   │   ├── ArgumentBuilder.cs
│   │   │   ├── ParameterResolver.cs
│   │   │   └── Abstractions/
│   │   │       └── IParameterResolver.cs
│   │   ├── Utilities/                     ← 工具类
│   │   │   └── StringUtils.cs
│   │   └── Abstractions/                  ← 共享接口
│   │       ├── ICommand.cs
│   │       ├── IExecutionContext.cs
│   │       └── IStateQuery.cs
│   └── Abstractions/                      ← 执行相关接口
│       ├── CommandPipeline/               ← 命令管线接口
│       │   ├── IControlFinder.cs
│       │   ├── IControlValueWriter.cs
│       │   ├── IEventBindingResolver.cs
│       │   ├── IEventTypeMapper.cs
│       │   ├── INonEventCommandLookup.cs
│       │   ├── INonEventCommandRegistry.cs
│       │   ├── IScriptConverter.cs
│       │   └── IUIController.cs
│       └── WorkflowEngine/                ← 工作流引擎接口
│           ├── INodeRepository.cs
│           └── IPipelineStage.cs
├── Abstractions/                          ← 非执行类接口（保留）
│   ├── CommandCriticality.cs
│   ├── ControlFinderOptions.cs
│   ├── EventBindingInfo.cs
│   ├── IConfigLoader.cs
│   └── ListData.cs
├── CommandFrameworkHost.cs                ← 统一入口（保留在根目录）
├── CommandFrameworkOptions.cs             ← 配置选项（保留）
├── CommandFrameworkUsage.cs               ← 使用示例（保留）
├── Registries.cs                          ← 注册器（保留）
└── docs/
    ├── execution/
    │   └── reorganization-plan.md         ← 本文件
    └── ...
```

### 4.2 目标结构设计原则

| 原则 | 说明 |
|------|------|
| **按执行路径划分** | 顶层分为 `CommandPipeline/`、`WorkflowEngine/`、`Shared/`，清晰区分两条执行路径和共享基础设施 |
| **接口就近原则** | 接口与实现放在同一目录下（如 `Execution/Abstractions/CommandPipeline/` 对应 `Execution/CommandPipeline/`） |
| **消除命名冲突** | 移除重复的 `Execution/` 目录，统一到 `Execution/` 根目录下 |
| **扁平化层级** | 工作流相关文件从 `WorkflowExecution/Engine/` 移到 `Execution/WorkflowEngine/`，减少嵌套 |
| **模型就近原则** | 每个执行路径下有独立的 `Models/` 目录，存放该路径专用模型 |

---

## 五、文件映射表

### 5.1 Move First — 可立即搬迁（仅命名空间变更）

**说明**：以下文件只需物理移动 + 更新命名空间，无需修改代码逻辑，可在一个 PR 中完成。

#### CommandPipeline 路径

| 当前路径 | 目标路径 | 优先级 |
|----------|----------|--------|
| `Parsing/CommandParser.cs` | `Execution/CommandPipeline/Parsing/CommandParser.cs` | P1 |
| `Parsing/ParsedCommand.cs` | `Execution/CommandPipeline/Parsing/ParsedCommand.cs` | P1 |
| `Models/JsonCommandResult.cs` | `Execution/CommandPipeline/Models/JsonCommandResult.cs` | P1 |
| `Models/LoadResult.cs` | `Execution/CommandPipeline/Models/LoadResult.cs` | P1 |
| `Libraries/NonEventCommands.cs` | `Execution/CommandPipeline/Libraries/NonEventCommands.cs` | P1 |
| `Validators/CommandContextValidator.cs` | `Execution/CommandPipeline/Validators/CommandContextValidator.cs` | P1 |
| `Validators/ParsedCommandValidator.cs` | `Execution/CommandPipeline/Validators/ParsedCommandValidator.cs` | P1 |
| `Execution/ControlValueAccessorBase.cs` | `Execution/CommandPipeline/ControlValueAccessorBase.cs` | P1 |
| `Execution/EnvironmentAdapterBase.cs` | `Execution/CommandPipeline/EnvironmentAdapterBase.cs` | P1 |
| `Execution/CommandResults.cs` | `Execution/CommandPipeline/Models/CommandResults.cs` | P1 |

#### WorkflowEngine 路径

| 当前路径 | 目标路径 | 优先级 |
|----------|----------|--------|
| `WorkflowExecution/Engine/WorkflowEngine.cs` | `Execution/WorkflowEngine/WorkflowEngine.cs` | P1 |
| `WorkflowExecution/Execution/FuncCallRun.cs` | `Execution/WorkflowEngine/FuncCallRun.cs` | P1 |
| `WorkflowExecution/Pipeline/CentralPipeline.cs` | `Execution/WorkflowEngine/CentralPipeline.cs` | P1 |
| `WorkflowExecution/Pipeline/PipelineStages.cs` | `Execution/WorkflowEngine/PipelineStages.cs` | P1 |
| `WorkflowExecution/Runtime/RuntimeContextManager.cs` | `Execution/WorkflowEngine/Runtime/RuntimeContextManager.cs` | P1 |
| `WorkflowExecution/Skeletons/NodeChainSkeleton.cs` | `Execution/WorkflowEngine/Skeletons/NodeChainSkeleton.cs` | P1 |
| `WorkflowExecution/Skeletons/NodeProcessSkeleton.cs` | `Execution/WorkflowEngine/Skeletons/NodeProcessSkeleton.cs` | P1 |
| `WorkflowExecution/PlaceholderResolution/PlaceholderEngine.cs` | `Execution/WorkflowEngine/PlaceholderResolution/PlaceholderEngine.cs` | P1 |
| `WorkflowExecution/Models/NodeDefinition.cs` | `Execution/WorkflowEngine/Models/NodeDefinition.cs` | P1 |
| `WorkflowExecution/Models/WorkflowDefinition.cs` | `Execution/WorkflowEngine/Models/WorkflowDefinition.cs` | P1 |

#### Shared 路径

| 当前路径 | 目标路径 | 优先级 |
|----------|----------|--------|
| `Execution/FunctionExecution.cs` | `Execution/Shared/FunctionExecution.cs` | P1 |
| `Execution/InMemoryStateStore.cs` | `Execution/Shared/StateManagement/InMemoryStateStore.cs` | P1 |
| `Parameters/ParameterResolver.cs` | `Execution/Shared/Parameters/ParameterResolver.cs` | P1 |
| `Parameters/Abstractions/IParameterResolver.cs` | `Execution/Shared/Parameters/Abstractions/IParameterResolver.cs` | P1 |
| `ICommand.cs` | `Execution/Shared/Abstractions/ICommand.cs` | P1 |
| `Abstractions/IExecutionContext.cs` | `Execution/Shared/Abstractions/IExecutionContext.cs` | P1 |
| `Abstractions/IStateQuery.cs` | `Execution/Shared/Abstractions/IStateQuery.cs` | P1 |

#### Abstractions 路径

| 当前路径 | 目标路径 | 优先级 |
|----------|----------|--------|
| `Abstractions/IControlFinder.cs` | `Execution/Abstractions/CommandPipeline/IControlFinder.cs` | P1 |
| `Abstractions/IControlValueWriter.cs` | `Execution/Abstractions/CommandPipeline/IControlValueWriter.cs` | P1 |
| `Abstractions/IEventBindingResolver.cs` | `Execution/Abstractions/CommandPipeline/IEventBindingResolver.cs` | P1 |
| `Abstractions/IEventTypeMapper.cs` | `Execution/Abstractions/CommandPipeline/IEventTypeMapper.cs` | P1 |
| `Abstractions/INonEventCommandLookup.cs` | `Execution/Abstractions/CommandPipeline/INonEventCommandLookup.cs` | P1 |
| `Abstractions/INonEventCommandRegistry.cs` | `Execution/Abstractions/CommandPipeline/INonEventCommandRegistry.cs` | P1 |
| `Abstractions/IScriptConverter.cs` | `Execution/Abstractions/CommandPipeline/IScriptConverter.cs` | P1 |
| `Abstractions/IUIController.cs` | `Execution/Abstractions/CommandPipeline/IUIController.cs` | P1 |
| `WorkflowExecution/Abstractions/INodeRepository.cs` | `Execution/Abstractions/WorkflowEngine/INodeRepository.cs` | P1 |
| `WorkflowExecution/Abstractions/IPipelineStage.cs` | `Execution/Abstractions/WorkflowEngine/IPipelineStage.cs` | P0 |

### 5.2 Refactor Then Move — 需要架构重构后搬迁

**说明**：以下文件依赖架构重构（上下文统一、管线统一等），需要在相应 Phase 完成后才能搬迁。

#### 依赖上下文统一（Phase 2）

| 当前路径 | 目标路径 | 依赖 | 优先级 |
|----------|----------|------|--------|
| `Models/CommandContext.cs` | `Execution/CommandPipeline/Models/CommandContext.cs` | 上下文统一完成 | P2 |
| `Models/CommandExecutionContext.cs` | `Execution/CommandPipeline/Models/CommandExecutionContext.cs` | 上下文统一完成 | P2 |
| `Models/CommandExecutionReport.cs` | `Execution/CommandPipeline/Models/CommandExecutionReport.cs` | 上下文统一完成 | P2 |
| `WorkflowExecution/Pipeline/WorkflowContext.cs` | `Execution/WorkflowEngine/Models/WorkflowContext.cs` | 上下文统一完成 | P2 |
| `WorkflowExecution/Runtime/RuntimeContext.cs` | `Execution/WorkflowEngine/Runtime/RuntimeContext.cs` | 上下文统一完成 | P2 |

#### 依赖管线统一（Phase 3）

| 当前路径 | 目标路径 | 依赖 | 优先级 |
|----------|----------|------|--------|
| `Pipeline/PipelineEntry.cs` | `Execution/CommandPipeline/PipelineEntry.cs` | 管线统一完成 | P2 |
| `Pipeline/PipelineStageBase.cs` | `Execution/CommandPipeline/PipelineStageBase.cs` | 管线统一完成（最终移除） | P3 |
| `Pipeline/Stages/ParseCommandStage.cs` | `Execution/CommandPipeline/Stages/ParseCommandStage.cs` | 适配为 IPipelineStage | P1 |
| `Pipeline/Stages/ValidationStage.cs` | `Execution/CommandPipeline/Stages/ValidationStage.cs` | 适配为 IPipelineStage | P1 |
| `Pipeline/Stages/ExecuteCommandStage.cs` | `Execution/CommandPipeline/Stages/ExecuteCommandStage.cs` | 适配为 IPipelineStage | P1 |
| `Pipeline/Stages/ResultCollectStage.cs` | `Execution/CommandPipeline/Stages/ResultCollectStage.cs` | 适配为 IPipelineStage | P1 |
| `Pipeline/Stages/ScriptLoadStage.cs` | `Execution/CommandPipeline/Stages/ScriptLoadStage.cs` | 适配为 IPipelineStage | P2 |
| `Pipeline/Stages/LoggingStage.cs` | `Execution/CommandPipeline/Stages/LoggingStage.cs` | 适配为 IPipelineStage | P2 |
| `Pipeline/Stages/RetryStage.cs` | `Execution/CommandPipeline/Stages/RetryStage.cs` | 适配为 IPipelineStage | P2 |

#### 依赖基础修复（Phase 1）

| 当前路径 | 目标路径 | 依赖 | 优先级 |
|----------|----------|------|--------|
| `WorkflowExecution/Engine/NodeExecutor.cs` | `Execution/WorkflowEngine/NodeExecutor.cs` | 修复并行取消问题 | P1 |
| `Execution/CommandEngine.cs` | `Execution/Shared/CommandEngine.cs` | 移除重复注册检查方法 | P1 |
| `Execution/StateStoreExtensions.cs` | `Execution/Shared/StateManagement/StateStoreExtensions.cs` | 修复序列化不一致问题 | P1 |
| `Parameters/ArgumentBuilder.cs` | `Execution/Shared/Parameters/ArgumentBuilder.cs` | 统一字符串拆分逻辑 | P1 |
| `Parsing/StringUtils.cs` | `Execution/Shared/Utilities/StringUtils.cs` | 统一字符串拆分逻辑 | P0 |

### 5.3 保留在根目录的文件

| 文件 | 原因 |
|------|------|
| `CommandFrameworkHost.cs` | 统一入口，外观模式，对外暴露的核心 API |
| `CommandFrameworkOptions.cs` | 配置选项，全局配置 |
| `CommandFrameworkUsage.cs` | 使用示例，帮助文档 |
| `Registries.cs` | 注册器集合，全局注册入口 |

### 5.4 保留在原 Abstractions/ 的文件

| 文件 | 原因 |
|------|------|
| `CommandCriticality.cs` | 枚举定义，非执行逻辑 |
| `ControlFinderOptions.cs` | 配置类，非执行逻辑 |
| `EventBindingInfo.cs` | 数据结构，非执行逻辑 |
| `IConfigLoader.cs` | 配置加载接口，非执行逻辑 |
| `ListData.cs` | 数据结构，非执行逻辑 |

---

## 六、分阶段搬迁步骤

### Phase 1：基础修复与共享工具统一

**前置条件：** 无

**目标：** 修复错误逻辑，统一共享工具类，为后续搬迁做准备

**步骤：**

| 步骤 | 任务 | 涉及文件 | 状态 |
|------|------|----------|------|
| 1.1 | 修复注释行标记问题 | `Parsing/CommandParser.cs` | ✅ |
| 1.2 | 修复枚举转换问题 | `WorkflowExecution/Engine/WorkflowEngine.cs` | ✅ |
| 1.3 | 修复并行取消问题 | `WorkflowExecution/Engine/NodeExecutor.cs` | ✅ |
| 1.4 | 修复序列化不一致问题 | `Execution/StateStoreExtensions.cs` | ✅ |
| 1.5 | 修复事件触发不一致问题 | `Execution/StateStoreExtensions.cs` | ✅ |
| 1.6 | 统一字符串拆分逻辑到 `StringUtils` | `Parsing/StringUtils.cs`, `Parsing/CommandParser.cs`, `Parameters/ArgumentBuilder.cs` | ✅ |
| 1.7 | 移除 `CommandEngine` 重复注册检查方法 | `Execution/CommandEngine.cs` | ✅ |

> **状态说明**：Phase 1 基础修复已完成，相关代码修改已通过编译验证。

### Phase 2：上下文统一

**前置条件：** Phase 1 完成

**目标：** 设计并实现统一的 `ExecutionContext<TChainInfo>` 模型

**步骤：**

| 步骤 | 任务 | 涉及文件 | 状态 |
|------|------|----------|------|
| 2.1 | 设计 `ExecutionContext<TChainInfo>` 统一模型 | 新建 `Execution/Shared/Models/ExecutionContext.cs` | ⏳ |
| 2.2 | 迁移 `CommandContext` 到新模型 | `Models/CommandContext.cs` | ⏳ |
| 2.3 | 迁移 `PipelineContext` 到新模型 | `WorkflowExecution/Pipeline/WorkflowContext.cs` | ⏳ |
| 2.4 | 更新所有引用点 | 全局搜索 `CommandContext`、`PipelineContext`、`RuntimeContext` | ⏳ |

### Phase 3：Move First 执行 + 管线统一

**前置条件：** Phase 1 完成（Move First），Phase 2 完成（管线统一）

**目标：** 执行所有可立即搬迁的文件，同时进行管线统一

**步骤：**

| 步骤 | 任务 | 涉及文件 | 状态 |
|------|------|----------|------|
| 3.1 | 搬迁 CommandPipeline Move First 文件 | `Parsing/`, `Models/`（部分）, `Libraries/`, `Validators/` | ⏳ |
| 3.2 | 搬迁 WorkflowEngine Move First 文件 | `WorkflowExecution/Engine/`（部分）, `WorkflowExecution/Execution/`, `WorkflowExecution/Pipeline/`（部分）, `WorkflowExecution/Runtime/`（部分）, `WorkflowExecution/Skeletons/`, `WorkflowExecution/PlaceholderResolution/`, `WorkflowExecution/Models/` | ⏳ |
| 3.3 | 搬迁 Shared Move First 文件 | `Execution/`（部分）, `Parameters/`（部分）, `ICommand.cs`, `Abstractions/`（部分） | ⏳ |
| 3.4 | 搬迁 Abstractions Move First 文件 | `Abstractions/`（部分）, `WorkflowExecution/Abstractions/` | ⏳ |
| 3.5 | 更新命名空间 | 全局搜索并替换旧命名空间 | ⏳ |
| 3.6 | **增量验证**：编译 + 运行测试 | 全部文件 | ⏳ |
| 3.7 | 提取 `CommandStageBase` 辅助方法为独立静态类 | `Pipeline/PipelineStageBase.cs` → `Execution/Shared/Helpers/` | ⏳ |
| 3.8 | 创建 `DependencyResolver` 和 `ControlAccessor` 静态类 | 新建 `Execution/Shared/Helpers/` | ⏳ |
| 3.9 | 适配 CommandFramework Stages 为 `IPipelineStage` | `Pipeline/Stages/` | ⏳ |
| 3.10 | 在 `CentralPipeline` 中动态注册 CommandFramework 阶段 | `CentralPipeline.cs` | ⏳ |
| 3.11 | 更新 `CommandFrameworkHost` 使用 `CentralPipeline` | `CommandFrameworkHost.cs` | ⏳ |
| 3.12 | **增量验证**：编译 + 运行测试 | 全部文件 | ⏳ |

### Phase 4：基础设施整合与清理

**前置条件：** Phase 3 完成

**目标：** 合并重复基础设施，清理冗余代码

**步骤：**

| 步骤 | 任务 | 涉及文件 | 状态 |
|------|------|----------|------|
| 4.1 | 合并 `ArgumentBuilder` 和 `ParameterResolver` | `Execution/Shared/Parameters/` | ⏳ |
| 4.2 | 统一状态存储接口 | `Execution/Shared/StateManagement/` | ⏳ |
| 4.3 | 移除冗余的 Host 层包装 | `CommandFrameworkHost.cs` | ⏳ |
| 4.4 | 删除空目录 | `Pipeline/`, `WorkflowExecution/`, `Parameters/`, `Parsing/`, `Models/`, `Libraries/`, `Validators/` | ⏳ |
| 4.5 | 更新项目文件（.csproj） | `CommandFramework.csproj` | ⏳ |
| 4.6 | **最终验证**：编译 + 运行所有测试 | 全部文件 | ⏳ |

---

## 七、增量验证策略

为确保搬迁过程安全可控，采用**增量验证**策略：

### 7.1 验证节点

| 验证节点 | 时机 | 验证内容 |
|----------|------|----------|
| **Commit 1** | Move First 完成后 | 编译项目，确保无命名空间错误 |
| **Commit 2** | 命名空间更新完成后 | 运行单元测试，确保功能不变 |
| **Commit 3** | 管线统一完成后 | 运行集成测试，确保执行流程正确 |
| **Commit 4** | 基础设施整合完成后 | 运行全量测试，确保无回归 |

### 7.2 验证工具

| 工具 | 用途 |
|------|------|
| `dotnet build` | 编译项目，检查命名空间引用错误 |
| `dotnet test` | 运行单元测试 |
| `dotnet test --filter Category=Integration` | 运行集成测试 |

---

## 八、搬迁注意事项

### 8.1 命名空间变更规则

| 当前命名空间 | 目标命名空间 |
|-------------|-------------|
| `CommandFramework.Pipeline` | `CommandFramework.Execution.CommandPipeline` |
| `CommandFramework.Pipeline.Stages` | `CommandFramework.Execution.CommandPipeline.Stages` |
| `CommandFramework.Parsing` | `CommandFramework.Execution.CommandPipeline.Parsing` |
| `CommandFramework.Models` | `CommandFramework.Execution.CommandPipeline.Models` |
| `CommandFramework.Libraries` | `CommandFramework.Execution.CommandPipeline.Libraries` |
| `CommandFramework.Validators` | `CommandFramework.Execution.CommandPipeline.Validators` |
| `CommandFramework.WorkflowExecution.Engine` | `CommandFramework.Execution.WorkflowEngine` |
| `CommandFramework.WorkflowExecution.Execution` | `CommandFramework.Execution.WorkflowEngine` |
| `CommandFramework.WorkflowExecution.Pipeline` | `CommandFramework.Execution.WorkflowEngine` |
| `CommandFramework.WorkflowExecution.Runtime` | `CommandFramework.Execution.WorkflowEngine.Runtime` |
| `CommandFramework.WorkflowExecution.Skeletons` | `CommandFramework.Execution.WorkflowEngine.Skeletons` |
| `CommandFramework.WorkflowExecution.PlaceholderResolution` | `CommandFramework.Execution.WorkflowEngine.PlaceholderResolution` |
| `CommandFramework.WorkflowExecution.Models` | `CommandFramework.Execution.WorkflowEngine.Models` |
| `CommandFramework.Execution` | `CommandFramework.Execution.Shared` |
| `CommandFramework.Parameters` | `CommandFramework.Execution.Shared.Parameters` |
| `CommandFramework.Parameters.Abstractions` | `CommandFramework.Execution.Shared.Parameters.Abstractions` |
| `CommandFramework.Abstractions` | `CommandFramework.Execution.Abstractions.CommandPipeline`（部分） |
| `CommandFramework.WorkflowExecution.Abstractions` | `CommandFramework.Execution.Abstractions.WorkflowEngine` |

### 8.2 项目文件更新

搬迁完成后需更新 `CommandFramework.csproj` 文件，移除旧的 `<Compile>` 和 `<EmbeddedResource>` 条目，添加新路径。

### 8.3 向后兼容性

- 在搬迁过程中，保留旧的命名空间别名（使用 `using` 别名），确保外部项目不受影响
- 逐步移除别名，在主版本更新时完成完全迁移

---

## 九、验证清单

### 9.1 Move First 完成验证

| 检查项 | 验证方法 |
|--------|---------|
| 所有 Move First 文件已搬迁到目标位置 | 检查 `Execution/` 目录结构 |
| 命名空间已更新 | 全局搜索旧命名空间 |
| 项目文件已更新 | `dotnet build` |
| 测试通过 | `dotnet test` |

### 9.2 管线统一完成验证

| 检查项 | 验证方法 |
|--------|---------|
| CommandFramework Stages 已适配为 `IPipelineStage` | 检查接口实现 |
| `CentralPipeline` 已注册所有 CommandFramework 阶段 | 检查注册代码 |
| `CommandFrameworkHost` 使用 `CentralPipeline` | 检查 Host 实现 |
| 集成测试通过 | `dotnet test --filter Category=Integration` |

### 9.3 最终验证

| 检查项 | 验证方法 |
|--------|---------|
| 所有文件已搬迁到目标位置 | 检查 `Execution/` 目录结构 |
| 无编译错误 | `dotnet build` |
| 无运行时异常 | 运行全量测试 |
| 空目录已删除 | 检查项目结构 |

---

## 十、总结

本规划将 CommandFramework 的执行逻辑整理分为**两个独立轨道**：

1. **Move First**（低风险）：30+ 个文件可立即搬迁，只需更新命名空间，无需修改代码逻辑
2. **Refactor Then Move**（中等风险）：需要架构重构后才能搬迁的文件

整理后的结构以**执行路径**为主线：

- `Execution/CommandPipeline/` — 命令管线执行路径
- `Execution/WorkflowEngine/` — 工作流执行路径
- `Execution/Shared/` — 共享基础设施
- `Execution/Abstractions/` — 执行相关接口

建议先执行 **Move First** 轨道，快速改善代码组织结构，再逐步进行架构重构。每个阶段完成后进行增量验证，确保迁移过程安全可控。