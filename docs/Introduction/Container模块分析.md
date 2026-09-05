# Container（通用执行容器框架）模块分析

> **模块路径**：`CsharpPrj/BasicMethod/Container`  
> **所属层**：Layer 1（基础层）  
> **技术栈**：MediatR + Polly  
> **设计哲学**：万物皆函数，一切皆配置

---

## 一、模块概述

Container 是一个**通用执行容器框架**，基于 MediatR + Polly 构建。它提供了一套标准化的命令执行基础设施，将"执行命令"这一通用能力抽象为可插拔的容器组件。

### 核心特性

- ✅ **双层容器模型**：原子执行单元（Cell）+ 引擎容器（Engine）
- ✅ **弹性策略管道**：重试、熔断、超时、日志开箱即用
- ✅ **不可变消息契约**：所有输入输出通过 record 传递，线程安全
- ✅ **外部框架桥接**：通过 `IRuntimeContext` 解耦，不依赖任何外部类型
- ✅ **一键 DI 注册**：`AddContainer()` 扩展方法，配置灵活

---

## 二、核心架构：双层容器模型

```
┌─────────────────────────────────────────────────┐
│  IExecutionEngine (引擎容器)                      │
│  管理生命周期：Idle → Running → Completed         │
│  ┌─────────────────────────────────────────────┐ │
│  │  IExecutionCell (执行容器/原子执行单元)        │ │
│  │  输入 ExecutionRequest → 输出 ExecutionResult │ │
│  │  ┌─────────────────────────────────────────┐ │ │
│  │  │  ExecuteCommandHandler (命令处理器)       │ │ │
│  │  │  三阶段管线: Parse → Execute → Collect   │ │ │
│  │  └─────────────────────────────────────────┘ │ │
│  └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

### 2.1 IExecutionCell — 原子执行单元

最基础的执行容器，无状态设计，每次 `ExecuteAsync` 完全独立。

```csharp
public interface IExecutionCell
{
    string Name { get; }
    Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default);
}
```

内部委托给 **MediatR** 的 `IMediator.Send`，自动经过所有注册的 PipelineBehavior。

### 2.2 IExecutionEngine — 引擎容器

管理整个执行环境的**生命周期**，提供状态变更事件。

```csharp
public interface IExecutionEngine : IDisposable
{
    string Name { get; }
    ContainerStatus Status { get; }
    Task<EngineExecutionResult> RunAsync(
        EngineRequest request,
        CancellationToken cancellationToken = default);
    event EventHandler<EngineStateChangedEventArgs>? StateChanged;
}
```

执行流程：`InitializeAsync` → `ExecuteAsync`（委托给 Cell）

---

## 三、弹性策略（PipelineBehavior 链）

通过 MediatR 的 `IPipelineBehavior` 机制，4 种弹性策略按顺序包裹命令执行：

```
请求 → Logging → Timeout → Retry → CircuitBreaker → Handler → 响应
```

| 策略 | 类名 | 默认配置 | 说明 |
|------|------|---------|------|
| 📝 日志 | `LoggingBehavior` | — | 记录每次执行的耗时和结果 |
| ⏱ 超时 | `TimeoutBehavior` | 30 秒 | 防止命令执行时间过长 |
| 🔄 重试 | `RetryBehavior` | 3 次，100ms 递增 | 失败自动重试 |
| ⛔ 熔断 | `CircuitBreakerBehavior` | 5 次/30 秒 | 连续失败后快速失败 |

所有策略可通过 `ContainerOptions` 配置启用/禁用及参数调整。

---

## 四、命令处理管线（ExecuteCommandHandler）

`ExecuteCommandHandler` 实现了 MediatR 的 `IRequestHandler<ExecutionRequest, ExecutionResult>`，内部采用**三阶段管线**：

### 阶段 1：ParseCommand — 解析命令文本

按空格分割为 `{action} {target} {value}` 三部分。

### 阶段 2：ExecuteCommand — 根据命令类型分发执行

```
命令文本 → 解析 action
    ├── 非事件命令 (exec/set/get/wait/assert/print/log/run/include/sideload/comment/#)
    │   └── 通过 INonEventCommandLookup 查找 handler 并执行
    └── 事件命令 (其他)
        └── 通过 IEventBindingResolver 查找绑定 → IFunctionExecutor 执行函数
```

### 阶段 3：ResultCollect — 收集执行结果

返回 `ExecutionResult`（Success / OutputValue / ErrorMessage / ExecutionTime）。

---

## 五、外部框架桥接（IRuntimeContext）

Container 通过 `IRuntimeContext` 接口实现与外部框架的解耦：

```csharp
public interface IRuntimeContext
{
    object? GetService(Type serviceType);
    T? GetService<T>() where T : class;
    IDictionary<string, object?> Items { get; }
}
```

### 定义的桥接接口

| 接口 | 用途 |
|------|------|
| `INonEventCommandLookup` | 非事件命令查找（exec/set/get 等） |
| `IEventBindingResolver` | 事件绑定解析（查找控件的事件绑定） |
| `IFunctionExecutor` | 函数执行器（执行绑定的函数） |

**设计意义**：Container **不直接依赖**任何外部类型，外部框架只需实现这些接口即可接入。

---

## 六、消息契约（不可变 record）

所有输入输出通过不可变 record 传递，无 SharedData：

### ExecutionRequest

```csharp
public record ExecutionRequest(
    string CommandText,
    IReadOnlyDictionary<string, object>? Parameters = null,
    string? Source = null,
    string? ScriptPath = null,
    ExecutionMode Mode = ExecutionMode.Cli,
    IRuntimeContext? RuntimeContext = null
) : IRequest<ExecutionResult>;
```

### ExecutionResult

```csharp
public record ExecutionResult(
    string CommandText,
    bool Success,
    string? OutputValue,
    string? ErrorMessage,
    TimeSpan ExecutionTime,
    string? BoundFunctionName = null
)
{
    public static ExecutionResult Ok(string cmd, string? value = null) => ...;
    public static ExecutionResult Fail(string cmd, string error) => ...;
}
```

### EngineRequest / EngineExecutionResult

```csharp
public record EngineRequest(string Mode, string? ConfigPath = null, ...);
public record EngineExecutionResult(bool Success, TimeSpan TotalExecutionTime, ...);
```

### 枚举

| 枚举 | 值 |
|------|-----|
| `ExecutionMode` | Cli, Gui, Test, Event |
| `ContainerStatus` | Idle, Running, Completed, Failed, Cancelled |

---

## 七、DI 注册

通过 `ServiceCollectionExtensions.AddContainer()` 扩展方法一键注册：

```csharp
services.AddContainer(options =>
{
    options.EnableRetry = true;           // 启用重试
    options.EnableCircuitBreaker = false;  // 禁用熔断
    options.EnableTimeout = true;         // 启用超时
    options.EnableLogging = true;         // 启用日志
    options.MaxRetries = 3;
    options.RetryDelayMs = 100;
    options.TimeoutSeconds = 30;
    options.CircuitBreakerThreshold = 5;
    options.CircuitBreakerDurationSeconds = 30;
});
```

### ContainerOptions 完整配置

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `EnableRetry` | true | 是否启用重试策略 |
| `EnableCircuitBreaker` | false | 是否启用熔断策略 |
| `EnableTimeout` | true | 是否启用超时策略 |
| `EnableLogging` | true | 是否启用日志记录 |
| `MaxRetries` | 3 | 最大重试次数 |
| `RetryDelayMs` | 100 | 重试间隔基数（毫秒） |
| `TimeoutSeconds` | 30 | 超时时间（秒） |
| `CircuitBreakerThreshold` | 5 | 熔断前允许的连续失败次数 |
| `CircuitBreakerDurationSeconds` | 30 | 熔断持续时间（秒） |

---

## 八、应用场景

### 场景 A：FileDriven 配置驱动执行

```
配置文件中的命令 → FileDriven 解析
    → 构造 ExecutionRequest（带 IRuntimeContext）
    → Container 执行（经过重试/超时/日志）
    → 通过 IRuntimeContext 回调 FileDriven 的 handler
    → 返回 ExecutionResult
```

### 场景 B：GUI 按钮点击

```
用户点击按钮 → EventTriggerFramework 捕获
    → 构造 ExecutionRequest
    → Container 执行
    → 通过 IEventBindingResolver 找到绑定的函数
    → 通过 IFunctionExecutor 执行函数
    → 返回结果更新 UI
```

### 场景 C：CLI 命令工具

```
用户输入命令 → CLI 入口
    → 构造 EngineRequest
    → ExecutionEngine.RunAsync（管理生命周期）
    → ExecutionCell.ExecuteAsync（执行具体命令）
    → 输出结果
```

---

## 九、在项目架构中的位置

```
Layer 0: 辅助/测试
Layer 1: 基础层 ← Container 在这里
    ├── BasicMethod (基础方法)
    │   └── Container (通用执行容器框架) ← ★
Layer 2: 模块层
Layer 3: 共享层
Layer 4: 操作层 ← FileDriven、EventTriggerFramework 在这里
Layer 5: 应用层
```

Container 位于 Layer 1（基础层），意味着它是**被上层依赖的基础设施**，不依赖任何业务逻辑。

---

## 十、设计亮点总结

1. **双层容器分离**：Cell 负责单次执行，Engine 负责生命周期管理，职责清晰
2. **MediatR 管道**：利用成熟的 MediatR 管道模式，弹性策略可插拔
3. **Polly 弹性策略**：重试、熔断、超时开箱即用，配置灵活
4. **IRuntimeContext 桥接**：Container 不依赖外部类型，通过接口适配外部框架
5. **不可变消息**：所有输入输出使用 record 类型，线程安全
6. **从 FileDriven 迁移**：三阶段管线逻辑从 FileDriven.Execution 迁移而来，统一了命令执行入口

---

## 十一、模块文件结构

```
Container/
├── Container.csproj              # 项目文件（MediatR + Polly 依赖）
├── Abstractions/
│   ├── IExecutionCell.cs         # 原子执行容器接口
│   ├── IExecutionEngine.cs       # 引擎容器接口
│   ├── IRuntimeContext.cs        # 运行时依赖上下文接口
│   └── Messages.cs               # 消息契约（record + 枚举）
├── Core/
│   ├── ExecutionCell.cs          # 执行容器实现
│   ├── ExecutionEngine.cs        # 引擎容器实现
│   └── ExecuteCommandHandler.cs  # 命令处理器（三阶段管线）
├── Behaviors/
│   ├── LoggingBehavior.cs        # 日志策略
│   ├── RetryBehavior.cs          # 重试策略
│   ├── CircuitBreakerBehavior.cs # 熔断策略
│   └── TimeoutBehavior.cs        # 超时策略
└── Extensions/
    └── ServiceCollectionExtensions.cs  # DI 注册扩展
```

---

> **一句话总结**：Container 是 WorkFlowPrj 的"命令执行标准化插座"——上层模块只需说"我要执行什么命令"，Container 负责"怎么执行"（解析、分发、重试、熔断、超时、日志），并通过 IRuntimeContext 桥接外部框架，实现完全解耦。
