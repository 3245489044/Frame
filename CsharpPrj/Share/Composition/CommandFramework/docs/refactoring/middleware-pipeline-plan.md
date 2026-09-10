# CommandFramework 补充方案 — 参考 UniversalJsonMiddleware 设计构建通用框架

> 版本：v1.0
> 日期：2026-06-05
> 状态：方案设计

---

## 一、背景与动机

### 1.1 当前 CommandFramework 的能力

经过全面审查，CommandFramework 目前已具备以下能力：

| 能力 | 文件 | 说明 |
|------|------|------|
| **命令解析** | `Parsing/CommandParser.cs` | 将命令字符串解析为 `ParsedCommand`，支持规范化 |
| **命令引擎** | `Execution/CommandEngine.cs` | 命令注册/执行/撤销/重做 |
| **命令注册表** | `Registration/CommandRegistry.cs` | 工厂模式注册表，延迟创建命令实例 |
| **委托命令** | `Execution/DelegateCommand.cs` | 通过委托实现 ICommand + CompositeCommand |
| **非事件命令处理器** | `Execution/NonEventCommandHandler.cs` | echo/sleep/wait 基础实现 |
| **函数注册表/执行器** | `Execution/FunctionRegistry.cs` + `FunctionExecutor.cs` | 委托注册、参数自动匹配、异步执行 |
| **状态存储** | `Execution/InMemoryStateStore.cs` | 线程安全的内存键值存储 |
| **值提取器** | `Execution/DefaultValueExtractor.cs` | Dictionary/IDictionary/反射属性提取 |
| **控件值读写器** | `Execution/ControlValueAccessorBase.cs` | 泛型适配器注册模式 |
| **环境适配器基类** | `Execution/EnvironmentAdapterBase.cs` | Do/Get/Look/Wait/Assert/Discover 默认实现 |
| **管线基础设施** | `Pipeline/CommandPipeline.cs` + `PipelineStageBase.cs` + `PipelineEntry.cs` | 线性阶段模式管线 |
| **命令模型** | `Models/ParsedCommand.cs` + `CommandExecutionContext.cs` + `NonEventCommandResult.cs` | 命令数据模型 |
| **接口定义** | `ICommand.cs` | 所有核心接口统一在 `CommandFramework.Abstractions` |

### 1.2 当前缺失的能力（参考 UniversalJsonMiddleware）

| 缺失能力 | UniversalJsonMiddleware 参考 | 说明 |
|---------|---------------------------|------|
| **洋葱模型中间件管线** | `MiddlewarePipeline` | 支持短路/横切关注点的洋葱模型，与现有线性阶段模式互补 |
| **通用中间件** | `LoggingMiddleware`、`ValidationMiddleware`、`RetryMiddleware` | 日志、验证、重试等横切关注点 |
| **轻量命令执行器** | `CommandExecutor` | 基于中间件管线的轻量执行器，补充 `CommandEngine` |
| **命令解析工具增强** | `JsonCommandParser` | 更丰富的分词/参数解析工具方法 |

### 1.3 设计原则

1. **不迁移任何 UniversalJsonMiddleware 的代码** — 所有新代码在 CommandFramework 中独立编写
2. **通用化设计** — 参考设计思想，构建泛型/通用版本
3. **与现有能力互补** — 不重复 CommandFramework 已有的功能
4. **保持向后兼容** — 不影响现有使用者的代码

---

## 二、洋葱模型中间件管线

### 2.1 设计思想

参考 UniversalJsonMiddleware 的 `MiddlewarePipeline`，但设计为**泛型通用版本**：

```
UniversalJsonMiddleware 的设计（特化版本）：
  MiddlewarePipeline<JsonCommand, JsonCommandResult>
    ├── Use(middleware) — 注册中间件
    ├── SetCoreHandler(handler) — 设置核心处理器
    └── ExecuteAsync(command) — 洋葱式调用

CommandFramework 的设计（通用版本）：
  MiddlewarePipeline<TContext, TResult>
    ├── Use(middleware) — 注册中间件
    ├── SetCoreHandler(handler) — 设置核心处理器
    └── ExecuteAsync(context) — 洋葱式调用
```

