# `WorkflowExecution` 通用化执行框架方案

> 版本：v1.0
> 日期：2026-06-28
> 状态：已定稿

---

## 一、现状全景分析

### 1.1 项目位置与依赖关系

```
WorkflowExecution (CsharpPrj/Share/WorkflowExecution/)
  ├── 引用 BasicMethod.csproj（基础库 + FunctionExecution）
  ├── 引用 Modules/Pipeline.csproj（管线编排）
  ├── 引用 StateManagement.csproj（状态管理）
  └── 被 WorkFlowMethod.csproj（Operation 层）引用
```

### 1.2 文件清单（22 个源文件）

```
Abstractions/          (7 文件)
├── IWorkflowContext.cs    — 工作流上下文接口
├── IWorkflowEngine.cs     — 工作流引擎接口
├── IWorkflowLogger.cs     — 工作流日志接口
├── IExecutionSession.cs   — 执行会话接口（通用）
├── IDataContext.cs        — 数据上下文接口（通用）
├── INodeExecutor.cs       — 节点执行器接口
├── IPipelineStage.cs      — 管线阶段接口（通用）+ PipelineStageNames
└── ISkeleton.cs           — 骨架接口（通用）+ SkeletonTypes

Models/                (3 文件)
├── WorkflowDefinition.cs  — 工作流定义 + ParameterDefinition + WorkflowConfig
├── NodeDefinition.cs      — 节点定义 + LoopConfig
└── ExecutionStatus.cs     — 执行状态枚举（通用）

Engine/                (5 文件)
├── WorkflowEngine.cs      — 工作流引擎实现
├── NodeExecutor.cs        — 节点执行器（含 Polly 弹性策略）
├── ExecutionOrchestrator.cs — 执行编排器抽象基类
├── DefaultExecutionOrchestrator.cs — 默认编排器
├── SessionManager.cs      — 会话管理器接口 + 默认实现
└── HotReloadService.cs    — 热重载服务

Session/               (4 文件)
├── DataContext.cs         — 数据上下文实现（通用）
├── ExecutionSession.cs    — 执行会话实现（通用）
├── NodeExecutionState.cs  — 节点执行状态（通用）
└── SessionFactory.cs      — 会话工厂接口 + 默认实现

Pipeline/              (3 文件)
├── CentralPipeline.cs     — 中央管线（通用）
├── PipelineStages.cs      — 管线阶段工厂（通用）
└── WorkflowContext.cs     — 管线上下文（继承 Modules.Pipeline.PipelineContext）

Logging/               (1 文件)
└── WorkflowLogger.cs      — 工作流日志实现

Skeletons/             (2 文件)
├── NodeProcessSkeleton.cs — 节点处理骨架（工作流特定）
└── NodeChainSkeleton.cs   — 节点链骨架（工作流特定）

Extensions/            (1 文件)
└── WorkflowExtensions.cs  — 扩展方法
```

### 1.3 分类评估

#### ✅ 已经是通用（可保留/仅重命名）— 14 个组件

| 组件 | 当前名称 | 通用化名称 | 说明 |
|------|---------|-----------|------|
| `IWorkflowContext` | 工作流上下文 | `IExecutionContext` | 执行上下文，与工作流无关 |
| `IExecutionSession` | 执行会话 | 不变 | 会话管理本身就是通用的 |
| `IDataContext` | 数据上下文 | 不变 | 键值数据存储，完全通用 |
| `ISessionManager` | 会话管理器 | 不变 | 会话注册/查询/取消，通用 |
| `ISessionFactory` | 会话工厂 | 不变 | 通用 |
| `IPipelineStage` | 管线阶段 | 不变 | 本身就是通用接口 |
| `PipelineStageNames` | 管线阶段名称 | 不变 | 常量定义，通用 |
| `CentralPipeline` | 中央管线 | 不变 | 阶段编排器，通用 |
| `PipelineStages` | 管线阶段工厂 | 不变 | 通用 |
| `PipelineContext` | 管线上下文 | 不变 | 继承自 `Modules.Pipeline.PipelineContext`，通用 |
| `ExecutionStatus` | 执行状态枚举 | 不变 | 通用 |
| `ExecutionOrchestrator` | 执行编排器 | 不变 | 抽象基类，通用 |
| `DefaultExecutionOrchestrator` | 默认编排器 | 不变 | 通用 |
| `DataContext` | 数据上下文 | 不变 | 通用 |
| `ExecutionSession` | 执行会话 | 不变 | 通用 |
| `NodeExecutionState` | 节点执行状态 | `StepExecutionState` | 步骤执行状态，通用 |
| `Guard` | 守卫标志 | 不变 | 通用 |
| `ISkeleton` | 骨架接口 | 不变 | 通用 |
| `SkeletonTypes` | 骨架类型常量 | 不变 | 通用 |
| `HotReloadService` | 热重载服务 | 不变 | 通用 |

