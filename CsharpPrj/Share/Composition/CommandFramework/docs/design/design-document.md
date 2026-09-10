# CommandFramework — 通用命令执行框架设计文档

> 版本：v2.0
> 日期：2026-06-05
> 状态：已实施

---

## 一、背景与动机

### 1.1 当前问题

在 FileDriven 框架中，命令执行相关的逻辑分散在两个项目中，存在严重的架构混乱：

**问题 1：两条并行的非事件命令处理路径**

```
EventTriggerFramework.Commands.NonEventCommandHandler
  ├─ 已注入 ParseCommandStage（通过 CreateCliPipeline）
  ├─ 只实现了 echo/sleep/wait
  └─ load/close/exit 等是占位符返回"未实现"

FileDriven.Pipeline.Skeletons.NonEventCommandHandler（静态类）
  ├─ 有完整的 HandleLoad/HandleClose/HandleExit
  ├─ 但从未被注册到管线中
  └─ 完全被架空
```

**问题 2：`BuildCommandPipeline` 使用 `CreateDefaultPipeline()` 没有注入 `NonEventCommandHandler`**

```csharp
// FileDriven/FileDriven.Pipeline/PipelineEntry.cs 第652行
var stagePipeline = EventTriggerFramework.Pipeline.PipelineEntry.CreateDefaultPipeline("CommandPipeline");
```

`CreateDefaultPipeline()` 创建 `ParseCommandStage` 时 `_nonEventHandler = null`，导致所有非事件命令（load/echo/sleep/close/exit）都被静默跳过。这是测试失败的直接原因。

**问题 3：`EventTriggerFramework` 的 `NonEventCommandHandler` 与 `FileDriven` 的 `NonEventCommandHandler` 命名冲突**

同名但功能不同，容易混淆。

**问题 4：`CreateContext` 的浅拷贝问题**

```csharp
// PipelineEntry.cs 第300行
LoadedConfigs = new Dictionary<string, object>(_loadedConfigs, StringComparer.OrdinalIgnoreCase),
```

浅拷贝后，`load` 命令存入 `_loadedConfigs` 的数据不会反映到 `context.LoadedConfigs` 中。

### 1.2 根本原因

框架缺乏一个**统一的命令执行抽象层**，导致：

- 命令解析逻辑散落在 `EventTriggerFramework` 中
- 非事件命令处理逻辑散落在 `FileDriven` 中
- 两者之间没有清晰的接口契约
- 业务命令处理器无法被管线发现和使用

---

## 二、框架设计

### 2.1 框架定位

在 `Operation/` 下新建 `CommandFramework` 项目，作为**通用的命令执行框架**，职责：

| 职责 | 说明 |
|------|------|
| 命令解析 | 将命令字符串解析为结构化命令对象 |
| 命令分类 | 区分事件命令（需 UI 交互）与非事件命令（控制指令） |
| 命令注册与执行 | 注册命令处理器，按名称执行 |
| 非事件命令基础实现 | echo/sleep/wait（与 UI 无关的通用命令） |
| 扩展点 | 业务层可注册自定义命令处理器 |

### 2.2 框架边界

| 属于 CommandFramework | 不属于 CommandFramework |
|----------------------|------------------------|
| 命令解析（CommandParser） | 事件绑定查找（FindBindingStage） |
| 命令分类（事件/非事件） | 控件值提取（ExtractValuesStage） |
| 命令注册/执行引擎 | 函数执行（ExecuteFunctionStage） |
| 非事件命令接口与基础实现 | 结果写入（WriteResultStage） |
| 命令模型（ParsedCommand） | 配置加载（LoadConfig） |
| 管线阶段接口 | UI 窗口管理（Show/Close） |
| **状态存储（IStateStore / InMemoryStateStore）** | |
| **值提取器（IValueExtractor / DefaultValueExtractor）** | |
| **函数注册表（IFunctionRegistry / FunctionRegistry）** | |
| **函数执行器（IFunctionExecutor / FunctionExecutor）** | |
| **控件值读写器（IControlValueAccessor / ControlValueAccessorBase）** | |

### 2.3 项目结构

