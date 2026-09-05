# FileDriven.Execution、FunctionStorage、CommandFramework 三者关系综合分析报告

> 分析日期：2026-06-12（第3版）
> 分析范围：`FileDriven/FileDriven.Execution`、`Operation/FunctionStorage`、`Share/CommandFramework`
> 最新更新：2026-06-12 — 反映消除适配器层后的架构状态

---

## 一、架构总览

### 1.1 三个项目的定位

| 项目 | 路径 | 定位 | 职责 |
|------|------|------|------|
| **CommandFramework** | `Share/CommandFramework` | 通用命令框架（底层基础设施） | 命令解析、管线编排、引擎执行、非事件命令路由委托 |
| **FunctionStorage** | `Operation/FunctionStorage` | 函数存储与注册中心（中间层） | 函数注册表、非事件命令注册表、执行上下文抽象 |
| **FileDriven.Execution** | `FileDriven/FileDriven.Execution` | FileDriven 执行引擎（业务层） | 管线构建、命令处理实现、脚本加载、UI 交互 |

### 1.2 依赖关系

```
FileDriven.Execution
    ↓ 依赖
FunctionStorage
    ↓ 依赖
CommandFramework
```

- **CommandFramework** 是最底层的基础设施，不依赖其他两个项目
- **FunctionStorage** 依赖 CommandFramework（使用其 `Share.Registry` 和 `CommandFramework.Abstractions`）
- **FileDriven.Execution** 同时依赖 FunctionStorage 和 CommandFramework

### 1.3 核心数据流（消除适配器后）

```
命令字符串
    ↓
[CommandParser] (CommandFramework)
    ↓ ParsedCommand
[ParseCommandStage] (CommandFramework Pipeline)
    ↓
┌─ 非事件命令? ─→ [findNonEventHandler 委托] (FileDriven.Execution 注入)
│                      ↓
│                 [FunctionStorageRegistry.NonEventRegistry]
│                      ↓
│                 [注册的处理函数] (如 HandleSideload)
│
└─ 事件命令? ──→ [FindBindingStage] → [ExecuteFunctionStage]
```

**关键变化：**
1. `ParseCommandStage` 不再持有 `NonEventCommandHandler` 实例，通过外部注入的 `findNonEventHandler` 委托查找并执行非事件命令
2. `CommandContext` 已直接实现 `IExecutionContext` 接口，消除了 `CommandContextAdapter` 适配器层
3. `FileDrivenPipelineContext`（继承 `CommandContext`）可直接作为 `IExecutionContext` 传入，消除了 `FunctionExecutionContextAdapter`

---

## 二、关键组件关系详解

### 2.1 非事件命令处理（核心关注点）

#### 2.1.1 非事件命令处理组件现状

| 组件 | 所在项目 | 状态 | 说明 |
|------|----------|------|------|
| `CommandFramework.Execution.NonEventCommandHandler` | CommandFramework | ✅ 已废弃 `[Obsolete]` | 旧版处理器，不再使用 |
| `FunctionStorage.Core.NonEventCommandHandler` | FunctionStorage | ✅ 保留 | 统一分发入口，通过 `INonEventCommandRegistry` 查找注册的处理函数执行 |
| `FileDriven.Execution.Command.Skeletons.NonEventCommandHandler` | FileDriven.Execution | ✅ 保留 | 业务命令实现（HandleLoad/HandleEcho 等） |
| `ParseCommandStage` 中的 `findNonEventHandler` 委托 | CommandFramework | ✅ 新方案 | 外部注入的查找委托，替代旧版 `NonEventCommandHandler` |

**调用链（消除适配器后）：**
```
ParseCommandStage.ExecuteCoreAsync()
    → _findNonEventHandler(action, context)  // 外部注入的委托
        → FunctionStorageRegistry.NonEventRegistry.TryGetHandler(action, out handler)
        → handler(context)  // context 已实现 IExecutionContext，无需适配器
            → 实际调用 PipelineFactory 中注册的 Wrap(HandleXxx)
```

#### 2.1.2 两个 INonEventCommandRegistry 接口

| 接口 | 所在项目 | 处理函数签名 | 状态 |
|------|----------|-------------|------|
| `CommandFramework.Abstractions.INonEventCommandRegistry` | CommandFramework | `Action<object?>` | ✅ 已废弃 `[Obsolete]` |
| `FunctionStorage.Abstractions.INonEventCommandRegistry` | FunctionStorage | `Action<IExecutionContext>` | ✅ 实际使用 |

