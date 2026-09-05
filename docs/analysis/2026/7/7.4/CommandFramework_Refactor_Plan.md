# CommandFramework 代码整理修复计划

> 基于 `CommandFramework_Analysis_Report.md` 的分析结果，规划实际的代码整理修复步骤。
> 分析日期：2026-07-03

---

## 目录

1. [已完成修复](#1-已完成修复)
2. [待修复项总览](#2-待修复项总览)
3. [批次 1：Pipeline 体系与上下文统一（P1）](#3-批次-1pipeline-体系与上下文统一p1)
4. [批次 2：参数解析器与运行时上下文合并（P1）](#4-批次-2参数解析器与运行时上下文合并p1)
5. [批次 3：冗余逻辑清理（P2）](#5-批次-3冗余逻辑清理p2)
6. [批次 4：低优先级清理（P3）](#6-批次-4低优先级清理p3)
7. [附录：文件索引](#7-附录文件索引)

---

## 1. 已完成修复

以下 9 项修复已在分析报告生成后完成，构建验证通过（0 警告 0 错误）：

| # | 优先级 | 问题 | 修复内容 | 涉及文件 |
|---|--------|------|----------|----------|
| 1 | P0 | `SetData<T>` 无限递归 | 改为调用非泛型重载 `SetData(key, (object)value!)` | `WorkflowExecution/Pipeline/WorkflowContext.cs` |
| 2 | P0 | `CancellationToken` setter 取消旧 CTS | 移除 `oldCts?.Cancel()`，旧 CTS 在下次 `ResetCancellation` 或 `Dispose` 时清理 | `WorkflowExecution/Pipeline/WorkflowContext.cs` |
| 3 | P0 | `Dispose` 使用 `new` 隐藏父类方法 | 改为 `override` + `base.Dispose(disposing)`，实现标准 `IDisposable` 模式 | `WorkflowExecution/Pipeline/WorkflowContext.cs` |
| 4 | P0 | `Parameters` 缓存未失效 | 添加 `ObservableDictionaryWrapper`，拦截 `Items` 修改操作触发缓存失效 | `WorkflowExecution/Pipeline/WorkflowContext.cs` |
| 5 | P1 | `GetParameter<T>(int)` 依赖字典顺序 | 改用 `OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)` 稳定排序 | `WorkflowExecution/Pipeline/WorkflowContext.cs` |
| 6 | P1 | `WorkflowEngine` 会话索引不更新 | 添加 `_sessionIndex.TryUpdate` 机制，新会话创建时更新索引 | `WorkflowExecution/Engine/WorkflowEngine.cs` |
| 7 | P2 | 冗余工厂方法 | 合并 `CreateFromOptions`/`CreateCliHost`/`CreateFullHost` 为统一的 `Create` 方法 | `CommandFrameworkHost.cs` |
| 8 | P2 | 更新调用方 | 更新 `CommandFrameworkUsage` 和 `EventTriggerFrameworkInitializer` 使用新 `Create` 方法 | `CommandFrameworkUsage.cs`、`EventTriggerFrameworkInitializer.cs` |
| 9 | - | 构建验证 | 执行 `dotnet build`，0 警告 0 错误 | - |

---

## 2. 待修复项总览

| 批次 | 优先级 | 问题 | 工作量 | 影响范围 |
|------|--------|------|--------|----------|
| **批次 1** | P1 | 两套 Pipeline 体系统一 | ⭐⭐⭐ | 核心架构变更 |
| **批次 1** | P1 | 两套 WorkflowContext 合并 | ⭐⭐ | 中等范围 |
| **批次 2** | P1 | 两套参数解析器合并 | ⭐⭐ | 中等范围 |
| **批次 2** | P1 | 两套运行时上下文管理合并 | ⭐⭐ | 中等范围 |
| **批次 3** | P2 | 冗余 Skeleton 体系移除 | ⭐ | 较小范围 |
| **批次 3** | P2 | 冗余 `ExecuteForMode` 删除 | ⭐ | 单文件 |
| **批次 3** | P2 | 冗余 `CanExecute` 检查简化 | ⭐ | 单文件 |
| **批次 3** | P2 | 冗余依赖解析逻辑简化 | ⭐ | 单文件 |
| **批次 3** | P2 | 冗余 `PipelineStageNames` 常量清理 | ⭐ | 单文件 |
| **批次 3** | P2 | 冗余 `PlaceholderEngine` 处理 | ⭐ | 单文件 |
| **批次 4** | P3 | 未使用的接口和类移除 | ⭐ | 多文件 |
| **批次 4** | P3 | GUI/CLI 模式非互斥修复 | ⭐ | 单文件 |
| **批次 4** | P3 | `NodeExecutor` Foreach 回退逻辑修复 | ⭐ | 单文件 |

---

## 3. 批次 1：Pipeline 体系与上下文统一（P1）

### 3.1 两套 Pipeline 体系统一

#### 问题描述

CommandFramework 内部存在两套独立的 Pipeline 体系：

- **体系 A**：`Modules.Pipeline.IPipelineStage<TContext>` — 用于命令管线
- **体系 B**：`WorkflowExecution.Abstractions.IPipelineStage` — 用于工作流执行管线

两套体系功能高度重叠（阶段注册、排序、执行），但接口签名不同，需要适配器桥接。

#### 涉及文件

| 文件 | 角色 | 操作 |
|------|------|------|
| `WorkflowExecution/Abstractions/IPipelineStage.cs` | 体系 B 接口定义 | **移除**，改用体系 A |
| `WorkflowExecution/Abstractions/PipelineToStageAdapter.cs` | 适配器 A→B | **移除** |
| `WorkflowExecution/Abstractions/StageToPipelineAdapter.cs` | 适配器 B→A | **移除** |
| `WorkflowExecution/Pipeline/CentralPipeline.cs` | 体系 B 管线实现 | **重构**，改用 `StagePipeline<TContext>` |
| `WorkflowExecution/Abstractions/IStagePipeline.cs` | 体系 B 管线接口 | **移除**，改用 `IStagePipeline<TContext>` |

#### 执行步骤

1. **确认体系 A 的接口能力**：检查 `Modules.Pipeline.IPipelineStage<TContext>` 和 `StagePipeline<TContext>` 是否满足体系 B 的所有使用场景
   - `CentralPipeline` 的 `Register`/`RegisterSkeleton`/`ExecuteAsync` 方法
   - `IStagePipeline` 接口的 `Register`/`InsertBefore`/`InsertAfter`/`Remove`/`ExecuteAsync` 方法

2. **重构 `CentralPipeline`**：
   - 将 `CentralPipeline` 改为继承或组合 `StagePipeline<TContext>`
   - 将 `RegisterSkeleton` 方法改为直接注册 `IPipelineStage<TContext>`
   - 更新 `ExecuteAsync` 方法签名匹配 `StagePipeline<TContext>`

3. **更新调用方**：
   - 搜索所有引用 `WorkflowExecution.Abstractions.IPipelineStage` 的地方
   - 搜索所有引用 `PipelineToStageAdapter` 和 `StageToPipelineAdapter` 的地方
   - 更新为使用 `Modules.Pipeline.IPipelineStage<TContext>`

4. **删除废弃文件**：
   - `IPipelineStage.cs`（体系 B）
   - `PipelineToStageAdapter.cs`
   - `StageToPipelineAdapter.cs`
   - `IStagePipeline.cs`（如果存在）

#### 风险与注意事项

- ⚠️ `Modules.Pipeline.IPipelineStage<TContext>` 的泛型参数 `TContext` 约束为 `IPipelineContext`，需要确保 `WorkflowContext` 实现了该接口
- ⚠️ 体系 B 的 `IPipelineStage` 可能有不带泛型参数的重载，需要逐一确认映射关系
- ✅ `WorkflowContext` 已继承 `Modules.Pipeline.PipelineContext`，满足 `IPipelineContext` 约束

---

### 3.2 两套 WorkflowContext 合并

#### 问题描述

- `WorkflowExecution/Abstractions/WorkflowContext.cs` — 简单实现（136 行）
- `WorkflowExecution/Pipeline/WorkflowContext.cs` — 完整实现（555 行，继承 `Modules.Pipeline.PipelineContext`）

两者都实现了 `IWorkflowContext` 接口，功能重叠。

#### 涉及文件

| 文件 | 角色 | 操作 |
|------|------|------|
| `WorkflowExecution/Abstractions/WorkflowContext.cs` | 简单版上下文 | **移除** |
| `WorkflowExecution/Abstractions/IWorkflowContext.cs` | 接口定义 | **保留** |
| `WorkflowExecution/Pipeline/WorkflowContext.cs` | 完整版上下文 | **保留**（已修复） |

#### 执行步骤

1. **搜索引用**：查找所有引用 `WorkflowExecution.Abstractions.WorkflowContext` 的地方
2. **更新引用**：将引用改为 `WorkflowExecution.Pipeline.PipelineContext`
3. **验证接口兼容性**：确保 `PipelineContext` 实现了 `IWorkflowContext` 的所有成员
4. **删除废弃文件**：删除 `WorkflowExecution/Abstractions/WorkflowContext.cs`

#### 风险与注意事项

- ⚠️ 简单版 `WorkflowContext` 可能被 `WorkFlowMethod` 或其他外部项目引用，需要全局搜索确认
- ✅ `PipelineContext` 已实现 `IWorkflowContext` 接口

---

## 4. 批次 2：参数解析器与运行时上下文合并（P1）

### 4.1 两套参数解析器合并

#### 问题描述

- `WorkflowExecution/Engine/ParameterResolver.cs` — 管理执行参数 + 节点结果的合并与存储
- `Execution/ArgumentBuilder.cs` — 从 `IFunctionDescriptor` 构建函数调用参数

两者职责边界模糊，`ParameterResolver.MergeParameters` 与 `ArgumentBuilder.BuildFromDescriptor` 存在功能重叠。

#### 涉及文件

| 文件 | 角色 | 操作 |
|------|------|------|
| `WorkflowExecution/Engine/ParameterResolver.cs` | 工作流参数解析器 | **重构** |
| `Execution/ArgumentBuilder.cs` | 命令参数构建器 | **保留** |
| `Execution/FunctionExecution.cs` | 函数执行参数构建 | **审查** |

#### 执行步骤

1. **分析职责边界**：
   - `ParameterResolver`：负责从多个来源（执行参数、节点结果、默认值）合并参数
   - `ArgumentBuilder`：负责将合并后的参数映射到 `IFunctionDescriptor` 的具体参数列表

2. **明确职责划分**：
   - `ParameterResolver` → 参数合并与存储（保留）
   - `ArgumentBuilder` → 参数映射与构建（保留）
   - 移除两者之间的功能重叠

3. **重构 `ParameterResolver`**：
   - 如果 `MergeParameters` 逻辑与 `ArgumentBuilder` 重叠，移除重复部分
   - 确保 `ParameterResolver` 只负责参数来源的合并，不负责参数到函数签名的映射

---

### 4.2 两套运行时上下文管理合并

#### 问题描述

- `RuntimeContext<TChainInfo>` — 泛型运行时上下文
- `RuntimeContextManager` — 上下文管理器
- `PipelineContext` — 管道上下文

三者都管理执行状态（`ExecutionId`、`CurrentNodeKey`、`CancellationToken`、数据存储等）。

#### 涉及文件

| 文件 | 角色 | 操作 |
|------|------|------|
| `WorkflowExecution/Runtime/RuntimeContext.cs` | 泛型运行时上下文 | **评估**是否可被 `PipelineContext` 替代 |
| `WorkflowExecution/Runtime/RuntimeContextManager.cs` | 上下文管理器 | **评估**是否与 `WorkflowEngine` 功能重叠 |

#### 执行步骤

1. **分析 `RuntimeContext<TChainInfo>` 的使用场景**：
   - 搜索所有引用 `RuntimeContext` 的地方
   - 确认 `TChainInfo` 泛型参数的具体用途

2. **评估替代方案**：
   - 如果 `RuntimeContext` 的功能完全被 `PipelineContext` 覆盖，移除 `RuntimeContext`
   - 如果 `RuntimeContext` 有独特功能（如 `TChainInfo` 泛型），保留但明确职责边界

3. **分析 `RuntimeContextManager` 与 `WorkflowEngine` 的关系**：
   - `RuntimeContextManager` 的创建/获取/移除/取消管理
   - `WorkflowEngine` 的 `_sessionIndex` 和 `_cancellationSources` 管理
   - 合并重复的管理逻辑

---

## 5. 批次 3：冗余逻辑清理（P2）

### 5.1 冗余 Skeleton 体系移除

#### 问题描述

`ISkeleton` 接口与 `IPipelineStage` 接口功能高度重叠，`CentralPipeline.RegisterSkeleton` 内部通过 `PipelineStages.CreateFromSkeleton` 将 `ISkeleton` 包装为 `IPipelineStage`。

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `WorkflowExecution/Skeletons/NodeProcessSkeleton.cs` | **移除**或重构为 `IPipelineStage` |
| `WorkflowExecution/Skeletons/NodeChainSkeleton.cs` | **移除**或重构为 `IPipelineStage` |
| `WorkflowExecution/Abstractions/ISkeleton.cs` | **移除** |

#### 执行步骤

1. 搜索所有引用 `ISkeleton` 的地方
2. 将 `NodeProcessSkeleton` 和 `NodeChainSkeleton` 改为直接实现 `IPipelineStage<TContext>`
3. 更新 `CentralPipeline` 移除 `RegisterSkeleton` 方法
4. 删除 `ISkeleton.cs` 和相关文件

---

### 5.2 冗余 `ExecuteForMode` 方法删除

#### 问题描述

`PipelineStageBase.ExecuteForMode` 方法（174-205 行）在 `ExecuteCommandStage` 中未被使用，已成为死代码。

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `Pipeline/PipelineStageBase.cs` | 删除 `ExecuteForMode` 方法 |

---

### 5.3 冗余 `CanExecute` 检查简化

#### 问题描述

`CommandStageBase.CanExecute` 已通过 `EventCommandOnly` 属性过滤非事件命令，但 `ExecuteCommandStage` 内部又通过 `action`/`verb` 判断命令类型。

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `Pipeline/Stages/ExecuteCommandStage.cs` | 简化内部的命令类型判断逻辑 |

---

### 5.4 冗余依赖解析逻辑简化

#### 问题描述

`ExecuteCommandStage` 中多次调用 `ResolveDependency`，但所有依赖已通过构造函数注入，三级回退机制只用到了第一级。

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `Pipeline/PipelineStageBase.cs` | 简化 `ResolveDependency` 方法 |
| `Pipeline/Stages/ExecuteCommandStage.cs` | 移除不必要的 `ResolveDependency` 调用 |

---

### 5.5 冗余 `PipelineStageNames` 常量清理

#### 问题描述

`PipelineStageNames.AllStages` 定义了 8 个阶段，但 `CentralPipeline` 只使用了 `Initialization` 和 `Finalization` 两个。

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `WorkflowExecution/Abstractions/IPipelineStage.cs` | 移除未使用的阶段常量 |

> 注意：如果此文件在批次 1 中被删除，则此步骤自动完成。

---

### 5.6 冗余 `PlaceholderEngine` 处理

#### 问题描述

`PlaceholderEngine` 依赖 `Newtonsoft.Json` 和 `Graphkey.Core`，且未被 CommandFramework 核心流程引用。

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `WorkflowExecution/PlaceholderResolution/PlaceholderEngine.cs` | 标记为废弃或移除 |

#### 执行步骤

1. 搜索所有引用 `PlaceholderEngine` 的地方
2. 如果无引用，添加 `[Obsolete]` 标记或直接移除
3. 如果被引用，评估是否可以迁移到 `System.Text.Json`

---

## 6. 批次 4：低优先级清理（P3）

### 6.1 未使用的接口和类移除

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `WorkflowExecution/Abstractions/INodeConfigExtractor.cs` | **移除**（未被引用） |
| `WorkflowExecution/Abstractions/FieldMappingEntry.cs` | **移除**（未被引用） |
| `WorkflowExecution/Abstractions/IExecutionLifecycle.cs` | **移除**（未被引用） |

#### 执行步骤

1. 全局搜索确认无引用
2. 删除对应文件

---

### 6.2 GUI/CLI 模式非互斥修复

#### 问题描述

`ExecuteCommandStage.HandleSet` 中 GUI 和 CLI 模式使用两个独立的 `if` 而非 `if-else if`，理论上可能同时执行。

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `Pipeline/Stages/ExecuteCommandStage.cs` | 将 `if` 改为 `if-else if` |

---

### 6.3 `NodeExecutor` Foreach 回退逻辑修复

#### 问题描述

当 `foreachItems` 为 `null` 且使用回退数据源时，`maxIterations` 使用的是 `loopConfig.MaxIterations`（默认 10），而不是回退数据源的实际长度。

#### 涉及文件

| 文件 | 操作 |
|------|------|
| `WorkflowExecution/Engine/NodeExecutor.cs` | 修复 `maxIterations` 的计算逻辑 |

---

## 7. 附录：文件索引

### 7.1 CommandFramework 项目文件结构

```
CsharpPrj/Share/CommandFramework/
├── CommandFramework.csproj
├── CommandFrameworkHost.cs                          ← 已修复（工厂方法合并）
├── CommandFrameworkUsage.cs                         ← 已修复（更新调用方）
│
├── Abstractions/
│   ├── IWorkflowContext.cs                          ← 保留
│   ├── WorkflowContext.cs                           ← 批次1：移除
│   ├── IPipelineStage.cs                            ← 批次1：移除
│   ├── IStagePipeline.cs                            ← 批次1：移除（如存在）
│   ├── ISkeleton.cs                                 ← 批次3：移除
│   ├── INodeConfigExtractor.cs                      ← 批次4：移除
│   ├── FieldMappingEntry.cs                         ← 批次4：移除
│   ├── IExecutionLifecycle.cs                       ← 批次4：移除
│   ├── PipelineToStageAdapter.cs                    ← 批次1：移除
│   └── StageToPipelineAdapter.cs                    ← 批次1：移除
│
├── Execution/
│   ├── ArgumentBuilder.cs                           ← 批次2：审查
│   ├── FunctionExecution.cs                         ← 批次2：审查
│   └── EnvironmentAdapterBase.cs                    ← 保留
│
├── Pipeline/
│   ├── PipelineEntry.cs                             ← 保留
│   ├── PipelineStageBase.cs                         ← 批次3：清理
│   └── Stages/
│       ├── ExecuteCommandStage.cs                   ← 批次3/4：清理
│       ├── ResultCollectStage.cs                    ← 保留
│       └── RetryStage.cs                            ← 保留
│
├── WorkflowExecution/
│   ├── Abstractions/                                ← 批次1/3/4：清理
│   ├── Engine/
│   │   ├── WorkflowEngine.cs                        ← 已修复（会话索引）
│   │   ├── NodeExecutor.cs                          ← 批次4：修复
│   │   └── ParameterResolver.cs                     ← 批次2：重构
│   ├── Pipeline/
│   │   ├── CentralPipeline.cs                       ← 批次1：重构
│   │   └── WorkflowContext.cs                       ← 已修复（多项）
│   ├── Runtime/
│   │   ├── RuntimeContext.cs                        ← 批次2：评估
│   │   └── RuntimeContextManager.cs                 ← 批次2：评估
│   ├── Skeletons/
│   │   ├── NodeProcessSkeleton.cs                   ← 批次3：移除
│   │   └── NodeChainSkeleton.cs                     ← 批次3：移除
│   └── PlaceholderResolution/
│       └── PlaceholderEngine.cs                     ← 批次3：处理
```

### 7.2 修复状态总览

| 状态 | 数量 | 说明 |
|------|------|------|
| ✅ 已完成 | 9 项 | P0/P1/P2 已修复并通过构建验证 |
| 📋 待修复 | 13 项 | 分 4 个批次执行 |
| 总计 | 22 项 | 分析报告识别的全部问题 |

---

> **文档版本**：v1.0
> **最后更新**：2026-07-03
> **下一步**：切换到 ACT MODE 按批次执行修复