```
Operation/
├── CommandFramework/                    ← .NET 类库项目
│   ├── CommandFramework.csproj
│   ├── Abstractions/                    ← 接口定义
│   │   ├── ICommandParser.cs            ← 命令解析器接口
│   │   ├── INonEventCommandHandler.cs   ← 非事件命令处理器接口（异步）
│   │   ├── ICommandEngine.cs            ← 命令引擎接口
│   │   ├── ICommand.cs                  ← 命令接口
│   │   ├── ICommandPipeline.cs          ← 命令管线接口（从 EventTriggerFramework 迁移）
│   │   ├── ICommandExecutionResult.cs   ← 命令执行结果接口（从 EventTriggerFramework 迁移）
│   │   ├── IEnvironmentAdapter.cs       ← 环境适配器接口（从 EventTriggerFramework 迁移）
│   │   ├── IStateStore.cs               ← 状态存储接口（从 EventTriggerFramework 迁移）
│   │   ├── IValueExtractor.cs           ← 值提取器接口（从 EventTriggerFramework 迁移）
│   │   ├── IFunctionRegistry.cs         ← 函数注册表接口族（从 EventTriggerFramework 迁移）
│   │   └── IControlValueAccessor.cs     ← 控件值读写器接口（从 EventTriggerFramework 迁移）
│   ├── Models/                          ← 数据模型

│   │   ├── ParsedCommand.cs             ← 解析后的命令
│   │   ├── NonEventCommandResult.cs     ← 非事件命令执行结果
│   │   └── CommandExecutionContext.cs   ← 命令执行上下文
│   ├── Parsing/                         ← 命令解析
│   │   └── CommandParser.cs             ← 命令解析器实现
│   ├── Execution/                       ← 命令执行
│   │   ├── CommandEngine.cs             ← 命令引擎实现
│   │   ├── NonEventCommandHandler.cs    ← 基础实现（echo/sleep/wait，异步）
│   │   ├── DelegateCommand.cs           ← 委托命令
│   │   ├── CommandExecutionResult.cs    ← 命令执行结果实现（从 EventTriggerFramework 迁移）
│   │   ├── EnvironmentAdapterBase.cs    ← 环境适配器基类（从 EventTriggerFramework 迁移）
│   │   ├── InMemoryStateStore.cs        ← 内存状态存储（从 EventTriggerFramework 迁移）
│   │   ├── DefaultValueExtractor.cs     ← 默认值提取器（从 EventTriggerFramework 迁移）
│   │   ├── FunctionRegistry.cs          ← 函数注册表实现（从 EventTriggerFramework 迁移）
│   │   ├── FunctionExecutor.cs          ← 函数执行器实现（从 EventTriggerFramework 迁移）
│   │   └── ControlValueAccessorBase.cs  ← 控件值读写器基类（从 EventTriggerFramework 迁移）
│   ├── Pipeline/                        ← 管线基础设施（从 EventTriggerFramework 迁移）
│   │   ├── PipelineStageBase.cs         ← 管线阶段基类 + CommandStageBase
│   │   ├── CommandPipeline.cs           ← 通用命令管线实现 + CommandContext
│   │   ├── PipelineEntry.cs             ← 管线入口（静态 + 实例类）
│   │   └── Stages/
│   │       └── ParseCommandStage.cs     ← 命令解析阶段
│   └── Registration/                    ← 命令注册
│       └── CommandRegistry.cs           ← 命令注册表（工厂模式）
```

---

## 三、代码审查发现的问题

### 3.1 Bug 修复（已实施）

#### B1：`CommandEngine.ExecuteFileAsync()` 传递完整命令字符串而非命令名称

**文件：** `Operation/CommandFramework/Execution/CommandEngine.cs`

**问题：** `ExecuteFileAsync` 读取文件行后，将完整命令字符串（如 `"do click addButton"`）作为命令名称传递给 `ExecuteAsync`，但 `ExecuteAsync` 期望的是注册的命令名称（如 `"click"`）。

```csharp
// 修复前
var success = await ExecuteAsync(line, ct);  // line = "do click addButton"

// 修复后
var commands = lines
    .Select(l => l.Trim())
    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#") && !l.StartsWith("//"))
    .ToList();
var results = await PipelineEntry.ExecuteCommandsAsync(commands);
```

