# FileDrivenExecutor.cs 重构分析报告

> **分析日期**：2026-06-15
> **分析目标**：识别 `FileDrivenExecutor.cs`（1845行）中应整理到通用框架的逻辑
> **项目架构**：BasicMethod → Share → Operation → FileDriven（四层架构）

---

## 一、当前问题

`FileDrivenExecutor.cs` 位于 `FileDriven.Execution` 命名空间（应用层），但其中包含了大量**通用框架能力**的硬编码实现：

| 问题 | 表现 |
|------|------|
| **职责过重** | 1845 行代码，包含异常捕获、日志、管道、模板引擎、命令注册等 15+ 个职责 |
| **重复代码** | `Run()` / `RunGui()` / `RunCli()` 三个入口有大量重复的初始化/清理逻辑 |
| **硬编码步骤** | `InitializeFramework()` 手动编排 15+ 个初始化步骤，顺序固定、不可插拔 |
| **未复用现有框架** | Share/FrameworkAbstractions 中已有 `LifecycleManager`，但未被使用 |
| **静态依赖** | 命令处理函数硬编码依赖 `CommandFrameworkUsage` 静态单例，难以单元测试 |

---

## 二、应抽象的逻辑清单（按优先级）

### 🔴 P0 - 高价值、低耦合（立即可以抽象）

| # | 逻辑 | 位置（行号） | 当前问题 | 目标位置 | 抽象方式 |
|---|------|-------------|---------|---------|---------|
| 1 | `FindFirstUnprotectedSeparator` | 1794-1822 | 纯字符串解析算法，与 FileDriven 业务无关 | `BasicMethod/Common/StringHelper.cs` | 新建静态方法 |
| 2 | `LoadPathAliasesFromEnvironment` | 701-728 | 硬编码 `FILEDRIVEN_ALIAS_` 前缀，路径管理是通用能力 | `BasicMethod/PathManager/PathAliasLoader.cs` | 新建静态类 |
| 3 | `SetupGlobalExceptionHandling` | 368-383 | 任何桌面应用都需要，不应在应用层硬编码 | `Share/Diagnostics/GlobalExceptionHandler.cs` | 新建静态类 |
| 4 | `ConnectPipe` / `DisconnectPipe` | 404-420, 1571-1576 | 命名管道是通用 IPC 机制 | `Share/Adapters/NamedPipeClientManager.cs` | 新建类(IDisposable) |

### 🟡 P1 - 中等价值、中等耦合

| # | 逻辑 | 位置（行号） | 当前问题 | 目标位置 | 抽象方式 |
|---|------|-------------|---------|---------|---------|
| 5 | `SetupLogging` / `ShutdownLogging` / `AutoInitializeLogging` | 432-442, 1559-1562, 1613-1653 | 日志是通用基础设施 | `Share/Diagnostics/LoggingBootstrapper.cs` | 新建静态类 |
| 6 | `InitializeBatchLogging` | 737-767 | 批次日志是通用日志能力 | `Share/Diagnostics/BatchLoggingBootstrapper.cs` | 新建静态类 |
| 7 | `SetWorkingDirectory` | 451-463 | 工作目录管理是基础能力 | `BasicMethod/PathManager/WorkingDirectoryManager.cs` | 新建静态类 |
| 8 | `PerformComponentValidation` | 776-801 | 验证逻辑应与 ComponentRegistry 绑定 | `Share/Component/ComponentValidationService.cs` | 新建静态类 |
| 9 | `PerformNonEventCommandValidation` | 816-842 | 命令校验是 CommandFramework 的职责 | `Share/CommandFramework/NonEventCommandValidator.cs` | 新建静态类 |
| 10 | `InitializeFramework` / `ShutdownFramework` | 474-590, 1500-1550 | **最大重构点**：硬编码步骤序列，未使用 LifecycleManager | `Share/FrameworkAbstractions/Bootstrapping/FrameworkBootstrapper.cs` | 新建类，使用 LifecycleManager |

