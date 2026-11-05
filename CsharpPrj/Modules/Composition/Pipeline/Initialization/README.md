# Modules.Pipeline.Initialization — 通用初始化编排器框架

## 概述

`Initialization` 是从 `Share.EventBindingOrchestrator.Init` 提取的**通用初始化编排器框架**，封装了应用初始化全流程的编排逻辑，包括阶段注册、依赖注入、服务创建和脚本执行。

该框架采用**策略模式**设计，通过三个策略接口（`IStageConfigProvider`、`IDependencyInjector<TContext>`、`IScriptExecutor<TContext>`）将核心编排逻辑与具体实现解耦，适用于 GUI、Console、Service 等任意初始化场景。

---

## 设计理念

### 解决的问题

在 `EventBindingOrchestrator` 中，`InitializationOrchestrator<TContext>` 负责 WinForms 应用的初始化编排。但该逻辑本质上是**跨领域通用**的——任何应用（GUI、CLI、后台服务）都需要：

1. 创建上下文对象
2. 注册初始化阶段
3. 注入依赖服务
4. 等待前置条件就绪
5. 执行主逻辑

### 架构模式

```
┌─────────────────────────────────────────────────────────┐
│              InitializationOrchestrator<TContext>        │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐  │
│  │ StageConfig  │  │ Dependency   │  │ ScriptExecutor │  │
│  │ Provider     │  │ Injector     │  │                │  │
│  │ (策略)       │  │ (策略)       │  │ (策略)         │  │
│  └──────┬───────┘  └──────┬───────┘  └───────┬────────┘  │
│         │                 │                  │           │
│         ▼                 ▼                  ▼           │
│  ┌──────────────────────────────────────────────────┐   │
│  │              IStageCoordinator                    │   │
│  │  (阶段编排器 — 注册/等待/完成阶段)                │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

### 与 Modules.Pipeline 的关系

| 体系 | 定位 | 适用场景 |
|------|------|----------|
| **体系 A**（链式传参） | `IPipeline<TIn, TOut>` | 数据流变换 |
| **体系 B**（共享配置） | `IStage<TTarget, TConfig, TContext>` | 渲染/处理会话 |
| **体系 C**（编排器） | `IPipelineStage<TContext>` + `StagePipeline` | 多阶段处理流程 |
| **Initialization**（初始化编排） | `InitializationOrchestrator<TContext>` | **应用初始化生命周期** |

`Initialization` 与体系 C 互补：体系 C 关注**数据处理管线**（阶段链式执行），`Initialization` 关注**初始化生命周期编排**（阶段注册→等待→完成）。

---

## 核心组件

### 1. IInitializationOrchestrator\<TContext\>

初始化编排器接口，定义两个核心方法：

```csharp
public interface IInitializationOrchestrator<TContext>
{
    // 创建并配置上下文对象（阶段编排 + DI 注入 + 服务创建）
    Task<TContext> CreateAndConfigureAsync(
        string configPath,
        CancellationToken cancellationToken = default);

    // 执行命令脚本（等待前置阶段 + 脚本执行）
    Task<bool> ExecuteScriptAsync(
        TContext context,
        string commandFile,
        CancellationToken cancellationToken = default);

    // 获取阶段编排器
    IStageCoordinator? StageCoordinator { get; }

    // 获取事件执行服务
    IEventExecutionService? EventExecutionService { get; }
}
```

### 2. InitializationOrchestrator\<TContext\>

通用实现，提供完整的初始化流程编排：

```
CreateAndConfigureAsync:
  Step 1: CreateStageCoordinatorAsync    — 创建阶段编排器
  Step 2: RegisterStagesAsync            — 注册初始化阶段
  Step 3: CreateContextAsync             — 创建上下文对象
  Step 4: InjectDependenciesAsync        — 注入依赖到上下文
  Step 5: CreateAndInjectEventBinderAsync — 创建事件执行服务
  Step 6: StartStageCoordinatorAsync     — 启动阶段编排

ExecuteScriptAsync:
  Step 1: WaitForPrerequisiteStagesAsync — 等待前置阶段完成
  Step 2: ExecuteScriptInternalAsync     — 执行命令脚本
  Step 3: CompleteScriptExecutionStageAsync — 完成脚本执行阶段
```

所有步骤均为 `protected virtual` 方法，子类可重写以自定义行为。

### 3. 策略接口

#### IStageConfigProvider

```csharp
public interface IStageConfigProvider
{
    IReadOnlyList<StageDefinition> GetStages();
    IReadOnlyList<string> GetPrerequisiteStageIds();
    string? GetScriptExecutionStageId();
}
```

#### IDependencyInjector\<TContext\>

```csharp
public interface IDependencyInjector<TContext>
{
    Task InjectDependenciesAsync(
        TContext context,
        IStageCoordinator? coordinator,
        IServiceProvider? services,
        CancellationToken cancellationToken);
}
```

#### IScriptExecutor\<TContext\>

```csharp
public interface IScriptExecutor<TContext>
{
    Task<bool> ExecuteScriptAsync(
        TContext context,
        string commandFile,
        CancellationToken cancellationToken);
}
```

### 4. StageDefinition

```csharp
public record StageDefinition(
    string StageId,
    string Description,
    int TimeoutMs = 30000);
