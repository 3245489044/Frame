# 基于 MetaCommand 的统一命令路由方案

> 基于 `BasicMethod/MetaCommand` 通用命令框架，重构 FileDriven 的命令路由体系，
> 消除 4 条并行执行路径、6 处 switch-case 重复分发、桥接层职责过重等架构问题。

---

## 一、现状分析（改造前）

### 1.1 改造前的架构问题

```
用户输入命令
    │
    ├─→ CliModeRouter.ExecuteAsync()  ←── 包装了 MetaCommand.CommandRouter
    │       ├─→ ExecuteViaBridgeAsync()  [桥接路径 - 绕过 CommandRouter]
    │       └─→ NormalizeCommand() → _router.ExecuteCommandAsync()  [回退路径]
    │
    ├─→ MainForm.ExecuteCommandInternal()  [完全绕过 CliModeRouter]
    │       └─→ ExecuteSimpleCommandAsync()  [自行解析 + 分发]
    │
    └─→ UICommandManager.ExecuteCommand()  [完全绕过，独立 CommandEngine 实例]
```

**核心问题**：
1. **4 条并行命令执行路径**，同一命令（如 `load`）行为不一致
2. **6 处 switch-case 重复**，添加新命令需修改 4-6 个文件
3. **桥接层（IEntryBridge）** 既做转发又做回调，变成双向通道
4. **UICommandManager** 持有独立 `CommandEngine` 实例，与桥接层不共享
5. **两套结果类型**：`OperationResult<T>`（MetaCommand）和 `ICommandExecutionResult`（FileDriven）

### 1.2 根因

阶段一（提取通用执行框架）创建了 `WorkFlowEngine.Abstractions`，阶段二（改造入口层）引入了桥接层，但保留了所有旧路径作为"向后兼容"，导致新旧路径长期并存。

---

## 二、目标架构

### 2.1 核心设计原则

1. **MetaCommand.CommandRouter 是唯一命令路由引擎**
2. **所有命令通过 MetaVerb 体系流转**
3. **Adapter 是唯一的命令执行器**
4. **CliModeRouter 只做命令规范化，不做路由**
5. **MainForm 只负责 UI 展示，不解析命令**
6. **UICommandManager 降级为纯 UI 操作工具**

### 2.2 改造后的调用图

```
用户输入命令
    │
    └─→ CliModeRouter.ExecuteAsync()  ←── 唯一入口
            │
            └─→ NormalizeToMetaCommand()  [将简写命令转为 MetaVerb 格式]
                    │
                    └─→ MetaCommand.CommandRouter.ExecuteCommandAsync()
                            │
                            └─→ MetaCommand.Parse()  [解析为 MetaVerb + Action + Target]
                                    │
                                    └─→ 根据 MetaVerb 调用 Adapter 对应方法
                                            │
                                            ├─→ Do → Adapter.DoAsync(action, target, params)
                                            │       ├─→ [GUI] GuiCommandAdapter
                                            │       │       ├─→ load → MainForm.LoadSideContainerAsync()
                                            │       │       ├─→ set → UICommandManager.SetControlValue()
                                            │       │       ├─→ click → UICommandManager.ClickControl()
                                            │       │       ├─→ get → UICommandManager.GetControlValue()
                                            │       │       ├─→ close → UICommandManager.CloseForm()
                                            │       │       ├─→ show → UICommandManager.ShowForm()
                                            │       │       ├─→ list → UICommandManager.GetRegisteredFormNames()
                                            │       │       ├─→ echo → 直接返回消息
                                            │       │       └─→ sleep → Task.Delay()
                                            │       │
                                            │       └─→ [CLI] BackendCommandAdapter
                                            │               ├─→ load → VirtualControlStore + 配置解析
                                            │               ├─→ set → VirtualControlStore.Set()
                                            │               ├─→ click → EventBindingResolver + FunctionExecution
                                            │               ├─→ get → VirtualControlStore.Get()
                                            │               ├─→ close → VirtualControlStore.Clear()
                                            │               ├─→ echo → 直接返回消息
                                            │               └─→ sleep → Thread.Sleep()
                                            │
                                            ├─→ Get → Adapter.GetAsync(entity, params)
                                            ├─→ Look → Adapter.LookAsync(scope, params)
                                            ├─→ Wait → Adapter.WaitAsync(condition, timeout)
                                            └─→ Assert → Adapter.AssertAsync(predicate, params)
```