### 🟢 P2 - 高价值、高耦合（需要更多重构设计）

| # | 逻辑 | 位置（行号） | 当前问题 | 目标位置 | 抽象方式 |
|---|------|-------------|---------|---------|---------|
| 11 | `RegisterNonEventCommands` | 868-1141 | sideload/close 硬编码依赖 MainForm 和静态单例 | `Operation/FunctionStorage/NonEventCommands/` | 新建 NonEventCommandProvider 基类 |
| 12 | `InitializeTemplateEngine` | 645-692 | 模板引擎是通用能力 | `Share/PlaceholderFramework/TemplateEngineBootstrapper.cs` | 新建静态类 |
| 13 | `InitializeExecutionCenter` | 1154-1252 | 任务执行池是通用能力 | `Share/ExecutionCenter/ExecutionCenterBootstrapper.cs` | 新建静态类 |
| 14 | `RegisterComponents` | 599-636 | 组件注册是通用 IoC 能力 | `Share/Component/ComponentRegistrationService.cs` | 新建类 |
| 15 | `InjectEventBinderToRenderPipeline` | 1261-1299 | 事件绑定是通用中间件能力 | `Share/UniversalMiddleware/EventBinderInjector.cs` | 新建静态类 |
| 16 | `SetWinFormsEventBinder` | 1732-1755 | 与 InjectEventBinderToRenderPipeline 合并 | 合并到 `EventBinderInjector.cs` | 合并 |
| 17 | `GetOutputTargetFromParameters` | 1704-1725 | 参数解析是通用能力 | `Share/ParameterResolver/OutputParameterResolver.cs` | 新建静态类 |
| 18 | `CreateExecutionEntry` | 1590-1608 | PipelineEntry 工厂是通用模式 | `Share/Pipeline/PipelineEntryFactory.cs` | 新建静态类 |
| 19 | `ResolveCommandFile` | 1669-1696 | 路径解析是基础能力 | `BasicMethod/PathManager/CommandFileResolver.cs` | 新建静态类 |

---

## 三、核心重构：FrameworkBootstrapper

### 3.1 当前问题

`InitializeFramework()` 手动编排了 15+ 个初始化步骤：

```
JsonConverter → WdUI → 路径检测 → 路径别名 → FunctionStorage → FileDrivenLifecycle
→ CommandManager → 组件注册 → 模板引擎 → 批次日志 → 组件验证 → CommandFrameworkUsage
→ 非事件命令注册 → 非事件命令验证 → 事件绑定注入 → ExecutionCenter
```

### 3.2 目标设计

使用 Share/FrameworkAbstractions 中已有的 `LifecycleManager` + `IModule` 接口：

```csharp
// Share/FrameworkAbstractions/Bootstrapping/IFrameworkModule.cs
public interface IFrameworkModule : IModule
{
    string Name { get; }
    bool CanRun(IBootstrapperContext context);
}

// Share/FrameworkAbstractions/Bootstrapping/IBootstrapperContext.cs
public interface IBootstrapperContext
{
    bool CliMode { get; }
    CliOptions Options { get; }
    IServiceProvider Services { get; }
}

// Share/FrameworkAbstractions/Bootstrapping/FrameworkBootstrapper.cs
public class FrameworkBootstrapper
{
    private readonly LifecycleManager _lifecycle = new();
    private readonly List<(IFrameworkModule Module, bool Conditional)> _modules = new();
    
    public FrameworkBootstrapper UseModule<T>() where T : IFrameworkModule, new() { ... }
    public FrameworkBootstrapper UseModule(IFrameworkModule module) { ... }
    public FrameworkBootstrapper UseConditional<T>(Func<IBootstrapperContext, bool> condition) { ... }
    
    public void Run(IBootstrapperContext context) { ... }
    public void Shutdown() { ... }
}
```

### 3.3 模块化后的使用方式