**现状：** `CommandFramework.Abstractions` 版本已标记 `[Obsolete]`，统一使用 `FunctionStorage.Abstractions` 版本。

#### 2.1.3 两个 CommandCriticality 枚举

| 枚举 | 所在文件 | 状态 |
|------|----------|------|
| `CommandFramework.Abstractions.CommandCriticality` | `Share/CommandFramework/Abstractions/INonEventCommandRegistry.cs` | ✅ 已废弃 `[Obsolete]` |
| `FunctionStorage.Abstractions.CommandCriticality` | `Operation/FunctionStorage/Abstractions.cs` | ✅ 实际使用 |

**现状：** `CommandFramework.Abstractions` 版本已标记 `[Obsolete]`，统一使用 `FunctionStorage.Abstractions` 版本。

### 2.2 命令注册机制

#### 2.2.1 PipelineFactory.BuildCommandPipeline()（注册入口）

位于 `FileDriven.Execution`，是内置命令的注册入口。在 `BuildCommandPipeline()` 中构造 `findNonEventHandler` 委托：

```csharp
Func<string, CommandFramework.Models.CommandContext, Task<bool>>? findNonEventHandler = (action, context) =>
{
    var registry = FunctionStorageRegistry.NonEventRegistry;
    if (!registry.TryGetHandler(action, out var handler) || handler == null)
        return Task.FromResult(false);

    var sw = System.Diagnostics.Stopwatch.StartNew();
    handler(context);  // context 已实现 IExecutionContext，直接传入
    sw.Stop();
    context.FunctionExecutionTime = sw.Elapsed;
    context.FunctionResultValue = context.FunctionResultValue ?? "OK";
    return Task.FromResult(true);
};
```

**关键设计：** `CommandContext` 已直接实现 `IExecutionContext` 接口，无需适配器即可直接传入 `NonEventRegistry` 的处理函数。

#### 2.2.2 FunctionStorageRegistry.NonEventRegistry（注册中心）

位于 `FunctionStorage`，是唯一的非事件命令注册中心：

```csharp
public static AbstractionsINonEventCommandRegistry NonEventRegistry { get; }
// 实际类型: FunctionStorage.Core.NonEventCommandRegistry
// 接口类型: FunctionStorage.Abstractions.INonEventCommandRegistry
```

#### 2.2.3 NonEventCommandSync（同步机制）

位于 `CommandFramework`，负责将 `NonEventRegistry` 中新注册的命令同步到 `CommandParser.DefaultNonEventCommands`：

```csharp
// FunctionStorageRegistry.InitializeNonEventRegistry() 中订阅
CommandFramework.Sync.NonEventCommandSync.SyncFromNonEventRegistry(
    subscribe: action => registry.OnCommandRegistered += action);
```

当 `NonEventRegistry` 有新命令注册时，自动添加到 `CommandParser.DefaultNonEventCommands` 集合，确保两套命令列表保持一致。

### 2.3 上下文适配（已消除）

#### CommandContext 直接实现 IExecutionContext

`CommandContext` 已直接实现 `IExecutionContext` 接口，`CommandContextAdapter` 和 `FunctionExecutionContextAdapter` 已删除。

**实现方式：** `CommandContext` 类声明 `: IExecutionContext`，直接提供 `IExecutionContext` 接口所需的全部成员。

**适配映射（已内联到 CommandContext）：**
| IExecutionContext | CommandContext |
|-------------------|---------------|
| CurrentCommand | CurrentCommand（直接属性） |
| ParsedCommand | ParsedCommand（直接属性） |
| BoundFunctionName | BoundFunctionName（直接属性） |
| ExtractedValues | ExtractedValues（直接属性） |
| FunctionResultValue | FunctionResultValue（直接属性） |
| FunctionExecuteSuccess | FunctionExecuteSuccess（直接属性） |
| FunctionErrorMessage | FunctionErrorMessage（直接属性） |
| SharedData | SharedData（直接属性） |

---

## 三、重复与冗余分析

### 3.1 已消除的重复

