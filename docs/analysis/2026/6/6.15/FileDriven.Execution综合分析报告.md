# FileDriven.Execution 综合分析报告

> **分析日期**：2026-06-15  
> **分析范围**：`FileDriven/FileDriven.Execution` 目录全部源文件  
> **总代码量**：约 4,600 行（9 个文件）

---

## 目录结构

```
FileDriven.Execution/
├── FileDrivenExecutor.cs          (1839行) — 五层架构统一执行门面
├── ExecutionResult.cs              (95行)  — 统一执行结果
├── GlobalUsings.cs                 (41行)  — 全局 using
├── FileDriven.Execution.csproj     (55行)  — 项目文件
├── Command/
│   ├── ControlServices.cs         (1254行) — 控件逻辑集中文件（合并5个文件）
│   ├── ExecutionPipelineEntry.cs   (539行) — 执行管线入口
│   ├── FileDrivenPipelineContext.cs(415行) — 管线统一上下文
│   ├── PipelineFactory.cs          (250行) — 管线工厂
│   └── Skeletons/
│       └── ScriptLoadSkeleton.cs   (106行) — 脚本加载骨架
```

---

## 一、重复逻辑（7处）

### 1. 🔴 Run() / RunGui() / RunCli() 三路初始化/清理模板重复

**位置**：`FileDrivenExecutor.cs` 第 150-354 行

**描述**：三个方法各自独立实现了几乎相同的生命周期管理模板：

```
SetupLogging → loggingInitialized 标志 → SetWorkingDirectory →
InitializeFramework → frameworkInitialized 标志 →
try/catch/finally → ShutdownFramework / ShutdownLogging / DisconnectPipe
```

**具体差异**：
- `RunGui()` 额外重复了 `Application.EnableVisualStyles()` 和 `Application.SetCompatibleTextRenderingDefault(false)`
- `RunCli()` 缺少 Stopwatch 计时（见错误逻辑#2）
- `Run()` 有完整的 ConnectPipe 步骤，`RunGui()` 和 `RunCli()` 没有

**影响**：约 60 行重复代码。修改生命周期逻辑（如新增初始化步骤、变更清理顺序）需要同步修改三处，极易遗漏。

**建议**：提取 `RunInternal(CliOptions, bool cliMode, Action<ExecutionPipelineEntry> executeAction)` 私有方法，三个入口方法只负责参数构建和委托传递。

---

### 2. 🟡 ControlServiceFacade 与 UnifiedControlService 功能重复

**位置**：`ControlServices.cs` 第 1072-1254 行（ControlServiceFacade） vs 第 420-653 行（UnifiedControlService）

**描述**：`ControlServiceFacade`（标记 `[Obsolete]`）提供了与 `UnifiedControlService` 几乎完全相同的功能：

| ControlServiceFacade | UnifiedControlService |
|---------------------|----------------------|
| `FindControl(name, root)` | `FindControl(name)` / `FindControl(container, name)` |
| `WriteValue(control, value)` | `SetValue(control, value)` |
| `ReadValue(control)` | `GetValue(control)` |
| `RegisterControl(name, control)` | `RegisterControl(name, control)` |
| `FindAndWriteValue(name, value, root)` | `TrySetControlValue(name, value, container)` |
| `IsFinderAvailable` | 无直接等价（通过 Resolve 检查） |

**影响**：约 170 行死代码。新开发者可能误用 `[Obsolete]` 的旧 API，且两套 API 的行为可能因实现细节不同而产生差异。

**建议**：删除 `ControlServiceFacade` 类。如果担心向后兼容，保留空类体仅用于编译通过。

---

### 3. 🟡 RunPipeline() 与 RunPipelineAsync() 的 IsScriptExecutionMode 设置重复

**位置**：`ExecutionPipelineEntry.cs` 第 236-253 行（RunPipeline）和第 280-348 行（RunPipelineAsync）

**描述**：两处都在 try/finally 中重复了事件抑制逻辑：

