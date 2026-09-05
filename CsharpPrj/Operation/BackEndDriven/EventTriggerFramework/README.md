# EventTriggerFramework

事件触发中间层框架，提供**事件总线**、**命令系统**、**管线系统**和 **WinForms 适配**四大子系统，支持按需组合、灵活扩展。

---

## 架构概览

```
┌─────────────────────────────────────────────────────────┐
│                    EventTriggerFramework                 │
│                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │   事件总线     │  │   命令系统     │  │   管线系统      │  │
│  │  EventBus     │  │  Commands     │  │  Pipeline      │  │
│  │              │  │              │  │               │  │
│  │ • 发布/订阅   │  │ • 命令解析器   │  │ • 管线阶段     │  │
│  │ • 增强装饰器   │  │ • 命令引擎     │  │ • 事件绑定阶段  │  │
│  │ • 去重装饰器   │  │ • 非事件命令   │  │ • 绑定查找阶段  │  │
│  │ • 事件队列     │  │ • 撤销/重做   │  │               │  │
│  │ • 事件作用域   │  │              │  │               │  │
│  └──────┬───────┘  └──────┬───────┘  └───────┬───────┘  │
│         │                 │                   │          │
│  ┌──────┴─────────────────┴───────────────────┴───────┐  │
│  │                    核心服务 (Core)                    │  │
│  │  • 函数注册表 / 函数执行器  • 状态存储  • 值提取器     │  │
│  │  • 事件触发服务  • 事件绑定服务  • 控件查找辅助        │  │
│  └──────────────────────┬──────────────────────────────┘  │
│                         │                                 │
│  ┌──────────────────────┴──────────────────────────────┐  │
│  │                WinForms 适配层                       │  │
│  │  • 控件值读写器  • 事件名称解析器  • 反射事件绑定器    │  │
│  │  • WinForms 事件总线  • WinForms 事件绑定器           │  │
│  └─────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## 核心模块

### 事件总线 (EventBus)

| 组件 | 文件 | 说明 |
|------|------|------|
| **事件总线核心** | `EventBus/EventBus.cs` | `IEventBusCore` 接口 + `DefaultEventBusCore` 默认实现，支持命名事件和类型化事件的发布/订阅 |
| **事件总线门面** | `EventBus/EventBus.cs` | `EventBusFacade` — 统一门面，包装核心总线，支持异步处理器自动包装 |
| **增强装饰器** | `EventBus/EnhancedEventBusCore.cs` | `EnhancedEventBusDecorator` — 装饰器模式，支持事件**过滤**、**数据转换**、**日志** |
| **去重装饰器** | `EventBus/DeduplicatingEventBusDecorator.cs` | `DeduplicatingEventBusDecorator` — 装饰器模式，防止同一事件在时间窗口内被重复处理，支持自动过期清理 |
| **事件队列** | `EventBus/EventQueue.cs` | `EventQueue` — 异步事件队列，替代同步递归冒泡，支持取消传播 |
| **事件作用域** | `EventBus/EventScope.cs` | `EventScope` — 父子层次结构的事件隔离与传播，支持 `Exact` / `CurrentAndChildren` / `CurrentAndParents` / `All` 四种策略 |
| **订阅令牌** | `EventBus/SubscriptionToken.cs` | `SubscriptionToken` — 订阅生命周期追踪，通过 `Dispose()` 统一取消订阅；`SubscriptionManager` — 线程安全的令牌集合管理 |
| **事件引擎** | `EventBus/EnhancedEventBusCore.cs` | `EventEngine` — 对 `IEventBusCore` 的类型安全封装 |

### 命令系统 (Commands)

| 组件 | 文件 | 说明 |
|------|------|------|
| **命令解析器** | 引用 `CommandFramework` | `CommandParser` — 解析命令字符串为结构化命令 |
| **命令引擎** | 引用 `CommandFramework` | `CommandEngine` — 支持撤销/重做的命令执行引擎 |
| **非事件命令处理器** | `ICommand.cs` | `NonEventCommandHandler` — 扩展版，注册占位处理器（look/discover/assert/list/validate/exit/load/close/show），返回"未实现"错误 |

### 管线系统 (Pipeline)

| 组件 | 文件 | 说明 |
|------|------|------|
| **事件绑定阶段** | `Pipeline/EventBindingStage.cs` | `EventBindingStage` — 抽象基类，模板方法模式，子类实现具体平台的事件绑定逻辑 |
| **绑定查找阶段** | `Pipeline/EventBindingStage.cs` | `FindBindingStage` — 查找控件的事件绑定，获取绑定的函数名和参数映射 |
| **管线入口** | 引用 `CommandFramework` | `PipelineEntry` / `PipelineEntryInstance` — 管线创建与共享状态管理 |

### 核心服务 (Core)

| 组件 | 文件 | 说明 |
|------|------|------|
| **函数注册表** | 引用 `CommandFramework` | `FunctionRegistry` — 注册和管理可执行函数 |
| **函数执行器** | 引用 `CommandFramework` | `FunctionExecutor` — 执行注册的函数 |
| **状态存储** | 引用 `CommandFramework` | `InMemoryStateStore` — 内存中的键值状态存储 |
| **值提取器** | 引用 `CommandFramework` | `DefaultValueExtractor` — 从控件或状态中提取值 |
| **事件触发服务** | `ControlValueAccessorBase.cs` | `EventTriggerServiceBase` — 模板方法模式，定义事件触发管线流程：值变更 → 查找绑定 → 提取输入值 → 执行函数 → 写入输出 |
| **事件触发结果** | `ControlValueAccessorBase.cs` | `EventTriggerResult` — 事件触发结果实现，继承 `EventBindingOutput` |
| **事件绑定执行器** | `ControlValueAccessorBase.cs` | `EventBindingExecutor` — 通用事件绑定执行器，统一通过 `IEventPipeline.ExecuteAsync()` 入口执行 |
| **事件绑定服务** | `DefaultEventBindingService.cs` | `DefaultEventBindingService` — 遍历 UI 配置，递归绑定控件事件，委托给 `IWinFormsEventBinder` |
| **控件查找辅助** | `ControlHelper.cs` | `ControlHelper` — 在 Form 中递归查找指定名称的控件，支持跨 Form 查找 |

### WinForms 适配

| 组件 | 文件 | 说明 |
|------|------|------|
| **控件值读写器** | `WinForms/WinFormsControlValueAccessor.cs` | `WinFormsControlValueAccessor` — 支持 TextBox、ComboBox、CheckBox、DataGridView 等 20+ 种控件的值读写 |
| **事件名称解析器** | `WinForms/WinFormsEventNameResolver.cs` | `WinFormsEventNameResolver` — 解析事件名称（含别名映射，如 `Closing` → `FormClosing`），支持 70+ 种常见事件 |
| **反射事件绑定器** | `WinForms/ReflectionEventBinder.cs` | `ReflectionEventBinder<TTarget>` — 通用反射事件绑定器，支持绑定/解绑/查询，使用 `ConditionalWeakTable` 防止内存泄漏 |
| **WinForms 事件总线** | `WinForms/WinFormsEventBus.cs` | `WinFormsEventBus` — 继承 `DefaultEventBusCore`，支持 WinForms 控件的 `BindEvent` / `UnbindEvent` |
| **WinForms 事件绑定器** | `WinForms/WinFormsEventBinder.cs` | `WinFormsEventBinder` — 实现 `IEventBinder`，绑定并执行事件，将结果写入输出控件 |

---

## 核心接口

| 接口 | 文件 | 说明 |
|------|------|------|
| `IEventBusCore` | `EventBus/EventBus.cs` | 事件总线核心接口：发布/订阅/取消订阅/绑定控件事件 |
| `IEventBusFacade` | `EventBus/EventBus.cs` | 事件总线门面接口：继承 `IEventBusCore`，增加异步处理器订阅 |
| `IEventBinder` | `ICommandExecutionResult.cs` | 通用事件绑定器接口：绑定执行、写入结果、查找容器 |
| `IEventBindingService` | `ICommandExecutionResult.cs` | 事件绑定服务接口：将 UI 配置中的事件声明绑定到实际控件 |
| `IEventBindingInfo` | `ICommandExecutionResult.cs` | 事件绑定信息接口：函数名、事件类型、输入/输出映射 |
| `IEventBindingResolver` | `ICommandExecutionResult.cs` | 事件绑定解析器接口：从配置模型中查找事件绑定信息 |
| `IEventPipeline` | `ICommandExecutionResult.cs` | 统一事件管线接口：所有事件处理通过此接口统一 |
| `IEventTriggerService` | `ICommandExecutionResult.cs` | 事件触发服务接口：设置控件值后自动触发事件绑定 |
| `IEventTriggerResult` | `ICommandExecutionResult.cs` | 事件触发结果接口：成功/失败、执行时间、函数名等 |
| `IEventNameResolver` | `ICommandExecutionResult.cs` | 事件名称解析器接口：解析/查询控件支持的事件 |
| `IWinFormsEventBinder` | `ICommandExecutionResult.cs` | WinForms 事件绑定器接口：绑定控件事件，支持脚本执行模式标记 |

---

## 数据模型

| 类型 | 文件 | 说明 |
|------|------|------|
| `EventRequest` | `EventBindingInfo.cs` | 统一事件请求，支持从 config/code/command/resize 四种来源创建 |
| `EventBindingInfo` | `EventBindingInfo.cs` | 事件绑定信息 record，实现 `IEventBindingInfo` 接口 |
| `EventBindingInput` | `EventBindingInfo.cs` | 事件绑定输入 DTO |
| `EventBindingOutput` | `EventBindingInfo.cs` | 事件绑定输出 DTO，继承 `Result<object>` |
| `ScopedEvent` | `EventBus/EventScope.cs` | 带作用域的事件，携带 ScopeId 和元数据 |
| `PropagationEvent` | `EventBus/EventScope.cs` | 传播事件，携带取消能力 |
| `SubscriptionToken` | `EventBus/SubscriptionToken.cs` | 订阅令牌，通过 Dispose() 取消订阅 |

---

## 快速开始

### 一键初始化

```csharp
// 一键初始化所有子系统（使用默认配置）
var ctx = EventTriggerFrameworkInitializer.Initialize();