#### ⚠️ 接口名含 Workflow（需重命名）— 3 个

| 当前名称 | 问题 | 改造方式 |
|---------|------|---------|
| `IWorkflowEngine` | 接口名含 Workflow | 重命名为 `IExecutionEngine` |
| `IWorkflowContext` | 接口名含 Workflow | 重命名为 `IExecutionContext` |
| `IWorkflowLogger` | 接口名含 Workflow | 重命名为 `IExecutionLogger` |

#### ⚠️ 类名含 Workflow（需重命名）— 3 个

| 当前名称 | 问题 | 改造方式 |
|---------|------|---------|
| `WorkflowEngine` | 类名含 Workflow | 重命名为 `ExecutionEngine` |
| `WorkflowLogger` | 类名含 Workflow | 重命名为 `ExecutionLogger` |
| `WorkflowContext` | 类名含 Workflow | 重命名为 `ExecutionContext` |

#### ⚠️ 类名含 Node（需重命名）— 2 个

| 当前名称 | 问题 | 改造方式 |
|---------|------|---------|
| `INodeExecutor` | 接口名含 Node | 重命名为 `IStepExecutor` |
| `NodeExecutor` | 类名含 Node | 重命名为 `StepExecutor` |

#### ❌ 工作流领域特定（需保留在 WorkflowExecution 中）— 4 个

| 组件 | 说明 | 处理方式 |
|------|------|---------|
| `WorkflowDefinition` | 强工作流模型 | 保留在 WorkflowExecution，继承通用 `ExecutionPlan` |
| `NodeDefinition` | 强工作流节点 | 保留在 WorkflowExecution，继承通用 `StepDefinition` |
| `NodeProcessSkeleton` | 节点处理骨架 | 保留在 WorkflowExecution |
| `NodeChainSkeleton` | 节点链骨架 | 保留在 WorkflowExecution |
| `WorkflowExtensions` | 扩展方法 | 拆分：通用部分（ToJson/FromJson）迁移，工作流特定部分保留 |

---

## 二、推荐方案：方案 A（轻量重构）

### 2.1 方案选择理由

经过全面代码审查，**推荐方案 A（轻量重构）**，理由如下：

1. **`Modules.Pipeline` 已提供完善的通用管线基础设施** — `IPipeline<TIn, TOut>`、`StagePipeline<TContext>`、`PipelineTryCatch`、`PipelineRetry`、`PipelineBranch`、`PipelineSwitch`、`ForEachPipeline`、`ParallelPipeline` 等组合子已覆盖所有编排需求
2. **`WorkflowExecution` 的核心价值在于执行引擎** — 会话管理（SessionManager）、状态机（ExecutionSession）、编排器（ExecutionOrchestrator）、热重载（HotReloadService）本身就是通用的
3. **轻量重构风险低** — 不改动现有代码结构，仅做命名抽象 + 类型别名兼容
4. **向后兼容** — 不影响 Operation 层引用代码，WorkFlowMethod 无需修改 using 语句
5. **`WorkflowDefinition` 和 `NodeDefinition` 已经是通用模型的超集** — 它们包含的 `ConditionExpression`、`LoopConfig`、`FalseChildren`、`RetryDelayMs` 等字段本身就是通用步骤定义的自然扩展