### 2.2 与现有 CommandPipeline 的对比

| 特性 | CommandPipeline（现有） | MiddlewarePipeline（新增） |
|------|----------------------|--------------------------|
| **模式** | 线性阶段模式 | 洋葱模型 |
| **执行方式** | 顺序执行，失败停止 | 洋葱式嵌套调用 |
| **短路支持** | 不支持（阶段失败即停止） | 支持（中间件可跳过后续） |
| **横切关注点** | 需手动插入阶段 | 天然支持（日志/验证/重试） |
| **核心处理** | 最后一个阶段 | 显式 SetCoreHandler |
| **适用场景** | 固定流程的命令执行 | 需要灵活拦截/增强的场景 |

### 2.3 接口定义

```csharp
// Pipeline/MiddlewarePipeline.cs
namespace CommandFramework.Pipeline;

/// <summary>
/// 中间件委托
/// </summary>
/// <typeparam name="TContext">上下文类型</typeparam>
/// <typeparam name="TResult">结果类型</typeparam>
/// <param name="context">上下文</param>
/// <param name="next">下一个中间件</param>
/// <returns>执行结果</returns>
public delegate Task<TResult> MiddlewareDelegate<TContext, TResult>(
    TContext context,
    Func<TContext, Task<TResult>> next);

/// <summary>
/// 洋葱模型中间件管线
/// <para>支持短路、横切关注点、洋葱式嵌套调用</para>
/// <para>与 CommandPipeline（线性阶段模式）互补</para>
/// </summary>
/// <typeparam name="TContext">上下文类型</typeparam>
/// <typeparam name="TResult">结果类型</typeparam>
public class MiddlewarePipeline<TContext, TResult>
{
    private readonly List<MiddlewareDelegate<TContext, TResult>> _middlewares = new();
    private Func<TContext, Task<TResult>>? _coreHandler;
    private readonly string _name;
    private readonly string? _description;

    /// <summary>管线名称</summary>
    public string Name => _name;

    /// <summary>管线描述</summary>
    public string? Description => _description;

    /// <summary>已注册的中间件数量</summary>
    public int MiddlewareCount => _middlewares.Count;

    /// <summary>是否已设置核心处理器</summary>
    public bool HasCoreHandler => _coreHandler != null;

    public MiddlewarePipeline(string name, string? description = null)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _description = description;
    }

    /// <summary>
    /// 注册中间件
    /// <para>按注册顺序执行，先注册的在外层，后注册的在内层</para>
    /// </summary>
    public MiddlewarePipeline<TContext, TResult> Use(MiddlewareDelegate<TContext, TResult> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// 注册中间件（通过实现 IMiddleware 接口的类型）
    /// </summary>
    public MiddlewarePipeline<TContext, TResult> Use<TMiddleware>()
        where TMiddleware : IMiddleware<TContext, TResult>, new()
    {
        return Use(new TMiddleware().InvokeAsync);
    }

    /// <summary>
    /// 注册中间件（通过 IMiddleware 实例）
    /// </summary>
    public MiddlewarePipeline<TContext, TResult> Use(IMiddleware<TContext, TResult> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        return Use(middleware.InvokeAsync);
    }

    /// <summary>
    /// 设置核心处理器
    /// <para>核心处理器位于洋葱的最内层，在所有中间件之后执行</para>
    /// </summary>
    public void SetCoreHandler(Func<TContext, Task<TResult>> handler)
    {
        _coreHandler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// 执行管线
    /// <para>按洋葱模型调用所有中间件，最后执行核心处理器</para>
    /// </summary>
    public Task<TResult> ExecuteAsync(TContext context)
    {
        if (_coreHandler == null)
            throw new InvalidOperationException("Core handler is not set. Call SetCoreHandler first.");

        // 构建洋葱调用链
        Func<TContext, Task<TResult>> pipeline = _coreHandler;

        // 从后往前包装，确保先注册的中间件在外层
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = ctx => middleware(ctx, next);
        }

        return pipeline(context);
    }

    /// <summary>
    /// 清空所有中间件和核心处理器
    /// </summary>
    public void Clear()
    {
        _middlewares.Clear();
        _coreHandler = null;
    }
}

/// <summary>
/// 中间件接口
/// <para>用于通过类型注册中间件</para>
/// </summary>
public interface IMiddleware<TContext, TResult>
{
    /// <summary>
    /// 执行中间件逻辑
    /// </summary>
    Task<TResult> InvokeAsync(TContext context, Func<TContext, Task<TResult>> next);
}
```