```csharp
// FileDrivenExecutor.cs 中的使用
var context = new BootstrapperContext { CliMode = cliMode, Options = options };
var bootstrapper = new FrameworkBootstrapper()
    .UseModule<JsonConverterModule>()
    .UseConditional<WdUIInitializationModule>(ctx => !ctx.CliMode)
    .UseModule<PathAliasModule>()
    .UseModule<FunctionStorageModule>()
    .UseModule<FileDrivenFunctionModule>()
    .UseModule<ComponentRegistrationModule>()
    .UseModule<TemplateEngineModule>()
    .UseModule<BatchLoggingModule>()
    .UseModule<NonEventCommandModule>()
    .UseModule<ExecutionCenterModule>()
    .UseConditional<EventBinderModule>(ctx => !ctx.CliMode);

bootstrapper.Run(context);
// ... 业务逻辑 ...
bootstrapper.Shutdown();
```

---

## 四、各模块详细设计

### 4.1 StringHelper.FindFirstUnprotectedSeparator

```csharp
// BasicMethod/Common/StringHelper.cs
namespace Common;

public static class StringHelper
{
    /// <summary>
    /// 查找字符串中第一个未被引号或方括号保护的分隔符
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <param name="separator">分隔符（默认 '|'）</param>
    /// <param name="protectChars">保护字符对（默认引号和方括号）</param>
    /// <returns>第一个未受保护的分隔符索引，未找到时返回 -1</returns>
    public static int FindFirstUnprotectedSeparator(
        string input, 
        char separator = '|',
        (char Open, char Close)[]? protectChars = null)
    {
        protectChars ??= new[] { ('"', '"'), ('[', ']') };
        
        bool inQuotes = false;
        int bracketDepth = 0;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == '[' && !inQuotes)
                bracketDepth++;
            else if (c == ']' && !inQuotes && bracketDepth > 0)
                bracketDepth--;
            else if (c == separator && !inQuotes && bracketDepth == 0)
                return i;
        }

        return -1;
    }
}
```

### 4.2 PathAliasLoader

```csharp
// BasicMethod/PathManager/PathAliasLoader.cs
namespace PathManager;

public static class PathAliasLoader
{
    /// <summary>
    /// 从环境变量加载路径别名并注册到 PathResolver
    /// </summary>
    /// <param name="prefix">环境变量前缀（默认 "FILEDRIVEN_ALIAS_"）</param>
    /// <returns>加载的别名数量</returns>
    public static int LoadFromEnvironment(string prefix = "FILEDRIVEN_ALIAS_")
    {
        var envVars = Environment.GetEnvironmentVariables();
        int aliasCount = 0;

        foreach (var key in envVars.Keys)
        {
            var keyStr = key?.ToString();
            if (string.IsNullOrEmpty(keyStr) || 
                !keyStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var alias = keyStr[prefix.Length..];
            var path = envVars[key]?.ToString();

            if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(path))
            {
                PathResolver.RegisterAlias(alias, path);
                aliasCount++;
            }
        }

        return aliasCount;
    }
}
```

### 4.3 GlobalExceptionHandler

```csharp
// Share/Diagnostics/GlobalExceptionHandler.cs
namespace Common.Diagnostics;

public static class GlobalExceptionHandler
{
    public static void Configure(
        Action<Exception>? onThreadException = null,
        Action<Exception>? onAppDomainException = null)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        
        Application.ThreadException += (sender, e) =>
            onThreadException?.Invoke(e.Exception);
        
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception 
                ?? new Exception(e.ExceptionObject.ToString());
            onAppDomainException?.Invoke(ex);
        };
    }
}
```

### 4.4 NamedPipeClientManager

