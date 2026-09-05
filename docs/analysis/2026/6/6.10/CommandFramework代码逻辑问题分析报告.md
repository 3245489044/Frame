# CommandFramework 代码逻辑问题综合分析报告

> 分析范围：`Share/CommandFramework` 全部源文件（约 30 个文件）
> 分析日期：2026-06-10
> 分析类型：重复逻辑、冗余逻辑、错误逻辑

---

## 目录

1. [重复逻辑（Duplicate Logic）](#1-重复逻辑duplicate-logic)
2. [冗余逻辑（Redundant Logic）](#2-冗余逻辑redundant-logic)
3. [错误逻辑（Buggy Logic）](#3-错误逻辑buggy-logic)
4. [总结与建议](#4-总结与建议)

---

## 1. 重复逻辑（Duplicate Logic）

### 1.1 `ICommandExecutionResult` 与 `Result<T>` / `IFunctionExecutionResult` 的重复

**涉及文件：**
- `ICommand.cs` — `ICommandExecutionResult` 接口（第 103-128 行）
- `ICommand.cs` — `Result<T>` 类（第 193-244 行）
- `ICommand.cs` — `IFunctionExecutionResult` 接口（第 581-594 行）

**问题描述：**
`ICommandExecutionResult` 和 `IFunctionExecutionResult` 都包含 `IsSuccess`、`Value`、`ErrorMessage`、`ExecutionTime` 等核心字段，语义高度重叠。`Result<T>` 实现了 `IFunctionExecutionResult`，但 `ICommandExecutionResult` 是独立接口，导致 `CommandExecutionResult` 类内部包装了一个 `Result<object>` 来适配。

**影响：**
- 调用方需要处理两种不同的结果类型
- `CommandExecutionResult` 多了一层包装，增加了不必要的间接性

**建议：** 统一为 `Result<T>` 体系，让 `ICommandExecutionResult` 继承 `IFunctionExecutionResult` 或直接废弃。

---

### 1.2 `ICommandBatchResult` 与 `BatchResult<T>` 的重复

**涉及文件：**
- `ICommand.cs` — `ICommandBatchResult` 接口（第 133-152 行）
- `ICommand.cs` — `BatchResult<T>` 类（第 263-282 行）

**问题描述：**
`ICommandBatchResult` 和 `BatchResult<T>` 定义了几乎相同的属性（`TotalCount`、`SuccessCount`、`FailCount`、`IsAllSuccess`、`Error`、`Results`），但 `BatchResult<T>` 没有实现 `ICommandBatchResult` 接口。

**影响：**
- 接口与实现分离但未关联，失去接口抽象的意义
- 调用方无法通过接口统一引用批量结果

**建议：** 让 `BatchResult<T>` 实现 `ICommandBatchResult`，或删除未使用的接口。

---

### 1.3 `CommandRegistry` 与 `CommandEngine` 的命令注册功能重复

**涉及文件：**
- `CommandRegistry.cs` — 工厂模式命令注册表
- `CommandEngine.cs` — 命令引擎（内部有 `ConcurrentDictionary<string, ICommand>`）

**问题描述：**
`CommandRegistry` 使用工厂模式延迟创建命令实例，`CommandEngine` 内部直接存储 `ICommand` 实例。两者都提供 `Register`、`Resolve`/`Execute`、`GetRegisteredCommands` 等方法，功能高度重叠。

**影响：**
- 调用方需要同时管理两个注册表，容易混淆
- 命令可能在一个注册表中注册但在另一个中找不到

**建议：** 明确职责边界：`CommandRegistry` 作为工厂注册表用于 DI 场景，`CommandEngine` 作为运行时执行引擎。考虑让 `CommandEngine` 内部使用 `CommandRegistry` 作为命令来源。

---

### 1.4 `CommandEngineExtensions` 与 `CommandEngine.RegisterCommand` 的重复

**涉及文件：**
- `CommandEngineExtensions.cs` — `RegisterHandler` 扩展方法
- `CommandEngine.cs` — `RegisterCommand(string name, Func<..., Task> execute, ...)`

**问题描述：**
`RegisterHandler` 扩展方法本质上是对 `RegisterCommand` 的包装，将 `Func<string, Task<bool>>` 适配为 `Func<CommandExecutionContext, CancellationToken, Task>`。但 `RegisterHandlerWithDescription` 与 `RegisterHandler` 的代码几乎完全重复（第 90-104 行 vs 第 28-42 行）。

**影响：**
- `RegisterHandler` 和 `RegisterHandlerWithDescription` 的 lambda 体完全一致，仅多了一个 `description` 参数
- 违反 DRY 原则

**建议：** 让 `RegisterHandlerWithDescription` 调用 `RegisterHandler` 并额外传递 description。

---

### 1.5 `NonEventCommandHandler` 与 `CommandParser` 的非事件命令判断逻辑重复

**涉及文件：**
- `CommandParser.cs` — `IsNonEventCommand` 方法（第 186-199 行）
- `NonEventCommandHandler.cs` — `CanHandle` 方法（第 77-80 行）

**问题描述：**
`CommandParser` 通过 `_nonEventCommands` HashSet 判断是否为非事件命令，`NonEventCommandHandler` 通过 `_handlers` Dictionary 判断是否能处理。两者维护了独立的命令列表，但语义上应该保持一致。

**影响：**
- 在 `CommandParser` 中注册的非事件命令可能未在 `NonEventCommandHandler` 中注册处理器
- 反之亦然，导致解析结果与执行能力不一致

**建议：** 统一命令列表来源，让 `NonEventCommandHandler` 作为单一事实来源。

---

### 1.6 `PipelineEntry.CreateDefaultPipeline()` 与 `PipelineEntryInstance.RunCommandAsync()` 的管线创建逻辑重复

**涉及文件：**
- `PipelineEntry.cs` — `CreateDefaultPipeline()`（第 43-64 行）
- `PipelineEntry.cs` — `PipelineEntryInstance.RunCommandAsync()`（第 191-199 行）

**问题描述：**
`RunCommandAsync` 和 `RunCommandsAsync` 在未传入 pipeline 时都调用 `PipelineEntry.CreateDefaultPipeline()` 创建默认管线。`ExecuteCommandsAsync` 静态方法也重复了相同的循环逻辑。

**影响：**
- 三个方法各自独立实现了"创建管线→执行命令"的流程
- 代码重复，维护成本高

**建议：** 抽取公共的"执行命令集合"方法，消除循环重复。

---

### 1.7 `ExtractValuesStage` 与 `WriteResultStage` 的 GUI/CLI 模式判断重复

**涉及文件：**
- `ExtractValuesStage.cs` — `ExecuteCoreAsync` 中的 `ExecuteForMode` 调用
- `WriteResultStage.cs` — `ExecuteCoreAsync` 中的 `ExecuteForMode` 调用

**问题描述：**
两个阶段都使用 `CommandStageBase.ExecuteForMode` 进行 GUI/CLI 模式判断，但 `ExtractValuesStage` 额外处理了 `set` 命令的特定逻辑（第 52-63 行），而 `WriteResultStage` 没有。这种"部分重复"模式容易导致后续修改时遗漏同步。

**影响：**
- 如果 `ExecuteForMode` 的行为发生变化，需要同时修改两个阶段
- `set` 命令的特殊处理在两个阶段中不一致

**建议：** 考虑将模式判断逻辑统一到 `CommandContext` 中，或使用策略模式。

---

### 1.8 `Result<T>.ToJson()` 与 `CommandExecutionResult.ToJson()` 的 JSON 序列化重复

**涉及文件：**
- `ICommand.cs` — `Result<T>.ToJson()`（第 239-240 行）
- `CommandExecutionResult.cs` — `ToJson()`（第 87-93 行）

**问题描述：**
两者都实现了 JSON 序列化，但 `Result<T>` 使用默认 `JsonSerializer.Serialize(this)`，而 `CommandExecutionResult` 使用 `WriteIndented = true` 选项。序列化行为不一致。

**影响：**
- 相同语义的结果可能输出不同格式的 JSON
- 调用方需要知道具体类型才能正确反序列化

**建议：** 统一序列化方式，或让 `CommandExecutionResult.ToJson()` 委托给内部的 `_result.ToJson()`。

---

## 2. 冗余逻辑（Redundant Logic）

### 2.1 `CommandEngine.ExecuteCommandAsync` 与 `UndoCommandAsync` 辅助方法中的类型检查

**涉及文件：**
- `CommandEngine.cs` — `ExecuteCommandAsync`（第 164-175 行）
- `CommandEngine.cs` — `UndoCommandAsync`（第 180-191 行）

**问题描述：**
这两个辅助方法检查 `command is DelegateCommand` 并调用带上下文的版本，否则调用无上下文的版本。这种类型检查是 `DelegateCommand` 设计缺陷的补偿——`ICommand` 接口的 `ExecuteAsync(CancellationToken)` 没有上下文参数，而 `DelegateCommand` 需要上下文。

**影响：**
- 每次执行/撤销都需要运行时类型检查
- 新增命令类型时容易忘记更新这里的类型检查

**建议：** 在 `ICommand` 接口中增加可选的上下文支持，或让 `DelegateCommand` 通过其他方式获取上下文（如 `AsyncLocal`）。

---

### 2.2 `CommandEngine` 的 `_lock` 对象与 `LinkedList` 的手动线程安全

**涉及文件：**
- `CommandEngine.cs` — 使用 `lock (_lock)` 保护 `_undoList` 和 `_redoList`

**问题描述：**
使用 `LinkedList<CommandHistoryEntry>` 配合手动 `lock` 实现撤销/重做栈。`ConcurrentDictionary` 用于命令存储，但历史记录使用手动锁，线程安全策略不一致。

**影响：**
- 混合使用 `ConcurrentDictionary` 和手动 `lock`，代码风格不统一
- 锁粒度较粗，可能影响性能

**建议：** 统一使用 `ConcurrentDictionary` 或 `ConcurrentStack` 等无锁数据结构，或提取为线程安全的 `HistoryManager` 类。

---

### 2.3 `DelegateCommand` 的双重构造函数设计

**涉及文件：**
- `DelegateCommand.cs` — 两个构造函数（第 39-51 行、第 57-78 行）

**问题描述：**
`DelegateCommand` 有两个构造函数：一个接收 `Func<CancellationToken, Task>`（无上下文），另一个接收 `Func<CommandExecutionContext, CancellationToken, Task>`（带上下文）。带上下文的构造函数在内部创建了 `_execute` 委托的闭包包装（第 72-77 行），导致 `_execute` 和 `_executeWithContext` 同时存在。

**影响：**
- 内部状态复杂：4 个委托字段（`_execute`、`_undo`、`_executeWithContext`、`_undoWithContext`）
- `ExecuteAsync` 方法需要判断使用哪个委托（第 89-96 行）
- 代码注释提到"消除递归闭包风险"，说明之前存在 bug

**建议：** 统一为带上下文的版本，无上下文版本作为便捷包装。

---

### 2.4 `CommandContext` 中的 `Mode`、`IsGuiMode`、`IsCliMode` 冗余

**涉及文件：**
- `CommandContext.cs` — `Mode` 属性（第 100 行）
- `CommandContext.cs` — `IsGuiMode` 属性（第 106 行）
- `CommandContext.cs` — `IsCliMode` 属性（第 112 行）

**问题描述：**
`Mode` 是字符串属性，`IsGuiMode` 和 `IsCliMode` 是派生属性。但 `IPipelineContext` 接口中已经定义了 `Mode` 属性（`ICommand.cs` 第 365 行），`CommandContext` 通过 `new` 关键字重新定义了 `Mode`。

**影响：**
- `Mode` 属性在 `IPipelineContext` 和 `CommandContext` 中重复定义
- 使用 `new` 关键字隐藏基类成员，可能导致多态行为不一致

**建议：** 删除 `CommandContext` 中的 `Mode` 重定义，直接使用继承的 `Mode` 属性。

---

### 2.5 `PipelineStageBase<TContext>` 的异常处理与 `CommandStageBase` 的重复

**涉及文件：**
- `PipelineStageBase.cs` — `ExecuteAsync` 的 try-catch（第 56-72 行）
- 各 Stage 的 `ExecuteCoreAsync` 中的 try-catch

**问题描述：**
`PipelineStageBase.ExecuteAsync` 已经捕获了异常并调用 `SetStageResult(Name, false, ...)`，但多个 Stage 的 `ExecuteCoreAsync` 内部又包裹了额外的 try-catch（如 `ExtractValuesStage` 第 107-110 行、`ExecuteFunctionStage` 第 118-122 行、`FindBindingStage` 第 103-106 行）。

**影响：**
- 异常被多次捕获和处理
- 外层 catch 可能覆盖内层 catch 的详细错误信息

**建议：** 依赖基类的异常处理，子类只在需要添加额外上下文信息时才捕获异常。

---

### 2.6 `LoggingStage<TContext>` 的空执行体

**涉及文件：**
- `LoggingStage.cs` — `ExecuteCoreAsync`（第 41-59 行）

**问题描述：**
`LoggingStage` 的 `ExecuteCoreAsync` 方法中，try 块内只有 `await Task.CompletedTask`，没有实际的业务逻辑。日志记录只在方法开始和结束时打印，但 `PipelineStageBase.ExecuteAsync` 已经提供了执行前后的框架。

**影响：**
- 该阶段除了打印日志外不做任何事
- 日志内容有限（只有阶段名称和耗时），价值不高

**建议：** 考虑使用 `MiddlewarePipeline` 的洋葱模型实现日志横切关注点，或直接删除此阶段。

---

### 2.7 `JsonCommandResult` 与 `Result<T>` 的继承冗余

**涉及文件：**
- `JsonCommandResult.cs` — 继承 `Result<object>`（第 75 行）

**问题描述：**
`JsonCommandResult` 继承 `Result<object>` 但添加了 `JsonErrorCode`、`CommandId` 等字段。`FromResult<T>` 方法（第 127 行）将 `Result<T>` 转换为 `JsonCommandResult`，但 `FromExecutionResult` 方法（第 150 行）又将 `ICommandExecutionResult` 转换为 `JsonCommandResult`。

**影响：**
- 存在三种结果类型之间的转换链：`Result<T>` ↔ `ICommandExecutionResult` ↔ `JsonCommandResult`
- 转换过程中可能丢失信息（如 `FromExecutionResult` 中错误码被简化为 `UnknownError`）

**建议：** 减少结果类型数量，统一使用 `Result<T>` 体系。

---

### 2.8 `CommandParser.SplitCommand` 不支持转义引号

**涉及文件：**
- `CommandParser.cs` — `SplitCommand` 方法（第 220-252 行）

**问题描述：**
`SplitCommand` 方法处理引号时，遇到 `"` 就切换 `inQuote` 状态，但不支持转义引号（如 `\"`）。这意味着无法在命令参数中包含字面引号。

**影响：**
- 功能受限，无法处理包含引号的参数值
- 与标准命令行解析行为不一致

**建议：** 增加对 `\"` 转义序列的支持。

---

## 3. 错误逻辑（Buggy Logic）

### 3.1 `CommandEngine.UndoAsync` 将不支持撤销的命令放回 `_undoList` 末尾

**涉及文件：**
- `CommandEngine.cs` — `UndoAsync` 方法（第 93-126 行）

**问题描述：**
第 108 行：当命令不支持撤销时，将其放回 `_undoList` 末尾。这会导致：
1. 如果列表顶部连续多个命令都不支持撤销，`UndoAsync` 会无限循环（每次取出、检查、放回）
2. 实际上 `_undoList` 是 `LinkedList`，`AddLast` 将条目添加到末尾，但 `RemoveLast` 从末尾移除，所以"放回末尾"实际上没有改变列表状态

**严重性：** ⭐⭐⭐ 高

**复现步骤：**
1. 注册一个 `CanUndo = false` 的命令并执行
2. 调用 `UndoAsync`
3. 命令被取出，检查 `CanUndo` 为 false，放回末尾
4. 下次 `UndoAsync` 再次取出同一个命令，无限循环

**建议：** 不支持撤销的命令不应加入 `_undoList`，或在 `ExecuteAsync` 时检查 `CanUndo` 决定是否加入历史。

---

### 3.2 `CommandEngine.UndoAsync` 的"放回末尾"逻辑错误

**涉及文件：**
- `CommandEngine.cs` — `UndoAsync` 第 108 行

**问题描述：**
```csharp
lock (_lock) { _undoList.AddLast(entry); }
```
`_undoList` 是 `LinkedList<CommandHistoryEntry>`，`RemoveLast` 从末尾移除，`AddLast` 添加到末尾。所以"放回末尾"实际上是将条目放回了原来的位置，下次 `UndoAsync` 会再次取出同一个条目，形成死循环。

**严重性：** ⭐⭐⭐ 高

**建议：** 改为 `AddFirst` 放回开头，或更合理的方式是在 `ExecuteAsync` 时就不将不支持撤销的命令加入 `_undoList`。

---

### 3.3 `CommandEngine.RedoAsync` 不检查 `CanUndo`

**涉及文件：**
- `CommandEngine.cs` — `RedoAsync` 方法（第 129-159 行）

**问题描述：**
`RedoAsync` 直接执行命令，不检查 `command.CanUndo`。而 `UndoAsync` 会检查 `CanUndo`。这意味着：
1. 一个 `CanUndo = false` 的命令被撤销（实际上不会发生，因为 UndoAsync 会拒绝）
2. 但如果通过其他方式进入了 `_redoList`，重做时会直接执行

**影响：**
- 行为不一致：撤销检查 `CanUndo`，重做不检查
- 潜在的安全问题

**建议：** `RedoAsync` 也应检查 `CanUndo`，或统一策略。

---

### 3.4 `DelegateCommand` 带上下文构造函数的闭包捕获问题

**涉及文件：**
- `DelegateCommand.cs` — 第 72-77 行

**问题描述：**
```csharp
var capturedExecute = _executeWithContext;
_execute = ct => capturedExecute(new CommandExecutionContext { Name = name }, ct);
var capturedUndo = _undoWithContext;
_undo = capturedUndo != null
    ? ct => capturedUndo(new CommandExecutionContext { Name = name }, ct)
    : null;
```
这里创建了 `_execute` 和 `_undo` 委托，它们捕获了 `capturedExecute` 和 `capturedUndo`。但 `_executeWithContext` 和 `_undoWithContext` 字段本身也被保留。当 `ExecuteAsync` 被调用时（第 84-97 行），优先使用 `_executeWithContext`，这意味着 `_execute` 闭包几乎不会被使用。

**影响：**
- 内存浪费：每个 `DelegateCommand` 实例持有 4 个委托引用
- 逻辑混淆：`_execute` 闭包仅作为 `ICommand.ExecuteAsync` 接口实现的回退

**建议：** 删除 `_execute` 和 `_undo` 字段，统一通过 `_executeWithContext` / `_undoWithContext` 实现所有执行路径。

---

### 3.5 `CommandContext.Mode` 属性隐藏（`new`）基类成员

**涉及文件：**
- `CommandContext.cs` — 第 100 行
- `ICommand.cs` — `IPipelineContext.Mode`（第 365 行）

**问题描述：**
`IPipelineContext` 接口定义了 `string Mode { get; set; }`，`BP.PipelineContext` 实现了该属性。但 `CommandContext` 使用 `new` 关键字重新定义了 `Mode` 属性：

```csharp
public string Mode { get; set; } = "Unknown";
```

这意味着通过 `IPipelineContext` 接口访问 `Mode` 和通过 `CommandContext` 类访问 `Mode` 可能得到不同的值。

**严重性：** ⭐⭐ 中

**建议：** 删除 `new` 关键字，让 `CommandContext` 继承基类的 `Mode` 属性。

---

### 3.6 `CommandContext.TryGetError` 的 `forEachSuccess` 参数设计缺陷

**涉及文件：**
- `CommandContext.cs` — `TryGetError` 方法（第 135-160 行）

**问题描述：**
`TryGetError` 方法接收 `forEachSuccess` 参数，但该参数的含义和调用方式不清晰：
1. 调用方需要传入一个布尔值表示"ForEachPipeline 的异常标记"
2. 方法内部检查 `!forEachSuccess` 时返回错误，但错误信息是固定的"ForEach pipeline encountered an exception"
3. 该方法名称暗示"尝试获取错误"，但实际上同时做了错误判断和信息获取两件事

**影响：**
- API 设计不清晰，调用方容易误用
- 固定的错误信息缺乏上下文

**建议：** 拆分为 `HasError` 属性和 `GetErrorMessage()` 方法，或删除 `forEachSuccess` 参数。

---

### 3.7 `ExecuteFunctionStage.SetRealControlValue` 的反射事件触发逻辑脆弱

**涉及文件：**
- `ExecuteFunctionStage.cs` — `SetRealControlValue` 方法（第 131-180 行）

**问题描述：**
该方法通过多层反射尝试触发 WinForms 控件的 `TextChanged` 事件：
1. 通过 `GetField("EventTextChanged")` 获取事件委托（第 150 行）
2. 备用方案通过 `Events` 属性和 `EventTextChanged` 静态字段获取（第 155-172 行）
3. 最后尝试调用 `PerformTextChanged` 方法（第 176-178 行）

**问题：**
- 反射路径依赖 WinForms 内部实现细节（`EventTextChanged` 字段名），不同 .NET 版本可能不同
- 多层反射嵌套，代码可读性差，调试困难
- 如果所有反射路径都失败，事件不会被触发，但方法静默返回

**严重性：** ⭐⭐ 中

**建议：** 考虑使用 `System.Windows.Forms` 的公开 API（如 `TextBox` 的 `Text` 属性设置后调用 `OnTextChanged`），或通过 `Control.SetText` 扩展方法。

---

### 3.8 `CommandParser.Parse` 中 `IsNonEventCommand` 的冗余判断

**涉及文件：**
- `CommandParser.cs` — `Parse` 方法第 131 行

**问题描述：**
```csharp
var isNonEvent = IsNonEventCommand(verb, action);
```
在 `Parse` 方法中，`action` 可能为 null（当 `parts.Count < 2` 时）。`IsNonEventCommand` 方法内部对 `action` 为 null 的情况有处理（第 194 行检查 `!string.IsNullOrEmpty(action)`），但 `verb` 也可能为 null（虽然 `parts[0]` 通常不为空）。

**影响：**
- 潜在的空引用风险
- 逻辑上，当 `parts.Count < 2` 时，`verb` 可能是一个非事件命令（如 `load`），但 `action` 为 null 导致 `IsNonEventCommand` 返回 false

**建议：** 当 `parts.Count == 1` 时，应检查 `verb` 本身是否为非事件命令。

---

### 3.9 `NonEventCommandHandler` 的 `echo` 命令参数处理逻辑错误

**涉及文件：**
- `NonEventCommandHandler.cs` — `echo` 处理器（第 90-106 行）

**问题描述：**
```csharp
var extraParts = cmd.Parameters
    .Where(kvp => kvp.Value is bool b && b)
    .Select(kvp => kvp.Key);
```
这里假设所有无值参数（如 `modifier=show` 中的 `modifier`）的值都是 `bool` 类型 `true`。但 `CommandParser.ParseParameters` 中（第 274 行）将无值参数的值设置为 `true`（`bool` 类型），而 `echo` 处理器通过 `is bool b && b` 过滤。

**问题：**
- 如果参数值不是 `bool` 类型（如 `value=123` 中的 `123` 是字符串），会被忽略
- 逻辑上，`echo` 应该拼接所有额外参数，而不只是 `bool` 类型的

**建议：** 改为拼接所有参数值，或明确 `echo` 命令的参数格式。

---

### 3.10 `InMemoryStateStore.RemoveByPrefix` 的迭代中修改集合

**涉及文件：**
- `InMemoryStateStore.cs` — `RemoveByPrefix` 方法（第 86-96 行）

**问题描述：**
```csharp
foreach (var key in GetAllKeys())
{
    if (key.StartsWith(prefix, ...))
    {
        Remove(key);
    }
}
```
`GetAllKeys()` 返回 `_store.Keys.ToArray()` 的副本，所以不会引发"集合已修改"异常。但 `Remove` 方法内部调用 `TryRemove`，如果其他线程同时修改了集合，可能导致某些键未被删除。

**影响：**
- 线程安全性不足：`GetAllKeys()` 和 `Remove` 之间没有锁保护
- 并发场景下可能漏删

**建议：** 使用 `lock` 或使用 `ConcurrentDictionary` 的原子操作方法。

---

### 3.11 `StateStoreExtensions.GetOrSet<T>` 的默认值序列化问题

**涉及文件：**
- `StateStoreExtensions.cs` — `GetOrSet<T>` 方法（第 88-110 行）

**问题描述：**
```csharp
var stringValue = defaultValue?.ToString();
var result = store.GetOrAdd(key, stringValue);
```
对于复杂类型，`defaultValue.ToString()` 返回的是类型名称而非 JSON 序列化结果。而 `Get<T>` 和 `Set<T>` 方法使用 JSON 序列化。

**影响：**
- `GetOrSet` 存储的值格式与 `Get`/`Set` 不一致
- 使用 `GetOrSet` 存储复杂类型后，`Get<T>` 反序列化会失败

**建议：** `GetOrSet` 应使用 JSON 序列化，与 `Set<T>` 保持一致。

---

### 3.12 `PipelineEntry.CreateCliPipeline` 只包含 `ParseCommandStage`

**涉及文件：**
- `PipelineEntry.cs` — `CreateCliPipeline` 方法（第 73-85 行）

**问题描述：**
`CreateCliPipeline` 创建的管线只包含 `ParseCommandStage`，没有 `FindBindingStage`、`ExtractValuesStage`、`ExecuteFunctionStage`、`WriteResultStage`。这意味着 CLI 模式下事件命令（如 `do click addButton`）只被解析，不会执行绑定查找和函数执行。

**影响：**
- CLI 模式下事件命令无法完整执行
- `CommandEngine.ExecuteFileAsync` 之前使用 `CreateCliPipeline` 导致事件命令不被执行（注释中已提到此问题，第 213 行）

**建议：** 如果 CLI 模式不需要完整管线，应明确文档说明；否则应包含完整阶段。

---

### 3.13 `CommandEngine.ExecuteFileAsync` 中 `NonEventCommandHandler` 的注入方式

**涉及文件：**
- `CommandEngine.cs` — `ExecuteFileAsync` 第 234 行

**问题描述：**
```csharp
context.SharedData["NonEventCommandHandler"] = nonEventHandler;
```
通过 `SharedData` 注入 `NonEventCommandHandler`，但 `ParseCommandStage` 的构造函数也可以注入。这种"共享数据注入"方式：
1. 类型不安全：`SharedData` 是 `IDictionary<string, object?>`，需要运行时类型检查
2. 隐式依赖：调用方需要知道 `ParseCommandStage` 从 `SharedData` 中查找 `NonEventCommandHandler`

**影响：**
- 依赖关系不明确，容易遗漏
- 运行时错误代替编译时错误

**建议：** 统一使用构造函数注入，或提供显式的配置方法。

---

### 3.14 `CompositeCommand.CanUndo` 的语义问题

**涉及文件：**
- `DelegateCommand.cs` — `CompositeCommand.CanUndo`（第 157 行）

**问题描述：**
```csharp
public bool CanUndo => _commands.Any(c => c.CanUndo);
```
只要有一个子命令支持撤销，`CompositeCommand` 就报告支持撤销。但 `UndoAsync` 方法（第 192-200 行）逆序遍历所有子命令，对每个检查 `CanUndo`，不支持的跳过。

**问题：**
- 如果 `CompositeCommand` 报告 `CanUndo = true`，但实际执行时部分子命令不支持撤销，可能导致部分撤销（部分子命令被撤销，部分未撤销）
- 调用方期望"全部撤销或全部不撤销"的原子语义

**建议：** 要么要求所有子命令都支持撤销才报告 `CanUndo = true`，要么在文档中明确说明"尽力撤销"语义。

---

### 3.15 `FunctionRegistry.CreateDescriptorFromDelegate` 的异步 Lambda 参数检测

**涉及文件：**
- `FunctionRegistry.cs` — `CreateDescriptorFromDelegate` 方法（第 98-134 行）

**问题描述：**
该方法尝试检测异步 Lambda 的真实参数：如果 `function.Method` 返回空参数列表，则通过委托类型的 `Invoke` 方法获取参数。但：
1. 并非所有异步 Lambda 都会导致 `function.Method` 返回空参数
2. 对于某些编译器生成的闭包，`Invoke` 方法也可能返回不准确的参数信息

**影响：**
- 参数检测结果可能不准确
- 依赖编译器实现细节，不同 .NET 版本行为可能不同

**建议：** 考虑使用 `System.Linq.Expressions` 或 `Func`/`Action` 泛型类型来获取准确的参数信息。

---

## 4. 总结与建议

### 4.1 问题统计

| 类别 | 数量 | 严重程度分布 |
|------|------|-------------|
| 重复逻辑 | 8 个 | 中 |
| 冗余逻辑 | 8 个 | 低-中 |
| 错误逻辑 | 15 个 | 高(3个)、中(12个) |

### 4.2 最严重的问题（需优先修复）

1. **`CommandEngine.UndoAsync` 死循环风险**（3.1/3.2）— 不支持撤销的命令被放回 `_undoList` 末尾，导致无限循环
2. **`CommandContext.Mode` 属性隐藏基类成员**（3.5）— 通过接口和类访问得到不同值
3. **`CommandEngine.RedoAsync` 不检查 `CanUndo`**（3.3）— 撤销/重做行为不一致
4. **`StateStoreExtensions.GetOrSet<T>` 序列化不一致**（3.11）— 与 `Set<T>` 使用不同的序列化方式

### 4.3 架构建议

1. **统一结果类型体系**：减少 `ICommandExecutionResult`、`IFunctionExecutionResult`、`Result<T>`、`JsonCommandResult` 之间的转换链
2. **明确 `CommandRegistry` 与 `CommandEngine` 的职责边界**：避免两个注册表并存
3. **简化 `DelegateCommand` 设计**：消除 4 个委托字段的复杂性
4. **统一依赖注入方式**：统一使用构造函数注入，减少 `SharedData` 的隐式依赖
5. **消除管线阶段的重复模式判断**：将 GUI/CLI 模式判断逻辑集中到 `CommandContext`
