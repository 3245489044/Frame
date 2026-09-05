# ExecutionContainer 通用执行容器框架 — 设计方案

> **版本**: v1.0  
> **日期**: 2026-06-29  
> **状态**: 设计阶段

---

## 目录

1. [背景与问题](#1-背景与问题)
2. [设计目标](#2-设计目标)
3. [数学基础：容器的代数定义](#3-数学基础容器的代数定义)
4. [技术选型](#4-技术选型)
5. [架构设计](#5-架构设计)
6. [消息契约](#6-消息契约)
7. [容器接口](#7-容器接口)
8. [容器实现](#8-容器实现)
9. [PipelineBehavior（基于 Polly）](#9-pipelinebehavior基于-polly)
10. [DI 注册](#10-di-注册)
11. [与当前架构的对比](#11-与当前架构的对比)
12. [迁移路径](#12-迁移路径)
13. [附录：NuGet 包信息](#13-附录nuget-包信息)

---

## 1. 背景与问题

### 1.1 当前 Execution 架构的问题

`FileDriven.Execution` 中的执行逻辑存在以下核心问题：

| 问题 | 具体表现 | 影响 |
|------|---------|------|
| **上帝对象** | `FileDrivenPipelineContext`（639 行）承载 30+ 属性 | 新增字段需同步更新 Save/Restore |
| **状态泄露** | `SharedData` 万能口袋传递隐式依赖 | 依赖关系不透明，调试困难 |
| **硬编码流程** | 9 步委托 + PipelineFactory 固定阶段顺序 | 难以扩展和复用 |
| **类型转换泛滥** | 4 种 ExecutionResult 类型互相转换 | 转换代码重复，容易遗漏 |
| **Reset 脆弱性** | 11 字段 Save/Restore，新增字段易遗漏 | 运行时错误难以排查 |

### 1.2 根因分析

```
FileDrivenPipelineContext（639 行）
├── 脚本加载状态（ScriptPath, CommandFilePath, ScriptLoadSuccess）
├── 命令解析状态（ParsedCommand, ParseSuccess, ParseErrorMessage）
├── 函数执行状态（FunctionResultValue, FunctionExecuteSuccess, FunctionErrorMessage）
├── 执行统计（TotalCommands, SuccessCount, FailCount, CommandResults）
├── 运行时依赖（BindingResolver, FunctionExecutor, TargetForm, VirtualStore, LoadedConfigs）
└── 日志/批次数据（BatchData, BatchId, BatchName...）
```

**核心矛盾**：一个类同时承载了 6 种不同职责，且通过 `SaveRuntimeDependencies/RestoreRuntimeDependencies` 手动保护 11 个字段。

---

## 2. 设计目标

### 2.1 核心目标

> **把执行构建为容器，运行状态和输入输出完全解耦。**

### 2.2 具体目标

1. **状态封装** — 运行状态完全封装在容器内部，不对外暴露
2. **消息驱动** — 输入输出通过不可变 record 传递，无 SharedData
3. **依赖注入** — 运行时依赖通过构造函数注入，无隐式依赖
4. **可复用** — 框架放在 BasicMethod 层，所有上层项目可用
5. **复用生态** — 基于 MediatR + Polly 等成熟 NuGet 包

### 2.3 非目标

- 不替代现有的 `FileDriven.Execution`（逐步迁移）
- 不引入新的 ORM 或持久化框架
- 不改变现有的命令解析和执行逻辑
- 不实现容器组合子（ForEach、Switch 等）

---

## 3. 数学基础：容器的代数定义

容器的本质是 **函数式编程中的 Functor（函子）和 Monad（单子）** 的具象化。

### 3.1 Functor（函子）— 映射

```
F<T> 支持 Select:  F<T> → (T → U) → F<U>
```

对应到容器框架：`IExecutionCell` 将 `ExecutionRequest` 映射为 `ExecutionResult`。

### 3.2 Monad（单子）— 扁平化

```
F<T> 支持 Bind:   F<T> → (T → F<U>) → F<U>
```

对应到容器框架：`IExecutionPipeline` 组合多个 `IExecutionCell`，扁平化嵌套执行。

### 3.3 容器代数定律

```
// 恒等律
Cell(Id) ∘ Execute = Execute
Execute ∘ Cell(Id) = Execute

// 结合律
(Cell(A) ∘ Cell(B)) ∘ Cell(C) = Cell(A) ∘ (Cell(B) ∘ Cell(C))
```

### 3.4 数学概念映射

| 数学概念 | 容器框架对应 | 说明 |
|---------|-------------|------|
| **Functor** | `IExecutionCell` | `ExecuteAsync(request)` 将 `ExecutionRequest` 映射为 `ExecutionResult` |
| **Monad** | `IExecutionPipeline` | 组合多个 `IExecutionCell`，扁平化嵌套执行 |
| **Identity** | `ExecutionCell` | 最原子的执行单元 |
| **Composition** | `ExecutionPipeline` | 按顺序组合多个 Cell |

---

## 4. 技术选型

### 4.1 NuGet 包选择

| NuGet 包 | 版本 | 用途 | 评分 |
|----------|------|------|------|
| **MediatR** | latest | 请求/响应模式 + PipelineBehavior 管线 | 82.69 |
| **Polly** | latest | 弹性策略（Retry、CircuitBreaker、Timeout） | 84.71 |

### 4.2 选型理由

**MediatR**：
- 成熟的进程内消息中介模式
- `IRequest<TResponse>` / `IRequestHandler<TRequest, TResponse>` 天然对应 `ExecutionRequest` / `ExecutionResult`
- `IPipelineBehavior` 机制天然支持容器组合子模式
- 社区广泛使用，生态成熟

**Polly**：
- .NET 生态最成熟的弹性策略库
- 支持 Retry、CircuitBreaker、Timeout、Fallback、Bulkhead 等策略
- 与 MediatR 的 PipelineBehavior 无缝集成
- 线程安全，支持异步

### 4.3 依赖关系

```
ExecutionContainer.csproj
├── MediatR（请求/响应 + PipelineBehavior）
├── Polly（弹性策略）
└── BasicMethod.csproj（项目引用，获取基础类型）
```

---

## 5. 架构设计

### 5.1 项目结构

```
CsharpPrj/BasicMethod/
├── ExecutionContainer/                    ← 新增：通用执行容器框架
│   ├── ExecutionContainer.csproj          ← 项目文件
│   ├── Abstractions/                      ← 容器接口 + 消息契约
│   │   ├── Messages.cs                    ← ExecutionRequest / ExecutionResult / PipelineResult
│   │   ├── IExecutionCell.cs              ← 原子执行单元接口
│   │   ├── IExecutionPipeline.cs          ← 管线容器接口
│   │   └── IExecutionEngine.cs            ← 引擎容器接口
│   ├── Core/                              ← 容器实现
│   │   ├── ExecutionCell.cs               ← 基于 MediatR IRequestHandler 实现
│   │   ├── ExecutionPipeline.cs           ← 管线容器实现
│   │   └── ExecutionEngine.cs             ← 引擎容器实现
│   ├── Behaviors/                         ← MediatR IPipelineBehavior 实现
│   │   ├── RetryBehavior.cs               ← 基于 Polly Retry
│   │   ├── CircuitBreakerBehavior.cs      ← 基于 Polly CircuitBreaker
│   │   ├── TimeoutBehavior.cs             ← 基于 Polly Timeout
│   │   └── LoggingBehavior.cs             ← 日志记录
│   └── Extensions/                        ← 扩展方法
│       └── ServiceCollectionExtensions.cs ← DI 注册扩展
```

### 5.2 架构分层

```
┌──────────────────────────────────────────────────────────────┐
│  ① 项目自定义容器接口（IExecutionCell / IExecutionPipeline）  │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  ② MediatR 适配层（IRequestHandler → IExecutionCell）   │  │
│  │  ┌──────────────────────────────────────────────────┐  │  │
│  │  │  ③ MediatR PipelineBehavior（容器组合子引擎）      │  │  │
│  │  │  ┌────────────────────────────────────────────┐  │  │  │
│  │  │  │  ④ Polly 弹性策略（Retry/CircuitBreaker）   │  │  │  │
│  │  │  └────────────────────────────────────────────┘  │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### 5.3 三层容器模型

```
┌──────────────────────────────────────────────────────────┐
│                    ExecutionEngine                        │
│  ┌──────────────┐    ┌──────────────────────────────┐    │
│  │  Lifecycle    │    │      ModeDispatcher          │    │
│  │  Init/Shutdown│    │  ┌─────────┐ ┌───────────┐  │    │
│  └──────┬───────┘    │  │CLI Runner│ │GUI Runner │  │    │
│         │            │  └────┬─────┘ └─────┬─────┘  │    │
│         ▼            └───────┼──────────────┘        │    │
│  ┌───────────────────────────┘                       │    │
│  │  ExecutionPipeline                                 │    │
│  │  ┌────────────────────────────────────────────┐    │    │
│  │  │ ScriptLoader → [ExecutionCell × N] →       │    │    │
│  │  │ ResultCollector                            │    │    │
│  │  └────────────────────────────────────────────┘    │    │
│  │                                                     │    │
│  │  ExecutionCell（无状态，可池化）                       │    │
│  │  ┌────────────────────────────────────────────┐    │    │
│  │  │ Parse → Execute → BuildResult              │    │    │
│  │  │ (内部状态完全私有，无 SharedData)             │    │    │
│  │  └────────────────────────────────────────────┘    │    │
│  └─────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────┘

数据流（单向）：
  ExecutionRequest → ExecutionCell → ExecutionResult
  ExecutionRequest → ExecutionPipeline → PipelineExecutionResult
  EngineRequest    → ExecutionEngine  → EngineExecutionResult
```

---

## 6. 消息契约

### 6.1 执行请求（输入）

```csharp
/// <summary>
/// 执行请求 — 不可变输入消息
/// 同时实现 MediatR 的 IRequest<ExecutionResult>，可直接通过 MediatR 发送
/// </summary>
public record ExecutionRequest(
    string CommandText,
    IReadOnlyDictionary<string, object>? Parameters = null,
    string? Source = null,          // "script" / "manual" / "event"
    string? ScriptPath = null,
    ExecutionMode Mode = ExecutionMode.Cli
) : IRequest<ExecutionResult>;
```

### 6.2 执行结果（输出）

```csharp
/// <summary>
/// 执行结果 — 不可变输出消息
/// </summary>
public record ExecutionResult(
    string CommandText,
    bool Success,
    string? OutputValue,
    string? ErrorMessage,
    TimeSpan ExecutionTime,
    string? BoundFunctionName = null
)
{
    public static ExecutionResult Success(string cmd, string? value = null)
        => new(cmd, true, value, null, TimeSpan.Zero);

    public static ExecutionResult Failed(string cmd, string error)
        => new(cmd, false, null, error, TimeSpan.Zero);
}
```

### 6.3 管线执行结果

```csharp
/// <summary>
/// 管线执行结果 — 包含多条命令的执行统计
/// </summary>
public record PipelineExecutionResult(
    IReadOnlyList<ExecutionResult> Results,
    bool OverallSuccess,
    int TotalCount,
    int SuccessCount,
    int FailCount,
    TimeSpan TotalExecutionTime,
    string? BatchId = null
);
```

### 6.4 引擎请求/结果

```csharp
public record EngineRequest(
    string Mode,                        // "GUI" / "CLI" / "Test"
    string? ConfigPath = null,
    IReadOnlyDictionary<string, object>? GlobalParameters = null
);

public record EngineExecutionResult(
    bool Success,
    IReadOnlyList<PipelineExecutionResult> PipelineResults,
    TimeSpan TotalExecutionTime,
    string? ErrorMessage = null
);
```

### 6.5 状态枚举

```csharp
public enum ExecutionMode
{
    Cli = 0,
    Gui = 1,
    Test = 2,
    Event = 3
}

public enum ContainerStatus
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
```

---

## 7. 容器接口

### 7.1 IExecutionCell — 原子执行容器

```csharp
/// <summary>
/// 执行容器 — 最原子的执行单元
/// 输入 ExecutionRequest → 输出 ExecutionResult
/// 运行状态完全封装在容器内部
/// 无状态设计：每次 ExecuteAsync 都是独立的
/// </summary>
public interface IExecutionCell
{
    /// <summary>容器名称</summary>
    string Name { get; }

    /// <summary>异步执行</summary>
    Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken ct = default);
}
```

### 7.2 IExecutionPipeline — 管线容器

```csharp
/// <summary>
/// 管线容器 — 编排多个 ExecutionCell
/// 支持顺序执行、条件分支、并行执行等组合模式
/// </summary>
public interface IExecutionPipeline : IDisposable
{
    /// <summary>管线名称</summary>
    string Name { get; }

    /// <summary>异步执行管线</summary>
    Task<PipelineExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        IProgress<ContainerProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>获取运行状态快照（只读）</summary>
    ContainerStatus GetStatus();
}
```

### 7.3 IExecutionEngine — 引擎容器

```csharp
/// <summary>
/// 引擎容器 — 管理整个执行环境的生命周期
/// 包含初始化、运行、关闭等生命周期管理
/// </summary>
public interface IExecutionEngine : IDisposable
{
    /// <summary>引擎名称</summary>
    string Name { get; }

    /// <summary>异步运行引擎</summary>
    Task<EngineExecutionResult> RunAsync(
        EngineRequest request,
        CancellationToken ct = default);

    /// <summary>生命周期状态变更事件</summary>
    event EventHandler<EngineStateChangedEventArgs>? StateChanged;
}
```

---

## 8. 容器实现

### 8.1 ExecutionCell — 基于 MediatR

```csharp
/// <summary>
/// 执行容器实现 — 基于 MediatR 的 IRequestHandler
/// 运行时依赖通过构造函数注入（由 DI 容器管理）
/// 内部状态完全私有，无 SharedData，无 Save/Restore
/// </summary>
public class ExecutionCell : IExecutionCell
{
    private readonly IMediator _mediator;
    private readonly string _name;

    public string Name => _name;

    public ExecutionCell(IMediator mediator, string? name = null)
    {
        _mediator = mediator;
        _name = name ?? "ExecutionCell";
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request, CancellationToken ct)
    {
        // 委托给 MediatR，自动经过所有 PipelineBehavior
        return await _mediator.Send(request, ct);
    }
}
```

### 8.2 ExecuteCommandHandler — 实际命令处理器

```csharp
/// <summary>
/// 命令处理器 — 实现 MediatR 的 IRequestHandler
/// 内部状态完全私有，无 SharedData，无 Save/Restore
/// </summary>
public class ExecuteCommandHandler : IRequestHandler<ExecutionRequest, ExecutionResult>
{
    private readonly IFunctionExecutor? _functionExecutor;
    private readonly IEventBindingResolver? _bindingResolver;

    // 运行时依赖通过构造函数注入
    public ExecuteCommandHandler(
        IFunctionExecutor? functionExecutor = null,
        IEventBindingResolver? bindingResolver = null)
    {
        _functionExecutor = functionExecutor;
        _bindingResolver = bindingResolver;
    }

    public async Task<ExecutionResult> Handle(
        ExecutionRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // 解析命令 → 执行 → 返回结果
            // 无状态！无 SharedData！无 Save/Restore！
            var result = await ExecuteCoreAsync(request, ct);
            sw.Stop();
            return result with { ExecutionTime = sw.Elapsed };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExecutionResult.Failed(
                request.CommandText, ex.Message) with { ExecutionTime = sw.Elapsed };
        }
    }

    private Task<ExecutionResult> ExecuteCoreAsync(
        ExecutionRequest request, CancellationToken ct)
    {
        // 实际的命令解析和执行逻辑
        // 从 FileDriven.Execution 迁移过来
        throw new NotImplementedException();
    }
}
```

### 8.3 内部状态类

```csharp
/// <summary>
/// 执行容器内部状态 — 完全不对外暴露
/// 没有 Save/Restore！没有 SharedData！没有 11 字段保护！
/// </summary>
internal class ExecutionCellState
{
    public ParsedCommand? ParsedCommand { get; set; }
    public bool ParseSuccess { get; set; }
    public bool ExecutionSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public Stopwatch Stopwatch { get; } = new();

    public void Reset()
    {
        ParsedCommand = null;
        ParseSuccess = false;
        ExecutionSuccess = false;
        ErrorMessage = null;
        Stopwatch.Reset();
    }
}
```

---

## 9. PipelineBehavior（基于 Polly）

### 9.1 RetryBehavior — 重试策略

```csharp
/// <summary>
/// 重试行为 — 基于 Polly Retry 策略
/// 作为 MediatR 的 IPipelineBehavior，自动包裹所有 ExecutionRequest
/// </summary>
public class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ExecutionRequest
{
    private readonly IAsyncPolicy<TResponse> _retryPolicy;

    public RetryBehavior(int maxRetries = 3, int delayMs = 100)
    {
        _retryPolicy = Policy<TResponse>
            .Handle<Exception>()
            .WaitAndRetryAsync(maxRetries,
                retryAttempt => TimeSpan.FromMilliseconds(delayMs * retryAttempt));
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync(() => next());
    }
}
```

### 9.2 CircuitBreakerBehavior — 熔断策略

```csharp
/// <summary>
/// 熔断行为 — 基于 Polly CircuitBreaker
/// 连续失败达到阈值后快速失败，避免资源耗尽
/// </summary>
public class CircuitBreakerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ExecutionRequest
{
    private readonly IAsyncPolicy<TResponse> _circuitBreaker;

    public CircuitBreakerBehavior(
        int exceptionsAllowedBeforeBreaking = 5,
        int durationOfBreakSeconds = 30)
    {
        _circuitBreaker = Policy<TResponse>
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking,
                TimeSpan.FromSeconds(durationOfBreakSeconds));
    }

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        return await _circuitBreaker.ExecuteAsync(() => next());
    }
}
```

### 9.3 TimeoutBehavior — 超时策略

```csharp
/// <summary>
/// 超时行为 — 基于 Polly Timeout
/// 防止命令执行时间过长
/// </summary>
public class TimeoutBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ExecutionRequest
{
    private readonly IAsyncPolicy<TResponse> _timeoutPolicy;

    public TimeoutBehavior(int timeoutSeconds = 30)
    {
        _timeoutPolicy = Policy<TResponse>
            .TimeoutAsync<TResponse>(timeoutSeconds);
    }

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        return await _timeoutPolicy.ExecuteAsync(() => next());
    }
}
```

### 9.4 LoggingBehavior — 日志记录

```csharp
/// <summary>
/// 日志行为 — 记录每次执行的耗时和结果
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ExecutionRequest
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            sw.Stop();
            _logger.LogInformation(
                "命令 {Command} 执行完成，耗时 {Elapsed}ms",
                request.CommandText, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "命令 {Command} 执行失败，耗时 {Elapsed}ms",
                request.CommandText, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
```

### 9.5 PipelineBehavior 执行顺序

```
ExecutionRequest
    │
    ▼
┌─────────────────────┐
│  LoggingBehavior    │  ← 最外层：记录日志
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│  TimeoutBehavior    │  ← 超时保护
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│  RetryBehavior      │  ← 重试策略
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│ CircuitBreaker      │  ← 熔断保护
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│  ExecuteCommand     │  ← 实际命令处理器
│  Handler            │
└─────────────────────┘
          │
          ▼
   ExecutionResult
```

---

## 10. DI 注册

### 10.1 配置选项

```csharp
public class ExecutionContainerOptions
{
    public bool EnableRetry { get; set; } = true;
    public bool EnableCircuitBreaker { get; set; } = false;
    public bool EnableTimeout { get; set; } = true;
    public bool EnableLogging { get; set; } = true;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
    public int TimeoutSeconds { get; set; } = 30;
}
```

### 10.2 服务注册扩展

```csharp
public static class ExecutionContainerExtensions
{
    public static IServiceCollection AddExecutionContainer(
        this IServiceCollection services,
        Action<ExecutionContainerOptions>? configure = null)
    {
        var options = new ExecutionContainerOptions();
        configure?.Invoke(options);

        // 注册 MediatR
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ExecuteCommandHandler>();

            // 按配置添加 PipelineBehavior（顺序决定执行顺序）
            if (options.EnableLogging)
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            if (options.EnableTimeout)
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TimeoutBehavior<,>));
            if (options.EnableRetry)
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));
            if (options.EnableCircuitBreaker)
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(CircuitBreakerBehavior<,>));
        });

        // 注册容器
        services.AddTransient<IExecutionCell, ExecutionCell>();
        services.AddTransient<IExecutionPipeline, ExecutionPipeline>();
        services.AddSingleton<IExecutionEngine, ExecutionEngine>();

        return services;
    }
}
```

### 10.3 使用示例

```csharp
// Program.cs — 注册
services.AddExecutionContainer(options =>
{
    options.EnableRetry = true;
    options.MaxRetries = 3;
    options.EnableCircuitBreaker = true;
    options.EnableLogging = true;
});

// 使用
public class MyService
{
    private readonly IExecutionCell _cell;

    public MyService(IExecutionCell cell)
    {
        _cell = cell;
    }

    public async Task RunAsync()
    {
        var result = await _cell.ExecuteAsync(new ExecutionRequest(
            CommandText: "MyCommand",
            Parameters: new Dictionary<string, object> { ["key"] = "value" },
            Source: "manual"
        ));

        if (result.Success)
            Console.WriteLine($"成功: {result.OutputValue}");
        else
            Console.WriteLine($"失败: {result.ErrorMessage}");
    }
}
```

---

## 11. 与当前架构的对比

### 11.1 核心差异

| 维度 | 当前（FileDriven.Execution） | 新框架（ExecutionContainer） |
|------|---------------------------|-----------------------------|
| **位置** | FileDriven 应用层 | BasicMethod 基础层 |
| **上下文对象** | FileDrivenPipelineContext（639 行） | ❌ 不存在 |
| **状态保护** | Save/Restore 11 字段 | 不需要，状态在 Handler 内部 |
| **输入输出** | 通过 Context 属性传递 | 不可变 record 消息 |
| **隐式依赖** | SharedData["TargetForm"] | 构造函数注入（DI 容器） |
| **弹性策略** | 自建 try/catch | Polly Retry/CircuitBreaker/Timeout |
| **可复用性** | 仅 FileDriven | **所有项目** |
| **依赖** | 依赖 Share、Operation 等 | MediatR + Polly（成熟 NuGet） |

### 11.2 文件对比

| 当前文件 | 行数 | 新框架对应 | 状态 |
|---------|------|-----------|------|
| `FileDrivenPipelineContext.cs` | 639 | ❌ 不存在 | 删除 |
| `ExecutionPipelineEntry.cs` | 655 | `ExecutionPipeline.cs` | 重写 |
| `FileDrivenExecutor.cs` | ~500 | `ExecutionEngine.cs` | 重写 |
| `ExecutionHelper.cs` | ~200 | ❌ 不需要 | 删除 |
| `PipelineFactory.cs` | ~150 | DI 容器自动装配 | 删除 |
| `StageBase.cs` | ~100 | `ExecuteCommandHandler.cs` | 重写 |

### 11.3 收益

1. **FileDrivenPipelineContext（639 行）→ 消失**
2. **不再有 SaveRuntimeDependencies/RestoreRuntimeDependencies（11 字段保护）**
3. **不再有 SharedData 隐式依赖传递**
4. **输入输出通过不可变 record 类型安全传递**
5. **容器可独立测试、独立复用**
6. **新增字段不需要同步更新任何 Save/Restore 方法**
7. **弹性策略由 Polly 提供，无需自建**

---

## 12. 迁移路径

### Phase 0：新建项目

- 新建 `BasicMethod/ExecutionContainer/ExecutionContainer.csproj`
- 添加 NuGet 引用：MediatR、Polly
- 添加 ProjectReference：BasicMethod.csproj

### Phase 1：定义消息契约

- `Abstractions/Messages.cs` — ExecutionRequest、ExecutionResult、PipelineExecutionResult 等

### Phase 2：定义容器接口

- `Abstractions/IExecutionCell.cs`
- `Abstractions/IExecutionPipeline.cs`
- `Abstractions/IExecutionEngine.cs`

### Phase 3：实现 ExecutionCell

- `Core/ExecutionCell.cs` — 基于 MediatR 的 IExecutionCell 实现
- `Core/ExecuteCommandHandler.cs` — 实际的命令处理器

### Phase 4：实现 PipelineBehavior

- `Behaviors/RetryBehavior.cs` — 基于 Polly Retry
- `Behaviors/CircuitBreakerBehavior.cs` — 基于 Polly CircuitBreaker
- `Behaviors/TimeoutBehavior.cs` — 基于 Polly Timeout
- `Behaviors/LoggingBehavior.cs` — 日志记录

### Phase 5：实现 ExecutionPipeline + ExecutionEngine

- `Core/ExecutionPipeline.cs` — 管线容器
- `Core/ExecutionEngine.cs` — 引擎容器

### Phase 6：DI 注册扩展

- `Extensions/ServiceCollectionExtensions.cs`

### Phase 7：FileDriven.Execution 迁移

- FileDrivenExecutor → 引用 ExecutionEngine
- ExecutionPipelineEntry → 引用 ExecutionPipeline
- 标记旧代码 Obsolete → 逐步删除

---

## 13. 附录：NuGet 包信息

### MediatR

| 属性 | 值 |
|------|-----|
| **包名** | MediatR |
| **ID** | `/luckypennysoftware/mediatr` |
| **描述** | 简单的 .NET 进程内消息中介实现，支持请求/响应、命令、查询、通知和事件 |
| **代码片段** | 58 |
| **来源信誉** | Medium |
| **基准评分** | 82.69 |

### Polly

| 属性 | 值 |
|------|-----|
| **包名** | Polly |
| **ID** | `/app-vnext/polly` |
| **描述** | .NET 弹性和瞬态故障处理库，支持 Retry、Circuit Breaker、Hedging、Timeout、Rate Limiter 和 Fallback |
| **代码片段** | 647 |
| **来源信誉** | High |
| **基准评分** | 84.71 |

---

> **文档结束**