| 重复项 | 状态 | 说明 |
|--------|------|------|
| `FileDriven.Execution.NonEventCommands` 命令列表 | ✅ 已消除 | 委托给 `FunctionStorage.Libraries.NonEventCommands` |
| `FunctionStorage.Core.DefaultCommandStrategies` | ✅ 已消除 | 纯逻辑命令由 FileDriven.Execution 统一注册 |
| `StagePipelineSkeleton` 适配器 | ✅ 已消除 | 改用 `DelegatePipeline` 包装 |
| `CommandFrameworkUsage.GetOrCreatePipeline` | ✅ 已消除 | 合并到 `CreateDefaultPipeline` |
| `CommandFrameworkHost.NonEventHandler` 属性 | ✅ 已消除 | 方案C移除，不再需要 |
| `CommandFramework.Execution.NonEventCommandHandler` 的独立注册表 | ✅ 已消除 | 方案C：`ParseCommandStage` 改用 `findNonEventHandler` 委托，不再使用旧版处理器 |
| `FileDrivenExecutor` 中旧版 `RegisterServices()` 补丁代码 | ✅ 已消除 | 方案C移除两处旧版补丁代码 |

### 3.2 已改造的组件（保留但重构）

| 组件 | 状态 | 说明 |
|------|------|------|
| `CommandFrameworkHost.RegisterServices()` | 🔄 已改造 | 保留作为外观方法，委托给 `PipelineInstance.RegisterServices()`，用于注册 `IConfigLoader` 等通用服务 |
| `CommandFrameworkHost.LoadConfig()` | 🔄 已改造 | 保留作为外观方法，委托给 `PipelineInstance.LoadConfig()`，用于加载 UI 配置 |
| `PipelineEntryInstance.RegisterServices()` | 🔄 已改造 | 保留，存储 `IConfigLoader`/`IEventBindingResolver` 等通用服务到本地字段 |
| `PipelineEntryInstance.LoadConfig()` | 🔄 已改造 | 保留，委托给已注册的 `IConfigLoader` 实现 |
| `PipelineEntryInstance.FindBinding()` | 🔄 已改造 | 保留，委托给已注册的 `IEventBindingResolver` 实现 |
| `FileDrivenExecutor` 中的 `RegisterServices()` 调用 | 🔄 已改造 | 保留，用于将 `FileDriven.Pipeline.PipelineEntry`（实现 `IConfigLoader`）注册到 `CommandFrameworkUsage`，供 `sideload` 命令的 `HandleSideload` 使用 |

**说明：** 方案C的设计文档中曾计划移除 `RegisterServices()`/`LoadConfig()` 等方法，但实际重构后发现这些方法有新的用途——作为通用服务注册机制，通过 `IConfigLoader` 接口将 `FileDriven.Pipeline.PipelineEntry` 注册到 `PipelineEntryInstance`。`sideload` 命令的 `HandleSideload` 需要通过 `IConfigLoader` 加载 UI 配置文件，因此这些方法被保留并改造，而非移除。


### 3.2 已废弃的重复（标记 `[Obsolete]`）

| 重复项 | 状态 | 说明 |
|--------|------|------|
| `CommandCriticality` 枚举（两个版本） | ✅ 已废弃 | `CommandFramework.Abstractions` 版本标记 `[Obsolete]`，请使用 `FunctionStorage.Abstractions.CommandCriticality` |
| `INonEventCommandRegistry` 接口（两个版本） | ✅ 已废弃 | `CommandFramework.Abstractions` 版本标记 `[Obsolete]`，请使用 `FunctionStorage.Abstractions.INonEventCommandRegistry` |
| `CommandFramework.Execution.NonEventCommandHandler` | ✅ 已废弃 | 类本身标记 `[Obsolete]`，请使用 `FunctionStorage.Core.NonEventCommandHandler` |
| `CommandParser.ParseAndRouteAsync()` | ✅ 已废弃 | 标记 `[Obsolete]`，请使用 `ParseCommandStage`（通过 Pipeline） |

### 3.3 待处理的重复

| 重复项 | 优先级 | 说明 |
|--------|--------|------|
| `NonEventCommands` 常量类（两个版本） | P3 | `FunctionStorage.Libraries` 和 `FileDriven.Execution` 各一份，后者已委托给前者 |
| `Sideload` 命令的异步化不完整 | P2 | ✅ 已修复（2026-06-12）：`HandleSideloadGuiMode` 中的 `GetAwaiter().GetResult()` 改为 `Task.Run` 包装 |

### 3.4 已修复的设计问题