```csharp
// RunPipeline (第236-253行)
if (winFormsEventBinder != null)
    winFormsEventBinder.IsScriptExecutionMode = true;
try { ... }
finally {
    if (winFormsEventBinder != null)
        winFormsEventBinder.IsScriptExecutionMode = false;
}

// RunPipelineAsync (第280-348行) — 完全相同的模式
if (winFormsEventBinder != null)
    winFormsEventBinder.IsScriptExecutionMode = true;
try { ... }
finally {
    if (winFormsEventBinder != null)
        winFormsEventBinder.IsScriptExecutionMode = false;
}
```

**影响**：约 12 行重复代码。修改事件抑制逻辑（如新增其他标志位）需同步两处。

**建议**：提取 `SetScriptExecutionMode(IWinFormsEventBinder? binder, bool enabled)` 辅助方法，或在 `RunPipeline` 内部调用 `RunPipelineAsync` 的同步回退路径（当前相反）。

---

### 4. 🟢 FileDrivenPipelineContext 7个便捷属性的 EnsureParsedCommand 模式重复

**位置**：`FileDrivenPipelineContext.cs` 第 164-217 行

**描述**：7 个便捷属性（`ParsedAction`、`ParsedTarget`、`ParsedParameters`、`NormalizedCommand`、`ParseSuccess`、`ParseErrorMessage`、`IsNonEventCommand`）都使用完全相同的模式：

```csharp
public string? ParsedAction
{
    get => ParsedCommand?.Action;
    set { EnsureParsedCommand(); ParsedCommand.Action = value; }
}
```

每个属性约 6 行，7 个共约 42 行，其中约 35 行是模板代码。

**影响**：约 35 行模板代码。新增便捷属性时容易忘记调用 `EnsureParsedCommand()`。

**建议**：使用源代码生成器（Source Generator）或 T4 模板自动生成这些属性。或者使用 `TrySetValue` 模式方法替代属性。

---

### 5. 🟢 ControlRegistryAwareFinder 两个 FindByName 重载的查找逻辑重复

**位置**：`ControlServices.cs` 第 703-763 行

**描述**：`FindByName(object root, string name)` 和 `FindByName(string name)` 都包含相同的 IControlRegistry 优先查找 + 日志记录逻辑：

```csharp
// FindByName(object root, string name) — 第703-741行
var registry = GetRegistry();
if (registry != null) {
    var registered = registry.Find(name);
    if (registered != null) return registered;
    // 日志：registry miss
} else {
    // 日志：registry null
}
// 回退到 WinFormsControlFinder

// FindByName(string name) — 第744-763行
var registry = GetRegistry();
if (registry != null) {
    var registered = registry.Find(name);
    if (registered != null) return registered;
    // 日志：registry miss
}
// 回退到 WinFormsControlFinder
```

**影响**：约 30 行重复代码。修改查找逻辑（如新增缓存层）需同步两处。

**建议**：提取 `FindFromRegistry(string name)` 私有方法，两个重载都调用此方法。

---

### 6. 🟢 CompositeControlFinder 与 CompositeValueWriter 的 Composite 模式结构重复

**位置**：`ControlServices.cs` 第 53-200 行（CompositeControlFinder）和第 213-309 行（CompositeValueWriter）

**描述**：两个类都实现了完全相同的 Composite 模式结构：

| 成员 | CompositeControlFinder | CompositeValueWriter |
|------|----------------------|---------------------|
| 内部集合 | `List<IControlFinder>` | `List<IControlValueWriter>` |
| AddFinder/AddWriter | ✅ | ✅ |
| RemoveFinder/RemoveWriter | ✅ | ✅ |
| ClearFinders/ClearWriters | ✅ | ✅ |
| FinderCount/WriterCount | ✅ | ✅ |
| 遍历委托的 foreach 循环 | ✅ | ✅ |

**影响**：约 60 行重复代码。每个新的 Composite 类型都需要重复实现。

**建议**：提取泛型基类 `CompositeCollection<T>`，包含 Add/Remove/Clear/Count 方法。两个类只需继承并实现接口方法。

---

### 7. 🟢 WinFormsControlValueWriter 中类型查找逻辑的三次重复

**位置**：`ControlServices.cs` 第 877-946 行

**描述**：`WriteValue`、`ReadValue`、`SupportsType` 三个方法都包含完全相同的类型查找循环：