### 2.2 改造范围

```
┌─────────────────────────────────────────────────────────┐
│  ExecutionFramework（通用执行框架）                        │
│  位置: CsharpPrj/Modules/ExecutionFramework/              │
│  命名空间: ExecutionFramework                             │
│                                                          │
│  Abstractions/                                           │
│  ├── IExecutionContext.cs       ← IWorkflowContext       │
│  ├── IExecutionEngine.cs        ← IWorkflowEngine        │
│  ├── IExecutionLogger.cs        ← IWorkflowLogger        │
│  ├── IStepExecutor.cs           ← INodeExecutor          │
│  ├── IExecutionSession.cs       (不变)                   │
│  ├── IDataContext.cs            (不变)                   │
│  ├── IPipelineStage.cs          (不变)                   │
│  └── ISkeleton.cs               (不变)                   │
│                                                          │
│  Models/                                                 │
│  ├── ExecutionPlan.cs           ← 新增通用执行计划        │
│  ├── StepDefinition.cs          ← 新增通用步骤定义        │
│  └── ExecutionStatus.cs         (不变)                   │
│                                                          │
│  Engine/                                                 │
│  ├── ExecutionEngine.cs         ← WorkflowEngine         │
│  ├── StepExecutor.cs            ← NodeExecutor           │
│  ├── ExecutionOrchestrator.cs   (不变)                   │
│  ├── DefaultExecutionOrchestrator.cs (不变)              │
│  ├── SessionManager.cs          (不变)                   │
│  └── HotReloadService.cs        (不变)                   │
│                                                          │
│  Session/                                                │
│  ├── DataContext.cs             (不变)                   │
│  ├── ExecutionSession.cs        (不变)                   │
│  ├── StepExecutionState.cs      ← NodeExecutionState     │
│  └── SessionFactory.cs          (不变)                   │
│                                                          │
│  Pipeline/                                               │
│  ├── CentralPipeline.cs         (不变)                   │
│  ├── PipelineStages.cs          (不变)                   │
│  └── PipelineContext.cs         ← WorkflowContext        │
│                                                          │
│  Logging/                                                │
│  └── ExecutionLogger.cs         ← WorkflowLogger         │
│                                                          │
│  Extensions/                                             │
│  └── ExecutionExtensions.cs     ← WorkflowExtensions     │
└─────────────────────────────────────────────────────────┘
                        │ 继承
                        ▼
┌─────────────────────────────────────────────────────────┐
│  WorkflowExecution（工作流领域扩展）                       │
│  位置: CsharpPrj/Share/WorkflowExecution/（精简后）       │
│  命名空间: WorkflowExecution                             │
│                                                          │
│  Models/                                                 │
│  ├── WorkflowDefinition : ExecutionPlan                  │
│  └── NodeDefinition : StepDefinition                     │
│                                                          │
│  Skeletons/                                              │
│  ├── NodeProcessSkeleton                                 │
│  └── NodeChainSkeleton                                   │
└─────────────────────────────────────────────────────────┘
```

### 2.3 类型别名兼容方案

为了确保向后兼容，在 `ExecutionFramework` 中保留类型别名：

```csharp
// ExecutionFramework/TypeAliases.cs
namespace WorkflowExecution
{
    // 旧命名空间的类型别名，确保现有代码无需修改 using 语句
    using ExecutionFramework;

    /// <summary>向后兼容：IWorkflowEngine = IExecutionEngine</summary>
    public interface IWorkflowEngine : IExecutionEngine { }

    /// <summary>向后兼容：IWorkflowContext = IExecutionContext</summary>
    public interface IWorkflowContext : IExecutionContext { }

    /// <summary>向后兼容：IWorkflowLogger = IExecutionLogger</summary>
    public interface IWorkflowLogger : IExecutionLogger { }
}
```