| 问题 | 状态 | 说明 |
|------|------|------|
| `sideload` 命令路由断裂 | ✅ 已修复 | 方案C：`ParseCommandStage` 改用 `findNonEventHandler` 委托，从 `FunctionStorageRegistry.NonEventRegistry` 查找命令 |
| `CommandFrameworkUsage.Reset()` 未同步 `FunctionStorageRegistry` | ✅ 已修复 | `Shutdown()` 和 `Clear()` 中同步清空 `FunctionStorageRegistry` |
| `CommandParser.DefaultNonEventCommands` 与 `NonEventRegistry` 不同步 | ✅ 已修复 | `NonEventCommandSync` 订阅 `OnCommandRegistered` 事件自动同步 |
| `FunctionStorageRegistry` 中 `NonEventRegistry` 初始化时未同步到 `CommandParser` | ✅ 已修复 | `InitializeNonEventRegistry()` 中调用 `SyncFromNonEventRegistry()` |

---

## 四、整合建议

### 4.1 已完成的重构（方案C）

1. **消除 `sideload` 命令路由断裂** ✅
   - `ParseCommandStage` 移除 `NonEventCommandHandler` 依赖，改用 `findNonEventHandler` 委托
   - `PipelineEntry` 工厂方法签名改用 `findNonEventHandler` 参数
   - `CommandFrameworkHost` 移除 `NonEventHandler` 属性
   - `PipelineFactory` 构造查找委托，从 `FunctionStorageRegistry.NonEventRegistry` 查找命令
   - `CommandFrameworkHost.RegisterServices()`/`LoadConfig()` 和 `PipelineEntryInstance.RegisterServices()`/`LoadConfig()` 保留并改造为通用服务注册机制，通过 `IConfigLoader` 接口将 `FileDriven.Pipeline.PipelineEntry` 注册到 `PipelineEntryInstance`


2. **统一非事件命令判断逻辑** ✅
   - `NonEventCommandSync.SyncFromNonEventRegistry()` 实现同步
   - `FunctionStorageRegistry.InitializeNonEventRegistry()` 中订阅 `OnCommandRegistered` 事件
   - 当 `NonEventRegistry` 有新命令注册时，自动同步到 `CommandParser.DefaultNonEventCommands`

3. **废弃重复接口和枚举** ✅
   - `CommandFramework.Abstractions.CommandCriticality` → `[Obsolete]`
   - `CommandFramework.Abstractions.INonEventCommandRegistry` → `[Obsolete]`
   - `CommandFramework.Execution.NonEventCommandHandler` → `[Obsolete]`

### 4.2 已完成的重构（消除适配器层）

1. **合并 `IExecutionContext` 和 `CommandContext`** ✅
   - `CommandContext` 已直接实现 `IExecutionContext` 接口
   - `CommandContextAdapter` 适配器层已删除
   - `FunctionExecutionContextAdapter` 适配器层已删除
   - `PipelineFactory` 中 `findNonEventHandler` 委托直接传入 `CommandContext` 作为 `IExecutionContext`

### 4.3 待完成的重构（P2）

1. **统一命令注册入口**（已评估：当前架构已合理，跳过）
   - `FunctionStorageRegistry` 是非事件命令的统一注册中心
   - `CommandEngine` 是事件命令（set/get/click 等）的注册和执行引擎，需要撤销/重做支持
   - 两者职责明确且语义不同，强行合并会破坏功能

2. **异步化 Sideload** ✅
   - `HandleSideloadGuiMode` 中的 `GetAwaiter().GetResult()` 改为 `Task.Run` 包装
   - 避免 UI 线程阻塞，采用同步外壳 + 异步内核的折中方案
   - 由于 `NonEventCommandRegistry` 的 `Action<IExecutionContext>` 签名要求同步，无法直接改为 async

---

## 五、架构图（重构后）

