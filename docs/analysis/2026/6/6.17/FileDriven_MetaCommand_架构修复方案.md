# FileDriven 层引用 MetaCommand 的架构修复方案

> **版本**：v1.0  
> **日期**：2026-06-16  
> **范围**：FileDriven 全部子项目  
> **目标**：从框架层面系统性解决 20 个逻辑错误，消除架构缺陷

---

## 目录

1. [问题概述](#1-问题概述)
2. [根本性架构缺陷分析](#2-根本性架构缺陷分析)
3. [框架层面修复方案](#3-框架层面修复方案)
4. [实施路线图](#4-实施路线图)
5. [预期效果](#5-预期效果)
6. [附录：问题与修复对照表](#6-附录问题与修复对照表)

---

## 1. 问题概述

经过对 FileDriven 全部子项目中引用 `MetaCommand` 的代码进行深入分析，发现了 **20 个逻辑错误**，按严重度分为 P0~P3 四个等级：

| 等级 | 数量 | 描述 |
|------|------|------|
| 🔴 P0 | 2 | 严重错误：资源泄漏、命令执行双轨制 |
| 🟠 P1 | 5 | 重要错误：静默降级、脆弱设计、职责过重 |
| 🟡 P2 | 8 | 一般错误：死引用、过度耦合、时序依赖 |
| 🟢 P3 | 5 | 轻微错误：复杂度过高、职责错位 |

这些问题的根因可归纳为 **5 个根本性架构缺陷**，而非孤立的技术债务。

---

## 2. 根本性架构缺陷分析

### 缺陷 1：命令生命周期无统一管理

**涉及问题**：#1, #2, #5, #13, #17

**现状**：命令的注册、执行、解析分散在三处：

```
命令注册：
  ├── CommandEngine.RegisterBuiltInCommands()    → sleep/echo/exit
  ├── NonEventCommandCenter (FileDrivenExecutor) → sleep/echo/exit (重复)
  └── FileDrivenExecutor.RegisterNonEventCommands() → sideload/exec/load/show/close

命令执行：
  ├── CommandEngine.Execute()          → 走 ICommandRouter 委托
  └── NonEventCommandCenter.Execute()  → 走 IExecutionContext 回调

命令解析：
  ├── CommandFramework.Parsing.CommandParser
  └── CommandTokenizerExtensions.Parse
```

**后果**：
- 同一命令（如 `sleep`）在两个地方有 handler，执行路径不同，行为可能不一致
- `_executionPool` 在 `InitializeFramework()` 中创建，但 `ShutdownFramework()` 中未释放，导致资源泄漏
- 两种解析器对同一命令字符串可能产生不同的解析结果

---

### 缺陷 2：分层架构依赖倒置缺失

**涉及问题**：#3, #8~12, #20

**现状**：FileDriven 各层直接依赖 BasicMethod 具体类型：

```
FileDriven.Utilities/CliOptions.cs
  → 直接依赖 MetaCommand.LogCategory 枚举

FileDriven.Utilities/CLI/LogCommandHandler.cs
  → 直接引用 10+ 个 MetaCommand 具体类型

FileDriven.UI/Core/MainForm.cs
  → CommandExecutor 返回 OperationResult<object>（来自 BasicMethod）

FileDriven.UI/Core/CommandHandler.cs
  → using MetaCommand; 但未使用（死引用）

FileDriven.UI/Skeletons/UISkeletons.cs
  → using MetaCommand; 但只剩注释（死引用）

FileDriven.Logging/Skeletons.cs
  → using MetaCommand; 仅用于日志方法
```

**后果**：
- 违反依赖倒置原则（DIP），高层模块依赖低层模块的具体实现
- 增加编译时间和耦合度
- 跨层依赖导致重构困难

---

### 缺陷 3：状态管理脆弱

**涉及问题**：#4, #6, #7

**现状**：

```
FileDrivenPipelineContext.Reset()
  → 手动保存和恢复 10 个运行时依赖字段
  → 注释警告"如果新增了运行时依赖字段，必须同步更新"
  → 典型的脆弱设计

CliOptions.ShouldRunGuiMode()
  → 通过"有 ConfigPath 且无 CommandFile/InitialCommand"隐式推断 GUI 模式
  → 当同时传入 ConfigPath 和 CommandFile 时，行为与直觉相反

MainForm.LoadSideContainerAsync()
  → 同时负责 4 个职责：加载配置、注册配置、注册控件、执行初始化命令
  → 违反单一职责原则
```

**后果**：
- 任何开发者新增字段时都可能忘记同步更新 `Reset()`，导致运行时依赖静默丢失
- 隐式模式推断逻辑难以理解和调试
- 职责过重的方法，任何一步失败都会影响其他步骤

---

### 缺陷 4：错误处理策略不统一

**涉及问题**：#3, #14, #15

**现状**：

```
ParsedVerb 静默降级：
  Enum.TryParse 失败 → 静默降级为 MetaVerb.Do
  → 无任何日志输出

日志过滤配置静默降级：
  LogFilterConfig.json 不存在 → 静默降级为不过滤
  → 无任何警告

路径解析逻辑分散：
  ├── ResolveCommandFile()
  ├── PathResolver.ResolveConfigPath()
  ├── ProjectDirectories.FileDriven_Scripts
  └── 多处独立的路径解析
```

**后果**：
- 调试时难以定位问题根源
- 路径解析不一致导致文件找不到
- 静默降级掩盖了配置错误

---

### 缺陷 5：类型系统混乱

**涉及问题**：#18, #19

**现状**：存在 5 种命令执行结果类型：

```
1. CommandExecutionResult      (CommandEngine)
2. ExecutionResult             (CommandFramework)
3. CommandFileLineResult       (FileDriven)
4. OperationResult<object>     (BasicMethod.FunctionExecution)
5. ICommandExecutionResult     (接口)
```

**后果**：
- 没有统一的转换机制
- 各层之间需要手动适配
- `FileDrivenPipelineContext.IsNonEventCommandAction` 职责错位

---

## 3. 框架层面修复方案

### 3.1 修复 1：统一命令生命周期管理

**解决**：#1, #2, #5, #13, #17

#### 3.1.1 架构设计

```
                    ┌─────────────────────┐
                    │   CommandRegistry    │  ← 唯一注册入口
                    │  (ICommandRegistry)  │
                    └──────────┬──────────┘
                               │
          ┌────────────────────┼────────────────────┐
          ▼                    ▼                    ▼
   RegisterBuiltIn()   RegisterFileDriven()   RegisterCustom()
   (sleep/echo/exit)   (sideload/exec/load)   (用户自定义)
          │                    │                    │
          └────────────────────┼────────────────────┘
                               ▼
                    ┌─────────────────────┐
                    │    CommandEngine     │  ← 唯一执行引擎
                    │  (不再自行注册命令)   │
                    └──────────┬──────────┘
                               │
                    ┌──────────▼──────────┐
                    │   CommandParser     │  ← 唯一解析器
                    │ (CommandFramework)  │
                    └─────────────────────┘
```

#### 3.1.2 接口定义

```csharp
// Share/CommandFramework/Abstractions/ICommandRegistry.cs
public interface ICommandRegistry
{
    void RegisterCommand(string name, Func<string[], Task<ICommandExecutionResult>> handler);
    void UnregisterCommand(string name);
    Func<string[], Task<ICommandExecutionResult>>? GetHandler(string name);
    bool ContainsCommand(string name);
    IEnumerable<string> GetRegisteredCommands();
}
```

#### 3.1.3 实现类

```csharp
// FileDriven.Execution/Command/CommandRegistry.cs
public class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, Func<string[], Task<ICommandExecutionResult>>> _handlers
        = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterCommand(string name, Func<string[], Task<ICommandExecutionResult>> handler)
    {
        _handlers[name] = handler;
    }

    public void UnregisterCommand(string name)
    {
        _handlers.Remove(name);
    }

    public Func<string[], Task<ICommandExecutionResult>>? GetHandler(string name)
    {
        _handlers.TryGetValue(name, out var handler);
        return handler;
    }

    public bool ContainsCommand(string name) => _handlers.ContainsKey(name);

    public IEnumerable<string> GetRegisteredCommands() => _handlers.Keys;

    // 批量注册内置命令
    public void RegisterBuiltInCommands(IAsyncSyncOrchestrator orchestrator)
    {
        RegisterCommand("sleep", async args =>
        {
            if (args.Length > 0 && int.TryParse(args[0], out var ms))
            {
                await orchestrator.DelayAsync(ms);
                return CommandExecutionResult.Ok($"sleep {ms}", $"Slept for {ms}ms");
            }
            return CommandExecutionResult.Fail("sleep", "Usage: sleep <milliseconds>");
        });

        RegisterCommand("echo", args =>
        {
            var message = string.Join(" ", args);
            return Task.FromResult(CommandExecutionResult.Ok($"echo {message}", message));
        });

        RegisterCommand("exit", args =>
        {
            return Task.FromResult(CommandExecutionResult.Ok("exit", "Exiting..."));
        });
    }
}
```

#### 3.1.4 修改 CommandEngine

```csharp
// FileDriven.Utilities/CommandExecution.cs
public class CommandEngine
{
    private readonly ICommandRegistry? _registry;

    // 新增：从 Registry 构造
    public CommandEngine(ICommandRegistry registry, IAsyncSyncOrchestrator? orchestrator = null)
    {
        _orchestrator = orchestrator ?? new AsyncSyncOrchestrator();
        _registry = registry;
        // 不再调用 RegisterBuiltInCommands()
        // 命令由 CommandRegistry 统一管理
    }

    // 修改 Execute 方法：优先从 Registry 查找 handler
    public async Task<ICommandExecutionResult> Execute(string commandLine)
    {
        var parts = CommandParser.Parse(commandLine);
        var verb = parts.Verb;
        var args = parts.Args;

        // 1. 优先从 Registry 查找
        if (_registry != null && _registry.ContainsCommand(verb))
        {
            var handler = _registry.GetHandler(verb);
            if (handler != null)
                return await handler(args);
        }

        // 2. 回退到内部 handler
        if (_asyncHandlers.TryGetValue(verb, out var asyncHandler))
            return await asyncHandler(args);

        if (_syncHandlers.TryGetValue(verb, out var syncHandler))
            return syncHandler(args);

        // 3. 回退到路由器
        if (_router != null)
            return await _router.RouteAsync(verb, args);

        return CommandExecutionResult.Fail(verb, $"Unknown command: {verb}");
    }
}
```

#### 3.1.5 修改 FileDrivenExecutor

```csharp
// FileDriven.Execution/FileDrivenExecutor.cs

// 1. 添加 _executionPool 释放（P0 #2）
public void ShutdownFramework()
{
    // ★ 修复 P0 #2：释放 _executionPool
    _executionPool?.Dispose();
    _executionPool = null;

    // ... 其余关闭步骤
}

// 2. 修改 RegisterNonEventCommands，使用 CommandRegistry
private void RegisterNonEventCommands()
{
    var registry = new CommandRegistry();
    registry.RegisterBuiltInCommands(_orchestrator);

    registry.RegisterCommand("sideload", async args => { /* ... */ });
    registry.RegisterCommand("exec", async args => { /* ... */ });
    // ...

    _commandEngine = new CommandEngine(registry, _orchestrator);
}

// 3. AutoInitializeLogging 添加警告（P2 #14）
private void AutoInitializeLogging()
{
    var filterPath = Path.Combine(ProjectDirectories.FileDriven, "LogFilterConfig.json");
    if (!File.Exists(filterPath))
    {
        // ★ 修复 P2 #14：输出警告日志
        Console.WriteLine("[WARN] LogFilterConfig.json 不存在，日志不过滤。"
            + " 如需过滤，请在 FileDriven 目录下创建 LogFilterConfig.json");
    }
    // ...
}
```

---

### 3.2 修复 2：依赖倒置 + 接口抽象

**解决**：#3, #8~12, #20

#### 3.2.1 新建抽象接口层

```csharp
// Share/CommandFramework/Abstractions/ILogService.cs
public interface ILogService
{
    void Log(string level, string module, string message);
    void LogWarning(string module, string message);
    void LogError(string module, string message, Exception? ex = null);
    bool IsCategoryEnabled(string categoryName);
}

// Share/CommandFramework/Abstractions/IExecutionContext.cs
public interface IExecutionContext
{
    Task<ICommandExecutionResult> ExecuteCommandAsync(string commandLine);
    T? GetService<T>() where T : class;
}
```

#### 3.2.2 修改 CliOptions

```csharp
// FileDriven.Utilities/CLI/CliOptions.cs
public class CliOptions
{
    // ★ 修复 P1 #6：移除隐式模式推断
    // 将推断逻辑提升到 FileDrivenExecutor.Run() 中

    // 移除：
    // public bool ShouldRunGuiMode() { ... }

    // 新增显式判断方法
    public RunMode ResolveMode()
    {
        if (Mode != RunMode.Unspecified)
            return Mode;

        // 当 Mode == Unspecified 时，由调用方决定
        return RunMode.Unspecified;
    }
}
```

#### 3.2.3 修改 FileDrivenExecutor.Run()

```csharp
// FileDriven.Execution/FileDrivenExecutor.cs
public void Run(CliOptions options)
{
    // ★ 修复 P1 #6：显式模式推断
    var resolvedMode = options.ResolveMode();
    if (resolvedMode == RunMode.Unspecified)
    {
        // 由 FileDrivenExecutor 显式判断
        if (!string.IsNullOrEmpty(options.ConfigPath)
            && string.IsNullOrEmpty(options.CommandFile)
            && string.IsNullOrEmpty(options.InitialCommand))
        {
            resolvedMode = RunMode.GUI;
        }
        else
        {
            resolvedMode = RunMode.CLI;
        }
    }

    Console.WriteLine($"[INFO] 运行模式: {resolvedMode}");
    // ...
}
```

#### 3.2.4 修改 MainForm

```csharp
// FileDriven.UI/Core/MainForm.cs
public class MainForm : Form
{
    // ★ 修复 P1 #12：使用接口类型替代具体类型
    // 修改前：
    // public Func<string, Task<OperationResult<object>>>? CommandExecutor { get; set; }
    // 修改后：
    public Func<string, Task<ICommandExecutionResult>>? CommandExecutor { get; set; }
}
```

#### 3.2.5 清理死引用

| 文件 | 修改 |
|------|------|
| `FileDriven.UI/Core/CommandHandler.cs` | 移除 `using MetaCommand;` |
| `FileDriven.UI/Skeletons/UISkeletons.cs` | 移除 `using MetaCommand;` |
| `FileDriven.Logging/Skeletons.cs` | 通过 `ILogService` 接口调用日志方法 |

---

### 3.3 修复 3：状态管理重构

**解决**：#4, #6, #7

#### 3.3.1 FileDrivenPipelineContext.Reset() 重构

```csharp
// FileDriven.Execution/Command/FileDrivenPipelineContext.cs
public class FileDrivenPipelineContext
{
    // ★ 修复 P1 #4：使用 SharedData 字典替代手动保存/恢复
    private readonly ConcurrentDictionary<string, object?> _sharedData = new();

    // 新增：运行时依赖存取方法
    public void SetRuntimeDependency<T>(string key, T value)
        where T : class
    {
        _sharedData[key] = value;
    }

    public T? GetRuntimeDependency<T>(string key)
        where T : class
    {
        if (_sharedData.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return null;
    }

    // 简化 Reset()
    public void Reset()
    {
        // 清空运行时依赖（不再手动保存/恢复 10 个字段）
        _sharedData.Clear();

        // 重置基础属性
        ParsedCommand = null;
        // ...
    }

    // ★ 修复 P1 #3：移除 ParsedVerb 属性，改用字符串比较
    // 移除：
    // public MetaVerb ParsedVerb { get; private set; }

    // 新增：直接使用字符串
    public string? ParsedCommandVerb => ParsedCommand?.Verb;

    // 判断命令类型的方法
    public bool IsCommandType(string verb) =>
        string.Equals(ParsedCommandVerb, verb, StringComparison.OrdinalIgnoreCase);
}
```

#### 3.3.2 LoadSideContainerAsync 拆分

```csharp
// FileDriven.UI/Core/MainForm.cs
public class MainForm : Form
{
    /// <summary>
    /// ★ 修复 P1 #7：拆分为 4 个独立方法
    /// </summary>
    public async Task<UIConfiguration?> LoadSideContainerAsync(string configPath)
    {
        // 步骤 1：仅加载配置
        var config = await LoadConfigToSideContainer(configPath);
        if (config == null) return null;

        // 步骤 2：注册到 PipelineEntry
        RegisterConfigToPipeline(config);

        // 步骤 3：注册控件到 ControlRegistry
        RegisterControlsToRegistry(config);

        // 步骤 4：执行初始化命令
        await ExecuteInitCommands(config);

        return config;
    }

    // 步骤 1：仅加载配置
    private async Task<UIConfiguration?> LoadConfigToSideContainer(string configPath)
    {
        return await ConfigLoader.LoadSideContainerAsync(
            _sideContainer, configPath, _sideContainerLoadLock, LogMessage);
    }

    // 步骤 2：注册到 PipelineEntry
    private void RegisterConfigToPipeline(UIConfiguration config)
    {
        ConfigRegistrar?.RegisterLoadedConfig(config);
    }

    // 步骤 3：注册控件
    private void RegisterControlsToRegistry(UIConfiguration config)
    {
        RegisterSideContainerControls(config);
    }

    // 步骤 4：执行初始化命令
    private async Task ExecuteInitCommands(UIConfiguration config)
    {
        if (config.InitCommands == null || config.InitCommands.Count == 0)
            return;
        if (CommandExecutor == null) return;

        LogMessage($"[INFO] 执行初始化命令: {config.InitCommands.Count} 条");
        foreach (var cmd in config.InitCommands)
        {
            if (string.IsNullOrWhiteSpace(cmd)) continue;
            try
            {
                var result = await CommandExecutor(cmd);
                if (result != null && !result.Success)
                {
                    LogMessage($"[WARN] 初始化命令执行失败: {cmd}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"[ERROR] 初始化命令执行异常: {cmd}, {ex.Message}");
            }
        }
    }
}
```

---

### 3.4 修复 4：统一错误处理策略

**解决**：#3, #14, #15

#### 3.4.1 ParsedVerb 降级警告

```csharp
// FileDriven.Execution/Command/FileDrivenPipelineContext.cs
public class FileDrivenPipelineContext
{
    // ★ 修复 P1 #3：移除 ParsedVerb，改用字符串比较
    // 当需要判断命令类型时，直接比较字符串

    // 如果必须保留枚举映射，添加警告日志：
    private MetaVerb? TryParseVerb(string verb)
    {
        if (Enum.TryParse<MetaVerb>(verb, ignoreCase: true, out var result))
            return result;

        // ★ 修复：输出警告日志
        Console.WriteLine($"[WARN] 无法识别的命令动词 '{verb}'，"
            + "请检查命令名称是否正确。"
            + "FileDriven 自定义命令（如 sideload、exec）应通过字符串比较判断。");
        return null;
    }
}
```

#### 3.4.2 统一路径解析

```csharp
// FileDriven.Utilities/PathResolutionService.cs（新建）
/// <summary>
/// ★ 修复 P2 #15：统一路径解析服务
/// 所有路径解析逻辑集中到此服务，消除分散在各处的路径解析逻辑
/// </summary>
public class PathResolutionService
{
    private readonly string _baseDirectory;

    public PathResolutionService(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    /// <summary>解析命令文件路径</summary>
    public string? ResolveCommandFile(string? commandFile)
    {
        if (string.IsNullOrEmpty(commandFile)) return null;

        // 如果是绝对路径，直接返回
        if (Path.IsPathRooted(commandFile))
            return File.Exists(commandFile) ? commandFile : null;

        // 相对路径：从基础目录查找
        var fullPath = Path.Combine(_baseDirectory, commandFile);
        if (File.Exists(fullPath)) return fullPath;

        // 从 FileDriven/Scripts 目录查找
        var scriptsPath = Path.Combine(_baseDirectory, "FileDriven", "Scripts", commandFile);
        if (File.Exists(scriptsPath)) return scriptsPath;

        Console.WriteLine($"[WARN] 命令文件未找到: {commandFile}（已尝试: {fullPath}, {scriptsPath}）");
        return null;
    }

    /// <summary>解析配置路径</summary>
    public string ResolveConfigPath(string? configPath, string defaultRelativePath)
    {
        if (!string.IsNullOrEmpty(configPath))
        {
            if (Path.IsPathRooted(configPath))
                return configPath;
            return Path.Combine(_baseDirectory, configPath);
        }
        return Path.Combine(_baseDirectory, defaultRelativePath);
    }
}
```

---

### 3.5 修复 5：类型系统统一

**解决**：#18, #19

#### 3.5.1 统一结果类型转换

```csharp
// Share/CommandFramework/Abstractions/ICommandExecutionResult.cs（扩展）
public interface ICommandExecutionResult
{
    bool Success { get; }
    string CommandName { get; }
    string Message { get; }
    Exception? Exception { get; }
    object? Data { get; }
}

// 扩展方法：类型转换
public static class CommandExecutionResultExtensions
{
    public static CommandExecutionResult ToCommandExecutionResult(this ICommandExecutionResult result)
    {
        if (result is CommandExecutionResult cer) return cer;
        return result.Success
            ? CommandExecutionResult.Ok(result.CommandName, result.Message)
            : CommandExecutionResult.Fail(result.CommandName, result.Message, result.Exception);
    }

    public static ExecutionResult ToExecutionResult(this ICommandExecutionResult result)
    {
        return new ExecutionResult
        {
            Success = result.Success,
            Message = result.Message,
            Exception = result.Exception
        };
    }

    public static OperationResult<T> ToOperationResult<T>(this ICommandExecutionResult result)
    {
        return result.Success
            ? OperationResult<T>.Ok(result.Message, (T?)result.Data)
            : OperationResult<T>.Fail(result.Message, result.Exception);
    }
}
```

#### 3.5.2 职责分离

```csharp
// FileDriven.Execution/Command/CommandClassifier.cs（新建）
/// <summary>
/// ★ 修复 P3 #18：命令分类器
/// 从 FileDrivenPipelineContext 中提取命令分类职责
/// </summary>
public class CommandClassifier
{
    // 非事件命令列表
    private static readonly HashSet<string> NonEventCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "sideload", "exec", "load", "show", "close",
        "click", "set", "get", "list", "sleep", "help"
    };

    public bool IsNonEventCommand(string? verb)
    {
        if (string.IsNullOrEmpty(verb)) return false;
        return NonEventCommands.Contains(verb);
    }

    public bool IsFileDrivenSpecificCommand(string? verb)
    {
        if (string.IsNullOrEmpty(verb)) return false;
        return verb == "sideload" || verb == "exec";
    }
}
```

---

## 4. 实施路线图

### Phase 1：接口定义（无破坏性）

| 步骤 | 文件 | 改动 |
|------|------|------|
| 1.1 | `Share/CommandFramework/Abstractions/ICommandRegistry.cs` | **新建**：命令注册中心接口 |
| 1.2 | `Share/CommandFramework/Abstractions/ILogService.cs` | **新建**：日志服务抽象接口 |
| 1.3 | `Share/CommandFramework/Abstractions/IExecutionContext.cs` | **新建**：执行上下文抽象接口 |
| 1.4 | `Share/CommandFramework/Abstractions/ICommandExecutionResult.cs` | **扩展**：添加 `Data` 属性和扩展方法 |
| 1.5 | `FileDriven.Utilities/PathResolutionService.cs` | **新建**：统一路径解析服务 |

### Phase 2：核心修复（P0 + P1）

| 步骤 | 文件 | 改动 | 解决 |
|------|------|------|------|
| 2.1 | `FileDriven.Execution/Command/CommandRegistry.cs` | **新建**：CommandRegistry 实现 | #1 |
| 2.2 | `FileDriven.Utilities/CommandExecution.cs` | **修改**：CommandEngine 支持从 Registry 构造 | #1 |
| 2.3 | `FileDriven.Execution/FileDrivenExecutor.cs` | **修改**：RegisterNonEventCommands 使用 Registry；ShutdownFramework 释放 _executionPool | #1, #2 |
| 2.4 | `FileDriven.Execution/Command/FileDrivenPipelineContext.cs` | **修改**：移除 ParsedVerb，Reset 使用 SharedData 字典 | #3, #4 |
| 2.5 | `FileDriven.Execution/FileDrivenExecutor.cs` | **修改**：Run() 中显式模式推断 | #6 |
| 2.6 | `FileDriven.UI/Core/MainForm.cs` | **修改**：拆分 LoadSideContainerAsync | #7 |
| 2.7 | `FileDriven.Execution/Command/CommandClassifier.cs` | **新建**：命令分类器 | #18 |

### Phase 3：清理优化（P2 + P3）

| 步骤 | 文件 | 改动 | 解决 |
|------|------|------|------|
| 3.1 | `FileDriven.UI/Core/CommandHandler.cs` | 移除 `using MetaCommand;` | #8 |
| 3.2 | `FileDriven.UI/Skeletons/UISkeletons.cs` | 移除 `using MetaCommand;` | #9 |
| 3.3 | `FileDriven.Utilities/CLI/CliOptions.cs` | 通过 ILogService 接口解耦 | #10 |
| 3.4 | `FileDriven.Utilities/CLI/LogCommandHandler.cs` | 通过 ILogService 接口解耦 | #11 |
| 3.5 | `FileDriven.UI/Core/MainForm.cs` | CommandExecutor 返回 ICommandExecutionResult | #12 |
| 3.6 | `FileDriven.Execution/FileDrivenExecutor.cs` | 添加 InjectEventBinderToRenderPipeline 时序警告 | #13 |
| 3.7 | `FileDriven.Execution/FileDrivenExecutor.cs` | AutoInitializeLogging 添加 LogFilterConfig 警告 | #14 |
| 3.8 | `FileDriven.Execution/FileDrivenExecutor.cs` | 使用 PathResolutionService 统一路径解析 | #15 |
| 3.9 | `FileDriven.Execution/FileDrivenExecutor.cs` | 拆分 exec handler | #16 |
| 3.10 | `FileDriven.Logging/Skeletons.cs` | 通过 ILogService 接口调用日志方法 | #20 |

---

## 5. 预期效果

### 5.1 量化指标

| 指标 | 修复前 | 修复后 |
|------|-------|-------|
| 命令注册中心数量 | 2 个（CommandEngine + NonEventCommandCenter） | **1 个**（CommandRegistry） |
| 命令解析器数量 | 2 个（CommandParser + CommandTokenizerExtensions） | **1 个**（CommandParser） |
| 命令执行结果类型 | 5 种 | **1 种接口 + 转换器** |
| 静默降级路径 | 3 处 | **0 处**（全部输出警告） |
| 手动状态保存/恢复 | 10 个字段 | **0 个**（自动字典管理） |
| 资源泄漏 | 1 处（_executionPool） | **0 处** |
| 死引用 | 3 处 | **0 处** |
| 跨层直接依赖 | 4 处 | **0 处**（全部通过接口） |

### 5.2 架构质量提升

| 维度 | 提升 |
|------|------|
| **可维护性** | 命令注册、执行、解析路径单一，新增命令只需注册一次 |
| **可测试性** | 接口抽象使各层可独立 Mock 测试 |
| **可观测性** | 所有降级路径输出警告日志，问题可追踪 |
| **安全性** | 资源泄漏消除，状态管理不再依赖手动同步 |
| **扩展性** | 新增命令类型只需实现 ICommandRegistry 接口 |

---

## 6. 附录：问题与修复对照表

| 编号 | 等级 | 问题描述 | 修复方案 | 阶段 |
|------|------|---------|---------|------|
| #1 | 🔴 P0 | 命令执行双轨制 | CommandRegistry 统一注册 | Phase 2 |
| #2 | 🔴 P0 | _executionPool 资源泄漏 | ShutdownFramework 添加 Dispose | Phase 2 |
| #3 | 🟠 P1 | ParsedVerb 静默降级 | 移除 ParsedVerb，改用字符串比较 | Phase 2 |
| #4 | 🟠 P1 | Reset() 手动保存/恢复脆弱 | SharedData 字典替代 | Phase 2 |
| #5 | 🟠 P1 | 命令解析双轨制 | 统一使用 CommandParser | Phase 2 |
| #6 | 🟠 P1 | CliOptions 隐式模式推断 | 提升到 FileDrivenExecutor.Run() 显式判断 | Phase 2 |
| #7 | 🟠 P1 | LoadSideContainerAsync 职责过重 | 拆分为 4 个独立方法 | Phase 2 |
| #8 | 🟡 P2 | CommandHandler 死引用 | 移除 using MetaCommand | Phase 3 |
| #9 | 🟡 P2 | UISkeletons 死引用 | 移除 using MetaCommand | Phase 3 |
| #10 | 🟡 P2 | CliOptions 直接依赖 LogCategory | 通过 ILogService 解耦 | Phase 3 |
| #11 | 🟡 P2 | LogCommandHandler 过度耦合 | 通过 ILogService 解耦 | Phase 3 |
| #12 | 🟡 P2 | MainForm 依赖 OperationResult | 使用 ICommandExecutionResult 接口 | Phase 3 |
| #13 | 🟡 P2 | 事件绑定注入时序依赖 | 添加显式警告日志 | Phase 3 |
| #14 | 🟡 P2 | 日志过滤配置静默降级 | 添加警告日志 | Phase 3 |
| #15 | 🟡 P2 | 路径解析逻辑分散 | PathResolutionService 统一 | Phase 3 |
| #16 | 🟢 P3 | exec handler 复杂度过高 | 拆分为多个独立方法 | Phase 3 |
| #17 | 🟢 P3 | CommandFrameworkUsage 初始化破坏状态 | 添加状态检查 | Phase 3 |
| #18 | 🟢 P3 | IsNonEventCommandAction 职责错位 | CommandClassifier 提取 | Phase 2 |
| #19 | 🟢 P3 | 命令执行结果类型混乱 | 统一 ICommandExecutionResult + 转换器 | Phase 3 |
| #20 | 🟢 P3 | Skeletons 引用 MetaCommand 仅用于日志 | 通过 ILogService 解耦 | Phase 3 |

---

## 7. 设计决策记录

### ADR-1：为什么选择 CommandRegistry 而非修改 CommandEngine？

**决策**：新建 `ICommandRegistry` 接口 + `CommandRegistry` 实现，而非在 `CommandEngine` 内部做兼容。

**理由**：
- `CommandEngine` 位于 `FileDriven.Utilities`，属于工具层，不应承担命令注册中心的职责
- `CommandRegistry` 放在 `FileDriven.Execution`，与 `FileDrivenExecutor` 同层，职责清晰
- 接口抽象使未来可以替换注册中心实现（如从配置文件加载）

### ADR-2：为什么选择字符串比较而非新增枚举？

**决策**：移除 `ParsedVerb` 属性，改用 `string ParsedCommandVerb` 直接字符串比较。

**理由**：
- FileDriven 自定义命令（`sideload`、`exec`）不属于 MetaCommand 的 `MetaVerb` 枚举
- 新增 FileDriven 专属枚举会增加维护成本（每次新增命令都要修改枚举）
- 字符串比较更灵活，且性能差异可忽略

### ADR-3：为什么选择 SharedData 字典而非上下文快照？

**决策**：使用 `ConcurrentDictionary<string, object?>` 替代手动保存/恢复 10 个字段。

**理由**：
- 字典方式无需在新增字段时同步修改 `Reset()` 方法
- `ConcurrentDictionary` 线程安全，支持并发访问
- 类型安全的泛型存取方法（`SetRuntimeDependency<T>` / `GetRuntimeDependency<T>`）
