# CommandFramework 代码分析报告

> 分析日期：2026-07-23
> 分析范围：`CsharpPrj/Share/CommandFramework` 全部核心文件
> 分析目标：重复逻辑、冗余逻辑、错误逻辑
>
> ## 修复记录
>
> | 日期 | 修复项 | 操作 | 状态 |
> |------|--------|------|------|
> | 2026-07-23 | 3.1 CommandContextExtensions 属性存储分离 | 扩展方法优先操作 CommandContext 直接属性，Items 字典仅用于无直接属性的字段 | ✅ 已修复 |
> | 2026-07-23 | 2.4 CommandContextExtensions 属性重复 | 同步修复，与 3.1 一同完成 | ✅ 已修复 |
> | 2026-07-23 | ExtractedValues 编译错误 | 移除已删除的 ExtractedValues 相关扩展方法 | ✅ 已修复 |
> | 2026-07-23 | 1.1 INonEventCommandLookup 歧义消除 | GlobalUsings.cs 添加别名指向 Container.Abstractions.INonEventCommandLookup | ✅ 已修复 |
> | 2026-07-23 | PipelineEntry.cs 参数名不匹配 | cmdFrameworkLookup → nonEventCommandLookup，移除已删除的 useCmdFrameworkLookup 参数 | ✅ 已修复 |
> | 2026-07-23 | NonEventCommandLookupAdapter 缺少 RegisteredCommandNames | 添加委托属性实现 | ✅ 已修复 |
> | 2026-07-23 | FrontEndDriven ExecuteCommandStage 类型不匹配 | 直接赋值 Container.Abstractions.INonEventCommandLookup，移除不必要的适配器包装 | ✅ 已修复 |



---

## 目录