```csharp
// 1. 精确类型匹配
if (_adapters.TryGetValue(type, out var entry)) { return ...; }
// 2. 基类/接口匹配
foreach (var kvp in _adapters) {
    if (kvp.Key.IsAssignableFrom(type)) { return ...; }
}
return default;
```

**影响**：约 30 行重复代码。修改查找逻辑（如新增缓存、变更优先级）需同步三处。

**建议**：提取 `TryFindAdapter(Type type, out AdapterEntry entry)` 私有方法。

---

## 二、错误逻辑（10处）

### 1. 🔴 RunGui() 缺少 ConnectPipe() 步骤

**位置**：`FileDrivenExecutor.cs` 第 258-304 行

**代码**：
```csharp
public ExecutionResult RunGui(string? configPath = null, string? commandFile = null)
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    // ★ 缺少：ConnectPipe(options);
    ...
    finally {
        ...
        DisconnectPipe();  // 但 _pipeClient 从未初始化
    }
}
```

**问题**：
- `RunGui()` 跳过了 `ConnectPipe(options)` 步骤
- finally 块中调用了 `DisconnectPipe()`，虽然 null 条件运算符保证了安全，但逻辑上不对称
- 如果 GUI 模式需要命名管道通信（如 PowerShell 集成），此路径完全不可用

**影响**：GUI 模式下命名管道功能不可用。如果未来 GUI 模式依赖管道通信，此方法需要重构。

**修复**：在 `RunGui()` 的 try 块开头添加 `ConnectPipe(options)` 调用。

---

### 2. 🔴 RunCli() 缺少 Stopwatch 计时

**位置**：`FileDrivenExecutor.cs` 第 1444-1484 行

**代码**：
```csharp
private ExecutionResult StartCliMode(CliOptions options)
{
    ...
    return new ExecutionResult
    {
        ...
        ExecutionTime = TimeSpan.Zero,  // ★ 始终为 Zero！
        ...
    };
}
```

**问题**：`StartCliMode()` 没有使用 Stopwatch 记录执行时间，返回的 `ExecutionTime` 始终为 `TimeSpan.Zero`。而 `Run()` 和 `StartGuiMode()` 都正确记录了执行时间。

**影响**：CLI 模式的执行时间数据完全丢失，无法用于性能分析和监控。

**修复**：在方法开头创建 Stopwatch，结束时赋值给 `ExecutionTime`。

---

### 3. 🟡 ExecutionResult 隐式转换调用 Obsolete 方法

**位置**：`ExecutionResult.cs` 第 72-94 行

**代码**：
```csharp
[Obsolete("请使用隐式转换 (FileDrivenPipelineContext, TimeSpan) → ExecutionResult")]
public static ExecutionResult FromContext(...) => ...;

public static implicit operator ExecutionResult(
    (FileDrivenPipelineContext Context, TimeSpan ExecutionTime) source)
    => FromContext(source.Context, source.ExecutionTime);  // 调用 Obsolete 方法
```

**问题**：隐式转换运算符调用标记为 `[Obsolete]` 的方法。这是设计矛盾——要么隐式转换也标记 Obsolete，要么内联转换逻辑。

**影响**：使用隐式转换时会触发 CS0618 警告（如果调用方启用了 TreatWarningsAsErrors 则编译失败）。

**修复**：将 `FromContext()` 的逻辑直接内联到隐式转换运算符中，然后删除 `FromContext()` 方法。

---

### 4. 🟡 ControlServiceCollection.RegisterAll() 二次调用的幂等性缺陷

**位置**：`ControlServices.cs` 第 333-399 行

**代码**：
```csharp
public static void RegisterAll(IControlRegistry? controlRegistry = null)
{
    if (_registered)
    {
        if (controlRegistry != null)
        {
            var registry = ComponentRegistry.Instance;
            registry.Update<IControlRegistry>(controlRegistry, ...);
            // ★ 问题：只更新了 IControlRegistry 注册
            //   但 CompositeControlFinder 内部的 ControlRegistryAwareFinder 的
            //   _initialRegistry 仍然是旧的
        }
        return;
    }
    ...
}
```