---

## 三、具体改造步骤

### 第 1 步：创建 ExecutionFramework 项目

**新项目**：`CsharpPrj/Modules/ExecutionFramework/ExecutionFramework.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ExecutionFramework</RootNamespace>
    <AssemblyName>ExecutionFramework</AssemblyName>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\BasicMethod\BasicMethod.csproj" />
    <ProjectReference Include="..\..\BasicMethod\FunctionExecution\FunctionExecution.csproj" />
    <ProjectReference Include="..\..\Share\StateManagement\StateManagement.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="$(MicrosoftExtensionsLoggingAbstractionsVersion)" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.9" />
    <PackageReference Include="Polly" Version="$(PollyVersion)" />
    <PackageReference Include="Polly.Core" Version="$(PollyCoreVersion)" />
  </ItemGroup>

  <!--
    设计说明：ExecutionFramework 不直接引用 Modules.Pipeline。
    ExecutionFramework 中的 CentralPipeline 使用 IPipelineStage 接口（定义在 Abstractions 中），
    而非 Modules.Pipeline 的具体类型。Modules.Pipeline 的引用由 WorkflowExecution（Share/）负责，
    因为只有 WorkflowExecution 中的 PipelineContext 继承了 Modules.Pipeline.PipelineContext。
  -->
</Project>
```

### 第 2 步：迁移通用代码到 ExecutionFramework

将 `WorkflowExecution` 中所有与工作流无关的代码迁移到 `ExecutionFramework`，同时进行重命名：

| 源文件（Share/WorkflowExecution/） | 目标文件（Modules/ExecutionFramework/） | 变更 |
|-----------------------------------|----------------------------------------|------|
| `Abstractions/IWorkflowContext.cs` | `Abstractions/IExecutionContext.cs` | 接口名 + 注释 |
| `Abstractions/IWorkflowEngine.cs` | `Abstractions/IExecutionEngine.cs` | 接口名 + 注释 |
| `Abstractions/IWorkflowLogger.cs` | `Abstractions/IExecutionLogger.cs` | 接口名 + 注释 |
| `Abstractions/INodeExecutor.cs` | `Abstractions/IStepExecutor.cs` | 接口名 + 注释 |
| `Abstractions/IExecutionSession.cs` | 不变 | 仅更新命名空间 |
| `Abstractions/IDataContext.cs` | 不变 | 仅更新命名空间 |
| `Abstractions/IPipelineStage.cs` | 不变 | 仅更新命名空间 |
| `Abstractions/ISkeleton.cs` | 不变 | 仅更新命名空间 |
| `Abstractions/WorkflowContext.cs` | `Abstractions/ExecutionContext.cs` | 类名 + 注释 |
| `Models/ExecutionStatus.cs` | 不变 | 仅更新命名空间 |
| `Engine/WorkflowEngine.cs` | `Engine/ExecutionEngine.cs` | 类名 + 注释 |
| `Engine/NodeExecutor.cs` | `Engine/StepExecutor.cs` | 类名 + 注释 |
| `Engine/ExecutionOrchestrator.cs` | 不变 | 仅更新命名空间 |
| `Engine/DefaultExecutionOrchestrator.cs` | 不变 | 仅更新命名空间 |
| `Engine/SessionManager.cs` | 不变 | 仅更新命名空间 |
| `Engine/HotReloadService.cs` | 不变 | 仅更新命名空间 |
| `Session/DataContext.cs` | 不变 | 仅更新命名空间 |
| `Session/ExecutionSession.cs` | 不变 | 仅更新命名空间 |
| `Session/NodeExecutionState.cs` | `Session/StepExecutionState.cs` | 类名 + 注释 |
| `Session/SessionFactory.cs` | 不变 | 仅更新命名空间 |
| `Logging/WorkflowLogger.cs` | `Logging/ExecutionLogger.cs` | 类名 + 注释 |
| `Pipeline/CentralPipeline.cs` | 不变 | 仅更新命名空间 |
| `Pipeline/PipelineStages.cs` | 不变 | 仅更新命名空间 |
| `Pipeline/WorkflowContext.cs` | `Pipeline/PipelineContext.cs` | 类名 + 注释 |
| `Extensions/WorkflowExtensions.cs` | `Extensions/ExecutionExtensions.cs` | 类名 + 注释 |