---

## 三、MetaCommand 更新

### 3.1 更新：Parse 支持简写命令

`MetaCommand.Parse()` 当前要求命令以 MetaVerb 开头（`do`/`get`/`look`/`wait`/`assert`），但 FileDriven 用户输入的是简写形式（`load`/`set`/`click`/`echo`/`sleep`/`close`/`show`/`list`）。

**更新内容**：在 `MetaCommand.Parse()` 中添加自动补全逻辑。

```csharp
// BasicMethod/MetaCommand/Models.cs - MetaCommand.Parse() 更新

public static MetaCommand Parse(string command)
{
    if (string.IsNullOrWhiteSpace(command))
        throw new ArgumentException("Command cannot be null or empty", nameof(command));

    var tokenizer = new CommandTokenizer(command);
    var tokens = tokenizer.Tokenize();

    if (tokens.Count == 0)
        throw new ArgumentException("Command cannot be empty", nameof(command));

    var verbStr = tokens[0].ToLower();

    // ★ 新增：如果第一个单词不是 MetaVerb，自动补全为 do 动词
    // ★ 例如: "load Configs/test.json" → do + load + Configs/test.json
    // ★ 例如: "echo hello" → do + echo + hello
    if (!Enum.TryParse<MetaVerb>(verbStr, true, out var verb))
    {
        return ParseAsDoCommand(tokens, command);
    }

    // ... 原有 do/get/look/wait/assert 解析逻辑不变 ...
}

/// <summary>
/// 将简写命令解析为 do 动词格式
/// </summary>
private static MetaCommand ParseAsDoCommand(List<string> tokens, string originalCommand)
{
    var cmd = new MetaCommand
    {
        Verb = MetaVerb.Do,
        OriginalCommand = originalCommand,
        CommandId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow
    };

    // tokens[0] = action (load/set/click/echo/sleep/close/show/list)
    cmd.Action = tokens[0];

    // tokens[1..] = target (剩余部分作为 target)
    if (tokens.Count > 1)
        cmd.Target = string.Join(" ", tokens.Skip(1));

    return cmd;
}
```

### 3.2 更新：CommandTokenizer 添加静态便捷方法

FileDriven 当前使用 `FileDrivenFramework.Core.CommandTokenizer`（静态类），而 MetaCommand 的 `CommandTokenizer` 是实例类。为方便 FileDriven 直接使用 MetaCommand 的 Tokenizer，添加静态便捷方法。

```csharp
// BasicMethod/MetaCommand/Parsing.cs - 添加静态便捷方法

public static class CommandTokenizerExtensions
{
    /// <summary>
    /// 解析命令字符串为命令名和参数数组
    /// </summary>
    public static (string commandName, string[] args) Parse(string command)
    {
        var tokenizer = new CommandTokenizer(command);
        var tokens = tokenizer.Tokenize();
        if (tokens.Count == 0)
            return (string.Empty, Array.Empty<string>());
        return (tokens[0], tokens.Skip(1).ToArray());
    }

    /// <summary>
    /// 提取命令的第一个单词（动词）
    /// </summary>
    public static string GetFirstWord(string command)
    {
        var tokenizer = new CommandTokenizer(command);
        var tokens = tokenizer.Tokenize();
        return tokens.Count > 0 ? tokens[0] : string.Empty;
    }

    /// <summary>
    /// 提取命令中第一个单词之后的剩余部分
    /// </summary>
    public static string GetRemaining(string command)
    {
        var (_, args) = Parse(command);
        return args.Length > 0 ? string.Join(" ", args) : string.Empty;
    }
}
```