### 2.4 文件位置

```
CommandFramework/Pipeline/
  ├── MiddlewarePipeline.cs          ← 新增：洋葱模型管线 + IMiddleware 接口
  ├── CommandPipeline.cs             ← 已有：线性阶段模式管线
  ├── PipelineStageBase.cs           ← 已有：管线阶段基类
  └── PipelineEntry.cs               ← 已有：管线入口
```

---

## 三、通用中间件

### 3.1 设计思想

参考 UniversalJsonMiddleware 的 `LoggingMiddleware`、`ValidationMiddleware`、`RetryMiddleware`，但设计为**泛型通用版本**。

### 3.2 日志中间件

```csharp
// Middleware/LoggingMiddleware.cs
namespace CommandFramework.Middleware;

/// <summary>
/// 通用日志中间件
/// <para>记录中间件管线的执行开始、完成、耗时和异常</para>
/// </summary>
public static class LoggingMiddleware
{
    /// <summary>
    /// 创建日志中间件
    /// </summary>
    /// <typeparam name="TContext">上下文类型</typeparam>
    /// <typeparam name="TResult">结果类型</typeparam>
    /// <param name="logger">日志输出委托（默认输出到 Debug）</param>
    /// <param name="contextFormatter">上下文格式化函数（可选，用于输出上下文信息）</param>
    public static MiddlewareDelegate<TContext, TResult> Create<TContext, TResult>(
        Action<string>? logger = null,
        Func<TContext, string?>? contextFormatter = null)
    {
        logger ??= msg => System.Diagnostics.Debug.WriteLine(msg);

        return async (context, next) =>
        {
            var contextInfo = contextFormatter?.Invoke(context) ?? context?.ToString();
            logger($"[Middleware] Starting... (Context: {contextInfo})");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await next(context);
                sw.Stop();
                logger($"[Middleware] Completed in {sw.ElapsedMilliseconds}ms");
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger($"[Middleware] Failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                throw;
            }
        };
    }
}
```

### 3.3 验证中间件

```csharp
// Middleware/ValidationMiddleware.cs
namespace CommandFramework.Middleware;

/// <summary>
/// 通用验证中间件
/// <para>在核心处理器执行前验证上下文合法性</para>
/// </summary>
public static class ValidationMiddleware
{
    /// <summary>
    /// 创建验证中间件
    /// </summary>
    /// <typeparam name="TContext">上下文类型</typeparam>
    /// <typeparam name="TResult">结果类型</typeparam>
    /// <param name="validator">验证函数，返回 null 表示验证通过，返回字符串表示错误信息</param>
    /// <param name="errorResultFactory">验证失败时的结果工厂（可选，默认抛出异常）</param>
    public static MiddlewareDelegate<TContext, TResult> Create<TContext, TResult>(
        Func<TContext, string?> validator,
        Func<TContext, string, TResult>? errorResultFactory = null)
    {
        ArgumentNullException.ThrowIfNull(validator);

        return async (context, next) =>
        {
            var error = validator(context);
            if (error != null)
            {
                if (errorResultFactory != null)
                    return errorResultFactory(context, error);

                throw new ValidationException(error);
            }

            return await next(context);
        };
    }
}

/// <summary>
/// 验证异常
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
```