```
┌─────────────────────────────────────────────────────────────────┐
│                    FileDriven.Execution                         │
│                                                                 │
│  ┌──────────────────────┐                                       │
│  │ PipelineFactory      │                                       │
│  │  - BuildCommand      │                                       │
│  │    Pipeline()        │                                       │
│  │  - findNonEvent      │                                       │
│  │    Handler 委托      │                                       │
│  └──────────┬───────────┘                                       │
│             │ 注入委托                                           │
│             │              ┌────────────────────────────────┐   │
│             └──────────────┤ FunctionStorageRegistry        │   │
│                            │  - NonEventRegistry (注册中心) │   │
│                            │  - RegisterNonEventHandler()   │   │
│                            │  - ValidateNonEventCommands()  │   │
│                            └────────────────────────────────┘   │
│                                                                 │
│  CommandContext : IExecutionContext（直接实现，无需适配器）      │
│  FileDrivenPipelineContext : CommandContext（继承）              │
└─────────────────────────────────────────────────────────────────┘
         │ 依赖                              │ 依赖
         ▼                                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                      FunctionStorage                            │
│                                                                 │
│  ┌──────────────────────┐  ┌────────────────────────────────┐   │
│  │ Abstractions         │  │ Core                           │   │
│  │  - IExecutionContext │  │  - NonEventCommandHandler      │   │
│  │  - INonEventCommand  │  │    .Handle() 分发入口          │   │
│  │    Registry           │  │  - NonEventCommandRegistry     │   │
│  │  - CommandCriticality │  │    (含 OnCommandRegistered)    │   │
│  │  - IFunctionExecutor  │  │  - FunctionRegistry            │   │
│  └──────────────────────┘  │  - FunctionExecutor            │   │
│                            └────────────────────────────────┘   │
│  ┌──────────────────────┐  ┌────────────────────────────────┐   │
│  │ Libraries            │  │ Registrations                  │   │
│  │  - NonEventCommands  │  │  - UIToolbox (注册业务命令)    │   │
│  │    (命令名称常量)    │  │  - MathLibrary                 │   │
│  └──────────────────────┘  │  - WorkflowLibrary             │   │
│                            └────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
         │ 依赖                              │ 依赖
         ▼                                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                     CommandFramework                            │
│                                                                 │
│  ┌──────────────────────┐  ┌────────────────────────────────┐   │
│  │ Parsing              │  │ Pipeline                       │   │
│  │  - CommandParser     │  │  - PipelineEntry               │   │
│  │    (DefaultNonEvent  │  │    (findNonEventHandler 委托)  │   │
│  │     Commands)        │  │  - ParseCommandStage           │   │
│  │  - ParsedCommand     │  │    (findNonEventHandler 委托)  │   │
│  │  - JsonCommand       │  │  - FindBindingStage            │   │
│  └──────────────────────┘  │  - ExecuteFunctionStage        │   │
│                            └────────────────────────────────┘   │
│  ┌──────────────────────┐  ┌────────────────────────────────┐   │
│  │ Sync                 │  │ Abstractions (已废弃)           │   │
│  │  - NonEventCommand   │  │  - INonEventCommandRegistry    │   │
│  │    Sync              │  │    [Obsolete]                   │   │
│  │    (同步到 Parser)   │  │  - CommandCriticality [Obsolete]│   │
│  └──────────────────────┘  │  - IConfigLoader / IStateQuery │   │
│                            │  - IUIController               │   │
│                            └────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

---

## 六、总结

### 6.1 当前架构评价

- **正向设计**：三层架构清晰，职责分离合理
- **CommandFramework** 作为通用基础设施，不依赖业务逻辑
- **FunctionStorage** 作为注册中心，统一管理函数和命令注册
- **FileDriven.Execution** 作为业务层，实现具体的命令处理逻辑
- **方案C成功消除路由断裂**：`ParseCommandStage` 通过外部注入的委托查找非事件命令，不再持有 `NonEventCommandHandler` 实例

### 6.2 主要问题（已解决）

| 问题 | 解决方案 |
|------|----------|
| `sideload` 命令路由断裂 | 方案C：`ParseCommandStage` 改用 `findNonEventHandler` 委托 |
| 两套非事件命令列表不同步 | `NonEventCommandSync` 订阅 `OnCommandRegistered` 事件自动同步 |
| `CommandFrameworkHost` 持有废弃的 `NonEventCommandHandler` | 方案C移除 |
| `FileDrivenExecutor` 中的旧版补丁代码 | 方案C改造：`RegisterServices()` 调用保留，用于将 `FileDriven.Pipeline.PipelineEntry`（实现 `IConfigLoader`）注册到 `CommandFrameworkUsage`，供 `sideload` 命令的 `HandleSideload` 使用 |


### 6.3 待改进方向

1. **统一命令注册入口**（已评估：当前架构已合理，跳过）
   - `FunctionStorageRegistry` 是非事件命令的统一注册中心
   - `CommandEngine` 是事件命令（set/get/click 等）的注册和执行引擎，需要撤销/重做支持
   - 两者职责明确且语义不同，强行合并会破坏功能

2. **异步化 Sideload** ✅（2026-06-12 已修复）
   - `HandleSideloadGuiMode` 中的 `GetAwaiter().GetResult()` 改为 `Task.Run` 包装
   - 避免 UI 线程阻塞，采用同步外壳 + 异步内核的折中方案
