# CommandFramework — 通用命令执行框架

[![.NET](https://img.shields.io/badge/.NET-10.0--windows-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![Namespace](https://img.shields.io/badge/Namespace-CommandFramework-blue)](.)

**CommandFramework** 是一个通用命令执行框架，提供从命令字符串解析、注册、执行到结果返回的完整生命周期管理。它是 WorkFlowPrj 项目中 `Share` 层的核心组件，被 `FileDriven`、`Operation` 等上层模块引用。

---

## 目录

- [设计目标与适用场景](#设计目标与适用场景)
- [核心特性](#核心特性)
- [快速开始](#快速开始)
- [项目结构](#项目结构)
- [架构概览](#架构概览)
- [核心组件](#核心组件)
- [命令执行流程](#命令执行流程)
- [管线阶段详解](#管线阶段详解)
- [命令规范](#命令规范)
- [函数执行体系](#函数执行体系)
- [扩展机制](#扩展机制)
- [使用示例](#使用示例)
- [设计模式](#设计模式)
- [关键设计决策](#关键设计决策)
- [项目依赖](#项目依赖)
- [相关文档](#相关文档)

---

## 设计目标与适用场景

### 设计目标

| 目标 | 说明 |
|------|------|
| **通用性** | 不绑定特定 UI 框架或业务领域，支持 CLI / GUI / Test 多种运行模式 |
| **可扩展** | 通过接口抽象、策略链、管线阶段等机制支持灵活扩展 |
| **双执行路径** | 同时提供 **CommandEngine**（轻量直接执行）和 **Pipeline**（阶段化执行）两条路径 |
| **撤销/重做** | 内置命令历史栈，支持 Undo / Redo |
| **多环境适配** | 通过 `IEnvironmentAdapter` 支持路由到不同后端环境 |
| **函数式集成** | 内置函数注册表与执行器，支持参数自动匹配与类型转换 |

### 适用场景

- **自动化测试框架** — 解析命令文件逐行执行，支持断言和等待
- **GUI 自动化** — 绑定 WinForms 控件事件到命令，支持 set / click / assert 等操作
- **CLI 工具** — 轻量级命令解析与执行引擎
- **工作流引擎** — 作为底层命令执行单元，支持组合命令和重试

---

## 核心特性

| 特性 | 说明 |
|------|------|
| 🚀 **命令解析** | 将命令字符串解析为结构化对象，支持规范化（简写 → 完整格式） |
| ⚡ **双执行路径** | 同时提供 **CommandEngine**（轻量直接执行）和 **Pipeline**（阶段化执行） |
| ↩️ **撤销/重做** | 内置命令历史栈，支持 Undo / Redo |
| 🧩 **管线系统** | 线性阶段模式 + 洋葱模型中间件，灵活编排执行流程 |
| 🔌 **函数执行** | 内置函数注册表与执行器，支持参数自动匹配与类型转换 |
| 🌐 **环境适配** | 通过 `IEnvironmentAdapter` 支持路由到不同后端环境 |
| 🎮 **控件值读写** | 通过 `IControlValueAccessor` 统一读写不同类型控件 |
| 💾 **状态存储** | 线程安全的内存键值存储，支持 CLI 模式模拟控件状态 |
| 🏗️ **统一入口** | `CommandFrameworkHost` + `CommandFrameworkUsage` 两种集成方式 |

---

## 快速开始

### 1. 添加项目引用

```xml
<ProjectReference Include="..\Share\CommandFramework\CommandFramework.csproj" />
```

### 2. 使用全局单例（最快方式）

```csharp
using CommandFramework;

// 初始化（程序启动时）
CommandFrameworkUsage.Initialize();

// 执行命令
var result = await CommandFrameworkUsage.ExecuteCommandAsync("echo Hello World");
Console.WriteLine(result.FunctionResultValue); // 输出: Hello World

// 关闭（程序退出时）
CommandFrameworkUsage.Shutdown();
```

### 3. 使用 Host 模式（自定义生命周期）

```csharp
using CommandFramework;

// 创建 CLI 模式主机
using var host = CommandFrameworkHost.CreateCliHost();

// 执行命令
var context = await host.ExecuteCommandAsync("echo Hello World");
Console.WriteLine(context.FunctionResultValue); // 输出: Hello World
```

---

## 项目结构

```
Share/CommandFramework/
├── CommandFramework.csproj          # 项目文件（依赖 Share.Pipeline）
├── ICommand.cs                      # ★ 核心接口定义（18 个接口/抽象类型）
├── CommandFrameworkHost.cs          # 统一入口（Host 模式）
├── CommandFrameworkOptions.cs       # 配置选项模型
├── CommandFrameworkUsage.cs         # 全局单例操作入口
├── CommandParser.cs                 # 命令解析器
├── CommandRegistry.cs               # 命令注册表（工厂模式）
│
├── Models/                          # 数据模型
│   ├── CommandContext.cs            # 管线执行上下文
│   ├── CommandExecutionContext.cs   # 引擎执行上下文
│   ├── ParsedCommand.cs             # 命令解析结果
│   └── JsonCommandResult.cs         # JSON 命令结果（含错误码枚举）
│
├── Execution/                       # 执行引擎
│   ├── CommandEngine.cs             # 命令引擎核心（含扩展方法 + 委托命令 + 复合命令）
│   ├── CommandResults.cs            # 执行结果模型（泛型 + JSON 序列化）
│   ├── NonEventCommandHandler.cs    # 非事件命令处理器
│   ├── FunctionExecution.cs         # 函数执行体系（注册表 + 执行器 + 参数匹配 + 类型转换）
│   ├── InMemoryStateStore.cs        # 内存状态存储
│   ├── StateStoreExtensions.cs      # 状态存储扩展（含 ObservableStateStore）
│   ├── ControlValueAccessorBase.cs  # 控件值读写器基类
│   ├── EnvironmentAdapterBase.cs    # 环境适配器基类
│   ├── FileDrivenCommandExtensions.cs # FileDriven 业务命令扩展
│   └── WinFormsEventTrigger.cs      # WinForms 事件触发器
│
├── Parsing/                         # 解析扩展
│   └── JsonCommandExtensions.cs     # JSON 命令解析扩展（JsonVerb 枚举 + 值类型推断）
│
└── Pipeline/                        # 管线系统
    ├── PipelineEntry.cs             # 管线入口（静态工厂 + 实例类）
    ├── PipelineStageBase.cs         # 管线阶段基类（含 CommandStageBase）
    └── Stages/                      # 管线阶段实现
        ├── ParseCommandStage.cs     # 命令解析阶段
        ├── FindBindingStage.cs      # 事件绑定查找阶段
        ├── ExtractValuesStage.cs    # 值提取阶段
        ├── ExecuteFunctionStage.cs  # 函数执行阶段
        ├── WriteResultStage.cs      # 结果写入阶段
        ├── ValidationStage.cs       # 验证阶段（横切关注点）
        ├── RetryStage.cs            # 重试阶段（横切关注点）
        └── LoggingStage.cs          # 日志阶段（横切关注点）
```

---

## 架构概览

### 三层架构

```
┌──────────────────────────────────────────────────────────────────┐
│                      接口抽象层 (ICommand.cs)                      │
│  ICommandEngine  ICommand  ICommandPipeline  IPipelineStage       │
│  ICommandParser  ICommandExecutionResult  IStateStore             │
│  IControlValueAccessor  IEnvironmentAdapter  IFunctionRegistry    │
│  IFunctionExecutor  INonEventCommandHandler  IValueExtractor      │
└────────────────────────────┬─────────────────────────────────────┘
                             │ 实现
┌────────────────────────────▼─────────────────────────────────────┐
│                       核心实现层 (Execution/)                      │
│  CommandEngine  CommandParser  NonEventCommandHandler             │
│  FunctionRegistry  FunctionExecutor  InMemoryStateStore           │
│  ControlValueAccessorBase  EnvironmentAdapterBase                 │
│  CommandExecutionResult<T>  DelegateCommand  CompositeCommand     │
└────────────────────────────┬─────────────────────────────────────┘
                             │ 编排
┌────────────────────────────▼─────────────────────────────────────┐
│                       管线编排层 (Pipeline/)                       │
│  PipelineEntry  PipelineStageBase  CommandStageBase               │
│  ParseCommandStage → FindBindingStage → ExtractValuesStage        │
│  → ExecuteFunctionStage → WriteResultStage                        │
│  ValidationStage<T>  RetryStage<T>  LoggingStage<T>               │
│  MiddlewarePipeline<TContext,TResult> (洋葱模型)                   │
└──────────────────────────────────────────────────────────────────┘
```

### 两种执行路径

| 特性 | CommandEngine 路径 | Pipeline 路径 |
|------|-------------------|---------------|
| **执行方式** | 直接调用注册的委托 | 经过多个阶段依次处理 |
| **适用场景** | 轻量级、高性能、需要 Undo/Redo | 复杂业务逻辑、需要值提取/绑定查找 |
| **核心类** | `CommandEngine` | `StagePipeline<CommandContext>` |
| **阶段化** | 否 | 是（5 个默认阶段） |
| **Undo/Redo** | ✅ 内置支持 | ❌ 不支持 |
| **文件执行** | ✅ `ExecuteFileAsync` | ✅ `RunCommandsAsync` |
| **依赖注入** | 构造函数参数 | SharedData + 构造函数 |

### 统一入口设计

```
CommandFrameworkHost          (可自定义生命周期)
        │
        ├── 工厂方法: CreateDefault() / CreateCliHost() / CreateFullHost()
        ├── 持有所有核心组件实例
        └── 提供 ExecuteCommandAsync() 便捷方法
                │
                ▼
CommandFrameworkUsage         (全局单例，快速集成)
        │
        ├── Initialize() / Shutdown() / Reset()
        ├── 线程安全（双重检查锁）
        └── 静态方法暴露所有功能
```

---

## 核心组件

### ICommand.cs — 接口定义层

该文件是整个框架的**契约定义中心**，包含 18 个接口/抽象类型：

| 接口 | 用途 | 关键方法 |
|------|------|----------|
| `ICommandEngine` | 命令引擎契约 | RegisterCommand, ExecuteAsync, UndoAsync, RedoAsync |
| `ICommand` | 单个命令契约 | ExecuteAsync, UndoAsync |
| `ICommandExecutionResult` | 执行结果契约 | IsSuccess, Value, Error, ToJson |
| `ICommandBatchResult` | 批量结果契约 | TotalCount, SuccessCount, FailCount |
| `ICommandParser` | 命令解析器契约 | Parse, Normalize, IsNonEventCommand |
| `ICommandPipeline<TContext>` | 管线契约 | Register, ExecuteAsync, ExecuteForEachAsync |
| `IPipelineContext` | 管线上下文契约 | StageSuccess, StageErrors, SharedData, Reset |
| `IPipelineStage<TContext>` | 管线阶段契约 | CanExecute, ExecuteAsync |
| `IControlValueAccessor` | 控件值读写器契约 | GetValue, SetValue, RegisterAdapter |
| `IEnvironmentAdapter` | 环境适配器契约 | DoAsync, GetAsync, LookAsync, WaitAsync, AssertAsync |
| `IFunctionRegistry` | 函数注册表契约 | Register, TryGetDescriptor, Contains |
| `IFunctionExecutor` | 函数执行器契约 | ExecuteAsync |
| `INonEventCommandHandler` | 非事件命令处理器契约 | HandleAsync, RegisterHandler |
| `IStateStore` | 状态存储契约 | Set, Get, Has, GetOrAdd |
| `IValueExtractor` | 值提取器契约 | ExtractValue, ExtractAllValues |

---

### CommandParser — 命令解析器

将命令字符串解析为结构化的 `ParsedCommand` 对象。

```csharp
var parser = new CommandParser();

// 解析命令
var parsed = parser.Parse("do click addButton");
Console.WriteLine(parsed.Verb);   // "do"
Console.WriteLine(parsed.Action); // "click"
Console.WriteLine(parsed.Target); // "addButton"

// 规范化（简写 → 完整格式）
var normalized = parser.Normalize("click addButton");
Console.WriteLine(normalized); // "do click addButton"

// 解析为 JsonCommand（带枚举动词和值类型推断）
var jsonCmd = parser.ParseAsJsonCommand("do set count value=42");
Console.WriteLine(jsonCmd.Verb); // JsonVerb.Do
```

**特性：**
- 支持命令规范化：`click` → `do click`，`set` → `do set`
- 内置默认非事件命令列表：`load`, `close`, `echo`, `sleep`, `show`, `look`, `wait`, `assert`, `discover`, `list`, `validate`, `exit`
- 支持转义引号 `\"`，支持注释行过滤（`#` 和 `//` 前缀）
- 参数解析支持 `key=value` 格式和无值标记

---

### CommandEngine — 命令引擎

提供命令的注册、执行、撤销、重做功能。

```csharp
var engine = new CommandEngine(parser, nonEventHandler);

// 注册命令（委托方式）
engine.RegisterCommand("greet", async (ctx, ct) =>
{
    Console.WriteLine($"Hello, {ctx.Data ?? "World"}!");
    ctx.Result = "done";
});

// 注册 ICommand 实例
engine.RegisterCommand(new DelegateCommand("ping", ct =>
{
    Console.WriteLine("pong");
    return Task.CompletedTask;
}));

// 执行命令
var success = await engine.ExecuteAsync("greet");

// 撤销/重做
await engine.UndoAsync();
await engine.RedoAsync();
```

**特性：**
- 支持委托命令 `DelegateCommand` 和复合命令 `CompositeCommand`
- 内置 Undo/Redo 历史栈
- 支持从文件批量执行命令 `ExecuteFileAsync`
- 找不到命令时自动回退到 `CommandRegistry` 工厂模式创建

---

### NonEventCommandHandler — 非事件命令处理器

处理不需要事件绑定的通用命令（echo/sleep/wait）。

```csharp
var handler = new NonEventCommandHandler();

// 内置命令
var echoResult = await handler.HandleAsync(parser.Parse("echo Hello World"));
Console.WriteLine(echoResult.Value); // "Hello World"

var sleepResult = await handler.HandleAsync(parser.Parse("sleep 1000"));
// 等待 1000ms

// 注册自定义命令
handler.RegisterHandler("mycommand", async cmd =>
{
    return CommandExecutionResult<object?>.Success($"Processed: {cmd.Target}");
});
```

**内置命令：**

| 命令 | 功能 | 说明 |
|------|------|------|
| `echo` | 返回目标文本 | 直接返回 cmd.Target |
| `sleep` | 等待指定毫秒数 | `Task.Delay(sleepMs)` |
| `wait` | sleep 的别名 | 默认 5000ms |

---

### Pipeline — 管线系统

提供两种管线模式：**线性阶段模式**（固定流程）和**洋葱模型中间件**（灵活拦截）。

#### 线性阶段管线

```csharp
// 创建默认管线（5 个阶段）
var pipeline = PipelineEntry.CreateDefaultPipeline();

// 执行命令
var context = new CommandContext { CurrentCommand = "do click addButton" };
await pipeline.ExecuteAsync(context);
```

默认阶段顺序：
1. **ParseCommandStage** — 解析命令字符串
2. **FindBindingStage** — 查找控件事件绑定
3. **ExtractValuesStage** — 从控件提取输入值
4. **ExecuteFunctionStage** — 执行绑定的函数
5. **WriteResultStage** — 将结果写回目标控件

#### 洋葱模型中间件管线

```csharp
// 创建中间件管线
var pipeline = new MiddlewarePipeline<CommandContext, CommandExecutionResult<object?>>("MyPipeline");

// 注册中间件（先注册的在外层）
pipeline.Use(LoggingMiddleware.Create<CommandContext, CommandExecutionResult<object?>>());
pipeline.Use(RetryMiddleware.Create<CommandContext, CommandExecutionResult<object?>>(maxRetries: 3));

// 设置核心处理器
pipeline.SetCoreHandler(async ctx =>
{
    return CommandExecutionResult<object?>.Success("done");
});

// 执行
var result = await pipeline.ExecuteAsync(context);
```

---

### FunctionRegistry / FunctionExecutor — 函数执行体系

支持委托注册、参数自动匹配、异步执行。

```csharp
var registry = new FunctionRegistry();
var executor = new FunctionExecutor(registry);

// 注册函数
registry.Register("add", (int a, int b) => a + b);
registry.Register("greet", (string name) => $"Hello, {name}!");

// 按名称执行
var result = await executor.ExecuteAsync("add", new Dictionary<string, object>
{
    ["a"] = 1,
    ["b"] = 2
});
Console.WriteLine(result.Value); // 3

// 执行委托
var result2 = await executor.ExecuteAsync((string name) => $"Hi {name}", "Alice");
Console.WriteLine(result2.Value); // "Hi Alice"
```

**参数匹配策略（按优先级）：**
1. **DictionaryExactMatcher** — 按参数名精确匹配
2. **PositionalMatcher** — 按位置顺序匹配
3. **SingleValueMatcher** — 单参数直接传入
4. **DefaultValueMatcher** — 可选参数填充默认值

**类型转换支持：** 内置 15 种类型的 TryParse 转换（string, int, long, float, double, bool, DateTime, Guid 等）

---

### CommandFrameworkHost — 统一入口

管理所有组件的生命周期，提供便捷的工厂方法和执行入口。

```csharp
// 创建默认主机（启用所有核心组件）
using var host = CommandFrameworkHost.CreateDefault();

// 创建 CLI 模式主机（轻量级）
using var cliHost = CommandFrameworkHost.CreateCliHost();

// 创建完整模式主机（含 GUI 支持）
using var fullHost = CommandFrameworkHost.CreateFullHost();

// 自定义配置
using var customHost = CommandFrameworkHost.Create(options =>
{
    options.EnableEngine = true;
    options.EnablePipeline = true;
    options.EnableStateStore = true;
    options.EnableFunctionRegistry = true;
    options.Mode = "CLI";
});

// 执行命令
var result = await host.ExecuteCommandAsync("echo Hello");
var results = await host.ExecuteCommandsAsync(new[] { "echo A", "echo B" });
var (success, fail) = await host.ExecuteFileAsync("commands.txt");
```

**初始化顺序：**
1. CommandParser → 2. NonEventCommandHandler → 3. FileDriven 业务命令 → 4. StateStore → 5. FunctionRegistry → 6. CommandEngine → 7. Pipeline → 8. CommandRegistry → 9. ControlValueAccessor → 10. EnvironmentAdapter

---

### CommandFrameworkUsage — 全局单例

提供所有核心功能的静态方法，适合快速集成场景。

```csharp
// 初始化（程序启动时）
CommandFrameworkUsage.Initialize();

// 或使用自定义配置
CommandFrameworkUsage.Initialize(options =>
{
    options.EnableEngine = true;
    options.EnablePipeline = true;
});

// 执行命令
var result = await CommandFrameworkUsage.ExecuteCommandAsync("echo Hello");

// 注册函数
CommandFrameworkUsage.RegisterFunction("add", (int a, int b) => a + b);

// 注册命令
CommandFrameworkUsage.RegisterCommand("mycmd", async (ctx, ct) =>
{
    Console.WriteLine($"Executing: {ctx.Name}");
});

// 撤销/重做
await CommandFrameworkUsage.UndoAsync();
await CommandFrameworkUsage.RedoAsync();

// 关闭（程序退出时）
CommandFrameworkUsage.Shutdown();
```

**特性：**
- 线程安全（双重检查锁）
- 防重复初始化
- 提供 16 类操作：生命周期、命令执行、命令注册、管线操作、解析器操作、引擎操作、状态存储、JSON 序列化等

---

### CommandContext — 管线执行上下文

继承 `BP.PipelineContext`，添加命令执行相关字段，作为管线各阶段之间的数据传递载体。

**核心字段：**

| 分类 | 字段 | 说明 |
|------|------|------|
| 命令解析 | `CurrentCommand` | 当前正在执行的命令字符串 |
| 命令解析 | `ParsedCommand` | 解析后的命令信息 |
| 绑定查找 | `BoundFunctionName` | 绑定的函数名 |
| 绑定查找 | `BoundInputs` | 绑定的输入参数列表 |
| 绑定查找 | `BoundOutputTarget` | 绑定的输出目标 |
| 值提取 | `ExtractedValues` | 从控件提取的输入值 |
| 函数执行 | `FunctionResultValue` | 函数执行结果值 |
| 函数执行 | `FunctionExecutionTime` | 函数执行耗时 |
| 运行时依赖 | `VirtualStore` | 虚拟控件存储（CLI 模式） |
| 运行时依赖 | `LoadedConfigs` | 已加载的配置缓存 |
| 执行统计 | `TotalCommands` / `SuccessCount` / `FailCount` | 执行统计 |

**关键方法：**
- `HasError` — 根据阶段成功标记判断命令是否执行失败
- `GetErrorMessage()` — 获取第一个失败阶段的错误信息
- `Reset()` — 重置上下文状态（保留运行时依赖）

---

## 命令执行流程

### Engine 执行路径

```
命令字符串
    │
    ▼
CommandParser.Parse() ──→ ParsedCommand
    │
    ▼
CommandEngine.ExecuteParsedCommandAsync()
    │
    ├── 已注册命令? ──→ 直接执行委托 ──→ 返回结果
    │
    ├── 非事件命令? ──→ NonEventCommandHandler.HandleAsync()
    │       │
    │       ├── echo ──→ 返回目标文本
    │       ├── sleep ──→ Task.Delay
    │       ├── wait  ──→ Task.Delay
    │       └── 自定义 ──→ 注册的处理器
    │
    └── 环境适配器命令? ──→ IEnvironmentAdapter
            │
            ├── do      ──→ DoAsync(target, value)
            ├── get     ──→ GetAsync(target)
            ├── look    ──→ LookAsync(target)
            ├── wait    ──→ WaitAsync(target, condition)
            ├── assert  ──→ AssertAsync(target, expected)
            └── discover ──→ DiscoverAsync(filter)
```

### Pipeline 执行路径

```
命令字符串
    │
    ▼
StagePipeline<CommandContext>.ExecuteAsync()
    │
    ├── [1] ParseCommandStage    ──→ 解析命令，非事件命令直接处理
    ├── [2] FindBindingStage     ──→ 查找控件事件绑定
    ├── [3] ExtractValuesStage   ──→ 从控件提取输入值
    ├── [4] ExecuteFunctionStage ──→ 执行绑定的函数
    └── [5] WriteResultStage     ──→ 将结果写回目标控件
```

### Host 自动路由

```
CommandFrameworkHost.ExecuteCommandAsync()
    │
    ├── Pipeline 启用? ──→ PipelineInstance.RunCommandAsync()
    │
    ├── Engine 启用?  ──→ Parser.Parse() → Engine.ExecuteAsync()
    │
    └── 最简路径      ──→ Parser.Parse() → NonEventHandler.HandleAsync()
```

---

## 管线阶段详解

### 默认阶段（5 个）

| 阶段 | 类名 | 职责 | 输入 | 输出 |
|------|------|------|------|------|
| 1 | `ParseCommandStage` | 解析命令字符串 | CurrentCommand | ParsedCommand |
| 2 | `FindBindingStage` | 查找事件绑定 | ParsedCommand | BoundFunctionName / Inputs / Output |
| 3 | `ExtractValuesStage` | 提取输入值 | BoundInputs | ExtractedValues |
| 4 | `ExecuteFunctionStage` | 执行绑定的函数 | BoundFunctionName + ExtractedValues | FunctionResultValue |
| 5 | `WriteResultStage` | 写回结果 | FunctionResultValue + BoundOutputTarget | 控件/存储更新 |

### 横切关注点阶段（3 个）

| 阶段 | 类名 | 职责 | 特性 |
|------|------|------|------|
| - | `ValidationStage<TContext>` | 通用验证 | 可配置验证规则列表，支持链式调用 |
| - | `RetryStage<TContext>` | 失败自动重试 | 支持 Fixed / ExponentialBackoff / Linear 三种延迟策略 |
| - | `LoggingStage<TContext>` | 日志记录 | 记录阶段开始/结束/耗时，支持上下文详情 |

### 阶段基类设计

```
PipelineStageBase<TContext>          (抽象基类)
    │
    ├── 自动处理: CanExecute 判断、异常捕获、状态更新
    ├── 子类只需重写: ExecuteCoreAsync()
    │
    └── CommandStageBase              (CommandContext 专用基类)
            │
            ├── IsEventCommand()      — 判断是否为事件命令
            ├── ResolveDependency()   — 多来源依赖解析
            ├── ExecuteForMode()      — GUI/CLI 模式分发
            ├── FindControl()         — 反射查找控件
            ├── GetControlText()      — 反射读取控件文本
            └── SetControlText()      — 反射设置控件文本
```

---

## 命令规范

### 命令格式

```
<verb> [action] [target] [key=value ...]
```

### 动词分类

| 动词 | 示例 | 说明 |
|------|------|------|
| `do` | `do click addButton` | 执行操作，action 为具体动作 |
| `get` | `get userName` | 获取值 |
| `look` | `look mainForm` | 查看结构 |
| `wait` | `wait 5000` | 等待 |
| `assert` | `assert resultLabel "Success"` | 断言验证 |
| `discover` | `discover` | 发现控件 |
| `echo` | `echo Hello World` | 输出文本（非事件命令） |
| `sleep` | `sleep 1000` | 等待毫秒数（非事件命令） |

### 规范化映射

| 简写 | 完整格式 |
|------|----------|
| `click` | `do click` |
| `set` | `do set` |
| `type` | `do type` |
| `select` | `do select` |
| `check` | `do check` |
| `uncheck` | `do uncheck` |

### 参数格式

- `key=value` 格式：`do set userName value=John`
- 无值标记：`do show details modifier=show`
- 引号支持：`echo "Hello World"`（支持转义 `\"`）
- 注释行：`#` 或 `//` 前缀的行会被跳过

### JSON 命令扩展

`JsonCommandExtensions` 提供额外的 JSON 命令解析能力：

- `JsonVerb` 枚举：Do / Get / Look / Wait / Assert
- `JsonCommand` record：基于 ParsedCommand 扩展，增加枚举动词和值类型推断
- 值类型推断：自动推断 `true` / `false` / `null` / `long` / `double` / `string`

---

## 函数执行体系

### 架构

```
FunctionRegistry          (注册表：存储委托 + 描述符)
    │
    ▼
FunctionExecutor          (执行器：参数构建 → 动态调用)
    │
    ▼
ArgumentMatcherPipeline   (参数匹配策略链)
    ├── DictionaryExactMatcher  (精确按名匹配)
    ├── PositionalMatcher       (按位匹配)
    ├── SingleValueMatcher      (单参数直接传入)
    └── DefaultValueMatcher     (默认值填充)
    │
    ▼
TypeConverter             (类型转换器)
    ├── 内置 15 种类型解析器 (string/int/long/float/double/bool 等)
    ├── 枚举转换支持
    ├── IConvertible 通用转换
    └── JSON 反序列化回退
```

### 参数匹配优先级

1. **DictionaryExactMatcher** — 如果参数是 `IDictionary`，按参数名精确匹配
2. **PositionalMatcher** — 如果参数是 `IList`，按位置顺序匹配
3. **SingleValueMatcher** — 如果函数只有一个参数，直接传入整个参数对象
4. **DefaultValueMatcher** — 未匹配的可选参数填充默认值

### 类型转换支持

内置 15 种类型的 TryParse 转换：string, int, long, short, byte, float, double, decimal, bool, char, DateTime, DateTimeOffset, TimeSpan, Guid, Version, Uri

---

## 扩展机制

### 环境适配器 (IEnvironmentAdapter)

**用途：** 将命令路由到不同的后端环境（如 WinForms、Web、API 等）。

**标准操作：**

| 操作 | 方法 | 说明 |
|------|------|------|
| Do | `DoAsync(target, value)` | 设置值/触发动作 |
| Get | `GetAsync(target)` | 获取值 |
| Look | `LookAsync(target)` | 查看结构 |
| Wait | `WaitAsync(target, condition)` | 等待条件 |
| Assert | `AssertAsync(target, expected)` | 断言验证 |
| Discover | `DiscoverAsync(filter)` | 发现控件 |

**基类：** `EnvironmentAdapterBase` 提供默认 "NotSupported" 实现，子类只需重写需要支持的操作。

### 控件值读写器 (IControlValueAccessor)

**用途：** 统一读写不同类型控件的值，支持注册式适配器。

**特性：**
- 支持泛型注册：`RegisterAdapter<TControl>(getter, setter)`
- 支持带返回值的注册：`RegisterAdapterWithResult<TControl>(getter, setterWithResult)`
- 运行时类型匹配：支持精确匹配、基类匹配、接口匹配
- 强类型获取：`GetValue<TResult>()`

### 状态存储 (IStateStore)

**用途：** 在无 UI 环境下模拟控件状态。

**实现：**
- `InMemoryStateStore` — 基于 `ConcurrentDictionary` 的线程安全实现
- `ObservableStateStore` — 包装器，在状态变更时触发事件通知

**扩展方法** (`StateStoreExtensions`)：
- `Get<T>()` / `Set<T>()` — 泛型序列化/反序列化
- `GetOrSet<T>()` — 原子性获取或添加
- `SetRange()` — 批量设置
- `GetAll()` — 获取所有值

### FileDriven 集成

`FileDrivenCommandExtensions` 将 FileDriven 特有的业务命令注册到 NonEventCommandHandler：

| 命令 | 说明 |
|------|------|
| `load` | 加载配置 |
| `close` | 关闭 |
| `show` | 显示 |
| `look` | 查看 |
| `assert` | 断言 |
| `discover` | 发现 |
| `list` | 列表 |
| `validate` | 验证 |
| `exit` | 退出 |

通过 `IFileDrivenCommandExecutor` 接口实现与 FileDriven 项目的解耦集成。

### WinForms 事件触发器

**用途：** 将 WinForms 控件事件绑定到命令引擎。

**支持的事件：**
- `Click` — 按钮点击
- `TextChanged` — 文本变更
- `SelectedIndexChanged` — 选择变更

**特性：**
- `SetControlTextAndTriggerEvent()` — 设置控件值并触发关联事件
- 支持 9 种控件类型：TextBox, ComboBox, Label, Button, CheckBox, RadioButton, ListBox, DateTimePicker, NumericUpDown
- 递归控件查找
- 事件解绑和资源释放

---

## 使用示例

### 从文件执行命令

```csharp
// commands.txt 内容：
// # 这是注释
// echo Hello World
// sleep 500
// do click submitButton

var (success, fail) = await host.ExecuteFileAsync("commands.txt");
Console.WriteLine($"成功: {success}, 失败: {fail}");
```

### 注册业务函数并执行

```csharp
CommandFrameworkUsage.Initialize();

// 注册业务函数
CommandFrameworkUsage.RegisterFunction("calculate", (double a, double b, string op) =>
{
    return op switch
    {
        "+" => a + b,
        "-" => a - b,
        "*" => a * b,
        "/" => a / b,
        _ => throw new ArgumentException($"Unknown operator: {op}")
    };
});

// 通过命令执行
var result = await CommandFrameworkUsage.ExecuteCommandAsync("echo 计算结果");
```

### 自定义环境适配器

```csharp
public class MyApiAdapter : EnvironmentAdapterBase
{
    public override string Name => "MyApi";

    public override async Task<ICommandExecutionResult> DoAsync(string target, string value)
    {
        // 调用外部 API
        var response = await httpClient.PostAsync(target, new StringContent(value));
        return new CommandExecutionResult<object>
        {
            IsSuccess = true,
            Command = $"do {target}",
            Value = await response.Content.ReadAsStringAsync()
        };
    }
}

// 注册到框架
var host = CommandFrameworkHost.Create(options =>
{
    options.EnableEnvironmentAdapter = true;
    options.EnvironmentAdapter = new MyApiAdapter();
});
```

### 自定义控件值读写器

```csharp
var accessor = new ControlValueAccessorBase();

// 注册 TextBox 适配器
accessor.RegisterAdapter<TextBox>(
    getter: tb => tb.Text,
    setter: (tb, value) => tb.Text = value?.ToString()
);

// 注册 ComboBox 适配器
accessor.RegisterAdapter<ComboBox>(
    getter: cb => cb.SelectedItem,
    setter: (cb, value) => cb.SelectedItem = value
);
```

### 使用状态存储（CLI 模式）

```csharp
var store = new InMemoryStateStore();

store.Set("userName", "Alice");
store.Set("count", "42");

var name = store.Get("userName"); // "Alice"
var exists = store.Has("count");  // true

// 泛型扩展方法
var count = store.Get<int>("count"); // 42
store.Set("score", 100);
```

### 管线模式

```csharp
// 创建默认管线
var pipeline = PipelineEntry.CreateDefaultPipeline();

// 执行单条命令
var context = new CommandContext { CurrentCommand = "do click addButton" };
await pipeline.ExecuteAsync(context);

// 使用 PipelineEntryInstance 管理共享状态
using var instance = new PipelineEntryInstance(parser, nonEventHandler);
var result = await instance.RunCommandAsync("echo Hello");
```

---

## 扩展指南

### 自定义管线阶段

```csharp
public class MyCustomStage : CommandStageBase
{
    public MyCustomStage() : base("MyCustom", "自定义阶段描述") { }

    protected override async Task ExecuteCoreAsync(CommandContext context, CancellationToken ct)
    {
        // 自定义逻辑
        context.FunctionResultValue = "processed";
        context.SetStageResult(Name, true);
    }
}

// 注册到管线
var pipeline = PipelineEntry.CreateDefaultPipeline(configure: p =>
{
    p.Register(new MyCustomStage());
});
```

### 自定义非事件命令处理器

```csharp
var handler = new NonEventCommandHandler();

handler.RegisterHandler("mycommand", async cmd =>
{
    // 自定义处理逻辑
    return CommandExecutionResult<object?>.Success($"Processed: {cmd.Target}");
});
```

### 自定义中间件

```csharp
public class MyMiddleware : IMiddleware<CommandContext, CommandExecutionResult<object?>>
{
    public async Task<CommandExecutionResult<object?>> InvokeAsync(
        CommandContext context,
        Func<CommandContext, Task<CommandExecutionResult<object?>>> next)
    {
        Console.WriteLine($"Before: {context.CurrentCommand}");
        var result = await next(context);
        Console.WriteLine($"After: {result.IsSuccess}");
        return result;
    }
}

// 注册
pipeline.Use<MyMiddleware>();
```

---

## 设计模式

| 模式 | 使用位置 |
|------|----------|
| **策略模式** | ArgumentMatcherPipeline 中的四种匹配策略 |
| **模板方法模式** | PipelineStageBase.ExecuteAsync → ExecuteCoreAsync |
| **工厂模式** | CommandRegistry 延迟创建 ICommand 实例 |
| **单例模式** | CommandFrameworkUsage 全局单例 |
| **装饰器模式** | ObservableStateStore 包装 IStateStore |
| **适配器模式** | ControlValueAccessorBase 的注册式适配器 |
| **管道-过滤器模式** | StagePipeline 的管线阶段链 |
| **命令模式** | ICommand / DelegateCommand / CompositeCommand |
| **外观模式** | CommandFrameworkHost 统一入口 |
| **洋葱模型** | MiddlewarePipeline 中间件管线 |

---

## 关键设计决策

1. **接口与实现分离** — 所有核心契约定义在 `ICommand.cs` 中，实现分散在 `Execution/` 目录
2. **双执行路径** — Engine 路径适合轻量场景，Pipeline 路径适合复杂业务
3. **无编译时依赖** — 通过 `Func` 委托和 `SharedData` 字典解耦，避免循环引用
4. **线程安全** — `CommandFrameworkUsage` 使用双重检查锁，`InMemoryStateStore` 使用 `ConcurrentDictionary`
5. **渐进式初始化** — `CommandFrameworkOptions` 支持按需启用/禁用各组件

---

## 项目依赖

```
CommandFramework.csproj
    └── Share/Pipeline/Pipeline.csproj    (管线基础类型)
```

---

## 相关文档

| 分类 | 文档 | 说明 |
|------|------|------|
| **设计文档** | [设计文档](docs/design/design-document.md) | 框架设计背景、动机、详细接口定义 |
| | [控件处理方案](docs/design/control-handling-design.md) | 通用控件查找与值写入逻辑设计方案 |
| | [字符串处理方案](docs/design/string-processing-plan.md) | 字符串拆分与参数处理统一方案 |
| **重构方案** | [重构方案](docs/refactoring/refactoring-plan.md) | 与 EventTriggerFramework 的重复类型消除方案 |
| | [中间件管线方案](docs/refactoring/middleware-pipeline-plan.md) | 洋葱模型中间件管线等补充设计 |
| **工作流** | [工作流重构分析](docs/workflow/workflow-refactoring-analysis.md) | WorkflowExecution 基于 ExecutionFramework 的优化重构分析 |
| | [节点处理提取分析](docs/workflow/nodeprocess-extraction-analysis.md) | NodeProcess 通用逻辑提取到 WorkflowExecution 的分析报告 |
| **集成分析** | [集成分析报告](docs/integration/integration-analysis.md) | CommandFramework 两套管线系统整合分析报告 |

---

## 许可证

本项目基于 MIT 许可证开源。