**影响：** 文件中的命令全部执行失败，返回 `(0, 0)`。

---

#### B2：`CommandEngine.UndoAsync()` 中不支持撤销的命令丢失问题

**文件：** `Operation/CommandFramework/Execution/CommandEngine.cs`

**问题：** 撤销时，从 `_undoList` 中移除命令后，如果命令不支持撤销（`CanUndo == false`），不会将其添加到 `_redoList`，但命令已经从 `_undoList` 中移除了，导致该命令永久丢失。

```csharp
// 修复前
lock (_lock)
{
    if (_undoList.Count == 0) return false;
    command = _undoList.Last!.Value;
    _undoList.RemoveLast();
}
// 如果 command.CanUndo == false，命令已从 _undoList 移除但未添加到 _redoList

// 修复后
lock (_lock)
{
    if (_undoList.Count == 0) return false;
    command = _undoList.Last!.Value;
    _undoList.RemoveLast();
}
if (!command.CanUndo)
    return false;  // 命令未撤销，不添加到 _redoList
```

**影响：** 不支持撤销的命令执行后，无法通过重做恢复。

---

#### B3：`CommandEngine.ExecuteAsync()` 忽略传入 context 参数

**文件：** `Operation/CommandFramework/Execution/CommandEngine.cs`

**问题：** `ExecuteAsync(string name, CommandContext? context, CancellationToken ct)` 方法接收 `context` 参数，但内部实现中未将 `context` 传递给命令执行。

```csharp
// 修复前
await command.ExecuteAsync(ct);  // 忽略 context

// 修复后
if (context != null && command is ICommandWithContext cmdWithCtx)
{
    await cmdWithCtx.ExecuteAsync(context, ct);
}
else
{
    await command.ExecuteAsync(ct);
}
```

**影响：** 调用方传入的 `CommandContext` 数据无法传递给命令。

---

#### B4：`NonEventCommandHandler.Handle()` 同步包装器死锁风险

**文件：** `Operation/CommandFramework/Execution/NonEventCommandHandler.cs`

**问题：** 接口 `INonEventCommandHandler` 中 `Handle` 是同步方法，但内部实现使用 `HandleAsync(...).GetAwaiter().GetResult()`，在 UI 上下文（如 WinForms）中会导致死锁。

```csharp
// 修复前（接口）
public interface INonEventCommandHandler
{
    NonEventCommandResult Handle(ParsedCommand command);  // 同步
}

// 修复后（接口）
public interface INonEventCommandHandler
{
    Task<NonEventCommandResult> HandleAsync(ParsedCommand command);  // 异步
}
```

**影响：** 在 WinForms UI 线程中调用 `Handle` 会导致死锁。

---

### 3.2 冗余逻辑消除（已实施）

#### R1：`CommandRegistry._commandTypes` 死代码

**文件：** `Operation/CommandFramework/Registration/CommandRegistry.cs`

**问题：** `_commandTypes` 字典仅用于 `Register<T>()` 方法中存储类型信息，但从未被读取。`Resolve` 方法只使用 `_commandFactories`。

```csharp
// 修复前
private readonly ConcurrentDictionary<string, Type> _commandTypes = ...;  // 死代码
private readonly ConcurrentDictionary<string, Func<IServiceProvider?, ICommand>> _commandFactories = ...;

public void Register<T>() where T : ICommand, new()
{
    var name = typeof(T).Name;
    _commandTypes[name] = typeof(T);  // 写入但从未读取
    _commandFactories[name] = _ => new T();
}

// 修复后
private readonly ConcurrentDictionary<string, Func<IServiceProvider?, ICommand>> _commandFactories = ...;

public void Register<T>() where T : ICommand, new()
{
    var name = typeof(T).FullName ?? typeof(T).Name;  // 使用 FullName 避免命名冲突
    _commandFactories[name] = _ => new T();
}
```

**影响：** 内存浪费，`_commandTypes` 占用内存但无任何用途。

---

#### R2：`CommandRegistry.TryResolve()` 冗余包装

**文件：** `Operation/CommandFramework/Registration/CommandRegistry.cs`

**问题：** `TryResolve` 只是 `Resolve` 的简单包装，没有提供额外价值。