### 第 3 步：新增通用模型

在 `ExecutionFramework` 中新增通用执行计划模型：

```csharp
// ExecutionFramework/Models/ExecutionPlan.cs
namespace ExecutionFramework.Models;

/// <summary>
/// 执行计划。
/// 描述一个可执行的计划，包含步骤列表、配置等信息。
/// 通用基类，工作流领域可继承此类扩展。
/// </summary>
public class ExecutionPlan
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = "1.0.0";
    public List<StepDefinition> Steps { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

// ExecutionFramework/Models/StepDefinition.cs
namespace ExecutionFramework.Models;

/// <summary>
/// 步骤定义。
/// 描述执行计划中的一个步骤，包含步骤类型、函数引用、参数等信息。
/// 通用基类，工作流领域可继承此类扩展。
/// </summary>
public class StepDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StepType { get; set; } = "Function";
    public string? FunctionName { get; set; }
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public List<StepDefinition> Children { get; set; } = new();
    public int TimeoutMs { get; set; } = 0;
    public int RetryCount { get; set; }
    public bool IsEnabled { get; set; } = true;
}
```

### 第 4 步：改造 WorkflowDefinition / NodeDefinition 为继承关系

```csharp
// WorkflowExecution/Models/WorkflowDefinition.cs
namespace WorkflowExecution.Models;

using ExecutionFramework.Models;

/// <summary>
/// 工作流定义。
/// 继承通用 ExecutionPlan，扩展工作流领域特定属性。
/// </summary>
public class WorkflowDefinition : ExecutionPlan
{
    // 兼容旧属性：Steps 的别名
    public List<NodeDefinition> Nodes
    {
        get => Steps.OfType<NodeDefinition>().ToList();
        set => Steps = value.Cast<StepDefinition>().ToList();
    }

    public List<NodeDefinition> PipelineNodes { get; set; } = new();
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();
    public Dictionary<string, string> Outputs { get; set; } = new();
    public WorkflowConfig? Config { get; set; }
    public List<string> Tags { get; set; } = new();
}

// WorkflowExecution/Models/NodeDefinition.cs
namespace WorkflowExecution.Models;

using ExecutionFramework.Models;

/// <summary>
/// 节点定义。
/// 继承通用 StepDefinition，扩展工作流领域特定属性。
/// </summary>
public class NodeDefinition : StepDefinition
{
    // 兼容旧属性
    public string NodeType
    {
        get => StepType;
        set => StepType = value;
    }

    public List<NodeDefinition> FalseChildren { get; set; } = new();
    public string? ConditionExpression { get; set; }
    public LoopConfig? LoopConfig { get; set; }
    public int RetryDelayMs { get; set; } = 1000;
    public string? Description { get; set; }
    public bool IsSkipped { get; set; }
}
```

### 第 5 步：更新引用关系

```
改造前：
  WorkFlowMethod → WorkflowExecution (Share/) → BasicMethod / Pipeline / StateManagement

改造后：
  WorkFlowMethod → WorkflowExecution (Share/) → ExecutionFramework (Modules/) → BasicMethod / Pipeline / StateManagement
                   (工作流领域扩展)                  (通用执行框架)
```

**Share.csproj 更新**：添加 `ExecutionFramework` 引用

```xml
<!-- Share.csproj -->
<ItemGroup>
  <ProjectReference Include="..\Modules\ExecutionFramework\ExecutionFramework.csproj" />
  <ProjectReference Include="WorkflowExecution\WorkflowExecution.csproj" />
  <!-- 其他引用不变 -->
</ItemGroup>
```

**WorkflowExecution.csproj 更新**：改为引用 ExecutionFramework

