# EventTriggerFramework 与 CommandFramework 依赖关系分析报告

## 1. 概述

### 1.1 项目定位

| 项目 | 路径 | 定位 |
|------|------|------|
| **CommandFramework** | `Share/CommandFramework/` | 底层通用命令框架，提供命令解析、管线、执行引擎、状态存储等核心能力 |
| **EventTriggerFramework** | `Operation/EventTriggerFramework/` | 上层事件触发框架，在 CommandFramework 基础上添加事件总线、WinForms 适配、事件绑定等 UI 相关能力 |

### 1.2 架构关系

```
EventTriggerFramework
    ├── 依赖 CommandFramework（命令解析、管线、执行引擎）
    ├── 依赖 BasicMethod.Pipeline（管线基础设施）
    ├── 依赖 BasicMethod.Common（通用工具）
    ├── 依赖 UniversalMiddleware（中间件接口）
    └── 依赖 MetaCommand（元命令）
```

**EventTriggerFramework 是 CommandFramework 的上层封装**，它包装了 CommandFramework 的核心组件（Parser、NonEventCommandHandler、Pipeline、Engine），并添加了事件总线（EventBus）、WinForms 适配等上层功能。

---

## 2. 依赖关系详解

### 2.1 项目引用

**EventTriggerFramework.csproj** 已添加对 **CommandFramework.csproj** 的显式项目引用：

```xml
<ProjectReference Include="..\..\Share\CommandFramework\CommandFramework.csproj" />
```

### 2.2 核心依赖映射

| EventTriggerFramework 组件 | 来源 | 说明 |
|---|---|---|
| `CommandParser` | `CommandFramework.Parsing` | 命令解析器，复用 CommandFrameworkHost |
| `NonEventCommandHandler` | `CommandFramework.Execution` | 非事件命令处理器，复用 CommandFrameworkHost |
| `CommandEngine` | `CommandFramework.Execution` | 命令引擎，复用 CommandFrameworkHost |
| `PipelineEntryInstance` | `CommandFramework.Pipeline` | 管线入口实例，复用 CommandFrameworkHost |
| `IStagePipeline<CommandContext>` | `CommandFramework.Pipeline` | 命令管线 |
| `CommandContext` | `CommandFramework.Models` | 命令上下文 |
| `CommandExecutionResult<T>` | `CommandFramework.Execution` | 命令执行结果 |
| `ICommandExecutionResult` | `CommandFramework.Abstractions` | 命令执行结果接口 |
| `IStateStore` | `CommandFramework.Abstractions` | 状态存储接口 |
| `InMemoryStateStore` | `CommandFramework.Execution` | 状态存储实现 |
| `FunctionRegistry` | `CommandFramework.Execution` | 函数注册表 |
| `FunctionExecutor` | `CommandFramework.Execution` | 函数执行器 |
| `IControlValueAccessor` | `CommandFramework.Abstractions` | 控件值读写器接口 |
| `ControlValueAccessorBase` | `CommandFramework.Execution` | 控件值读写器基类 |

---

## 3. 已实施的优化

### 3.1 P0: EventTriggerFrameworkInitializer 复用 CommandFrameworkHost

**优化前：** EventTriggerFrameworkInitializer 手动创建 CommandParser、NonEventCommandHandler、PipelineEntryInstance 等组件：

```csharp
// 优化前：手动创建
ctx.CommandParser = new CommandParser();
ctx.NonEventCommandHandler = new CommandFramework.Execution.NonEventCommandHandler();
ctx.CommandEngine = new CommandFramework.Execution.CommandEngine();
ctx.PipelineInstance = new CmdPipelineEntryInstance(
    new CommandParser(),
    new CommandFramework.Execution.NonEventCommandHandler());
```

**优化后：** 内部使用 CommandFrameworkHost 统一管理 CommandFramework 组件的生命周期：

```csharp
// 优化后：复用 CommandFrameworkHost
ctx.CommandFrameworkHost = CommandFramework.CommandFrameworkHost.CreateFromOptions(...);
var host = ctx.CommandFrameworkHost;
ctx.CommandParser = host.Parser;
ctx.NonEventCommandHandler = host.NonEventHandler;
ctx.CommandEngine = host.Engine;
ctx.PipelineInstance = host.PipelineInstance;
```