### 3.4 重试中间件

```csharp
// Middleware/RetryMiddleware.cs
namespace CommandFramework.Middleware;

/// <summary>
/// 通用重试中间件
/// <para>在核心处理器失败时自动重试</para>
/// </summary>
public static class RetryMiddleware
{
    /// <summary>
    /// 创建重试中间件
    /// </summary>
    /// <typeparam name="TContext">上下文类型</typeparam>
    /// <typeparam name="TResult">结果类型</typeparam>
    /// <param name="maxRetries">最大重试次数（默认 3）</param>
    /// <param name="delayMs">重试间隔毫秒（默认 100，每次重试加倍）</param>
    /// <param name="retryCondition">重试条件（默认所有异常都重试）</param>
    /// <param name="onRetry">重试回调（可选，用于日志）</param>
    public static MiddlewareDelegate<TContext, TResult> Create<TContext, TResult>(
        int maxRetries = 3,
        int delayMs = 100,
        Func<Exception, bool>? retryCondition = null,
        Action<int, Exception>? onRetry = null)
    {
        retryCondition ??= _ => true;

        return async (context, next) =>
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await next(context);
                }
                catch (Exception ex) when (retryCondition(ex))
                {
                    attempt++;
                    if (attempt > maxRetries)
                        throw;

                    onRetry?.Invoke(attempt, ex);
                    await Task.Delay(delayMs * (int)Math.Pow(2, attempt - 1));
                }
            }
        };
    }
}
```

### 3.5 文件位置

```
CommandFramework/Middleware/
  ├── LoggingMiddleware.cs          ← 新增：通用日志中间件
  ├── ValidationMiddleware.cs       ← 新增：通用验证中间件
  └── RetryMiddleware.cs            ← 新增：通用重试中间件
```

---

## 四、轻量命令执行器

### 4.1 设计思想

参考 UniversalJsonMiddleware 的 `CommandExecutor`，但基于 CommandFramework 已有的 `ICommandParser` 和新增的 `MiddlewarePipeline` 构建。

### 4.2 与现有 CommandEngine 的对比

| 特性 | CommandEngine（现有） | CommandExecutor（新增） |
|------|----------------------|------------------------|
| **命令模型** | `ICommand` 接口 | 字符串命令 |
| **注册方式** | `RegisterCommand(name, execute, undo)` | `RegisterHandler(name, handler)` |
| **执行方式** | 按名称查找并执行 | 解析字符串 → 中间件管线执行 |
| **撤销/重做** | 支持 | 不支持（轻量设计） |
| **中间件支持** | 无 | 支持（基于 MiddlewarePipeline） |
| **适用场景** | 需要撤销/重做的复杂命令 | 简单的字符串命令处理 |

### 4.3 接口定义

```csharp
// Abstractions/ICommandExecutor.cs
namespace CommandFramework.Abstractions;

/// <summary>
/// 命令执行器接口
/// <para>基于中间件管线的轻量命令执行器</para>
/// <para>与 ICommandEngine 互补：ICommandEngine 用于需要撤销/重做的场景，
/// ICommandExecutor 用于简单的字符串命令处理场景</para>
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    /// 注册命令处理器
    /// </summary>
    /// <param name="command">命令名称</param>
    /// <param name="handler">处理函数（接收命令字符串，返回是否成功）</param>
    void RegisterHandler(string command, Func<string, Task<bool>> handler);

    /// <summary>
    /// 执行命令
    /// </summary>
    /// <param name="command">命令字符串</param>
    /// <returns>执行结果</returns>
    Task<CommandExecutionResult> ExecuteAsync(string command);

    /// <summary>
    /// 批量执行命令
    /// </summary>
    /// <param name="commands">命令字符串列表</param>
    /// <returns>执行结果列表</returns>
    Task<List<CommandExecutionResult>> ExecuteBatchAsync(IEnumerable<string> commands);
}
```