```csharp
// 修复前
public bool TryResolve(string name, out ICommand? command, IServiceProvider? serviceProvider = null)
{
    command = Resolve(name, serviceProvider);
    return command != null;
}

// 修复后：已删除
```

**影响：** 代码膨胀，增加维护成本。

---

#### R3：`CommandParser.Parse()` 中 `verb == "do"` 分支的冗余判断

**文件：** `Operation/CommandFramework/Parsing/CommandParser.cs`

**问题：** `verb == "do"` 分支中 `parts.Count >= 2` 的判断是冗余的，因为外层 `if (parts.Count >= 2)` 已经保证了这一点。

```csharp
// 修复前
if (verb == "do")
{
    action = parts.Count >= 2 ? parts[1].ToLowerInvariant() : null;  // 冗余判断
    target = parts.Count >= 3 ? parts[2] : null;
}

// 修复后
if (verb == "do")
{
    action = parts[1].ToLowerInvariant();  // 直接取值
    target = parts.Count >= 3 ? parts[2] : null;
}
```

**影响：** 无功能影响，仅代码可读性。

---

### 3.3 重复定义消除（已实施）

#### D1：`EventTriggerFramework` 与 `CommandFramework` 的重复接口定义

**文件：** `Operation/EventTriggerFramework/ICommandPipeline.cs` → 已修改

**问题：** `EventTriggerFramework.Abstractions` 命名空间中定义了与 `CommandFramework` 完全相同的 `ParsedCommand`、`NonEventCommandResult`、`ICommandParser`、`INonEventCommandHandler`。

**修改：** 删除重复定义，改为 `using CommandFramework.Abstractions;` 和 `using CommandFramework.Models;`。

---

#### D2：`EventTriggerFramework` 与 `CommandFramework` 的重复实现类

**文件：** `Operation/EventTriggerFramework/ICommand.cs` → 已修改

**问题：** `EventTriggerFramework.Commands` 命名空间中定义了与 `CommandFramework` 完全相同的 `CommandEngineBase`、`CommandParser`、`DelegateCommand`、`CompositeCommand`、`CommandExecutionContext`、`ICommand`、`ICommandEngine`。

**修改：** 删除重复实现，仅保留 `EventTriggerFramework` 特有的：
- `NonEventCommandHandler`（继承 `CommandFramework.Execution.NonEventCommandHandler`，添加占位处理器）
- `ICommandWithContext`（`EventTriggerFramework` 特有的扩展接口）

---

#### D3：`CommandRegistry` 与 `CommandEngine` 的功能重叠

**文件：** `Operation/CommandFramework/Registration/CommandRegistry.cs`

**问题：** `CommandRegistry` 和 `CommandEngine` 都提供了命令注册和解析功能，职责边界模糊。

**处理：** 明确职责边界：
- `CommandEngine`：命令执行引擎，管理执行/撤销/重做，内部维护命令存储
- `CommandRegistry`：工厂模式注册表，用于延迟创建命令实例（依赖注入场景）

---

### 3.4 代码质量改进（已实施）

#### Q1：`CommandRegistry.Register<T>()` 使用完整类型名称

**文件：** `Operation/CommandFramework/Registration/CommandRegistry.cs`

**问题：** `Register<T>()` 使用 `typeof(T).Name`（不含命名空间），不同命名空间下的同名类会导致注册冲突。

```csharp
// 修复前
var name = typeof(T).Name;  // 如 "MyCommand"

// 修复后
var name = typeof(T).FullName ?? typeof(T).Name;  // 如 "MyApp.Commands.MyCommand"
```

---

## 四、接口定义

### 4.1 `ICommandParser` — 命令解析器

```csharp
namespace CommandFramework.Abstractions;

/// <summary>
/// 命令解析器接口
/// 将命令字符串解析为结构化的 ParsedCommand
/// 支持命令规范化（简写 → 完整格式）
/// </summary>
public interface ICommandParser
{
    /// <summary>解析命令字符串</summary>
    ParsedCommand Parse(string command);

    /// <summary>规范化命令字符串（简写 → 完整格式），注释行返回 null</summary>
    string? Normalize(string command);

    /// <summary>判断是否为非事件命令</summary>
    bool IsNonEventCommand(string verb, string? action);
}
```