---

## 四、FileDriven 改造方案（已实施）

### 4.1 CliModeRouter — 彻底重写

**文件**：`FileDriven/FileDriven.CLI/CliModeRouter.cs`

**改造内容**：
- 移除 `ExecuteViaBridgeAsync()` 方法
- 移除 `NormalizeCommand()` 方法
- 移除 `ActionCommands` 静态字段
- 移除 `IEntryBridge? _bridge` 字段
- 新增 `NormalizeToMetaCommand()` 方法
- `ExecuteAsync()` 只做规范化 + 委托给 `_router.ExecuteCommandAsync()`

```csharp
using MetaCommand;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileDriven.CLI
{
    /// <summary>
    /// CLI 模式路由器 - 唯一命令入口
    /// 职责：将简写命令规范化为 MetaVerb 格式，委托给 MetaCommand.CommandRouter 执行
    /// </summary>
    public class CliModeRouter
    {
        private readonly CommandRouter _router;
        private readonly ILogger<CliModeRouter> _logger;

        /// <summary>
        /// 需要转换为 do 动词的命令列表
        /// </summary>
        private static readonly HashSet<string> ActionCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "load", "set", "click", "get", "echo", "sleep", "close", "show", "list"
        };

        public CliModeRouter(bool useGuiMode, ILoggerFactory? loggerFactory = null)
        {
            loggerFactory ??= NullLoggerFactory.Instance;
            _logger = loggerFactory.CreateLogger<CliModeRouter>();

            var logger = loggerFactory.CreateLogger<CommandRouter>();
            var statistics = new CommandStatistics();
            _router = new CommandRouter(logger, statistics);

            // 始终注册后端适配器（CLI 模式默认使用）
            var backendAdapter = new Adapters.BackendCommandAdapter(loggerFactory);
            _router.RegisterAdapter("backend", backendAdapter);

            if (!useGuiMode)
            {
                _router.SetActiveAdapter("backend");
                _logger.LogInformation("CliModeRouter initialized in CLI mode (backend adapter active)");
            }
            else
            {
                _logger.LogInformation("CliModeRouter initialized in GUI mode (GUI adapter pending)");
            }
        }

        /// <summary>
        /// 设置 GUI 适配器（在 GUI 模式下调用）
        /// </summary>
        public void SetGuiAdapter(Adapters.GuiCommandAdapter guiAdapter)
        {
            _router.RegisterAdapter("gui", guiAdapter);
            _router.SetActiveAdapter("gui");
            _logger.LogInformation("GUI adapter registered and activated");
        }

        /// <summary>
        /// 执行命令 - 唯一入口
        /// 将简写命令规范化为 MetaVerb 格式，全部委托给 MetaCommand.CommandRouter
        /// </summary>
        public async Task<OperationResult<object>> ExecuteAsync(string command)
        {
            var trimmed = command.TrimStart();

            // 跳过注释行
            if (trimmed.StartsWith("#"))
                return OperationResult<object>.SuccessResult(null, "");

            // 将简写命令转为 MetaCommand 可识别的格式
            // 例如: load Configs/test.json → do load Configs/test.json
            //       echo hello → do echo hello
            var normalized = NormalizeToMetaCommand(trimmed);

            // ★ 全部委托给 MetaCommand.CommandRouter
            // ★ CommandRouter 内部会:
            //   1. MetaCommand.Parse() 解析为 MetaVerb.Do + Action="load" + Target="Configs/test.json"
            //   2. 根据 MetaVerb.Do 调用当前活跃 Adapter 的 DoAsync("load", "Configs/test.json", null)
            return await _router.ExecuteCommandAsync(normalized);
        }

        /// <summary>
        /// 将简写命令规范化为 MetaVerb 格式
        /// </summary>
        private static string NormalizeToMetaCommand(string command)
        {
            var firstWord = CommandTokenizerExtensions.GetFirstWord(command);
            if (string.IsNullOrEmpty(firstWord))
                return command;

            // 如果已经是 MetaVerb（do/get/look/wait/assert）开头，不需要转换
            if (Enum.TryParse<MetaVerb>(firstWord, true, out _))
                return command;

            // 如果是简写命令，添加 "do " 前缀
            if (ActionCommands.Contains(firstWord))
                return "do " + command;

            return command;
        }

        /// <summary>
        /// 获取当前活跃的适配器 ID
        /// </summary>
        public string? GetActiveAdapterId() => _router.GetActiveAdapterId();

        /// <summary>
        /// 获取命令执行指标
        /// </summary>
        public CommandMetrics GetMetrics() => _router.GetMetrics();
    }
}
```