// 使用事件总线
ctx.EventBus?.Publish("myEvent", data);

// 订阅事件
ctx.EventBus?.Subscribe("myEvent", data =>
{
    Console.WriteLine($"收到事件: {data}");
});

// 关闭框架
EventTriggerFrameworkInitializer.Shutdown();
```

### 按需初始化

```csharp
// 仅初始化事件总线
var ctx = EventTriggerFrameworkInitializer.InitializeEventBus();

// 仅初始化命令系统
var ctx = EventTriggerFrameworkInitializer.InitializeCommands();

// 仅初始化管线系统
var ctx = EventTriggerFrameworkInitializer.InitializePipeline();

// 仅初始化核心服务
var ctx = EventTriggerFrameworkInitializer.InitializeCore();
```

### 事件总线高级用法

```csharp
var ctx = EventTriggerFrameworkInitializer.Initialize(options =>
{
    options.EnableEventBus = true;
    options.EnableDeduplication = true;     // 启用事件去重
    options.DedupWindow = TimeSpan.FromSeconds(3);
    options.EnableEventQueue = true;        // 启用事件队列
    options.EnableEventScope = true;        // 启用事件作用域
});

// 类型安全的事件发布/订阅
ctx.EventEngine?.Publish(new MyEvent { Data = "hello" });
ctx.EventEngine?.Subscribe<MyEvent>(e => Console.WriteLine(e.Data));