### 4.2 `INonEventCommandHandler` — 非事件命令处理器（异步）

```csharp
namespace CommandFramework.Abstractions;

/// <summary>
/// 非事件命令处理器接口
/// 处理不需要事件绑定的命令（echo/sleep/wait 等）
/// 支持注册自定义命令处理器
/// ★ 已改为异步接口，避免死锁 ★
/// </summary>
public interface INonEventCommandHandler
{
    /// <summary>异步处理非事件命令</summary>
    Task<NonEventCommandResult> HandleAsync(ParsedCommand command);

    /// <summary>注册自定义命令处理器（异步）</summary>
    void RegisterHandler(string actionName, Func<ParsedCommand, Task<NonEventCommandResult>> handler);

    /// <summary>判断是否支持指定的命令</summary>
    bool CanHandle(string actionName);
}
```

### 4.3 `ICommandEngine` — 命令引擎

```csharp
namespace CommandFramework.Abstractions;

/// <summary>
/// 命令引擎接口
/// 提供命令的注册、执行、撤销、重做功能
/// </summary>
public interface ICommandEngine : IDisposable
{
    /// <summary>注册命令（委托方式）</summary>
    void RegisterCommand(string name, Func<CommandExecutionContext, CancellationToken, Task> execute,
        Func<CommandExecutionContext, CancellationToken, Task>? undo = null, string? description = null);

    /// <summary>注册 ICommand 实例</summary>
    void RegisterCommand(ICommand command);

    /// <summary>执行命令</summary>
    Task<bool> ExecuteAsync(string name, CommandContext? context = null, CancellationToken ct = default);

    /// <summary>从文件异步执行命令</summary>
    Task<(int SuccessCount, int FailCount)> ExecuteFileAsync(string filePath, CancellationToken ct = default);

    /// <summary>撤销上一次执行的命令</summary>
    Task<bool> UndoAsync(CancellationToken ct = default);

    /// <summary>重做上一次撤销的命令</summary>
    Task<bool> RedoAsync(CancellationToken ct = default);

    /// <summary>是否可以撤销</summary>
    bool CanUndo { get; }

    /// <summary>是否可以重做</summary>
    bool CanRedo { get; }

    /// <summary>清空历史记录</summary>
    void ClearHistory();
}
```

### 4.4 `ICommand` — 命令接口

```csharp
namespace CommandFramework.Abstractions;

/// <summary>
/// 命令接口
/// </summary>
public interface ICommand
{
    /// <summary>命令名称</summary>
    string Name { get; }

    /// <summary>命令描述</summary>
    string? Description { get; }

    /// <summary>是否支持撤销</summary>
    bool CanUndo { get; }

    /// <summary>执行命令</summary>
    Task ExecuteAsync(CancellationToken ct = default);

    /// <summary>撤销命令</summary>
    Task UndoAsync(CancellationToken ct = default);
}
```

---

## 五、数据模型

### 5.1 `ParsedCommand`

```csharp
namespace CommandFramework.Models;

/// <summary>
/// 命令解析结果
/// 包含解析后的命令结构信息
/// </summary>
public class ParsedCommand
{
    /// <summary>动词（如 do、look、load）</summary>
    public string Verb { get; set; } = string.Empty;

    /// <summary>动作（如 click、set、load）</summary>
    public string? Action { get; set; }

    /// <summary>目标（如控件名 addButton、文件名 MathFunctions.json）</summary>
    public string? Target { get; set; }

    /// <summary>参数字典</summary>
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>规范化后的原始命令字符串</summary>
    public string? NormalizedCommand { get; set; }

    /// <summary>是否为非事件命令（如 load/close/echo 等）</summary>
    public bool IsNonEventCommand { get; set; }

    /// <summary>解析是否成功</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>解析错误信息</summary>
    public string? ErrorMessage { get; set; }
}
```

### 5.2 `NonEventCommandResult`

```csharp
namespace CommandFramework.Models;

/// <summary>
/// 非事件命令处理结果
/// </summary>
public class NonEventCommandResult
{
    /// <summary>是否成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>结果值</summary>
    public object? ResultValue { get; set; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }

    public static NonEventCommandResult Success(object? value = null)
        => new() { IsSuccess = true, ResultValue = value };

    public static NonEventCommandResult Failure(string error)
        => new() { IsSuccess = false, ErrorMessage = error };
}
```