### 4.2 MainForm — 移除命令解析

**文件**：`FileDriven/FileDriven.UI/Core/MainForm.cs`

**改造内容**：
- 移除 `EntryBridge` 属性（不再需要桥接层）
- 新增 `CommandExecutor` 属性（类型 `Func<string, Task<OperationResult<object>>>?`）
- `ExecuteCommandInternal()` 只处理 help/clear，其余委托给 `CommandExecutor`
- 移除 `ExecuteSimpleCommandAsync()` 方法
- 移除 `ExecuteSimpleCommandSync()` 方法

```csharp
// MainForm.cs - 新增属性
/// <summary>
/// 命令执行委托（由外部注入，指向 CliModeRouter.ExecuteAsync）
/// 所有命令通过此委托执行，MainForm 不再自行解析命令
/// </summary>
[System.ComponentModel.DesignerSerializationVisibility(
    System.ComponentModel.DesignerSerializationVisibility.Hidden)]
public Func<string, Task<OperationResult<object>>>? CommandExecutor { get; set; }

// MainForm.cs - 改造 ExecuteCommandInternal
private void ExecuteCommandInternal(string command)
{
    if (string.IsNullOrEmpty(command))
        return;

    // 内置命令直接处理
    if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
    {
        ShowHelp();
        return;
    }

    if (command.Equals("clear", StringComparison.OrdinalIgnoreCase))
    {
        _outputTextBox.Clear();
        return;
    }

    LogMessage($"> {command}");

    // ★ 所有其他命令委托给 CommandExecutor
    if (CommandExecutor != null)
    {
        _ = ExecuteCommandViaExecutorAsync(command);
    }
    else
    {
        LogMessage("[ERROR] Command executor not available");
    }
}

/// <summary>
/// 通过命令执行委托异步执行命令
/// </summary>
private async Task ExecuteCommandViaExecutorAsync(string command)
{
    try
    {
        var result = await CommandExecutor!(command);
        if (!result.Success)
        {
            LogMessage($"[ERROR] {result.Message}");
        }
    }
    catch (Exception ex)
    {
        LogMessage($"[ERROR] {ex.Message}");
    }
}
```

### 4.3 UICommandManager — 降级为纯 UI 操作工具

**文件**：`FileDriven/FileDriven.UI/Commands/UICommandManager.cs`

**改造内容**：
- 移除 `_commandEngine` 字段
- 移除 `ExecuteCommand()` 方法
- 移除 `ExecuteCommandWithResultAsync()` 方法
- 移除 `ExecuteFileAsync()` 方法
- 构造函数简化（不再需要 `CommandEngine` 参数）
- 保留所有 UI 操作方法