### 4.4 实现

```csharp
// Execution/CommandExecutor.cs
namespace CommandFramework.Execution;

/// <summary>
/// 基于中间件管线的轻量命令执行器
/// <para>解析字符串命令，通过中间件管线执行</para>
/// </summary>
public class CommandExecutor : ICommandExecutor
{
    private readonly Dictionary<string, Func<string, Task<bool>>> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICommandParser _parser;
    private readonly MiddlewarePipeline<CommandContext, CommandExecutionResult> _pipeline;

    public CommandExecutor(ICommandParser? parser = null)
    {
        _parser = parser ?? new CommandParser();
        _pipeline = new MiddlewarePipeline<CommandContext, CommandExecutionResult>("CommandExecutor");

        // 设置默认核心处理器
        _pipeline.SetCoreHandler(async context =>
        {
            if (context.ParsedCommand == null || !context.ParsedCommand.IsSuccess)
            {
                return CommandExecutionResult.Fail(
                    context.CurrentCommand ?? "unknown",
                    context.ParsedCommand?.ErrorMessage ?? "Parse failed");
            }

            var cmd = context.ParsedCommand;
            var handlerKey = cmd.Action ?? cmd.Verb;

            if (_handlers.TryGetValue(handlerKey, out var handler))
            {
                var success = await handler(context.CurrentCommand ?? string.Empty);
                return success
                    ? CommandExecutionResult.Ok(context.CurrentCommand ?? string.Empty)
                    : CommandExecutionResult.Fail(context.CurrentCommand ?? string.Empty, "Handler returned false");
            }

            return CommandExecutionResult.Fail(
                context.CurrentCommand ?? string.Empty,
                $"No handler registered for '{handlerKey}'");
        });
    }

    /// <summary>注册命令处理器</summary>
    public void RegisterHandler(string command, Func<string, Task<bool>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[command] = handler;
    }

    /// <summary>注册中间件</summary>
    public CommandExecutor Use(MiddlewareDelegate<CommandContext, CommandExecutionResult> middleware)
    {
        _pipeline.Use(middleware);
        return this;
    }

    /// <summary>执行命令</summary>
    public async Task<CommandExecutionResult> ExecuteAsync(string command)
    {
        var context = new CommandContext
        {
            CurrentCommand = command,
            ParsedCommand = _parser.Parse(command)
        };

        try
        {
            return await _pipeline.ExecuteAsync(context);
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Fail(command, $"Execution error: {ex.Message}");
        }
    }

    /// <summary>批量执行命令</summary>
    public async Task<List<CommandExecutionResult>> ExecuteBatchAsync(IEnumerable<string> commands)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var command in commands)
        {
            results.Add(await ExecuteAsync(command));
        }
        return results;
    }
}
```

### 4.5 文件位置

```
CommandFramework/
  ├── Abstractions/ICommandExecutor.cs    ← 新增：命令执行器接口
  └── Execution/CommandExecutor.cs        ← 新增：命令执行器实现
```

---

## 五、命令解析工具增强

### 5.1 设计思想

参考 UniversalJsonMiddleware 的 `JsonCommandParser` 中的分词和参数解析逻辑，为 CommandFramework 的 `CommandParser` 补充更丰富的工具方法。

### 5.2 增强内容

