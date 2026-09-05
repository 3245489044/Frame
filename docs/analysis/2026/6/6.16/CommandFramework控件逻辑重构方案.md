# CommandFramework 通用控件逻辑重构方案

## 概述

本文档分析 `FileDriven` 和 `Operation/FunctionStorage` 中应基于 `Share/CommandFramework` 通用控件逻辑进行优化重构的具体内容，提供详细的重构方案和优先级排序。

---

## 一、CommandFramework 通用控件接口总览

`Share/CommandFramework/Abstractions/` 中定义了以下通用控件接口：

| 接口 | 文件 | 职责 |
|------|------|------|
| `IControlFinder` | `IControlFinder.cs` | 按名称/类型/谓词查找控件，支持跨 Form 查找 |
| `IControlValueWriter` | `IControlValueWriter.cs` | 读写控件值，支持注册式适配器 |
| `IWinFormsEventTrigger` | `IWinFormsEventTrigger.cs` | GUI 模式下设置控件值并触发事件 |
| `IEventBindingResolver` | `IEventBindingResolver.cs` | 查找控件的事件绑定信息 |
| `INonEventCommandLookup` | `INonEventCommandLookup.cs` | 非事件命令只读查找 |
| `IConfigLoader` | `IConfigLoader.cs` | 配置加载契约 |
| `IUIController` | `IUIController.cs` | UI 控制器契约 |
| `IStateQuery` | `IStateQuery.cs` | 状态查询契约 |

`PipelineStageBase.cs` 中的 `CommandStageBase` 基类已提供以下通用辅助方法：
- `FindControl(object form, string controlName, IControlFinder)` — 通过 `IControlFinder` 查找控件
- `GetControlText(object control, IControlValueWriter)` — 通过 `IControlValueWriter` 读取控件值
- `SetControlText(object control, string? value, IControlValueWriter)` — 通过 `IControlValueWriter` 设置控件值
- `ExecuteForMode(CommandContext, guiAction, cliAction)` — 统一 GUI/CLI 模式操作

---

## 二、FileDriven 中应重构的控件逻辑

### 1. ControlHelper.cs — 控件查找辅助类

**文件**：`FileDriven/FileDriven.Pipeline/Core/ControlHelper.cs`

**当前实现**：
```csharp
internal static class ControlHelper
{
    public static Control? FindControlByName(Control form, string name) { ... }
    public static Control? FindControlOnAnyOpenForm(string controlName) { ... }
}
```

**问题**：
- 使用 `System.Windows.Forms.Control` 具体类型，与 CommandFramework 的通用 `object` 类型不兼容
- `FindControlByName` 对应 `IControlFinder.FindByName(object root, string name)`
- `FindControlOnAnyOpenForm` 对应 `IControlFinder.FindByName(string name)`
- 功能完全重叠，属于重复代码

**重构方案**：
1. 删除 `ControlHelper` 类
2. 所有调用方改用 `IControlFinder` 接口
3. 在 FileDriven 中实现 `IControlFinder` 的具体实现（如 `WinFormsControlFinder`），注入到管线

**影响范围**：
- `EventCore.cs` 中的 `EventBindingResolver` 不再需要 `ControlHelper`
- `Skeletons/` 目录中的骨架类不再需要 `ControlHelper`

---

### 2. EventBindingResolver — 事件绑定解析器

**文件**：`FileDriven/FileDriven.Pipeline/Core/EventCore.cs`（第 287-501 行）

**当前实现**：
```csharp
public class EventBindingResolver : 
    EventTriggerFramework.Abstractions.IEventBindingResolver, 
    CommandFramework.Abstractions.IEventBindingResolver
{
    // 同时实现两个框架的接口
    public IEventBindingInfo? FindEventBinding(string controlName, string eventType) { ... }
    CommandFramework.Abstractions.IEventBindingInfo? 
        CommandFramework.Abstractions.IEventBindingResolver.FindBinding(...) { ... }
}
```

**问题**：
- 同时实现 `EventTriggerFramework` 和 `CommandFramework` 两个版本的接口，存在适配转换
- `FindEventBinding` 返回 `EventTriggerFramework.Abstractions.IEventBindingInfo`
- `FindBinding`（显式接口实现）返回 `CommandFramework.Abstractions.IEventBindingInfo`
- 两个接口的 `IEventBindingInfo` 定义不同，需要适配转换
- `GetEventFunctions` 方法中的 switch 分支（第 445-469 行）硬编码了 7 种事件类型