**问题**：当 `_registered == true` 且传入新的 `controlRegistry` 时，只更新了 `ComponentRegistry` 中的 `IControlRegistry` 注册，但 `CompositeControlFinder` 内部的 `ControlRegistryAwareFinder` 的 `_initialRegistry` 字段仍然是旧的。虽然 `ControlRegistryAwareFinder.GetRegistry()` 会回退到 `ComponentRegistry` 动态获取，但构造时传入的 `_initialRegistry` 优先级更高。

**影响**：`StartGuiMode()` 中第二次调用 `RegisterAll(mainForm.ControlRegistry)` 时，`ControlRegistryAwareFinder` 可能仍使用旧的 registry 实例。

**修复**：在二次调用时，从 `ComponentRegistry` 重新解析 `IControlFinder`，如果它是 `CompositeControlFinder`，更新其内部的查找器。

---

### 5. 🟡 CreateContext() 中 GUI 模式 VirtualStore 为 null

**位置**：`ExecutionPipelineEntry.cs` 第 164-188 行

**代码**：
```csharp
var context = new FileDrivenPipelineContext
{
    ...
    VirtualStore = targetForm == null ? new Dictionary<string, string?>() : null,
    // GUI 模式 (targetForm != null) 时 VirtualStore 为 null
    ...
};
```

**问题**：GUI 模式下 `VirtualStore` 为 null。如果管线中某阶段依赖 `VirtualStore`（如 `ExtractValuesStage` 或自定义阶段），在 GUI 模式下会得到 null 引用异常。

**影响**：GUI 模式下如果某阶段尝试写入 `VirtualStore`，会触发 NRE。

**修复**：GUI 模式也应初始化 `VirtualStore`，或确保所有消费方在 GUI 模式下使用 `TargetForm` 替代。

---

### 6. 🟡 PipelineFactory CLI 模式 bindingFinder 注释与实现不一致

**位置**：`PipelineFactory.cs` 第 188-196 行

**代码**：
```csharp
else
{
    // ★ CLI 模式：bindingResolver 为 null，click 命令不可用
    //   传入 fallback bindingFinder，给出明确的错误信息
    bindingFinder = (controlName, eventType) =>
    {
        // 返回一个特殊的 BindingResult，其 FunctionName 为 null 但携带错误信息
        // ExecuteCommandStage.HandleClick 中 binding == null 时会输出错误信息
        return null;  // ★ 实际返回 null，不是 BindingResult
    };
}
```

**问题**：注释说"返回一个特殊的 BindingResult，其 FunctionName 为 null 但携带错误信息"，但实际上返回的是 `null`，不是 `BindingResult` 对象。如果未来 `ExecuteCommandStage.HandleClick` 的代码改为检查 `BindingResult` 的属性而不是 null 检查，会导致 NRE。

**影响**：误导后续维护者。如果有人按照注释实现"特殊的 BindingResult"，会改变当前行为。

**修复**：修正注释使其与实际实现一致，或实际返回一个 `FunctionName` 为 null 的 `BindingResult` 对象。

---

### 7. 🟡 RunPipelineAsync 回退同步路径可能阻塞 UI 线程

**位置**：`ExecutionPipelineEntry.cs` 第 335-339 行

**代码**：
```csharp
// ★ 修复（P3）：回退到同步路径（向后兼容）
//   当 _pool 为 null 时，RunPipelineAsync 回退到同步 RunPipeline。
//   注意：此回退路径是同步阻塞的，如果在 UI 线程上调用会导致 UI 线程阻塞。
//   调用方应确保在非 UI 线程上调用此方法，或确保 _pool 不为 null。
return RunPipeline(options, commandFile, targetForm, uiCommandManager, winFormsEventBinder);
```

**问题**：注释明确指出了风险，但没有提供任何保护机制。没有 `SynchronizationContext` 检查，没有 `Task.Run` 包装，没有异常提示。

**影响**：如果在 UI 线程上调用 `RunPipelineAsync` 且 `_pool == null`，UI 线程会被阻塞，导致界面冻结。

**修复**：至少添加 `SynchronizationContext` 检查，在 UI 线程上时使用 `Task.Run` 包装同步调用。