```csharp
// Share/Adapters/NamedPipeClientManager.cs
namespace Common.Diagnostics; // 或 Share.Adapters

public class NamedPipeClientManager : IDisposable
{
    private NamedPipeClient? _client;
    private bool _disposed;

    public bool IsConnected => _client != null && _client.IsConnected;

    public void Connect(string? pipeName)
    {
        if (string.IsNullOrEmpty(pipeName)) return;
        
        try
        {
            _client = new NamedPipeClient(pipeName);
            _client.Connect();
            _client.RedirectConsoleOut();
        }
        catch
        {
            _client?.Dispose();
            _client = null;
        }
    }

    public void Disconnect()
    {
        if (_client == null) return;
        
        try
        {
            _client.RestoreConsoleOut();
            _client.Dispose();
        }
        finally
        {
            _client = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
```

### 4.5 LoggingBootstrapper

```csharp
// Share/Diagnostics/LoggingBootstrapper.cs
namespace Common.Diagnostics;

public static class LoggingBootstrapper
{
    private static bool _initialized;

    public static void Initialize(CliOptions options)
    {
        if (_initialized) return;
        
        // 自动选择日志模式
        if (!options.IsLogModeExplicitlySet)
        {
            options.LogOutputMode = options.ShouldRunGuiMode()
                ? LogOutputMode.Database
                : LogOutputMode.Console;
        }

        FileDrivenLogManager.Initialize(options);
        _initialized = true;
    }

    public static void AutoInitialize()
    {
        try
        {
            var projectRoot = ProjectPathManager.FindProjectRoot();
            var projectPath = System.IO.Path.Combine(
                projectRoot, "FileDriven", "filedriven_log_messages.json");
            if (File.Exists(projectPath))
            {
                FileDrivenLogPlaceholderHandler.Initialize(projectPath);
            }

            // 加载日志过滤配置
            var filterConfigPath = System.IO.Path.Combine(
                projectRoot, "FileDriven", "LogFilterConfig.json");
            LogFilterLoader.Load(filterConfigPath);
        }
        catch { /* 静默失败，日志系统降级 */ }
    }

    public static void Shutdown()
    {
        if (!_initialized) return;
        FileDrivenLogManager.Shutdown();
        _initialized = false;
    }
}
```

### 4.6 BatchLoggingBootstrapper

```csharp
// Share/Diagnostics/BatchLoggingBootstrapper.cs
namespace Common.Diagnostics;

public static class BatchLoggingBootstrapper
{
    public static string Initialize(
        string? batchId = null, 
        string? batchName = null,
        string? batchSource = null,
        string? batchDesc = null)
    {
        batchId ??= Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_ID");
        batchName ??= Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_NAME");
        batchSource ??= Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE") ?? "Executor";
        batchDesc ??= Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_DESC");

        if (string.IsNullOrWhiteSpace(batchId))
        {
            batchId = $"Run_{DateTime.Now:yyyyMMdd_HHmmss}";
            batchName ??= batchId;
        }

        batchName ??= batchId;

        BatchManager.Instance.RegisterBatch(batchId, batchName, batchSource, batchDesc);
        BatchLoggerBridge.EnterBatch(batchId);

        return batchId;
    }
}
```

### 4.7 WorkingDirectoryManager

```csharp
// BasicMethod/PathManager/WorkingDirectoryManager.cs
namespace PathManager;

public static class WorkingDirectoryManager
{
    public static bool SetToProjectRoot()
    {
        try
        {
            var projectRoot = ProjectPathManager.FindProjectRoot();
            Environment.CurrentDirectory = projectRoot;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

### 4.8 ComponentValidationService

```csharp
// Share/Component/ComponentValidationService.cs
namespace FrameworkAbstractions.ComponentRegistry;

public static class ComponentValidationService
{
    public static HealthReport Validate()
    {
        return FrameworkHealthCheck.ExecuteAndReport(
            msg => FileDrivenLogPlaceholderHandler.Log(
                "Executor_ComponentValidation", new { message = msg }),
            typeof(ComponentValidationService));
    }
}
```

### 4.9 NonEventCommandValidator

```csharp
// Share/CommandFramework/NonEventCommandValidator.cs
namespace CommandFramework;