1. [重复逻辑（Duplicate Logic）](#1-重复逻辑duplicate-logic)
2. [冗余逻辑（Redundant Logic）](#2-冗余逻辑redundant-logic)
3. [错误逻辑（Error Logic / Bugs）](#3-错误逻辑error-logic--bugs)
4. [总结与优先级建议](#4-总结与优先级建议)

---

## 1. 重复逻辑（Duplicate Logic）

### 1.1 🔴 INonEventCommandLookup 双重定义

| 属性 | 值 |
|------|-----|
| **位置** | `CommandFramework.Abstractions.INonEventCommandLookup` vs `Container.Abstractions.INonEventCommandLookup` |
| **文件** | `Execution/Abstractions/CommandPipeline/INonEventCommandLookup.cs` |
| **严重性** | 🔴 高 |

**问题描述**：
两个接口语义相同（都是非事件命令查找），但签名不同：
- CommandFramework 版本：`TryGetHandler(string, out Action<TConfig.IExecutionContext>?)`
- Container 版本：`TryGetHandler(string, out Action<CommandExecutionContext>?)`

**后果**：
需要 `NonEventCommandLookupAdapter` 和 `ReverseNonEventCommandLookupAdapter` 两个适配器来桥接，增加了不必要的复杂性。每新增一个方法，两个接口和两个适配器都需要同步修改。

**建议**：
统一为一个接口，消除适配器层。

---

### 1.2 🔴 IControlValueWriter 与 TConfig.Controls.IControlValueAccessor

| 属性 | 值 |
|------|-----|
| **位置** | `IControlValueWriter`（已标记 `[Obsolete]`）vs `TConfig.Controls.IControlValueAccessor` |
| **文件** | `Execution/Abstractions/CommandPipeline/IControlValueWriter.cs` |
| **严重性** | 🔴 高 |

**问题描述**：
`IControlValueWriter` 已标记 `[Obsolete("Use TConfig.Controls.IControlValueAccessor instead")]` 但未删除。`ControlValueAccessorBase` 实现了新接口，但旧接口仍保留在 Abstractions 中。

**后果**：
新旧两套控件值读写体系并存，调用方可能混用。废弃代码增加维护成本。

**建议**：
确认无引用后删除 `IControlValueWriter`。

---

### 1.3 🔴 IControlValueAccessor 双重定义

| 属性 | 值 |
|------|-----|
| **位置** | `CommandFramework.Abstractions.IControlValueAccessor`（标记 `[Obsolete]`）vs `TConfig.Controls.IControlValueAccessor` |
| **文件** | `Execution/Shared/Abstractions/ICommand.cs`（第 391-414 行） |
| **严重性** | 🔴 高 |

**问题描述**：
两个接口功能完全相同：
- `GetValue(object)` / `SetValue(object, object?)`
- `GetValue<TResult>(object)`
- `IsSupported(object)`
- `RegisterAdapter<TControl>(...)` / `RegisterAdapterWithResult<TControl>(...)`
- `ClearAdapters()`

**后果**：
`ControlValueAccessorBase` 实现 `TConfig.Controls.IControlValueAccessor`，但旧接口仍保留在 Abstractions 中。调用方可能引用错误的接口。

**建议**：
确认无引用后删除 `CommandFramework.Abstractions.IControlValueAccessor`。

---

### 1.4 🟡 结果类型体系重复

| 属性 | 值 |
|------|-----|
| **位置** | `CommandResults.cs` |
| **文件** | `Execution/CommandPipeline/Models/CommandResults.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
`CommandResults.cs` 中定义了 4 种结果类型：

| 类型 | 用途 | 工厂方法 |
|------|------|----------|
| `ExecutionResult` | TConfig.ExecutionResult 的轻量包装 | Success/Failure/Cancelled/Timeout |
| `ExecutionResult<T>` | 强类型版本 | Success/Failure/Cancelled |
| `CommandExecutionResult<T>` | 命令层结果（含 Command/ErrorCode/ToJson） | Success/Failure |
| `JsonCommandResult` | JSON 序列化静态方法 | Serialize/SerializeBatch/Deserialize |

**后果**：
- `ExecutionResult` 和 `CommandExecutionResult<T>` 功能高度重叠，都有 `IsSuccess`/`ErrorMessage`/`ExecutionTime`
- 调用方需要知道使用哪个结果类型，增加了认知负担
- 两者之间需要隐式转换，增加了复杂性

**建议**：
考虑合并 `ExecutionResult` 和 `CommandExecutionResult<T>`，或明确分层职责。

---

### 1.5 🟡 NonEventCommands 静态类中的命令关键性映射

| 属性 | 值 |
|------|-----|
| **位置** | `NonEventCommands.cs` |
| **文件** | `Execution/CommandPipeline/Libraries/NonEventCommands.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
`_commandCriticalityMap` 是单一真相来源，但通过 4 个 `Lazy<IReadOnlyCollection<string>>` 生成了 4 个派生集合：

```csharp
private static readonly Lazy<IReadOnlyCollection<string>> _criticalCommands = new(() =>
    _commandCriticalityMap.Where(kvp => kvp.Value == CommandCriticality.Critical).Select(kvp => kvp.Key).ToList().AsReadOnly());

private static readonly Lazy<IReadOnlyCollection<string>> _importantCommands = new(() =>
    _commandCriticalityMap.Where(kvp => kvp.Value == CommandCriticality.Important).Select(kvp => kvp.Key).ToList().AsReadOnly());

private static readonly Lazy<IReadOnlyCollection<string>> _optionalCommands = new(() =>
    _commandCriticalityMap.Where(kvp => kvp.Value == CommandCriticality.Optional).Select(kvp => kvp.Key).ToList().AsReadOnly());
```

**后果**：
4 个 Lazy 字段 + 4 个 Where 查询 + 4 个 ToList 的重复模式，可以用更简洁的方式实现（如一次性分组）。

**建议**：
使用 `ILookup<CommandCriticality, string>` 或一次性分组减少重复。

---

### 1.6 🟡 CommandContext 中 HasError 的 new 关键字

| 属性 | 值 |
|------|-----|
| **位置** | `CommandContext.HasError` |
| **文件** | `Models/CommandContext.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
`CommandContext.HasError` 使用 `new` 关键字隐藏基类 `PipelineContext.HasError`。

```csharp
// PipelineContext 中
public bool HasError => StageErrors.Values.Any(e => e != null);

// CommandContext 中
public new bool HasError => ...;
```

**后果**：
- 通过 `IPipelineContext` 接口访问和通过 `CommandContext` 类直接访问的行为可能不一致
- `new` 关键字是设计警告信号，说明继承体系设计有问题

**建议**：
使用 `override` 替代 `new`，或重构继承体系。

---

## 2. 冗余逻辑（Redundant Logic）

### 2.1 🔴 适配器膨胀

| 属性 | 值 |
|------|-----|
| **位置** | 多个适配器文件 |
| **严重性** | 🔴 高 |

**问题描述**：
存在 5 个适配器用于桥接类型差异：

| 适配器 | 来源 → 目标 | 原因 |
|--------|-------------|------|
| `NonEventCommandLookupAdapter` | CommandFramework → Container | INonEventCommandLookup 双重定义 |
| `ReverseNonEventCommandLookupAdapter` | Container → CommandFramework | INonEventCommandLookup 双重定义 |
| `CommandContextAdapter` | CommandExecutionContext → IExecutionContext | 上下文类型不统一 |
| `EventBindingInfoWrapper` | ResolvedEventBinding → IEventBindingInfo | 事件绑定类型不统一 |
| `NonNullDictionaryAdapter` | IDict<string, object?> → IDict<string, object> | 可空值类型差异 |

**后果**：
- 5 个适配器说明接口设计未统一
- 每新增一个适配器就增加一份维护成本
- 适配器本身可能引入 Bug（如 `CommandContextAdapter.WorkflowId` 返回空字符串）

**建议**：
从根源上统一接口设计，消除适配器需求。

---

### 2.2 🟡 ExecuteCommandStage 构造函数重载

| 属性 | 值 |
|------|-----|
| **位置** | `ExecuteCommandStage.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
两个构造函数，第二个通过 `useCmdFrameworkLookup` 哨兵参数区分：

```csharp
public ExecuteCommandStage(IContainerExecutionCell executionCell, ...)
public ExecuteCommandStage(INonEventCommandLookup cmdFrameworkLookup, ..., bool useCmdFrameworkLookup = true)
```

**后果**：
- `useCmdFrameworkLookup` 哨兵参数是设计不良的标志
- 调用方需要知道使用哪个构造函数

**建议**：
使用统一的接口抽象，或使用工厂方法模式。

---

### 2.3 🟡 PipelineEntryOptions 的 Fluent API

| 属性 | 值 |
|------|-----|
| **位置** | `PipelineEntryOptions.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
每个属性都有对应的 `WithXxx` 方法，但属性本身已是 public set：

```csharp
public string? ScriptPath { get; set; }
public PipelineEntryOptions WithScriptPath(string scriptPath)
{
    ScriptPath = scriptPath;
    return this;
}
```

**后果**：
Fluent API 与属性设置重复，增加维护量。

**建议**：
选择一种风格（推荐 Fluent API + 私有 setter），保持一致。

---

### 2.4 🟡 CommandContextExtensions 与 CommandContext 的属性重复

| 属性 | 值 |
|------|-----|
| **位置** | `CommandContextExtensions.cs` vs `CommandContext.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
CommandContext 有直接属性，而 CommandContextExtensions 通过 Items 字典存储同名数据：

| 属性 | CommandContext 直接属性 | CommandContextExtensions Items 键 |
|------|----------------------|----------------------------------|
| FunctionResultValue | `public object? FunctionResultValue { get; set; }` | `__CommandCtx_FunctionResultValue` |
| BoundFunctionName | `public string? BoundFunctionName { get; set; }` | `__CommandCtx_BoundFunctionName` |
| ExtractedValues | `public IReadOnlyDictionary<string, object?>? ExtractedValues { get; set; }` | `__CommandCtx_ExtractedValues` |

**后果**：
两套存储机制并存，可能导致状态不一致：
- `context.FunctionResultValue = x` 设置的是类属性
- `context.SetFunctionResultValue(x)` 设置的是 Items 字典
- 两者互不干扰，但调用方可能误以为它们是等价的，导致数据丢失

**建议**：
统一为一种存储方式（推荐直接属性），删除扩展方法中的重复存储。

---

### 2.5 🟡 管线阶段中的横切关注点分散

| 属性 | 值 |
|------|-----|
| **位置** | `LoggingStage.cs`、`RetryStage.cs`、`ValidationStage.cs` vs `ExecuteCommandStage.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
日志、重试、验证作为独立阶段注册，但 `ExecuteCommandStage` 内部也有 try-catch 和日志。

**后果**：
- 横切关注点分散在阶段内部和独立阶段中
- 日志可能重复输出
- 错误处理策略不统一

**建议**：
明确横切关注点的归属：要么全部由独立阶段处理，要么全部由内部处理。

---

### 2.6 🟡 CommandContext 中 TConfig.IExecutionContext 的显式接口实现

| 属性 | 值 |
|------|-----|
| **位置** | `CommandContext.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
`GetParameter`、`TryGetParameter`、`GetData`、`TryGetData` 等方法大量重复 `TryGetValue + Convert.ChangeType` 模式：

```csharp
object? IFunctionExecutionContext.GetParameter(string name)
{
    if (Items.TryGetValue(name, out var value) && value != null)
    {
        if (value is object obj) return obj;
        try { return Convert.ChangeType(value, typeof(object)); }
        catch { return null; }
    }
    return null;
}
```

**后果**：
代码冗余，每个方法都重复相同的模式。

**建议**：
提取为私有辅助方法，如 `TryGetItem<T>(string key, out T? value)`。

---

## 3. 错误逻辑（Error Logic / Bugs）

### 3.1 🔴 CommandContextExtensions 与 CommandContext 属性存储分离（数据丢失风险）

| 属性 | 值 |
|------|-----|
| **位置** | `CommandContextExtensions.cs` 第 22-196 行 |
| **严重性** | 🔴 高 |

**问题描述**：
扩展方法通过 `ctx.Items` 字典存储属性，但 `CommandContext` 类本身也有同名属性。两者操作的是不同的存储位置。

**具体影响**：
- `context.FunctionResultValue = x` → 存储在类字段
- `context.SetFunctionResultValue(x)` → 存储在 `ctx.Items["__CommandCtx_FunctionResultValue"]`
- 读取时同样存在分歧

**示例场景**：
```csharp
// 阶段 A 使用扩展方法设置
context.SetFunctionResultValue(result);

// 阶段 B 通过属性读取
var value = context.FunctionResultValue; // null! 因为存储在 Items 中
```

**建议**：
1. 删除扩展方法中的重复存储，统一使用 CommandContext 的直接属性
2. 或将 CommandContext 中的直接属性改为委托给 Items 字典

---

### 3.2 🔴 NonEventCommandLookupAdapter.CommandContextAdapter 的 WorkflowId/NodeKey

| 属性 | 值 |
|------|-----|
| **位置** | `NonEventCommandLookupAdapter.cs` |
| **严重性** | 🔴 高 |

**问题描述**：
`WorkflowId` 和 `NodeKey` 的 getter 返回 `string.Empty`，setter 空实现：

```csharp
public string WorkflowId
{
    get => string.Empty;
    set { }
}

public string NodeKey
{
    get => string.Empty;
    set { }
}
```

**后果**：
违反最小惊讶原则（Principle of Least Astonishment）。调用方设置值后读取不到，可能导致难以追踪的 Bug。

**建议**：
- 如果不需要这些属性，抛出 `NotSupportedException`
- 如果需要，使用真实存储

---

### 3.3 🟡 CommandContext.Reset() 不重置 Mode

| 属性 | 值 |
|------|-----|
| **位置** | `CommandContext.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
`Reset()` 方法重置大部分属性（`CurrentCommand`、`ParsedCommand`、`FunctionResultValue` 等），但**不重置 `Mode`**。

**后果**：
如果 Mode 在命令循环中被修改，后续命令会继承错误的 Mode。例如：
- 命令 1 将 Mode 改为 "Test"
- Reset() 被调用
- 命令 2 执行时 Mode 仍然是 "Test" 而非默认值

**建议**：
在 `Reset()` 中添加 `Mode` 的重置，或明确注释 Mode 不被重置的原因。

---

### 3.4 🟡 SetCommandHandler 中 GUI/CLI 模式判断逻辑

| 属性 | 值 |
|------|-----|
| **位置** | `SetCommandHandler.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
模式判断逻辑存在覆盖不全的问题：

```csharp
if (context.IsGuiMode && context.TargetForm != null)
{
    // GUI 模式处理
}
else if (context.IsCliMode && context.VirtualStore != null)
{
    // CLI 模式处理
}
else
{
    return "既不是 GUI 也不是 CLI 模式";
}
```

**后果**：
- 如果 `IsGuiMode=true` 但 `TargetForm=null`，错误信息是"既不是 GUI 也不是 CLI 模式"
- 实际是 GUI 模式但缺少 TargetForm，错误信息不准确
- 同样的问题适用于 CLI 模式

**建议**：
细化错误判断，提供更准确的错误信息：
```csharp
if (context.IsGuiMode)
{
    if (context.TargetForm == null)
        return "GUI 模式但 TargetForm 为 null";
    // GUI 处理
}
else if (context.IsCliMode)
{
    if (context.VirtualStore == null)
        return "CLI 模式但 VirtualStore 为 null";
    // CLI 处理
}
else
{
    return "既不是 GUI 也不是 CLI 模式";
}
```

---

### 3.5 🟡 ClickCommandHandler 中 bindingFinder 为 null 的错误时机

| 属性 | 值 |
|------|-----|
| **位置** | `ClickCommandHandler.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
`bindingFinder` 为 null 时在运行时才报错 "No binding finder configured"。

**后果**：
这是配置/初始化问题，应该在构造函数或初始化时检测（Fail Fast），而不是在运行时。

**建议**：
在构造函数中检查：
```csharp
public ClickCommandHandler(IBindingFinder bindingFinder, ...)
{
    _bindingFinder = bindingFinder ?? throw new ArgumentNullException(nameof(bindingFinder));
}
```

---

### 3.6 🟡 NonEventCommandHandler 中 context.HasError 的误判风险

| 属性 | 值 |
|------|-----|
| **位置** | `NonEventCommandHandler.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
调用 handler 后检查 `context.HasError`，但 `HasError` 检查的是**所有阶段**，而不仅仅是当前 handler 设置的阶段。

**后果**：
可能误判其他阶段的错误为当前 handler 的错误，导致 handler 的执行结果被错误地忽略。

**建议**：
检查特定阶段的错误，而非全局 `HasError`：
```csharp
var stageError = context.GetStageError("NonEventCommand");
if (stageError != null) return;
```

---

### 3.7 🟡 ResultCollectStage 中 Duration 始终为 TimeSpan.Zero

| 属性 | 值 |
|------|-----|
| **位置** | `ResultCollectStage.cs` 第 71 行 |
| **严重性** | 🟡 中 |

**问题描述**：
```csharp
results.Add(new StageResult
{
    StageName = stageName,
    IsSuccess = success,
    ErrorMessage = error,
    Duration = TimeSpan.Zero // 暂不收集耗时
});
```

**后果**：
作为结果收集阶段，不收集耗时是功能缺失。执行报告中的 Duration 永远为 0，对性能分析无帮助。

**建议**：
在管线执行过程中记录每个阶段的耗时，或在 `StageResult` 中添加耗时字段并在阶段执行时填充。

---

### 3.8 🟡 PipelineEntryInstance 中 _cachedDefaultPipeline 的线程安全

| 属性 | 值 |
|------|-----|
| **位置** | `PipelineEntryInstance.cs` |
| **严重性** | 🟡 中 |

**问题描述**：
使用 `volatile` + 双检锁，但 `GetOrCreateDefaultPipeline` 是 `protected virtual`：

```csharp
private volatile CommandPipeline<CommandContext>? _cachedDefaultPipeline;

protected virtual CommandPipeline<CommandContext> GetOrCreateDefaultPipeline()
{
    if (_cachedDefaultPipeline != null)
        return _cachedDefaultPipeline;
    lock (_pipelineLock)
    {
        if (_cachedDefaultPipeline != null)
            return _cachedDefaultPipeline;
        _cachedDefaultPipeline = CreateDefaultPipeline();
        return _cachedDefaultPipeline;
    }
}
```

**后果**：
子类重写 `GetOrCreateDefaultPipeline` 后可能绕过缓存机制，导致每次调用都创建新管线。

**建议**：
将缓存逻辑放在非虚方法中，虚方法只负责创建：
```csharp
public CommandPipeline<CommandContext> GetPipeline()
{
    if (_cachedDefaultPipeline != null)
        return _cachedDefaultPipeline;
    lock (_pipelineLock)
    {
        if (_cachedDefaultPipeline != null)
            return _cachedDefaultPipeline;
        _cachedDefaultPipeline = CreateDefaultPipeline();
        return _cachedDefaultPipeline;
    }
}

protected virtual CommandPipeline<CommandContext> CreateDefaultPipeline() { ... }
```

---

### 3.9 🟡 CommandExecutionResult 隐式转换

| 属性 | 值 |
|------|-----|
| **位置** | `CommandResults.cs` 第 245-255 行 |
| **严重性** | 🟡 中 |

**问题描述**：
```csharp
public static implicit operator CommandExecutionResult<object?>(CommandExecutionResult<T> result)
```

**后果**：
隐式转换可能在不期望的地方触发，导致难以调试的问题。例如方法重载解析时可能选择错误的版本。

**建议**：
使用显式方法（如 `ToUntyped()`）替代隐式转换。

---

### 3.10 🟡 ExecutionResult 与 TConfig.ExecutionResult 的双向隐式转换

| 属性 | 值 |
|------|-----|
| **位置** | `CommandResults.cs` 第 86-101 行 |
| **严重性** | 🟡 中 |

**问题描述**：
`ExecutionResult` 和 `TConfig.ExecutionResult` 之间定义了双向隐式转换：

```csharp
public static implicit operator TConfig.ExecutionResult(ExecutionResult result)
public static implicit operator ExecutionResult(TConfig.ExecutionResult result)
```

**后果**：
双向隐式转换可能导致歧义，编译器可能报错或在运行时产生意外行为。例如在泛型方法中，编译器无法确定使用哪个转换。

**建议**：
使用显式转换方法或工厂方法替代双向隐式转换。

---

## 4. 总结与优先级建议

### 按严重性汇总

| 类别 | 编号 | 问题 | 严重性 |
|------|------|------|--------|
| 重复 | 1.1 | INonEventCommandLookup 双重定义 | 🔴 高 |
| 重复 | 1.2 | IControlValueWriter 废弃未删除 | 🔴 高 |
| 重复 | 1.3 | IControlValueAccessor 双重定义 | 🔴 高 |
| 重复 | 1.4 | 结果类型体系重复 | 🟡 中 |
| 重复 | 1.5 | NonEventCommands 关键性映射重复 | 🟡 中 |
| 重复 | 1.6 | HasError new 关键字 | 🟡 中 |
| 冗余 | 2.1 | 适配器膨胀 | 🔴 高 |
| 冗余 | 2.2 | ExecuteCommandStage 构造函数重载 | 🟡 中 |
| 冗余 | 2.3 | PipelineEntryOptions Fluent API | 🟡 中 |
| 冗余 | 2.4 | CommandContextExtensions 属性重复 | 🟡 中 |
| 冗余 | 2.5 | 横切关注点分散 | 🟡 中 |
| 冗余 | 2.6 | 显式接口实现重复 | 🟡 中 |
| 错误 | 3.1 | 属性存储分离（数据丢失风险） | 🔴 高 |
| 错误 | 3.2 | WorkflowId/NodeKey 空实现 | 🔴 高 |
| 错误 | 3.3 | Reset() 不重置 Mode | 🟡 中 |
| 错误 | 3.4 | SetCommandHandler 模式判断 | 🟡 中 |
| 错误 | 3.5 | bindingFinder null 检查时机 | 🟡 中 |
| 错误 | 3.6 | HasError 误判风险 | 🟡 中 |
| 错误 | 3.7 | Duration 始终为 0 | 🟡 中 |
| 错误 | 3.8 | 管线缓存线程安全 | 🟡 中 |
| 错误 | 3.9 | 隐式转换风险 | 🟡 中 |
| 错误 | 3.10 | 双向隐式转换风险 | 🟡 中 |

### 修复优先级建议

**P0（立即修复）**：
- 3.1 CommandContextExtensions 与 CommandContext 属性存储分离
- 3.2 NonEventCommandLookupAdapter 的 WorkflowId/NodeKey 空实现

**P1（尽快修复）**：
- 1.1 INonEventCommandLookup 双重定义（消除适配器根源）
- 2.1 适配器膨胀（系统性降低复杂度）
- 1.2 / 1.3 废弃接口清理

**P2（计划修复）**：
- 1.4 结果类型体系合并
- 2.4 CommandContextExtensions 属性重复
- 3.3 Reset() 不重置 Mode
- 3.4 SetCommandHandler 模式判断

**P3（低优先级）**：
- 1.5 NonEventCommands 关键性映射优化
- 2.3 Fluent API 统一
- 3.7 Duration 收集
- 3.9 / 3.10 隐式转换清理