---

### 8. 🟢 Reset() 运行时依赖维护无编译时检查

**位置**：`FileDrivenPipelineContext.cs` 第 326-372 行

**代码**：
```csharp
public override void Reset()
{
    // 1. 保存运行时依赖
    //    ★ 如果新增了运行时依赖字段，请在此处添加保存代码
    var savedTargetForm = TargetForm;
    var savedFunctionExecutor = FunctionExecutor;
    // ... 10个保存语句

    // 2. 调用基类 Reset
    base.Reset();

    // 3. 恢复运行时依赖
    //    ★ 如果新增了运行时依赖字段，请在此处添加恢复代码
    TargetForm = savedTargetForm;
    FunctionExecutor = savedFunctionExecutor;
    // ... 10个恢复语句
}
```

**问题**：保存/恢复列表需要手动维护，没有任何编译时检查机制。新增字段时极易遗漏同步更新保存和恢复语句。

**影响**：新增运行时依赖字段后，如果忘记在 `Reset()` 中保存/恢复，该字段会在命令循环中被清空。

**修复**：使用反射自动保存/恢复所有标记了特定 Attribute 的字段，或使用 `[ResetPreserve]` 自定义属性标记需要保留的字段。

---

### 9. 🟢 ScriptLoadSkeleton 非 JSON 文件错误信息不完整

**位置**：`ScriptLoadSkeleton.cs` 第 65-82 行

**代码**：
```csharp
if (!File.Exists(fullPath))
{
    input.ScriptLoadSuccess = false;
    input.ScriptLoadErrorMessage = $"Command file not found: {input.ScriptPath}";
    return input;  // ★ 没有记录日志
}
```

**问题**：当 `ext != ".json"` 且文件不存在时，设置了错误信息但没有记录日志。而 JSON 转换失败时有日志记录（第 59 行）。

**影响**：非 JSON 文件加载失败时，日志中没有任何记录，排查问题困难。

**修复**：添加日志记录，与 JSON 转换失败的处理保持一致。

---

### 10. 🟢 exec 命令 output 参数解析日志不明确

**位置**：`FileDrivenExecutor.cs` 第 1698-1719 行

**代码**：
```csharp
private static string? GetOutputTargetFromParameters(IExecutionContext context)
{
    if (context.ParsedCommand?.Parameters == null)
    {
        FileDrivenLogPlaceholderHandler.Log("Executor_GetOutputTarget_result",
            new { outputTarget = "(null)", hasParameters = false, parameterKeys = "(null)" });
        return null;
    }
    ...
}
```

**问题**：当 `ParsedCommand` 为 null 时，日志显示 `"(null)"` 而不是明确的"命令未解析"错误。且无论是否找到 output 参数都记录日志，包括 `parameterKeys` 的完整列表，产生大量日志噪音。

**影响**：日志噪音大，且错误信息不明确。

**修复**：区分"命令未解析"和"未找到 output 参数"两种情况，分别记录不同的日志消息。

---

## 三、冗余逻辑（13处）

### 1. 🔴 ControlServiceFacade 死代码（~170行）

**位置**：`ControlServices.cs` 第 1072-1254 行

**描述**：整个 `ControlServiceFacade` 类标记为 `[Obsolete("请使用 IControlService...")]`，但仍然保留完整实现。所有功能已被 `UnifiedControlService` 替代。

**影响**：约 170 行死代码，增加文件大小和维护负担。新开发者可能误用。

**建议**：删除整个类。如果担心编译兼容性，保留空类体：
```csharp
[Obsolete("请使用 IControlService...")]
public static class ControlServiceFacade { }
```

---

### 2. 🟡 ExecutionResult.FromContext() 死代码

**位置**：`ExecutionResult.cs` 第 72-86 行

**描述**：`FromContext()` 标记为 `[Obsolete]`，但隐式转换运算符仍然调用它。

**建议**：将 `FromContext()` 的逻辑内联到隐式转换运算符中，然后删除 `FromContext()` 方法。

---

### 3. 🟢 Reset() 中已移除 SharedData 写入的注释残留

**位置**：`FileDrivenPipelineContext.cs` 第 362-371 行