**重构方案**：
1. 移除对 `EventTriggerFramework.Abstractions.IEventBindingResolver` 的实现
2. 仅实现 `CommandFramework.Abstractions.IEventBindingResolver`
3. 内部控件配置查找委托给 `IControlFinder` 实现
4. 事件类型映射使用 `IEventBindingResolver.ResolveEventType` 统一契约

**代码示例**（重构后）：
```csharp
public class EventBindingResolver : CommandFramework.Abstractions.IEventBindingResolver
{
    private readonly IDictionary<string, LoadedConfigInfo> _loadedConfigs;
    private readonly IControlFinder _controlFinder;  // 注入通用控件查找器

    public IEventBindingInfo? FindBinding(string controlName, string action)
    {
        var eventType = ResolveEventType(controlName, action);
        // 查找控件配置
        var control = FindControlConfig(controlName);
        if (control == null) return null;
        // 获取绑定信息
        return GetBindingInfo(control, eventType);
    }

    public string ResolveEventType(string controlName, string operationType)
    {
        // 统一的事件类型推断逻辑
        if (!string.Equals(operationType, "set", StringComparison.OrdinalIgnoreCase))
            return operationType;
        var controlType = GetControlType(controlName);
        return controlType?.ToLowerInvariant() switch
        {
            "combobox" => "selectedindexchanged",
            "textbox" => "textchanged",
            "checkbox" => "checkedchanged",
            "numericupdown" or "trackbar" => "valuechanged",
            _ => "textchanged"
        };
    }
}
```

---

### 3. EventTypeMapping — 事件类型映射表

**文件**：`FileDriven/FileDriven.Pipeline/Core/EventCore.cs`（第 223-269 行）

**当前实现**：
```csharp
public static class EventTypeMapping
{
    public static readonly IReadOnlyDictionary<string, string> EventTypeToCommandAction = ...;
    public static readonly IReadOnlyDictionary<string, string> CommandActionToDefaultEventType = ...;
    public static string ToCommandAction(string eventType) { ... }
    public static string ToDefaultEventType(string commandAction) { ... }
}
```

**问题**：
- `EventTypeToCommandAction` 映射（事件类型 → 命令动作）与 `IEventBindingResolver.ResolveEventType` 的职责重叠
- `CommandActionToDefaultEventType` 反向映射在 `ExecuteCommandStage` 中已有等价逻辑
- 两处维护同一映射关系，容易不一致

**重构方案**：
1. 删除 `EventTypeMapping` 静态类
2. `ToCommandAction` 的调用方改为使用 `IEventBindingResolver.ResolveEventType`
3. `ToDefaultEventType` 的调用方改为使用 `ExecuteCommandStage` 内部的事件类型推断

**调用方修改**：
- `ExecutionPipelineEntry.BuildEventCommand()`：`EventTypeMapping.ToCommandAction(eventType)` → `bindingResolver.ResolveEventType(controlName, "set")`

---

### 4. PipelineEntry.FindBinding — 重复暴露

**文件**：`FileDriven/FileDriven.Pipeline/PipelineEntry.cs`（第 334-340 行）

**当前实现**：
```csharp
public CommandFramework.Abstractions.IEventBindingInfo? FindBinding(string controlName, string action)
{
    return ((CommandFramework.Abstractions.IEventBindingResolver)_bindingResolver).FindBinding(controlName, action);
}
```

**问题**：
- `PipelineEntry` 同时暴露 `BindingResolver` 属性（第 106 行）和 `FindBinding` 方法
- `FindBinding` 只是对 `_bindingResolver.FindBinding` 的简单委托
- CommandFramework 的 `PipelineEntryInstance` 已通过 `RegisterServices` 注入 `IEventBindingResolver`

**重构方案**：
1. 删除 `PipelineEntry.FindBinding` 方法
2. 调用方改为通过 `PipelineEntry.BindingResolver.FindBinding()` 直接调用
3. 或通过 `IEventBindingResolver` 接口注入获取

---

### 5. PipelineFactory.bindingFinder 委托创建 — 重复包装逻辑