**收益：**
- 消除组件创建逻辑的重复（CommandFrameworkHost 和 EventTriggerFrameworkInitializer 各有一份）
- 统一生命周期管理（Dispose 时自动释放 CommandFrameworkHost）
- 确保 Parser/NonEventHandler/Engine 等组件在 Host 和 EventTriggerFramework 之间共享同一实例

### 3.2 P1: 废弃 EventTriggerFramework.NonEventCommandHandler

**优化前：** EventTriggerFramework 定义了自己的 `NonEventCommandHandler`（继承自 CommandFramework 的版本），注册了占位处理器：

```csharp
public class NonEventCommandHandler : CommandFramework.Execution.NonEventCommandHandler
{
    public NonEventCommandHandler()
    {
        RegisterPlaceholderHandlers(); // 注册占位处理器
    }
}
```

**问题：** 占位处理器会覆盖 CommandFramework 通过 `RegisterServiceCommands()` 注册的真实处理器，导致业务命令无法正常执行。

**优化后：** 该子类未被任何代码引用，直接使用 CommandFramework 的 `NonEventCommandHandler`。

### 3.3 P1: 废弃 EventTriggerFramework.FindBindingStage

**优化前：** EventTriggerFramework 定义了自己的 `FindBindingStage`（在 `Pipeline/EventBindingStage.cs` 中），与 CommandFramework 的 `FindBindingStage` 功能完全重叠。

**问题：** 两个版本功能重叠，EventTriggerFramework 版本直接依赖 `IEventBindingResolver` 接口，而 CommandFramework 版本已通过 `Func` 委托解耦。

**优化后：** 添加 `[Obsolete]` 标记，建议使用 `CommandFramework.Pipeline.Stages.FindBindingStage`。

---

## 4. 可进一步优化的点（建议）

### 4.1 统一 IEventBindingResolver 接口（P2）

两个项目各自定义了 `IEventBindingResolver`：

| 项目 | 命名空间 | 方法 |
|---|---|---|
| CommandFramework | `CommandFramework.Abstractions` | `FindBinding(controlName, action)` |
| EventTriggerFramework | `EventTriggerFramework.Abstractions` | `FindEventBinding(controlName, eventType)` + `ResolveEventType()` |

**建议：** 将 EventTriggerFramework 的 `IEventBindingResolver` 扩展方法（如 `ResolveEventType`）迁移到 CommandFramework 的接口中，或通过扩展方法实现。

### 4.2 统一 IEventBindingInfo 接口（P2）

两个项目各自定义了 `IEventBindingInfo`：

| 项目 | 命名空间 | 属性 |
|---|---|---|
| CommandFramework | `CommandFramework.Abstractions` | `FunctionName`, `Inputs`, `Output` |
| EventTriggerFramework | `EventTriggerFramework.Abstractions` | `FunctionName`, `EventType`, `Inputs`, `Output`, `FunctionNames` |

**建议：** 将 EventTriggerFramework 的 `IEventBindingInfo` 扩展属性（如 `FunctionNames`）通过继承或扩展方法实现。

### 4.3 统一 InitializeCore 中的组件创建（P2）

EventTriggerFrameworkInitializer 的 `InitializeCore` 方法手动创建了 `FunctionRegistry`、`FunctionExecutor`、`InMemoryStateStore` 等，而这些组件已经由 CommandFrameworkHost 创建。

**建议：** 复用 CommandFrameworkHost 的 `FunctionRegistry`、`FunctionExecutor`、`StateStore` 属性。

---

## 5. 总结

### 5.1 已解决的问题

| 问题 | 优先级 | 状态 |
|---|---|---|
| EventTriggerFramework.csproj 缺少对 CommandFramework.csproj 的显式引用 | P0 | ✅ 已修复 |
| EventTriggerFrameworkInitializer 手动创建 CommandFramework 组件 | P0 | ✅ 已修复 |
| EventTriggerFramework.NonEventCommandHandler 占位处理器覆盖真实处理器 | P1 | ✅ 已废弃 |
| EventTriggerFramework.FindBindingStage 与 CommandFramework 版本功能重叠 | P1 | ✅ 已废弃 |

### 5.2 架构改进

- **单一职责**：CommandFramework 负责命令处理的核心逻辑，EventTriggerFramework 负责事件触发和 UI 适配
- **依赖倒置**：EventTriggerFramework 依赖 CommandFramework 的抽象接口，而非具体实现
- **复用优先**：通过 CommandFrameworkHost 统一管理组件生命周期，消除重复创建逻辑