```csharp
public class UICommandManager
{
    /// <summary>
    /// ★ 移除 _commandEngine 字段
    /// ★ UICommandManager 不再执行命令，只做 UI 操作
    /// </summary>
    public UICommandManager()
    {
        if (!WdUIComposer.IsInitialized)
            WdUIComposer.Initialize();
    }

    // ★ 移除 ExecuteCommand() 方法
    // ★ 移除 ExecuteCommandWithResultAsync() 方法
    // ★ 移除 ExecuteFileAsync() 方法

    // ★ 保留以下纯 UI 操作方法：

    public async Task<Form?> LoadFormConfigAsync(string configPath, string? formName = null) { ... }
    public void ShowForm(string formName) => ViewManager.Instance.ShowForm(formName);
    public void CloseForm(string formName) => ViewManager.Instance.CloseForm(formName);
    public List<string> GetRegisteredFormNames() => ViewManager.Instance.GetRegisteredFormNames().ToList();
    public bool TryGetForm(string formName, out Form? form) { ... }

    public static Control? FindControlByName(string name) { ... }
    public bool SetControlValue(string controlName, string value) { ... }
    public bool ClickControl(string controlName) { ... }
    public bool GetControlValue(string controlName) { ... }

    public void Shutdown() => WdUIComposer.Shutdown();
}
```

### 4.4 GuiCommandAdapter — 移除桥接路径

**文件**：`FileDriven/FileDriven.CLI/Adapters/GuiCommandAdapter.cs`

**改造内容**：
- 移除 `_bridge` 字段
- 移除 `DoViaBridgeAsync()` 方法
- `DoAsync()` 直接执行，不再判断桥接路径

```csharp
public class GuiCommandAdapter : BaseAdapter
{
    private readonly UICommandManager _uiManager;
    private readonly ILogger<GuiCommandAdapter> _logger;
    private FileDriven.UI.Core.MainForm? _mainForm;

    public GuiCommandAdapter(UICommandManager uiManager, ILoggerFactory? loggerFactory = null)
        : base("gui", "GUI Command Adapter", "1.0")
    {
        _uiManager = uiManager ?? throw new ArgumentNullException(nameof(uiManager));
        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<GuiCommandAdapter>();
    }

    public void SetMainForm(FileDriven.UI.Core.MainForm mainForm)
    {
        _mainForm = mainForm;
    }

    public override async Task<OperationResult<object>> DoAsync(string action, string? target,
        Dictionary<string, object>? parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            // ★ 直接执行，不再判断桥接路径
            return action.ToLowerInvariant() switch
            {
                "load"  => await DoLoadAsync(target, parameters),
                "set"   => await DoSetAsync(target, parameters),
                "click" => await DoClickAsync(target, parameters),
                "close" => DoClose(target),
                "echo"  => OperationResult<object>.SuccessResult(target, target ?? ""),
                "sleep" => await DoSleepAsync(target),
                "show"  => DoShow(target),
                "list"  => DoList(),
                _       => OperationResult<object>.FailureResult(
                              $"Unknown action: {action}", ErrorCode.InvalidParameter)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Do/{Action}", action);
            return OperationResult<object>.FailureResult(
                $"Error executing {action}: {ex.Message}", ErrorCode.UnknownError, ex);
        }
    }

    // ★ 移除 DoViaBridgeAsync() 方法

    // ★ 保留以下具体命令实现：
    private async Task<OperationResult<object>> DoLoadAsync(string? target, ...) { ... }
    private Task<OperationResult<object>> DoSetAsync(string? target, ...) { ... }
    private Task<OperationResult<object>> DoClickAsync(string? target, ...) { ... }
    private OperationResult<object> DoClose(string? target) { ... }
    private async Task<OperationResult<object>> DoSleepAsync(string? target) { ... }
    private OperationResult<object> DoShow(string? target) { ... }
    private OperationResult<object> DoList() { ... }

    // ★ 保留 BaseAdapter 重写：
    public override Task<OperationResult<object>> GetAsync(...) { ... }
    public override Task<OperationResult<WorldModel>> LookAsync(...) { ... }
    public override Task<OperationResult<bool>> WaitAsync(...) { ... }
    public override Task<OperationResult<bool>> AssertAsync(...) { ... }
    public override Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(...) { ... }
}
```