**文件**：`FileDriven/FileDriven.Execution/Command/PipelineFactory.cs`（第 168-194 行）

**当前实现**：
```csharp
Func<string, string, ExecuteCommandStage.BindingResult?>? bindingFinder = null;
if (bindingResolver != null)
{
    var innerFinder = bindingResolver.ToFindBindingFunc();
    bindingFinder = (controlName, eventType) =>
    {
        var result = innerFinder(controlName, eventType);
        if (result.FunctionName == null) return null;
        return new ExecuteCommandStage.BindingResult(
            FunctionName: result.FunctionName,
            Parameter: result.Inputs?.Count > 0 ? string.Join(",", result.Inputs) : null,
            Target: result.Output);
    };
}
```

**问题**：
- `bindingFinder` 委托的包装逻辑与 `EventBindingResolverExtensions.ToFindBindingFunc()` 扩展方法（定义在 `FileDrivenPipelineContext.cs` 第 398-414 行）存在重复
- 两处都在做 `IEventBindingResolver` → `BindingResult` 的转换

**重构方案**：
1. 统一使用 `EventBindingResolverExtensions.ToFindBindingFunc()` 扩展方法
2. 在扩展方法中直接返回 `ExecuteCommandStage.BindingResult?` 类型

```csharp
public static Func<string, string, ExecuteCommandStage.BindingResult?> ToExecuteCommandBindingFunc(
    this EventTriggerFramework.Abstractions.IEventBindingResolver resolver)
{
    return (controlName, eventType) =>
    {
        var binding = resolver.FindEventBinding(controlName, eventType);
        if (binding == null) return null;
        return new ExecuteCommandStage.BindingResult(
            FunctionName: binding.FunctionName,
            Parameter: binding.Inputs?.Count > 0 ? string.Join(",", binding.Inputs) : null,
            Target: binding.Output);
    };
}
```

---

### 6. ExecutionPipelineEntry.BuildEventCommand — 事件命令构建

**文件**：`FileDriven/FileDriven.Execution/Command/ExecutionPipelineEntry.cs`（第 468-474 行）

**当前实现**：
```csharp
public static string BuildEventCommand(string controlName, string eventType)
{
    var commandAction = FileDriven.Pipeline.Core.EventTypeMapping.ToCommandAction(eventType);
    return $"{commandAction} {controlName}";
}
```

**问题**：
- 依赖 `EventTypeMapping` 静态类（将被删除）
- 事件类型 → 命令动作的映射应属于 `IEventBindingResolver.ResolveEventType` 的职责

**重构方案**：
```csharp
public string BuildEventCommand(string controlName, string eventType)
{
    // 通过 IEventBindingResolver 推断命令动作
    var commandAction = _bindingResolver?.ResolveEventType(controlName, eventType) ?? "set";
    return $"{commandAction} {controlName}";
}
```

---

### 7. LoadedConfigDictionaryAdapter / ReverseLoadedConfigDictionaryAdapter — 配置字典适配器

**文件**：`FileDriven/FileDriven.Pipeline/Core/EventCore.cs`（第 95-210 行）

**当前实现**：
- `LoadedConfigDictionaryAdapter`：`IDictionary<string, object>` → `IDictionary<string, LoadedConfigInfo>`
- `ReverseLoadedConfigDictionaryAdapter`：反向适配

**问题**：
- 两个适配器类共约 116 行代码，功能单一
- CommandFramework 的 `IConfigLoader` 接口已定义配置加载契约
- `PipelineEntry` 已实现 `IConfigLoader`，配置存储格式应由 `IConfigLoader` 统一管理

**重构方案**：
1. 在 `IConfigLoader` 接口中增加类型安全的配置访问方法
2. 或让 `EventBindingResolver` 直接操作 `ConcurrentDictionary<string, object>`，避免适配器转换
3. 适配器模式保留但简化：仅在 `EventBindingResolver` 构造函数中做一次转换

---

## 三、FunctionStorage 中应重构的控件逻辑

### 8. NonEventCommandRegistry — 直接修改 CommandParser 内部状态

**文件**：`Operation/FunctionStorage/Framework/NonEventCommands.cs`（第 96-100 行）