```csharp
// Parsing/CommandTokenizer.cs（新增）
namespace CommandFramework.Parsing;

/// <summary>
/// 命令分词器
/// <para>提供更丰富的命令分词和参数解析工具方法</para>
/// </summary>
public static class CommandTokenizer
{
    /// <summary>
    /// 分割命令字符串（支持引号字符串）
    /// </summary>
    public static string[] Tokenize(string command) { ... }

    /// <summary>
    /// 解析 key=value 参数（支持类型推断）
    /// </summary>
    public static Dictionary<string, object> ParseKeyValueParameters(IEnumerable<string> parts) { ... }

    /// <summary>
    /// 解析位置参数
    /// </summary>
    public static List<string> ParsePositionalParameters(IEnumerable<string> parts) { ... }

    /// <summary>
    /// 推断值类型（int/double/bool/string）
    /// </summary>
    public static object InferValueType(string value) { ... }
}
```

### 5.3 文件位置

```
CommandFramework/Parsing/
  ├── CommandParser.cs              ← 已有：命令解析器
  ├── CommandTokenizer.cs           ← 新增：命令分词工具
  └── ICommandParser.cs             ← 已有（在 Abstractions 中）
```

---

## 六、新增文件清单

| 文件 | 说明 |
|------|------|
| `Pipeline/MiddlewarePipeline.cs` | 洋葱模型中间件管线 + IMiddleware 接口 |
| `Middleware/LoggingMiddleware.cs` | 通用日志中间件 |
| `Middleware/ValidationMiddleware.cs` | 通用验证中间件 + ValidationException |
| `Middleware/RetryMiddleware.cs` | 通用重试中间件 |
| `Abstractions/ICommandExecutor.cs` | 轻量命令执行器接口 |
| `Execution/CommandExecutor.cs` | 轻量命令执行器实现 |
| `Parsing/CommandTokenizer.cs` | 命令分词工具 |

---

## 七、项目结构变化

```
CommandFramework/（补充后）
├── CommandFramework.csproj
├── ICommand.cs                          ← 已有：所有接口定义
├── Abstractions/
│   ├── ICommandExecutor.cs              ← 新增：命令执行器接口
│   └── ...（已有接口）
├── Models/
│   ├── ParsedCommand.cs                 ← 已有
│   ├── CommandExecutionContext.cs       ← 已有
│   └── NonEventCommandResult.cs         ← 已有
├── Parsing/
│   ├── CommandParser.cs                 ← 已有
│   └── CommandTokenizer.cs              ← 新增：命令分词工具
├── Execution/
│   ├── CommandEngine.cs                 ← 已有
│   ├── CommandExecutor.cs               ← 新增：轻量命令执行器
│   ├── CommandExecutionResult.cs        ← 已有
│   ├── DelegateCommand.cs               ← 已有
│   ├── NonEventCommandHandler.cs        ← 已有
│   ├── FunctionRegistry.cs              ← 已有
│   ├── FunctionExecutor.cs              ← 已有
│   ├── InMemoryStateStore.cs            ← 已有
│   ├── DefaultValueExtractor.cs         ← 已有
│   ├── ControlValueAccessorBase.cs      ← 已有
│   └── EnvironmentAdapterBase.cs        ← 已有
├── Pipeline/
│   ├── CommandPipeline.cs               ← 已有：线性阶段模式
│   ├── MiddlewarePipeline.cs            ← 新增：洋葱模型管线
│   ├── PipelineStageBase.cs             ← 已有
│   ├── PipelineEntry.cs                 ← 已有
│   └── Stages/                          ← 已有
├── Middleware/                          ← 新增目录
│   ├── LoggingMiddleware.cs             ← 新增
│   ├── ValidationMiddleware.cs          ← 新增
│   └── RetryMiddleware.cs              ← 新增
├── Registration/
│   └── CommandRegistry.cs               ← 已有
└── doc/
    ├── CommandFramework_设计文档.md      ← 已有
    ├── CommandFramework_重构方案.md      ← 已有
    └── CommandFramework_补充方案.md      ← 新增：本文档
```

---

## 八、使用示例

### 8.1 洋葱模型中间件管线