public static class NonEventCommandValidator
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public int TotalCommands { get; set; }
        public int RegisteredCount { get; set; }
        public string[] MissingHandlers { get; set; } = Array.Empty<string>();
        public string[] CriticalMissing { get; set; } = Array.Empty<string>();
    }

    public static ValidationResult Validate(INonEventCommandRegistry registry)
    {
        var result = registry.ValidateAll();
        return new ValidationResult
        {
            IsValid = result.IsValid,
            TotalCommands = result.TotalCommands,
            RegisteredCount = result.RegisteredCount,
            MissingHandlers = result.MissingHandlers,
            CriticalMissing = result.CriticalMissing
        };
    }

    public static void ValidateOrThrow(INonEventCommandRegistry registry)
    {
        var result = Validate(registry);
        if (result.CriticalMissing.Length > 0)
        {
            throw new InvalidOperationException(
                $"关键非事件命令缺少处理函数：{string.Join(", ", result.CriticalMissing)}");
        }
    }
}
```

### 4.10 TemplateEngineBootstrapper

```csharp
// Share/PlaceholderFramework/TemplateEngineBootstrapper.cs
namespace Common.PlaceholderFramework.TemplateExpansion.Core;

public static class TemplateEngineBootstrapper
{
    public static Func<string, string?>? TemplateProcessor { get; private set; }

    public static void Initialize(Action<TemplateConfigurationBuilder>? configure = null)
    {
        try
        {
            TemplateConfiguration.InitializeAll(builder =>
            {
                configure?.Invoke(builder);
                // 默认配置
                builder.UseExtends(JsonPipelineRegistry.Storage)
                       .UseStyleApply(JsonPipelineRegistry.Storage)
                       .UseTemplateExpand(JsonPipelineRegistry.Storage)
                       .UseDefaultsApply(JsonPipelineRegistry.Storage)
                       .UseParameterResolve()
                       .UseValidation(false);
            });

            if (!JsonPipelineRegistry.IsConfigured)
                return;

            TemplateProcessor = path => ProcessJsonWithTemplate(path: path);

            UnifiedJsonSerializer.Preprocessor.RegisterStep(
                json => ProcessJsonWithTemplate(json: json) ?? json,
                "ReferenceExpansion");
        }
        catch { /* 模板引擎初始化失败，降级运行 */ }
    }

    private static string? ProcessJsonWithTemplate(string? path = null, string? json = null)
    {
        return FileDriven.JsonRefer.JsonProcessing.JsonTemplateProcessor.Process(path, json);
    }
}
```

### 4.11 ExecutionCenterBootstrapper

```csharp
// Share/ExecutionCenter/ExecutionCenterBootstrapper.cs
namespace ExecutionCenter.Core;

public static class ExecutionCenterBootstrapper
{
    public static ITaskExecutorPool? CreateAndStart(
        IPipelineEntryInstance worker,
        Action<ITaskExecutorPool>? registerHandlers = null)
    {
        try
        {
            var pool = new TaskExecutorPool(worker);
            pool.Start();
            registerHandlers?.Invoke(pool);
            return pool;
        }
        catch
        {
            return null;
        }
    }
}
```

### 4.12 PipelineEntryFactory

```csharp
// Share/Pipeline/PipelineEntryFactory.cs
namespace FileDriven.Pipeline;

public static class PipelineEntryFactory
{
    public static PipelineEntry CreateExecutionEntry(
        out ExecutionPipelineEntry executionEntry,
        ITaskExecutorPool? pool = null)
    {
        var pipelineEntry = new PipelineEntry(new FunctionStorageFunctionExecutor());
        executionEntry = new ExecutionPipelineEntry(pipelineEntry, pool);
        CommandFrameworkUsage.RegisterServices(configLoader: pipelineEntry);
        return pipelineEntry;
    }
}
```

### 4.13 CommandFileResolver

```csharp
// BasicMethod/PathManager/CommandFileResolver.cs
namespace PathManager;