### 4.5 BackendCommandAdapter — 无需改造

**文件**：`FileDriven/FileDriven.CLI/Adapters/BackendCommandAdapter.cs`

**说明**：BackendCommandAdapter 本身没有桥接层依赖，其 `DoAsync()` 中的 switch-case 由 MetaCommand 体系调用，无需改造。

### 4.6 Program.cs — 简化初始化

**文件**：`FileDriven/FileDriven.Launcher/Program.cs`

**改造内容**：
- 移除 `_entryBridge` 字段
- 移除 `DefaultEntryBridge` 创建
- 移除 `RegisterUiDelegates()` 方法
- 注入 `CommandExecutor` 委托到 `MainForm`

```csharp
internal static class Program
{
    private static UICommandManager _commandManager = null!;
    private static CliModeRouter? _cliRouter;

    // ★ 移除 _entryBridge 字段

    static void Main(string[] args)
    {
        ParseCommandLineArgs(args);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            InitializeFramework();

            if (ShouldRunGuiMode())
                RunGuiMode();
            else
                RunCliMode().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FATAL] {ex.Message}");
            Console.WriteLine($"[FATAL] {ex.StackTrace}");
        }

        ShutdownFramework();
    }

    private static void InitializeFramework()
    {
        Console.WriteLine("[INFO] Initializing WdUI framework...");
        WdUIComposer.Initialize();

        var projectRoot = ProjectPathManager.FindProjectRoot();
        WorkflowExecutionLayer.SetConfigBasePath(projectRoot);

        Console.WriteLine("[INFO] Initializing built-in functions...");
        MathLibrary.RegisterAll();
        ModelLibrary.RegisterAll();

        _commandManager = new UICommandManager();

        // ★ 移除 _entryBridge 创建
        // ★ 不再需要桥接层

        Console.WriteLine("[INFO] Framework initialized");
    }

    // ★ 移除 RegisterUiDelegates() 方法

    private static void RunGuiMode()
    {
        Console.WriteLine("[INFO] Starting GUI mode...");

        var mainForm = new MainForm();
        ViewManager.Instance.RegisterForm(mainForm);

        // ★ 创建 GuiCommandAdapter（不再需要桥接层）
        var guiAdapter = new GuiCommandAdapter(_commandManager);
        guiAdapter.SetMainForm(mainForm);

        // ★ 创建 CliModeRouter（GUI 模式）
        _cliRouter = new CliModeRouter(useGuiMode: true);
        _cliRouter.SetGuiAdapter(guiAdapter);

        // ★ 注入 CommandExecutor 委托到 MainForm
        mainForm.CommandExecutor = async (command) =>
        {
            return await _cliRouter.ExecuteAsync(command);
        };

        // 如果有 --config 参数，自动加载配置
        if (!string.IsNullOrEmpty(_configPath))
        {
            Console.WriteLine($"[INFO] Loading config into main area: {_configPath}");
            mainForm.LoadConfig(_configPath);
        }

        // 如果有 --file 参数，在初始化完成后执行脚本
        if (!string.IsNullOrEmpty(_commandFile))
        {
            mainForm.InitializationCompleted += async (s, e) =>
            {
                Console.WriteLine($"[INFO] Executing command file in GUI mode: {_commandFile}");
                var executor = new CommandFileExecutor(
                    async line =>
                    {
                        var result = await _cliRouter.ExecuteAsync(line);
                        var msg = result.Success
                            ? $"[OK] {result.Message}"
                            : $"[ERROR] {result.Message}";
                        Console.WriteLine(msg);
                        return result.Success;
                    });

                var (successCount, failCount) = await executor.ExecuteFileAsync(
                    _commandFile,
                    outputAction: msg => Console.WriteLine(msg),
                    lineDelayMs: 50);

                Console.WriteLine($"[INFO] GUI script execution completed: {successCount} succeeded, {failCount} failed.");
            };
        }

        Console.WriteLine($"[INFO] Starting WinForms message loop with MainForm: {mainForm.Text}");
        Application.Run(mainForm);
    }

    private static async Task RunCliMode()
    {
        Console.WriteLine("\n=====================================");
        Console.WriteLine("   FileDriven Launcher (CLI Mode)");
        Console.WriteLine("=====================================");

        // ★ CLI 模式不再需要桥接层
        _cliRouter = new CliModeRouter(useGuiMode: false);

        try
        {
            if (!string.IsNullOrEmpty(_commandFile))
            {
                Console.WriteLine($"[INFO] Executing command file: {_commandFile}");
                var executor = new CommandFileExecutor(
                    async line =>
                    {
                        var result = await _cliRouter.ExecuteAsync(line);
                        if (result.Success)
                            Console.WriteLine($"[OK] {result.Message}");
                        else
                            Console.WriteLine($"[ERROR] {result.Message}");
                        return result.Success;
                    });

                var (successCount, failCount) = await executor.ExecuteFileAsync(
                    _commandFile,
                    outputAction: msg => Console.WriteLine(msg));

                Console.WriteLine();
                Console.WriteLine($"[INFO] CLI script execution completed: {successCount} succeeded, {failCount} failed.");
            }

            if (!string.IsNullOrEmpty(_initialCommand))
            {
                Console.WriteLine($"[INFO] Executing initial command: {_initialCommand}");
                var result = await _cliRouter.ExecuteAsync(_initialCommand);
                Console.WriteLine(result.Success ? $"[OK] {result.Message}" : $"[ERROR] {result.Message}");
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FATAL] {ex.Message}");
        }

        Console.WriteLine("[INFO] CLI commands executed, exiting.");
    }
}
```