---

## 6. 附录：CommandFramework 中的"事件"相关代码分析

### 6.1 问题：CommandFramework 中的事件逻辑是否应迁移到 EventTriggerFramework？

**结论：不应迁移。** CommandFramework 中的事件相关代码是合理的、必要的，理由如下：

### 6.2 CommandFramework 中的事件相关代码清单

| 文件 | 内容 | 性质 |
|---|---|---|
| `WinFormsEventTrigger.cs` | `WinFormsEventTrigger` 类 + `IWinFormsEventTrigger` 接口 | WinForms 控件事件绑定工具 |
| `IEventBindingResolver.cs` | `IEventBindingInfo` 接口 + `IEventBindingResolver` 接口 | 通用抽象接口 |
| `IUIController.cs` | `IUIController` 接口 | 通用抽象接口 |
| `IStateQuery.cs` | `IStateQuery` 接口 | 通用抽象接口 |
| `FindBindingStage.cs` | `FindBindingStage` 管线阶段 | 管线阶段 |

### 6.3 分析：为什么这些代码应该在 CommandFramework 中

#### 6.3.1 抽象接口遵循依赖倒置原则

`IEventBindingResolver`、`IUIController`、`IStateQuery` 都是**纯抽象接口**，定义在 `CommandFramework.Abstractions` 命名空间中。它们的作用是让 CommandFramework 的 `NonEventCommandHandler` 和 `FindBindingStage` 能够**通过接口调用**事件绑定逻辑，而不依赖具体实现。

```
CommandFramework (抽象层)
  ├── IEventBindingResolver (接口)
  ├── IUIController (接口)
  ├── IStateQuery (接口)
  └── NonEventCommandHandler.RegisterServiceCommands()
        └── 通过接口调用 → 由 FileDriven 等上层项目实现

EventTriggerFramework (具体实现层)
  └── 实现 IEventBindingResolver、IUIController、IStateQuery
```

如果把这些接口迁移到 EventTriggerFramework，CommandFramework 的 `NonEventCommandHandler` 和 `FindBindingStage` 就无法调用它们了，**会导致循环依赖**（CommandFramework → EventTriggerFramework → CommandFramework）。

#### 6.3.2 WinFormsEventTrigger 是底层工具类

`WinFormsEventTrigger` 是一个**通用的 WinForms 控件事件绑定工具**，它：
- 不依赖 EventTriggerFramework 的任何类型
- 只依赖 `ICommandEngine` 接口（CommandFramework 自身定义）
- 提供 `Bind()`、`Unbind()`、`SetControlTextAndTriggerEvent()` 等通用方法

它类似于一个"WinForms 控件事件 → 命令引擎"的适配器，放在 CommandFramework 中作为基础设施是合理的。

#### 6.3.3 FindBindingStage 已经通过 Func 委托解耦

CommandFramework 的 `FindBindingStage` 使用 `Func<string, string, (string?, List<string>?, string?)>` 委托替代了 `IEventBindingResolver` 接口，通过 `SharedData["FindBindingFunc"]` 传递查找函数。这已经做到了**完全不依赖 EventTriggerFramework**。

### 6.4 总结：不应迁移的代码

| 代码 | 位置 | 是否应迁移 | 原因 |
|---|---|---|---|
| `IEventBindingResolver` 接口 | CommandFramework | ❌ 不应迁移 | 抽象接口，供 CommandFramework 内部调用 |
| `IEventBindingInfo` 接口 | CommandFramework | ❌ 不应迁移 | 同上 |
| `IUIController` 接口 | CommandFramework | ❌ 不应迁移 | 同上 |
| `IStateQuery` 接口 | CommandFramework | ❌ 不应迁移 | 同上 |
| `WinFormsEventTrigger` | CommandFramework | ❌ 不应迁移 | 通用 WinForms 工具类，不依赖 EventTriggerFramework |
| `FindBindingStage` | CommandFramework | ❌ 不应迁移 | 已通过 Func 委托解耦，设计合理 |
| **EventTriggerFramework.FindBindingStage** | EventTriggerFramework | ✅ **已废弃** | 与 CommandFramework 版本功能重叠 |

**核心原则**：CommandFramework 作为底层框架，定义抽象接口供上层实现，这是正确的依赖倒置原则。真正需要清理的是 EventTriggerFramework 中与 CommandFramework 功能重叠的代码。