```csharp
// 创建管线
var pipeline = new MiddlewarePipeline<CommandContext, CommandExecutionResult>("MyPipeline");

// 注册中间件（按注册顺序，先注册的在外层）
pipeline.Use(LoggingMiddleware.Create<CommandContext, CommandExecutionResult>());
pipeline.Use(ValidationMiddleware.Create<CommandContext, CommandExecutionResult>(
    validator: ctx => ctx.CurrentCommand == null ? "Command is null" : null));
pipeline.Use(RetryMiddleware.Create<CommandContext, CommandExecutionResult>(maxRetries: 3));

// 设置核心处理器
pipeline.SetCoreHandler(async ctx =>
{
    // 核心处理逻辑
    return CommandExecutionResult.Ok(ctx.CurrentCommand ?? "");
});

// 执行
var result = await pipeline.ExecuteAsync(context);
```

### 8.2 轻量命令执行器

```csharp
// 创建执行器
var executor = new CommandExecutor();

// 注册中间件
executor.Use(LoggingMiddleware.Create<CommandContext, CommandExecutionResult>());

// 注册命令处理器
executor.RegisterHandler("echo", async cmd =>
{
    Console.WriteLine(cmd);
    return true;
});

// 执行命令
var result = await executor.ExecuteAsync("echo hello world");
```

### 8.3 与现有 CommandPipeline 配合使用

```csharp
// 线性阶段模式 — 适用于固定流程
var stagePipeline = new CommandPipeline<CommandContext>("StagePipeline");
stagePipeline.Register(new ParseCommandStage());
await stagePipeline.ExecuteAsync(context);

// 洋葱模型 — 适用于需要灵活拦截的场景
var middlewarePipeline = new MiddlewarePipeline<CommandContext, CommandExecutionResult>("MiddlewarePipeline");
middlewarePipeline.Use(LoggingMiddleware.Create<CommandContext, CommandExecutionResult>());
middlewarePipeline.SetCoreHandler(async ctx => { ... });
var result = await middlewarePipeline.ExecuteAsync(context);
```

---

## 九、与 UniversalJsonMiddleware 的对应关系

| UniversalJsonMiddleware | CommandFramework（补充后） | 关系 |
|------------------------|--------------------------|------|
| `MiddlewarePipeline` | `Pipeline/MiddlewarePipeline<TContext, TResult>` | 参考设计，泛型化重构 |
| `LoggingMiddleware` | `Middleware/LoggingMiddleware` | 参考设计，泛型化重构 |
| `ValidationMiddleware` | `Middleware/ValidationMiddleware` | 参考设计，泛型化重构 |
| `RetryMiddleware` | `Middleware/RetryMiddleware` | 参考设计，泛型化重构 |
| `CommandExecutor` | `Execution/CommandExecutor` | 参考设计，基于 CommandFramework 现有能力构建 |
| `ICommandExecutor` | `Abstractions/ICommandExecutor` | 参考设计，接口定义 |
| `JsonCommandParser` 分词逻辑 | `Parsing/CommandTokenizer` | 参考设计，工具方法增强 |

**注意**：以上所有新代码均为**独立编写**，不迁移 UniversalJsonMiddleware 的任何代码。

---

## 十、实施步骤

1. 创建 `Pipeline/MiddlewarePipeline.cs` — 洋葱模型管线 + IMiddleware 接口
2. 创建 `Middleware/LoggingMiddleware.cs` — 通用日志中间件
3. 创建 `Middleware/ValidationMiddleware.cs` — 通用验证中间件
4. 创建 `Middleware/RetryMiddleware.cs` — 通用重试中间件
5. 创建 `Abstractions/ICommandExecutor.cs` — 命令执行器接口
6. 创建 `Execution/CommandExecutor.cs` — 命令执行器实现
7. 创建 `Parsing/CommandTokenizer.cs` — 命令分词工具
8. 编译验证