public static class CommandFileResolver
{
    public static string? Resolve(string commandFile)
    {
        var resolvedPath = PathResolver.ResolveConfigPath(commandFile);
        if (resolvedPath == null)
        {
            var scriptsDir = ProjectDirectories.FileDriven_Scripts;
            var scriptsPath = System.IO.Path.Combine(scriptsDir, commandFile);
            if (File.Exists(scriptsPath))
                resolvedPath = scriptsPath;
        }

        if (resolvedPath == null)
            return null;

        // JSON 脚本自动转换为命令文件
        var ext = System.IO.Path.GetExtension(resolvedPath).ToLowerInvariant();
        if (ext == ".json")
            return ScriptConverter.ConvertJsonScriptToCommandFile(resolvedPath);

        return resolvedPath;
    }
}
```

### 4.14 OutputParameterResolver

```csharp
// Share/ParameterResolver/OutputParameterResolver.cs
namespace CommandFramework.Abstractions;

public static class OutputParameterResolver
{
    public static string? GetOutputTarget(IExecutionContext context)
    {
        if (context.ParsedCommand?.Parameters == null)
            return null;

        if (context.ParsedCommand.Parameters.TryGetValue("output", out var outputObj) 
            && outputObj != null)
        {
            return outputObj.ToString();
        }

        return null;
    }
}
```

### 4.15 EventBinderInjector

```csharp
// Share/UniversalMiddleware/EventBinderInjector.cs
namespace UniversalMiddleware.Abstractions;

public static class EventBinderInjector
{
    /// <summary>
    /// 将事件绑定服务从 ComponentRegistry 注入到 RenderPipeline
    /// </summary>
    public static void InjectToRenderPipeline()
    {
        if (!WdUIUsage.IsInitialized || WdUIUsage.RenderPipeline == null)
            return;

        var eventBinder = ComponentRegistry.Instance
            .Resolve<IEventBindingService>();
        
        if (eventBinder != null)
            FileDrivenWdUIBridge.InjectEventBinder(eventBinder);
    }

    /// <summary>
    /// 延迟注入 IWinFormsEventBinder 到已注册的 DefaultEventBindingService
    /// </summary>
    public static void SetWinFormsEventBinder(IWinFormsEventBinder winFormsEventBinder)
    {
        var eventBinder = ComponentRegistry.Instance
            .Resolve<IEventBindingService>();

        if (eventBinder is DefaultEventBindingService defaultBinder)
            defaultBinder.WinFormsEventBinder = winFormsEventBinder;
    }
}
```

---

## 五、重构后的 FileDrivenExecutor

```csharp
public class FileDrivenExecutor : IDisposable
{
    private FrameworkBootstrapper? _bootstrapper;
    private NamedPipeClientManager _pipeManager = new();
    private bool _disposed;

    public ExecutionResult Run(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();

        // 步骤 0：全局异常捕获
        GlobalExceptionHandler.Configure(
            onThreadException: ex => FileDrivenLogPlaceholderHandler.Log(...),
            onAppDomainException: ex => FileDrivenLogPlaceholderHandler.Log(...));

        // 步骤 1：解析参数
        var options = ParseArguments(args);
        if (options.Mode == RunMode.ShowUsage) { ... }
        if (options.Mode == RunMode.Demo) { Demo(); return; }

        // 步骤 2：管道连接
        _pipeManager.Connect(options.PipeName);

        // 步骤 3-4：日志 + 工作目录
        LoggingBootstrapper.AutoInitialize();
        LoggingBootstrapper.Initialize(options);
        WorkingDirectoryManager.SetToProjectRoot();

        // 步骤 5：框架初始化（模块化）
        bool cliMode = !options.ShouldRunGuiMode();
        _bootstrapper = CreateBootstrapper(cliMode);
        _bootstrapper.Run(new BootstrapperContext(cliMode, options));

        // 步骤 6：模式分发
        try
        {
            if (options.ShouldRunGuiMode())
                StartGuiMode(options);
            else
                LastResult = StartCliMode(options);
        }
        catch (Exception ex) { ... }
        finally
        {
            _bootstrapper?.Shutdown();
            LoggingBootstrapper.Shutdown();
            _pipeManager.Disconnect();
        }

        return LastResult!;
    }