**代码**：
```csharp
// ★ 已移除：SharedData["FunctionExecutor"] 写入
//   ExecuteCommandStage 不再从 SharedData 读取 FunctionExecutor，
//   改用构造函数注入的 functionExecutor 参数。此代码为死代码。
if (WinFormsEventBinder != null)
{
    SharedData["WinFormsEventTrigger"] = WinFormsEventBinder;
}
// ★ 已移除：SharedData["FindBindingFunc"] 写入
//   ExecuteCommandStage 不再从 SharedData 读取 FindBindingFunc，
//   改用构造函数注入的 bindingFinder 参数。此代码为死代码。
```

**建议**：删除这些注释，它们只对历史重构有意义，对当前代码维护没有帮助。

---

### 4. 🟢 ExecutionPipelineEntry 中 _cachedPipeline 已移除的注释残留

**位置**：`ExecutionPipelineEntry.cs` 第 211-213 行

**代码**：
```csharp
// ★ _cachedPipeline 已移除（死代码）
//   IEventPipelineExecutor.Execute() 使用独立的单命令管线...
```

**建议**：删除此注释。

---

### 5. 🟢 PipelineFactory.BuildCommandPipeline() 末尾多余空行

**位置**：`PipelineFactory.cs` 第 223-226 行

**描述**：方法末尾有 4 个连续空行。

**建议**：缩减为 1 个空行。

---

### 6. 🟢 FileDrivenExecutor 中过度详细的日志记录

**位置**：多处，典型如 `GetOutputTargetFromParameters()`（第 1698-1719 行）

**描述**：
- `GetOutputTargetFromParameters()` 每次调用都记录 `parameterKeys` 完整列表
- `RegisterNonEventCommands()` 中每个命令注册前后都有日志
- `InitializeFramework()` 中几乎每个子步骤都有日志

**影响**：生产环境中产生大量日志噪音，淹没真正重要的错误信息。

**建议**：将调试级别的日志降级为条件编译（`#if DEBUG`）或使用日志级别控制。

---

### 7. 🟢 ControlRegistryAwareFinder 中重复的日志模式

**位置**：`ControlServices.cs` 第 703-763 行

**描述**：两个 `FindByName` 重载中都有几乎相同的日志记录逻辑（registry miss、registry null、inner miss）。

**建议**：提取 `LogRegistryLookup(string name, IControlRegistry? registry, ...)` 私有方法。

---

### 8. 🟢 WinFormsControlValueWriter 中类型查找逻辑的三次重复

**位置**：`ControlServices.cs` 第 877-946 行

**描述**：见重复逻辑#7。

**建议**：提取 `TryFindAdapter(Type type, out AdapterEntry entry)` 私有方法。

---

### 9. 🟢 CompositeControlFinder 与 CompositeValueWriter 的结构重复

**位置**：`ControlServices.cs` 第 53-200 行和第 213-309 行

**描述**：见重复逻辑#6。

**建议**：提取泛型基类 `CompositeCollection<T>`。

---

### 10. 🟢 FileDrivenExecutor 中 try/catch 静默失败模式的重复

**位置**：`FileDrivenExecutor.cs` 中 `InitializeTemplateEngine`（第 642-689 行）、`InitializeBatchLogging`（第 734-764 行）、`PerformComponentValidation`（第 773-798 行）、`InitializeExecutionCenter`（第 1151-1249 行）等

**描述**：多个方法都使用相同的 try/catch → Log → 静默失败模式：
```csharp
try { ... }
catch (Exception ex) {
    FileDrivenLogPlaceholderHandler.Log("..._error", new { errorMessage = ex.Message });
}
```

**建议**：提取 `SafeExecute(string logKey, Action action)` 辅助方法。

---

### 11. 🟢 CreateContext() 与 CreateMinimalContext() 的字段设置重复

**位置**：`ExecutionPipelineEntry.cs` 第 157-188 行（CreateContext）和第 361-389 行（CreateMinimalContext）

**描述**：两个方法都独立设置 `BindingResolver`、`FunctionExecutor`、`LoadedConfigs`。`CreateMinimalContext` 是 `CreateContext` 的子集，但独立实现了相同的字段设置逻辑。