// 增强功能：过滤和转换
if (ctx.EnhancedEventBus is EnhancedEventBusDecorator enhanced)
{
    enhanced.AddFilter("myEvent", data => data != null);
    enhanced.AddTransform("myEvent", data => data?.ToString()?.ToUpper());
    enhanced.EnableLogging("myEvent");
}

// 事件作用域：父子隔离与传播
var rootScope = ctx.RootEventScope;
var childScope = rootScope?.CreateChildScope("child1");

childScope?.Publish("scopeEvent", "只在子作用域内可见");
childScope?.PublishAndPropagate("propagateEvent", "传播到父作用域");

// 订阅（接收当前和子作用域的事件）
rootScope?.Subscribe("scopeEvent", scoped =>
{
    Console.WriteLine($"收到来自 {scoped.ScopeId} 的事件");
}, EventScopePolicy.CurrentAndChildren);
```

### 命令管线

```csharp
var ctx = EventTriggerFrameworkInitializer.Initialize(options =>
{
    options.EnableCommands = true;
    options.EnablePipeline = true;
    options.EnableCore = true;
});

// 解析并执行命令
var parsed = ctx.CommandParser?.Parse("set TextBox1.Text hello");
if (parsed != null)
{
    var result = await ctx.PipelineInstance?.RunCommandsAsync(
        ctx.Pipeline, new[] { "set TextBox1.Text hello" });
}
```

### WinForms 事件绑定

```csharp
var ctx = EventTriggerFrameworkInitializer.Initialize(options =>
{
    options.EnableWinForms = true;  // WinForms 适配默认关闭
});

// 绑定配置中定义的事件到 Form
ctx.EventBindingService?.BindEvents(myForm, uiConfig);