```xml
<!-- WorkflowExecution.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\Modules\ExecutionFramework\ExecutionFramework.csproj" />
</ItemGroup>
<!-- 移除对 BasicMethod / Pipeline / StateManagement 的直接引用 -->
```

**Modules.csproj 更新**：添加 ExecutionFramework 子项目引用

```xml
<!-- Modules.csproj -->
<ItemGroup>
  <ProjectReference Include="ExecutionFramework\ExecutionFramework.csproj" />
  <!-- 其他子项目引用不变 -->
</ItemGroup>
```

### 第 6 步：更新 WorkFlowMethod 中的 using 语句

搜索 `WorkFlowMethod` 项目中所有引用 `WorkflowExecution.Abstractions` 和 `WorkflowExecution.Models` 的 using 语句，更新为新的命名空间。

主要变更：
- `WorkflowExecution.Abstractions.IWorkflowEngine` → `ExecutionFramework.Abstractions.IExecutionEngine`
- `WorkflowExecution.Abstractions.IWorkflowContext` → `ExecutionFramework.Abstractions.IExecutionContext`
- `WorkflowExecution.Abstractions.IWorkflowLogger` → `ExecutionFramework.Abstractions.IExecutionLogger`
- `WorkflowExecution.Abstractions.INodeExecutor` → `ExecutionFramework.Abstractions.IStepExecutor`
- `WorkflowExecution.Models.WorkflowDefinition` → 不变（仍在 WorkflowExecution.Models）
- `WorkflowExecution.Models.NodeDefinition` → 不变（仍在 WorkflowExecution.Models）

---

## 四、与 Modules.Pipeline 的关系

`Modules.Pipeline` 已经是一个**非常完善的通用管线框架**，提供了：

| 组件 | 功能 |
|------|------|
| `IPipeline<TIn, TOut>` | 链式传参管线 |
| `IPipelineStage<TContext>` | 通用阶段接口（带条件执行） |
| `IStagePipeline<TContext>` | 通用阶段编排器（带动态阶段管理） |
| `PipelineContext` | 通用管线上下文（三体系共用） |
| `StagePipeline<TContext>` | 阶段编排器实现 |
| `PipelineTryCatch` / `PipelineRetry` / `PipelineBranch` / `PipelineSwitch` | 组合子 |
| `ForEachPipeline` / `ParallelPipeline` | 集合遍历/并行执行 |
| `ErrorCollector` / `PipelineResult` | 错误收集/结果包装 |
| `StrategyRegistry` / `PipelineStageRegistry` | 策略/阶段注册表 |

**`ExecutionFramework` 与 `Modules.Pipeline` 的关系**：

```
Modules.Pipeline = 管线编排基础设施（低层级，关注"如何编排步骤"）
ExecutionFramework = 执行引擎框架（高层级，关注"如何管理执行生命周期"）
```

`ExecutionFramework` 在 `Modules.Pipeline` 之上构建，提供：
- **会话管理**（SessionManager）— 会话注册/查询/取消/暂停/恢复
- **执行状态机**（ExecutionSession）— 基于 ConditionalStateMachine 的线程安全状态管理
- **热重载**（HotReloadService）— 运行中切换执行计划
- **执行编排器**（ExecutionOrchestrator）— 拓扑遍历执行骨架
- **执行计划模型**（ExecutionPlan / StepDefinition）— 通用执行计划描述
- **弹性执行**（StepExecutor）— 基于 Polly 的重试/超时策略

---

## 五、影响评估

| 维度 | 评估 |
|------|------|
| **代码改动量** | ~800 行（新增 ExecutionFramework 项目 + 重命名 + 类型别名） |
| **向后兼容** | ✅ 完全兼容（类型别名 + 继承关系确保旧代码可编译） |
| **通用性** | ✅ 彻底通用（ExecutionFramework 无任何工作流依赖） |
| **与 Modules.Pipeline 关系** | 明确分层（ExecutionFramework 依赖 Pipeline，而非替代） |
| **对 Operation 层影响** | ⚠️ 需更新 using 语句（接口名变更） |
| **可扩展性** | 高（可添加 Batch/Stream/Event 执行器） |
| **测试影响** | 无（现有测试用例无需修改） |