---

## 五、删除桥接层相关代码（已实施）

### 5.1 已删除文件

| 文件 | 说明 |
|------|------|
| `FileDrivenFramework.Abstractions/IEntryBridge.cs` | 桥接层接口，不再需要 |
| `FileDriven.CLI/Bridge/DefaultEntryBridge.cs` | 桥接层默认实现 |
| `FileDriven.CLI/Bridge/CircularDependencyGuard.cs` | 循环依赖防护 |

### 5.2 已移除引用

- `FileDriven.UI.csproj` 中移除对 `FileDriven.CLI` 的引用（解决循环依赖）

---

## 六、改造前后对比

| 维度 | 改造前 | 改造后 |
|------|--------|--------|
| **命令入口数量** | 4 个（CliModeRouter/MainForm/UICommandManager/Adapter） | 1 个（CliModeRouter） |
| **命令解析引擎** | 3 套（各自实现字符串分割） | 1 套（MetaCommand.Parse） |
| **命令分发位置** | 6 处 switch-case | 1 处（MetaCommand.CommandRouter 根据 MetaVerb） |
| **桥接层** | 存在（双向通道，5 种职责） | 删除 |
| **UICommandManager 职责** | 命令解析 + 命令执行 + UI 操作 | 纯 UI 操作 |
| **MainForm 命令处理** | 自行解析 + 回退路径 | 委托给 CliModeRouter |
| **结果类型** | 两套（OperationResult + ICommandExecutionResult） | 一套（OperationResult） |
| **添加新命令** | 修改 4-6 个文件 | 修改 1 个 Adapter |
| **命令行为一致性** | 不一致（同一命令不同实现） | 一致（单一实现） |
| **调试难度** | 高（4 条路径交叉） | 低（单一路径） |

---

## 七、实施步骤（已完成）