### 5.3 `CommandExecutionContext`

```csharp
namespace CommandFramework.Models;

/// <summary>
/// 命令执行上下文（用于 ICommandEngine 的命令执行）
/// </summary>
public class CommandExecutionContext
{
    /// <summary>命令名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>命令参数</summary>
    public Dictionary<string, object?>? Parameters { get; set; }

    /// <summary>命令数据</summary>
    public object? Data { get; set; }
}
```

---

## 六、已修改文件清单

### CommandFramework 内部修改

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Execution/CommandEngine.cs` | Bug 修复 | B1: ExecuteFileAsync 传递命令名称而非完整字符串 |
| `Execution/CommandEngine.cs` | Bug 修复 | B2: UndoAsync 不支持撤销的命令不丢失 |
| `Execution/CommandEngine.cs` | Bug 修复 | B3: ExecuteAsync 传递 context 参数 |
| `Execution/NonEventCommandHandler.cs` | Bug 修复 | B4: 接口改为异步，消除死锁风险 |
| `Abstractions/INonEventCommandHandler.cs` | Bug 修复 | B4: 接口改为异步 |
| `Registration/CommandRegistry.cs` | 冗余消除 | R1: 删除 _commandTypes 死代码 |
| `Registration/CommandRegistry.cs` | 冗余消除 | R2: 删除 TryResolve 冗余包装 |
| `Registration/CommandRegistry.cs` | 质量改进 | Q1: Register<T>() 使用 FullName |
| `Parsing/CommandParser.cs` | 冗余消除 | R3: 简化 verb=="do" 分支的冗余判断 |

### CommandFramework 新增文件（从 EventTriggerFramework 迁移）

| 文件 | 说明 |
|------|------|
| `Abstractions/ICommandPipeline.cs` | 命令管线接口、管线上下文接口、管线阶段接口、Result<T>/Result/BatchResult<T> |
| `Abstractions/ICommandExecutionResult.cs` | 命令执行结果接口 ICommandExecutionResult、ICommandBatchResult |
| `Abstractions/IEnvironmentAdapter.cs` | 环境适配器接口 IEnvironmentAdapter（Do/Get/Look/Wait/Assert/Discover） |
| `Abstractions/IStateStore.cs` | 状态存储接口 IStateStore（Set/Get/Has/Clear/Remove） |
| `Abstractions/IValueExtractor.cs` | 值提取器接口 IValueExtractor（ExtractValue/ExtractAllValues） |
| `Abstractions/IFunctionRegistry.cs` | 函数注册表接口族（IParameterDescriptor/IFunctionDescriptor/IFunctionExecutionResult/IFunctionRegistry/IFunctionExecutor） |
| `Abstractions/IControlValueAccessor.cs` | 控件值读写器接口 IControlValueAccessor（GetValue/SetValue/RegisterAdapter） |
| `Execution/CommandExecutionResult.cs` | 命令执行结果实现（Ok/Fail 工厂方法） |
| `Execution/EnvironmentAdapterBase.cs` | 环境适配器基类（默认返回 NotSupported） |
| `Execution/InMemoryStateStore.cs` | 内存状态存储（ConcurrentDictionary 实现，线程安全） |
| `Execution/DefaultValueExtractor.cs` | 默认值提取器（支持 Dictionary/IDictionary/反射属性） |
| `Execution/FunctionRegistry.cs` | 函数注册表实现（委托注册、参数自动匹配、异步执行） |
| `Execution/FunctionExecutor.cs` | 函数执行器（按名/按委托执行，参数自动匹配，异步 Task<T> 结果提取） |
| `Execution/ControlValueAccessorBase.cs` | 控件值读写器基类（泛型适配器注册模式，支持 WinForms/WPF/Avalonia） |
| `Pipeline/PipelineStageBase.cs` | 管线阶段基类 PipelineStageBase<TContext> + CommandStageBase |
| `Pipeline/CommandPipeline.cs` | 通用命令管线实现 CommandPipeline<TContext> + CommandContext |
| `Pipeline/PipelineEntry.cs` | 管线入口（静态 PipelineEntry + 实例 PipelineEntryInstance） |
| `Pipeline/Stages/ParseCommandStage.cs` | 命令解析阶段（ParseCommandStage） |

### EventTriggerFramework 修改

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `EventTriggerFramework.csproj` | 依赖添加 | 添加对 CommandFramework 的项目引用 |
| `ICommandPipeline.cs` | 重复消除 | D1: 删除重复接口定义，引用 CommandFramework |
| `ICommand.cs` | 重复消除 | D2: 删除重复实现类，保留特有扩展 |
| `Pipeline/EventBindingStage.cs` | 适配修改 | 添加 using CommandFramework.Parsing; Handle→HandleAsync |
| `Pipeline/CommandPipeline.cs` | 适配修改 | 添加 using CommandFramework.Models; |

---

## 七、依赖关系变化

### 迁移前

```
EventTriggerFramework
  └─ 自包含：接口 + 实现都在自身