### 5.1 需要更新的文件清单

| 文件 | 变更类型 |
|------|---------|
| `CsharpPrj/Modules/ExecutionFramework/`（新目录） | 新增 ~24 个文件 |
| `CsharpPrj/Share/WorkflowExecution/` | 保留 6 个文件，删除 18 个文件 |
| `CsharpPrj/Share/Share.csproj` | 添加 ExecutionFramework 引用 |
| `CsharpPrj/Modules/Modules.csproj` | 添加 ExecutionFramework 子项目引用 |
| `CsharpPrj/Share/WorkflowExecution/WorkflowExecution.csproj` | 改为引用 ExecutionFramework |
| `CsharpPrj/Operation/WorkFlowMethod/` | 更新 using 语句 |

### 5.2 接口变更映射

| 旧接口 | 新接口 | 兼容方式 |
|-------|--------|---------|
| `IWorkflowEngine` | `IExecutionEngine` | 类型别名 |
| `IWorkflowContext` | `IExecutionContext` | 类型别名 |
| `IWorkflowLogger` | `IExecutionLogger` | 类型别名 |
| `INodeExecutor` | `IStepExecutor` | 类型别名 |
| `WorkflowEngine` | `ExecutionEngine` | 直接替换 |
| `WorkflowLogger` | `ExecutionLogger` | 直接替换 |
| `WorkflowContext` | `ExecutionContext` | 直接替换 |
| `NodeExecutor` | `StepExecutor` | 直接替换 |
| `NodeExecutionState` | `StepExecutionState` | 直接替换 |
| `WorkflowDefinition` | 不变（继承 ExecutionPlan） | 继承 |
| `NodeDefinition` | 不变（继承 StepDefinition） | 继承 |

---

## 六、实施路线图

### 阶段 1：创建 ExecutionFramework 项目（1 天）

1. 创建 `CsharpPrj/Modules/ExecutionFramework/` 目录结构
2. 创建 `ExecutionFramework.csproj`
3. 从 `WorkflowExecution` 复制通用代码到 `ExecutionFramework`
4. 重命名接口和类（IWorkflowEngine → IExecutionEngine 等）
5. 新增 `ExecutionPlan` 和 `StepDefinition` 通用模型
6. 新增 `TypeAliases.cs` 提供向后兼容

### 阶段 2：改造 WorkflowExecution（0.5 天）

1. 修改 `WorkflowDefinition` 继承 `ExecutionPlan`
2. 修改 `NodeDefinition` 继承 `StepDefinition`
3. 更新 `WorkflowExecution.csproj` 引用 `ExecutionFramework`
4. 删除已迁移到 ExecutionFramework 的文件

### 阶段 3：更新引用和编译（0.5 天）

1. 更新 `Share.csproj` 添加 `ExecutionFramework` 引用
2. 更新 `Modules.csproj` 添加 `ExecutionFramework` 子项目引用
3. 更新 `WorkFlowMethod` 中的 using 语句
4. 编译验证，修复编译错误

### 阶段 4：测试验证（0.5 天）

1. 运行现有测试用例
2. 验证向后兼容性
3. 验证 WorkflowDefinition/NodeDefinition 继承关系

---

## 七、后续演进方向

轻量重构完成后，可根据需要逐步演进：

1. **BatchExecutor** — 批量步骤执行器，支持并行批量处理
2. **StreamExecutor** — 流式步骤执行器，支持数据流处理
3. **EventExecutor** — 事件驱动步骤执行器，支持异步事件处理
4. **DistributedExecutor** — 分布式执行器，支持跨进程/跨机器执行
5. **VisualExecutor** — 可视化执行器，支持执行过程可视化

这些扩展都可以基于 `ExecutionFramework` 的通用接口实现，无需修改核心框架。