| 步骤 | 内容 | 涉及文件 | 状态 |
|------|------|---------|------|
| 1 | 更新 MetaCommand.Parse() 支持简写命令 | `BasicMethod/MetaCommand/Models.cs` | ✅ 已完成 |
| 2 | 更新 MetaCommand.CommandTokenizer 添加静态方法 | `BasicMethod/MetaCommand/Parsing.cs` | ✅ 已完成 |
| 3 | 重写 CliModeRouter | `FileDriven/FileDriven.CLI/CliModeRouter.cs` | ✅ 已完成 |
| 4 | 改造 MainForm | `FileDriven/FileDriven.UI/Core/MainForm.cs` | ✅ 已完成 |
| 5 | 降级 UICommandManager | `FileDriven/FileDriven.UI/Commands/UICommandManager.cs` | ✅ 已完成 |
| 6 | 改造 GuiCommandAdapter | `FileDriven/FileDriven.CLI/Adapters/GuiCommandAdapter.cs` | ✅ 已完成 |
| 7 | 改造 BackendCommandAdapter | `FileDriven/FileDriven.CLI/Adapters/BackendCommandAdapter.cs` | ✅ 无需改造 |
| 8 | 改造 Program.cs | `FileDriven/FileDriven.Launcher/Program.cs` | ✅ 已完成 |
| 9 | 删除桥接层代码 | `IEntryBridge.cs`, `DefaultEntryBridge.cs` 等 | ✅ 已完成 |
| 10 | 更新 csproj 引用 | 各 `.csproj` 文件 | ✅ 已完成 |

---

## 八、附录：MetaCommand 体系核心概念

### 8.1 MetaVerb 动词体系

| 动词 | 用途 | 对应 Adapter 方法 | 示例 |
|------|------|-------------------|------|
| `do` | 执行操作 | `DoAsync(action, target, params)` | `do load Configs/test.json` |
| `get` | 获取数据 | `GetAsync(entity, params)` | `get workflowComboBox` |
| `look` | 查看状态 | `LookAsync(scope, params)` | `look default` |
| `wait` | 等待条件 | `WaitAsync(condition, timeout)` | `wait 1000` |
| `assert` | 断言检查 | `AssertAsync(predicate, params)` | `assert executeButton` |

### 8.2 简写命令映射

FileDriven 的简写命令在 `NormalizeToMetaCommand()` 中被自动转换为 `do` 动词格式：

| 用户输入 | 规范化后 | MetaCommand 解析结果 |
|---------|---------|-------------------|
| `load Configs/test.json` | `do load Configs/test.json` | Verb=Do, Action=load, Target=Configs/test.json |
| `set ctrl value` | `do set ctrl value` | Verb=Do, Action=set, Target=ctrl value |
| `click btn` | `do click btn` | Verb=Do, Action=click, Target=btn |
| `echo hello` | `do echo hello` | Verb=Do, Action=echo, Target=hello |
| `sleep 1000` | `do sleep 1000` | Verb=Do, Action=sleep, Target=1000 |
| `close form` | `do close form` | Verb=Do, Action=close, Target=form |
| `show form` | `do show form` | Verb=Do, Action=show, Target=form |
| `list` | `do list` | Verb=Do, Action=list, Target=null |

### 8.3 命令流转过程

```
用户输入: "load Configs/test.json"
    │
    ▼
CliModeRouter.ExecuteAsync("load Configs/test.json")
    │
    ▼
NormalizeToMetaCommand("load Configs/test.json")
    → "do load Configs/test.json"
    │
    ▼
MetaCommand.CommandRouter.ExecuteCommandAsync("do load Configs/test.json")
    │
    ▼
MetaCommand.Parse("do load Configs/test.json")
    → Verb=Do, Action="load", Target="Configs/test.json"
    │
    ▼
根据 Verb=Do → 调用 Adapter.DoAsync("load", "Configs/test.json", null)
    │
    ├─→ [GUI] GuiCommandAdapter.DoAsync("load", "Configs/test.json", null)
    │       → _mainForm.LoadSideContainerAsync("Configs/test.json")
    │
    └─→ [CLI] BackendCommandAdapter.DoAsync("load", "Configs/test.json", null)
            → WdUIComposer.LoadConfigAsync → VirtualControlStore
```