**建议**：`CreateMinimalContext` 调用 `CreateContext` 的简化版本，或提取公共的字段设置方法。

---

### 12. 🟢 FileDrivenPipelineContext 中 8 个 BatchData 委托属性的代码冗余

**位置**：`FileDrivenPipelineContext.cs` 第 50-104 行

**描述**：8 个属性（`BatchId`、`BatchName`、`Source`、`Description`、`WorkflowId`、`ProjectRoot`、`IsPipeMode`、`PipeName`）都只是委托给 `BatchData` 的 getter/setter，每个约 5 行，共约 40 行纯委托代码。

**建议**：删除这些委托属性，直接通过 `context.BatchData.BatchId` 访问。或者使用 `[DelegateTo("BatchData")]` 自定义属性 + 源代码生成器。

---

### 13. 🟢 GlobalUsings.cs 中过多的类型别名

**位置**：`GlobalUsings.cs` 第 1-41 行

**描述**：41 行中定义了 20+ 个类型别名，特别是：
```csharp
global using IEventBindingResolver = EventTriggerFramework.Abstractions.IEventBindingResolver;
global using IEventBindingInfo = EventTriggerFramework.Abstractions.IEventBindingInfo;
```
这些别名将 `CommandFramework.Abstractions` 中的同名接口隐藏了，可能导致歧义和编译错误。

**建议**：减少不必要的别名，只保留真正解决歧义的别名。对于 `IEventBindingResolver` 和 `IEventBindingInfo`，在需要 `CommandFramework` 版本时使用完全限定名。

---

## 优先级汇总

| 优先级 | 分类 | 问题 | 影响评估 |
|--------|------|------|---------|
| **P0** | 错误 | RunGui() 缺少 ConnectPipe() | 功能缺失 |
| **P0** | 错误 | RunCli() 缺少 Stopwatch 计时 | 数据不完整 |
| **P1** | 重复 | Run/RunGui/RunCli 三路初始化重复 | 维护风险高 |
| **P1** | 冗余 | ControlServiceFacade 死代码 | 误导新代码 |
| **P1** | 错误 | ExecutionResult 隐式转换调用 Obsolete | 设计矛盾 |
| **P1** | 错误 | RegisterAll() 幂等性缺陷 | 潜在 Bug |
| **P2** | 错误 | GUI 模式 VirtualStore 为 null | 潜在 NRE |
| **P2** | 错误 | bindingFinder 注释/实现不一致 | 误导维护 |
| **P2** | 错误 | RunPipelineAsync 回退阻塞 UI 线程 | 性能风险 |
| **P2** | 冗余 | ExecutionResult.FromContext() 死代码 | 代码冗余 |
| **P3** | 重复 | 便捷属性 EnsureParsedCommand 模式重复 | 代码质量 |
| **P3** | 重复 | FindByName 查找逻辑重复 | 代码质量 |
| **P3** | 重复 | Composite 模式结构重复 | 代码质量 |
| **P3** | 重复 | WinFormsControlValueWriter 类型查找重复 | 代码质量 |
| **P3** | 冗余 | 其余 9 处冗余逻辑 | 代码整洁度 |

---

## 量化统计

| 类别 | 数量 | 预估影响代码行数 |
|------|------|-----------------|
| 🔴 重复逻辑 | 1 处 | ~60 行 |
| 🟡 重复逻辑 | 2 处 | ~182 行 |
| 🟢 重复逻辑 | 4 处 | ~125 行 |
| 🔴 错误逻辑 | 2 处 | 功能影响 |
| 🟡 错误逻辑 | 5 处 | 潜在 Bug |
| 🟢 错误逻辑 | 3 处 | 代码质量 |
| 🔴 冗余逻辑 | 1 处 | ~170 行 |
| 🟡 冗余逻辑 | 1 处 | ~15 行 |
| 🟢 冗余逻辑 | 11 处 | ~100 行 |
| **合计** | **30 处** | **~652 行可优化** |

> 总代码量约 4,600 行，可优化代码约 652 行（14%），其中 P0-P1 级别问题约 427 行（9.3%）。