**当前实现**：
```csharp
public void Register(string commandName, Action<IExecutionContext> handler, ...)
{
    // ...
    // 直接修改 CommandParser 的内部状态
    if (!string.IsNullOrWhiteSpace(commandName) &&
        !CommandParser.DefaultNonEventCommands.Contains(commandName))
    {
        CommandParser.DefaultNonEventCommands.Add(commandName);
    }
}
```

**问题**：
- `NonEventCommandRegistry` 直接修改 `CommandParser.DefaultNonEventCommands`（`public static` 集合）
- 这是对 CommandFramework 内部状态的侵入式修改，破坏了封装性
- `CommandParser.DefaultNonEventCommands` 被设计为 `public` 但语义上属于内部实现细节

**重构方案**：
1. 在 CommandFramework 中增加事件或回调机制，通知 `CommandParser` 同步非事件命令列表
2. 或让 `CommandParser` 通过 `INonEventCommandLookup` 接口动态查询非事件命令

```csharp
// 方案 A：事件通知
public class NonEventCommandRegistry : INonEventCommandRegistry
{
    public event Action<string>? OnCommandRegistered;
    
    public void Register(string commandName, ...)
    {
        _handlers[commandName] = ...;
        OnCommandRegistered?.Invoke(commandName);
    }
}

// CommandParser 订阅事件
nonEventRegistry.OnCommandRegistered += cmd => 
    DefaultNonEventCommands.Add(cmd);
```

```csharp
// 方案 B：动态查询（推荐）
// CommandParser 不再维护 DefaultNonEventCommands 静态集合，
// 改为通过 INonEventCommandLookup 动态查询
public bool IsNonEventCommand(string command)
{
    return _nonEventLookup?.RegisteredCommandNames.Contains(command) ?? false;
}
```

---

### 9. FunctionStorageRegistry — 两条函数执行路径

**文件**：`Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs`（第 161-168 行）

**当前实现**：
```csharp
public static async Task<IFunctionExecutionResult> ExecuteAsync(string functionName, object? parameters = null)
{
    return await Executor.ExecuteAsync(functionName, parameters);
}
```

**问题**：
- `FunctionStorageRegistry.ExecuteAsync` 委托给 `CommandFramework.Execution.FunctionExecutor`
- CommandFramework 的 `ExecuteCommandStage` 通过 `IFunctionExecutor` 接口执行函数
- 存在两条执行路径：`FunctionStorageRegistry.ExecuteAsync` 和 `IFunctionExecutor.ExecuteAsync`
- 两条路径最终都到达 `FunctionExecutor`，但调用链不同，增加了维护成本

**重构方案**：
1. `FunctionStorageRegistry.ExecuteAsync` 应委托给 `IFunctionExecutor` 接口
2. 确保 `ExecuteCommandStage` 和 `FunctionStorageRegistry` 使用同一个 `IFunctionExecutor` 实例

```csharp
public static class FunctionStorageRegistry
{
    // 使用 IFunctionExecutor 接口而非具体类型
    private static IFunctionExecutor? _functionExecutor;
    
    public static void Initialize(IFunctionExecutor executor)
    {
        _functionExecutor = executor;
    }
    
    public static async Task<IFunctionExecutionResult> ExecuteAsync(string functionName, object? parameters = null)
    {
        if (_functionExecutor == null)
            throw new InvalidOperationException("FunctionExecutor not initialized");
        return await _functionExecutor.ExecuteAsync(functionName, parameters);
    }
}
```

---

### 10. INonEventCommandHandler 接口 — 是否可被替代

**文件**：`Operation/FunctionStorage/Framework/Abstractions.cs`（第 62-90 行）

**当前实现**：
```csharp
public interface INonEventCommandHandler
{
    void HandleLoad(IExecutionContext context);
    void HandleClose(IExecutionContext context);
    void HandleShow(IExecutionContext context);
    void HandleLook(IExecutionContext context);
    void HandleDiscover(IExecutionContext context);
    void HandleList(IExecutionContext context);
    void HandleValidate(IExecutionContext context);
    void HandleExit(IExecutionContext context);
    void HandleSideload(IExecutionContext context);
}
```

**问题**：
- 定义了 9 个具体命令的处理方法，每个方法对应一个非事件命令
- CommandFramework 的 `INonEventCommandLookup` 使用通用 `TryGetHandler` 方法，更加灵活
- 新增命令时需要修改接口，违反开闭原则