// 脚本执行模式：管线执行 set 命令时跳过运行时事件触发
if (ctx.WinFormsEventBinder is IWinFormsEventBinder binder)
{
    binder.IsScriptExecutionMode = true;  // 管线模式
    // ... 执行 set 命令 ...
    binder.IsScriptExecutionMode = false; // 恢复用户交互模式
}
```

---

## 配置选项

```csharp
var ctx = EventTriggerFrameworkInitializer.Initialize(options =>
{
    // ---- 子系统开关 ----
    options.EnableEventBus = true;      // 事件总线（默认 true）
    options.EnableCommands = true;      // 命令系统（默认 true）
    options.EnablePipeline = true;      // 管线系统（默认 true）
    options.EnableCore = true;          // 核心服务（默认 true）
    options.EnableWinForms = false;     // WinForms 适配（默认 false）

    // ---- 管线配置 ----
    options.PipelineName = "DefaultCommandPipeline";  // 管线名称
    options.CliMode = false;            // CLI 模式（默认 false）
    options.ConfigurePipeline = pipeline =>
    {
        // 自定义管线阶段注入
        // pipeline.AddStage(new MyCustomStage());
    };

    // ---- 事件总线增强功能 ----
    options.EnableDeduplication = true; // 事件去重（默认 false）
    options.DedupWindow = TimeSpan.FromSeconds(5);  // 去重时间窗口
    options.EnableEventQueue = true;    // 事件队列（默认 false）
    options.EnableEventScope = true;    // 事件作用域（默认 false）

    // ---- 日志 ----
    options.EventBusLogger = msg => File.AppendAllText("eventbus.log", msg);
});
```

---

## 设计模式

| 模式 | 使用场景 |
|------|----------|
| **装饰器模式** | `EnhancedEventBusDecorator` 包装 `IEventBusCore` 添加过滤/转换/日志；`DeduplicatingEventBusDecorator` 添加去重功能 |
| **门面模式** | `EventBusFacade` 统一封装事件总线核心，提供简化的 API |
| **模板方法模式** | `EventTriggerServiceBase` 定义事件触发管线流程，子类实现具体步骤；`EventBindingStage` 定义绑定流程，子类实现平台特定逻辑 |
| **策略模式** | `EventScopePolicy` 四种作用域过滤策略（Exact / CurrentAndChildren / CurrentAndParents / All） |
| **订阅令牌模式** | `SubscriptionToken` 通过 `Dispose()` 统一取消订阅，解决匿名函数无法取消的问题 |

---

## 生命周期管理

```csharp
// 初始化
var ctx = EventTriggerFrameworkInitializer.Initialize();

// 检查状态
bool isReady = EventTriggerFrameworkInitializer.IsInitialized;
var context = EventTriggerFrameworkInitializer.Context;

// 重置（先关闭再初始化）
ctx = EventTriggerFrameworkInitializer.Reset(options => { ... });

// 关闭框架，释放所有资源
EventTriggerFrameworkInitializer.Shutdown();
```

---

## 项目结构

```
EventTriggerFramework/
├── EventTriggerFrameworkInitializer.cs   # 框架初始化器（一键/按需初始化）
├── FrameworkContext.cs                   # 框架上下文（持有所有子系统引用）
├── FrameworkOptions.cs                   # 框架配置选项
├── EventBindingInfo.cs                   # 事件绑定数据模型（DTO/Record）
├── EventPipeline.cs                      # 事件触发管线扩展方法
├── ICommand.cs                           # 命令接口与实现（占位处理器）
├── ICommandExecutionResult.cs            # 框架特有抽象接口定义
├── ControlHelper.cs                      # 控件查找辅助类
├── ControlValueAccessorBase.cs           # 事件触发服务基类 + 执行器
├── DefaultEventBindingService.cs         # 事件绑定服务默认实现
│
├── EventBus/
│   ├── EventBus.cs                       # 事件总线核心 + 门面
│   ├── EnhancedEventBusCore.cs           # 增强装饰器 + 事件引擎
│   ├── DeduplicatingEventBusDecorator.cs # 去重装饰器
│   ├── EventQueue.cs                     # 异步事件队列
│   ├── EventScope.cs                     # 事件作用域
│   └── SubscriptionToken.cs              # 订阅令牌 + 管理器
│
├── Pipeline/
│   ├── EventBindingStage.cs              # 事件绑定阶段 + 绑定查找阶段
│   └── ANALYSIS_REPORT.md               # 代码整理分析报告
│
└── WinForms/
    ├── WinFormsControlValueAccessor.cs   # 控件值读写器
    ├── WinFormsEventNameResolver.cs      # 事件名称解析器
    ├── ReflectionEventBinder.cs          # 反射事件绑定器
    ├── WinFormsEventBus.cs               # WinForms 事件总线
    └── WinFormsEventBinder.cs            # WinForms 事件绑定器
```