    private FrameworkBootstrapper CreateBootstrapper(bool cliMode)
    {
        return new FrameworkBootstrapper()
            .UseModule<JsonConverterModule>()
            .UseConditional<WdUIInitializationModule>(ctx => !ctx.CliMode)
            .UseModule<PathAliasModule>()
            .UseModule<FunctionStorageModule>()
            .UseModule<FileDrivenFunctionModule>()
            .UseModule<ComponentRegistrationModule>()
            .UseModule<TemplateEngineModule>()
            .UseModule<BatchLoggingModule>()
            .UseModule<NonEventCommandModule>()
            .UseModule<ExecutionCenterModule>()
            .UseConditional<EventBinderModule>(ctx => !ctx.CliMode);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipeManager.Dispose();
    }
}
```

---

## 六、重构效果预估

| 指标 | 当前 | 重构后 | 改善 |
|------|------|--------|------|
| FileDrivenExecutor 行数 | ~1845 行 | ~400 行 | **-78%** |
| 初始化步骤硬编码 | 15+ 步顺序硬编码 | 模块化注册 | 可插拔、可测试 |
| 重复代码（Run/RunGui/RunCli） | 3 份相似流程 | 共享 Bootstrapper | 消除重复 |
| 单元测试难度 | 高（静态依赖多） | 低（IModule 可 mock） | 可测试性提升 |
| 跨项目复用 | 不可复用 | 各模块可独立复用 | 通用框架能力 |
| 新建文件数 | - | 15 个新文件 | 关注点分离 |

---

## 七、实施建议

### 7.1 实施顺序

```
Phase 1（P0，安全重构）
├── StringHelper.FindFirstUnprotectedSeparator → BasicMethod/Common
├── PathAliasLoader → BasicMethod/PathManager
├── GlobalExceptionHandler → Share/Diagnostics
└── NamedPipeClientManager → Share/Adapters

Phase 2（P1，中等重构）
├── LoggingBootstrapper → Share/Diagnostics
├── BatchLoggingBootstrapper → Share/Diagnostics
├── WorkingDirectoryManager → BasicMethod/PathManager
├── ComponentValidationService → Share/Component
├── NonEventCommandValidator → Share/CommandFramework
└── FrameworkBootstrapper → Share/FrameworkAbstractions/Bootstrapping

Phase 3（P2，复杂重构）
├── NonEventCommandProvider → Operation/FunctionStorage/NonEventCommands
├── TemplateEngineBootstrapper → Share/PlaceholderFramework
├── ExecutionCenterBootstrapper → Share/ExecutionCenter
├── ComponentRegistrationService → Share/Component
├── EventBinderInjector → Share/UniversalMiddleware
├── OutputParameterResolver → Share/ParameterResolver
├── PipelineEntryFactory → Share/Pipeline
└── CommandFileResolver → BasicMethod/PathManager
```

### 7.2 注意事项

1. **向后兼容**：每个抽象步骤应保持原有接口签名，确保现有调用方不受影响
2. **渐进式重构**：建议按 Phase 1 → 2 → 3 的顺序逐步实施，每个 Phase 完成后进行测试
3. **依赖注入**：Phase 3 中应考虑将静态依赖（CommandFrameworkUsage、MainForm）改为可注入的接口
4. **日志兼容**：`FileDrivenLogPlaceholderHandler.Log()` 的日志键应保持兼容，或提供映射机制