**重构方案**：
1. 标记 `INonEventCommandHandler` 为 `[Obsolete]`
2. 所有命令处理通过 `INonEventCommandRegistry.Register` 注册策略实现
3. 最终删除 `INonEventCommandHandler` 接口

---

## 四、重构优先级与实施计划

### P0 — 立即重构（功能完全重叠，无风险）

| # | 文件 | 操作 | 预计工时 |
|---|------|------|----------|
| 1 | `ControlHelper.cs` | 删除，改用 `IControlFinder` | 2h |
| 2 | `EventTypeMapping` | 删除，委托给 `ResolveEventType` | 1h |

### P1 — 本周重构（接口统一，影响可控）

| # | 文件 | 操作 | 预计工时 |
|---|------|------|----------|
| 3 | `PipelineFactory.cs` bindingFinder | 统一使用扩展方法 | 1h |
| 4 | `ExecutionPipelineEntry.cs` BuildEventCommand | 委托给 `ResolveEventType` | 1h |
| 5 | `NonEventCommandRegistry.cs` 直接修改 | 改为事件通知或动态查询 | 3h |

### P2 — 本月重构（涉及接口调整，需要测试覆盖）

| # | 文件 | 操作 | 预计工时 |
|---|------|------|----------|
| 6 | `EventBindingResolver` | 统一到 `IEventBindingResolver` | 4h |
| 7 | `PipelineEntry.cs` FindBinding | 统一到 `IEventBindingResolver` | 2h |
| 8 | 配置字典适配器 | 基于 `IConfigLoader` 重构 | 3h |

### P3 — 下月重构（涉及架构调整，需要充分评估）

| # | 文件 | 操作 | 预计工时 |
|---|------|------|----------|
| 9 | `FunctionStorageRegistry` 执行路径 | 统一到 `IFunctionExecutor` | 4h |
| 10 | `INonEventCommandHandler` 接口 | 标记废弃并逐步迁移 | 2h |

---

## 五、重构后的架构关系

### 重构前（当前状态）

```
FileDriven.Pipeline
  ├── ControlHelper (WinForms 具体类型)
  ├── EventBindingResolver (实现两个框架接口)
  ├── EventTypeMapping (独立映射表)
  └── PipelineEntry.FindBinding (重复暴露)

FileDriven.Execution
  ├── PipelineFactory.bindingFinder (内联包装)
  └── ExecutionPipelineEntry.BuildEventCommand (依赖 EventTypeMapping)

FunctionStorage
  ├── NonEventCommandRegistry (直接修改 CommandParser)
  ├── FunctionStorageRegistry (两条执行路径)
  └── INonEventCommandHandler (违反开闭原则)
```

### 重构后（目标状态）

```
Share/CommandFramework (通用接口层)
  ├── IControlFinder ──────────────── WinFormsControlFinder (FileDriven 实现)
  ├── IControlValueWriter ─────────── WinFormsControlValueWriter (FileDriven 实现)
  ├── IEventBindingResolver ───────── EventBindingResolver (FileDriven 实现)
  ├── INonEventCommandLookup ──────── NonEventCommandRegistry (FunctionStorage 实现)
  ├── IFunctionExecutor ───────────── FunctionExecutor (CommandFramework 实现)
  └── IConfigLoader ───────────────── PipelineEntry (FileDriven 实现)

FileDriven (仅实现层，无重复逻辑)
  └── 所有控件操作通过 CommandFramework 接口完成

FunctionStorage (仅注册层，无侵入修改)
  └── 通过事件/回调通知 CommandFramework
```

---

## 六、风险与注意事项

1. **向后兼容**：所有重构应保持现有公开 API 不变，或提供兼容层
2. **测试覆盖**：重构前确保相关功能有足够的单元测试覆盖
3. **分步实施**：按 P0→P1→P2→P3 顺序逐步推进，每次重构后运行完整测试套件
4. **依赖注入**：`IControlFinder` 和 `IControlValueWriter` 的具体实现应通过构造函数注入，而非服务定位器模式
5. **线程安全**：`EventBindingResolver` 中的 `_loadedConfigs` 遍历已使用快照枚举模式，重构后需保持