```

### 5. StringStageCoordinator

`IStageCoordinator` 的完整实现，使用 `string stageId` 管理阶段编排：

- 基于 `IStepCoordinator` 构建，线程安全
- 支持超时保护和状态追踪
- 自动推进阶段（完成当前阶段后自动启动下一个）
- 阶段失败时自动标记后续所有阶段为 Failed
- 提供 `StageStateChanged` 事件通知

### 6. PipelineHelper

```csharp
public static class PipelineHelper
{
    public static async Task WaitForStagesAsync(
        IStageCoordinator stageCoordinator,
        IReadOnlyList<string> stageIds,
        ILogger logger,
        CancellationToken cancellationToken = default);
}
```

---

## 使用方式

### 基本用法

```csharp
// 1. 创建编排器
var orchestrator = new InitializationOrchestrator<MyContext>(
    serviceProvider,
    stageConfigProvider,    // 提供阶段定义
    dependencyInjector,     // 提供依赖注入逻辑
    scriptExecutor,         // 提供脚本执行逻辑
    logger);

// 2. 创建并配置上下文
var context = await orchestrator.CreateAndConfigureAsync(configPath);

// 3. 执行命令脚本
var success = await orchestrator.ExecuteScriptAsync(context, commandFile);
```

### 自定义子类

```csharp
public class MyInitializer : InitializationOrchestrator<MyContext>
{
    protected override async Task<MyContext> CreateContextAsync(
        string configPath, CancellationToken cancellationToken)
    {
        // 自定义上下文创建逻辑
        return new MyContext { ConfigPath = configPath };
    }

    protected override async Task<IStageCoordinator> CreateStageCoordinatorAsync(
        CancellationToken cancellationToken)
    {
        // 使用 StringStageCoordinator
        return new StringStageCoordinator();
    }
}
```

### 策略实现示例

```csharp
// 阶段配置
public class MyStageConfig : IStageConfigProvider
{
    public IReadOnlyList<StageDefinition> GetStages() => new[]
    {
        new StageDefinition("Init", "初始化", 10000),
        new StageDefinition("Ready", "就绪", 30000),
    };

    public IReadOnlyList<string> GetPrerequisiteStageIds() => new[] { "Init" };
    public string? GetScriptExecutionStageId() => "Ready";
}

// 依赖注入
public class MyDependencyInjector : IDependencyInjector<MyContext>
{
    public Task InjectDependenciesAsync(
        MyContext context,
        IStageCoordinator? coordinator,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        context.StageCoordinator = coordinator;
        return Task.CompletedTask;
    }
}

// 脚本执行
public class MyScriptExecutor : IScriptExecutor<MyContext>
{
    public async Task<bool> ExecuteScriptAsync(
        MyContext context,
        string commandFile,
        CancellationToken cancellationToken)
    {
        // 执行命令脚本
        return true;
    }
}
```

---

## 迁移说明

### 从 EventBindingOrchestrator.Init 迁移

| 原位置 | 新位置 |
|--------|--------|
| `Share.EventBindingOrchestrator.Init.IInitializationOrchestrator` | `Modules.Pipeline.Initialization.IInitializationOrchestrator` |
| `Share.EventBindingOrchestrator.Init.InitializationOrchestrator` | `Modules.Pipeline.Initialization.InitializationOrchestrator` |
| `Share.EventBindingOrchestrator.Init.IStageConfigProvider` | `Modules.Pipeline.Initialization.IStageConfigProvider` |
| `Share.EventBindingOrchestrator.Init.IDependencyInjector` | `Modules.Pipeline.Initialization.IDependencyInjector` |
| `Share.EventBindingOrchestrator.Init.IScriptExecutor` | `Modules.Pipeline.Initialization.IScriptExecutor` |
| `Share.EventBindingOrchestrator.Init.StageDefinition` | `Modules.Pipeline.Initialization.StageDefinition` |
| `Share.EventBindingOrchestrator.Init.StringStageCoordinator` | `Modules.Pipeline.Initialization.StringStageCoordinator` |
| `Share.EventBindingOrchestrator.Abstractions.PipelineHelper` | `Modules.Pipeline.Initialization.PipelineHelper` |

### 变更内容

1. **命名空间**：`Share.EventBindingOrchestrator.Init` → `Modules.Pipeline.Initialization`
2. **IStageCoordinator 引用**：通过 global using 指向 `TConfig.IStageCoordinator`
3. **IEventExecutionService 引用**：保留但标记为可选（通过 `IServiceProvider` 解析）
4. **EventBindingDiagnosticContext 引用**：从 `StringStageCoordinator` 中移除（诊断记录保留在 EventBindingOrchestrator 中）

---

## 依赖关系

```
Pipeline.csproj
├── BasicMethod.csproj（IStepCoordinator、ILogger 等基础设施）
└── TConfig.csproji（IStageCoordinator 接口定义，通过 global using 引用）
```

---

## 扩展点

| 扩展点 | 方法 | 说明 |
|--------|------|------|
| 阶段编排器创建 | `CreateStageCoordinatorAsync` | 默认从 DI 容器解析，可重写为 `StringStageCoordinator` |
| 阶段注册 | `RegisterStagesAsync` | 从 `IStageConfigProvider` 获取阶段定义 |
| 上下文创建 | `CreateContextAsync` | 默认从 DI 容器解析，子类必须重写 |
| 依赖注入 | `InjectDependenciesAsync` | 委托给 `IDependencyInjector` 策略 |
| 事件服务创建 | `CreateAndInjectEventBinderAsync` | 创建 `IEventExecutionService` 并完成阶段 |
| 脚本执行 | `ExecuteScriptInternalAsync` | 委托给 `IScriptExecutor` 策略 |
| 阶段等待 | `WaitForPrerequisiteStagesAsync` | 委托给 `PipelineHelper.WaitForStagesAsync` |