FileDriven.Pipeline
  ├─ 引用 EventTriggerFramework
  └─ 自包含：NonEventCommandHandler（静态类）独立存在
```

### 迁移后

```
CommandFramework（通用命令框架）
  ├─ 接口定义
  ├─ 基础实现（echo/sleep/wait）
  └─ 扩展点（INonEventCommandHandler）

EventTriggerFramework（事件框架）
  ├─ 引用 CommandFramework
  └─ 使用 CommandFramework 的接口和基础实现

FileDriven.Pipeline（业务层）
  ├─ 引用 CommandFramework
  ├─ 注册业务命令处理器（load/close/exit 等）
  └─ 注入到 EventTriggerFramework 的管线中
```

---

## 八、后续待办

| 优先级 | 任务 | 说明 |
|--------|------|------|
| 高 | FileDriven 引用 CommandFramework | 修改 `FileDriven.Pipeline.csproj` 添加项目引用 |
| 高 | FileDriven 业务命令注册 | 将 `Skeletons.NonEventCommandHandler` 改为实现 `INonEventCommandHandler` |
| 高 | PipelineEntry 注入修复 | `BuildCommandPipeline` 注入 `NonEventCommandHandler` |
| 中 | CreateContext 浅拷贝修复 | 直接引用 `_loadedConfigs` 而非浅拷贝 |
| 低 | 编译测试 | 确保所有项目编译通过，运行测试验证 |

---

## 九、附录

### 9.1 术语表

| 术语 | 说明 |
|------|------|
| 事件命令 | 需要操作 UI 控件的命令，如 `set`、`click` |
| 非事件命令 | 不需要操作 UI 控件的命令，如 `load`、`echo`、`sleep` |
| 命令解析 | 将命令字符串解析为结构化命令对象 |
| 命令规范化 | 将简写命令转换为完整格式，如 `set` → `do set` |
| 管线阶段 | 命令执行流程中的一个步骤 |

### 9.2 参考文件

| 文件 | 说明 |
|------|------|
| `Operation/CommandFramework/Abstractions/ICommandParser.cs` | 命令解析器接口 |
| `Operation/CommandFramework/Abstractions/INonEventCommandHandler.cs` | 非事件命令处理器接口（异步） |
| `Operation/CommandFramework/Abstractions/ICommandEngine.cs` | 命令引擎接口 |
| `Operation/CommandFramework/Abstractions/ICommand.cs` | 命令接口 |
| `Operation/CommandFramework/Models/ParsedCommand.cs` | 解析后的命令模型 |
| `Operation/CommandFramework/Models/NonEventCommandResult.cs` | 非事件命令执行结果 |
| `Operation/CommandFramework/Models/CommandExecutionContext.cs` | 命令执行上下文 |
| `Operation/CommandFramework/Parsing/CommandParser.cs` | 命令解析器实现 |
| `Operation/CommandFramework/Execution/CommandEngine.cs` | 命令引擎实现 |
| `Operation/CommandFramework/Execution/NonEventCommandHandler.cs` | 非事件命令处理器实现 |
| `Operation/CommandFramework/Execution/DelegateCommand.cs` | 委托命令实现 |
| `Operation/CommandFramework/Registration/CommandRegistry.cs` | 命令注册表 |
