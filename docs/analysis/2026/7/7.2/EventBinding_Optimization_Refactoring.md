# 事件绑定系统优化重构方案

> **基于 WorkFlowMethod + ExecutionFramework 新框架的事件绑定重构**
> 日期：2026-07-01 | 作者：Cline

---

## 目录

1. [问题分析](#1-问题分析)
2. [新框架架构概览](#2-新框架架构概览)
3. [重构目标](#3-重构目标)
4. [详细设计方案](#4-详细设计方案)
5. [实施计划](#5-实施计划)
6. [风险与回退](#6-风险与回退)

---

## 1. 问题分析

### 1.1 核心问题：handler 空壳

**文件**: `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs:281-298`

```csharp
private static EventHandler CreateEventHandler(...)
{
    return (sender, args) =>
    {
        // ★ 只打印日志，不执行任何函数！
        ctx?.Log($"[EventBinder] 触发事件 '{eventName}' → '{functionName}'");
    };
}
```

`EventBinderTransformer.CreateEventHandler` 创建的委托**只记录日志**，没有调用 `functionName` 对应的实际函数。当用户点击按钮时，事件确实被触发了，但 handler 内部没有任何逻辑去执行 JSON 配置中定义的函数。

### 1.2 根因：_eventExecutionService 未被使用

**文件**: `FileDriven/FileDriven.Execution/EventBinding/WinFormsEventBinder.cs:73-78`

```csharp
public WinFormsEventBinder(IEventExecutionService eventExecutionService)
{
    _eventExecutionService = eventExecutionService ?? throw ...;
    // _eventExecutionService 在整个类中从未被使用！
}
```

`IEventExecutionService` 在构造函数中被注入，但 `BindControlEvent` 方法只做了反射绑定（`eventInfo.AddEventHandler(control, handler)`），没有将 `_eventExecutionService` 传递给 handler。

### 1.3 调用链全景

```
用户点击按钮
  → WinForms 原生事件触发
    → EventBinderTransformer 创建的 EventHandler
      → 只打印日志，不执行函数 ← BUG!

期望的调用链：
用户点击按钮
  → WinForms 原生事件触发
    → EventHandler 调用 IEventExecutionService.Execute(controlName, eventType, value)
      → EventExecutionService.BuildEventCommand → "do set {controlName}"
        → EventPipelineExecutor.ExecutePipelineInternal(command, context)
          → 单命令管线：ParseCommand → ExecuteCommand → ResultCollect
            → 执行 JSON 配置中定义的函数
```

### 1.4 架构问题汇总

| 问题 | 层级 | 严重程度 | 说明 |
|------|------|---------|------|
| handler 空壳 | EventTriggerFramework | **P0** | `CreateEventHandler` 不执行函数 |
| _eventExecutionService 未使用 | FileDriven.Execution | **P0** | 注入的服务未在 handler 中使用 |
| 默认管线不含 WinFormsControlFinder | EventTriggerFramework | P2 | 影响非 sideContainer 场景的控件查找 |
| EventBindingContext 不含 IEventExecutionService | EventTriggerFramework | P1 | 管线上下文缺少执行服务引用 |
| 控件查找策略分散 | EventTriggerFramework | P2 | 4 种查找策略分散在多个 Transformer 中 |

---

## 2. 新框架架构概览

### 2.1 三层执行架构（已就绪）

```
ExecutionCenter (TaskExecutorPool)
    ↓ 任务调度：排队 + 分发 + 取消
CentralPipeline (ScriptLoadSkeleton → ForEach)
    ↓ 管线编排：ParseCommand → ExecuteCommand → ResultCollect
Container (IExecutionCell)
    ↓ 弹性执行：重试/熔断/超时/日志
实际命令执行
```

### 2.2 事件执行链路（已就绪）

```
IEventExecutionService.Execute(controlName, eventType, value)
    ↓
EventExecutionService.BuildEventCommand()
    ↓ "do set {controlName}" / "do click {controlName}"
EventPipelineExecutor.ExecutePipelineInternal(command, context)
    ↓
单命令管线：ParseCommandStage → ExecuteCommandStage → ResultCollectStage
    ↓
IExecutionCell.ExecuteAsync() → 弹性执行 → 实际函数调用
```

### 2.3 函数注册体系（已就绪）

```
WorkFlowMethodUsage (统一静态入口)
    ↓
IWorkflowRegistry / IFunctionRegistry (双注册表桥接)
    ↓
RegisterFunction(name, Func<WfContext, object>)
RegisterDelegate(name, Delegate, description)
    ↓
PipelineExecutor / FunctionExecutor (执行器)
```

### 2.4 事件绑定管线（需重构）

```
当前（有缺陷）：
EventBindingPipelineRunner
    → ViewExtractorTransformer
    → ControlListExtractorTransformer
    → ControlFinderTransformer / WinFormsControlFinderTransformer
    → ScriptModeGuardTransformer
    → BindingMetadataTransformer
    → EventBinderTransformer (handler 空壳 ← BUG)
    → RecursiveTraversalTransformer / ControlTreeWalkerTransformer
    → BindingStatisticsTransformer

目标（修复后）：
EventBindingPipelineRunner
    → ViewExtractorTransformer
    → ControlListExtractorTransformer
    → WinFormsControlFinderTransformer (统一使用)
    → ScriptModeGuardTransformer
    → BindingMetadataTransformer
    → EventBinderTransformer (handler 调用 IEventExecutionService)
    → ControlTreeWalkerTransformer (统一使用)
    → BindingStatisticsTransformer
```

---

## 3. 重构目标

### 3.1 核心目标

1. **修复 handler 空壳**：事件触发时通过 `IEventExecutionService` 执行配置中定义的函数
2. **统一控件查找策略**：默认使用 `WinFormsControlFinderTransformer` + `ControlTreeWalkerTransformer`
3. **消除重复管线定义**：只保留一个完整的绑定管线定义
4. **增强诊断能力**：事件触发时输出完整的调用链日志

### 3.2 非目标

- ❌ 不改变 `IWinFormsEventBinder` 接口定义
- ❌ 不改变 `IEventExecutionService` 接口定义
- ❌ 不改变三层执行架构（ExecutionCenter → CentralPipeline → Container）
- ❌ 不改变函数注册体系（WorkFlowMethodUsage）

---

## 4. 详细设计方案

### 4.1 修复 EventBinderTransformer.CreateEventHandler

#### 4.1.1 问题

`CreateEventHandler` 创建的委托不执行函数。

#### 4.1.2 方案

在 `EventBinderTransformer` 中增加 `IEventExecutionService` 的引用，handler 触发时调用 `Execute` 方法。

**修改文件**: `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`

```csharp
public class EventBinderTransformer : ITransformer<EventBindingContext, EventBindingContext>
{
    public string Name => "EventBinder";

    // ★ 新增：事件执行服务引用
    private readonly IEventExecutionService? _eventExecutionService;

    public EventBinderTransformer(IEventExecutionService? eventExecutionService = null)
    {
        _eventExecutionService = eventExecutionService;
    }

    // ... 现有 Transform 方法 ...

    private static EventHandler CreateEventHandler(
        Control control, string eventName, string functionName,
        IWinFormsEventBinder binder, TransformContext? ctx,
        IEventExecutionService? executionService = null)  // ★ 新增参数
    {
        return (sender, args) =>
        {
            if (binder.IsScriptExecutionMode)
            {
                ctx?.Log($"[EventBinder] 跳过事件 '{eventName}'（脚本执行模式）");
                return;
            }

            if (sender is not Control ctrl || string.IsNullOrEmpty(ctrl.Name))
                return;

            ctx?.Log($"[EventBinder] 触发事件 '{eventName}' → '{functionName}' (控件: '{ctrl.Name}')");

            // ★ 修复：通过 IEventExecutionService 执行函数
            if (executionService != null)
            {
                var value = GetControlValue(ctrl);
                var success = executionService.Execute(ctrl.Name, eventName, value);
                ctx?.Log($"[EventBinder] 执行结果: {(success ? "成功" : "失败")}");
            }
            else
            {
                ctx?.Log($"[EventBinder] 警告: IEventExecutionService 未注入，无法执行函数 '{functionName}'");
            }
        };
    }

    // ★ 新增：获取控件当前值
    private static string GetControlValue(Control control)
    {
        return control switch
        {
            TextBox tb => tb.Text,
            ComboBox cb => cb.SelectedItem?.ToString() ?? cb.Text,
            CheckBox chk => chk.Checked.ToString(),
            NumericUpDown nud => nud.Value.ToString(),
            Label lbl => lbl.Text,
            _ => control.Text ?? string.Empty
        };
    }
}
```

#### 4.1.3 依赖关系

`EventBinderTransformer` 在 `EventTriggerFramework` 项目中，需要引用 `BackEndDriven.Abstractions` 中的 `IEventExecutionService`。

```
EventTriggerFramework
    → 引用 BackEndDriven.Abstractions (IEventExecutionService)
    → 通过构造函数注入，不直接依赖具体实现
```

### 4.2 统一事件绑定管线定义

#### 4.2.1 问题

`EventBindingPipeline` 中有 4 个管线定义方法，职责重叠：

| 方法 | 控件查找 | 控件遍历 |
|------|---------|---------|
| `CreateFullBindingPipeline()` | ControlFinderTransformer | RecursiveTraversalTransformer |
| `CreateFullBindingPipelineWithWinFormsControlFinder()` | WinFormsControlFinderTransformer | RecursiveTraversalTransformer |
| `CreateFullBindingPipelineWithTreeWalker()` | ControlFinderTransformer | ControlTreeWalkerTransformer |
| `CreateFullBindingPipelineWithWinFormsAndTreeWalker()` | WinFormsControlFinderTransformer | ControlTreeWalkerTransformer |

#### 4.2.2 方案

只保留一个完整管线定义，使用 `WinFormsControlFinderTransformer` + `ControlTreeWalkerTransformer`。

**修改文件**: `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs`

```csharp
public static class EventBindingPipeline
{
    // ★ 重构：只保留一个完整管线定义
    // 使用 WinFormsControlFinderTransformer（策略1-4）+ ControlTreeWalkerTransformer
    public static ITransformer<EventBindingContext, EventBindingContext> CreateFullBindingPipeline(
        IEventExecutionService? eventExecutionService = null)  // ★ 新增参数
    {
        var bindingPipeline = CreateBindingPipeline(eventExecutionService);

        return new ViewExtractorTransformer()
            .Then(new ControlListExtractorTransformer())
            .Then(new WinFormsControlFinderTransformer())  // ★ 统一使用
            .Then(bindingPipeline)
            .Then(new ControlTreeWalkerTransformer())       // ★ 统一使用
            .Then(new BindingStatisticsTransformer());
    }

    // ★ 重构：单控件绑定管线也支持传入 IEventExecutionService
    public static ITransformer<EventBindingContext, EventBindingContext> CreateBindingPipeline(
        IEventExecutionService? eventExecutionService = null)
    {
        return new ScriptModeGuardTransformer()
            .Then(new BindingMetadataTransformer())
            .Then(new EventBinderTransformer(eventExecutionService));  // ★ 传入执行服务
    }

    // ★ 删除以下冗余方法：
    // [删除] CreateFullBindingPipelineWithWinFormsControlFinder()
    // [删除] CreateFullBindingPipelineWithTreeWalker()
    // [删除] CreateFullBindingPipelineWithWinFormsAndTreeWalker()
}
```

### 4.3 修复 WinFormsEventBinder.BindControlEvent

#### 4.3.1 问题

`_eventExecutionService` 注入后从未使用。

#### 4.3.2 方案

在 `BindControlEvent` 中，将 `_eventExecutionService` 封装到 handler 内部。

**修改文件**: `FileDriven/FileDriven.Execution/EventBinding/WinFormsEventBinder.cs`

```csharp
public void BindControlEvent(Control control, string eventName, Delegate handler,
    string? output = null, string? inputs = null)
{
    ArgumentNullException.ThrowIfNull(control);
    ArgumentNullException.ThrowIfNull(handler);
    if (string.IsNullOrWhiteSpace(eventName))
        throw new ArgumentException("Event name cannot be empty", nameof(eventName));

    // 保存 output/inputs 到控件 Tag
    SaveControlParameters(control, output, inputs);

    // ★ 修复：将 _eventExecutionService 封装到 handler 中
    //   当 EventBinderTransformer 传入的 handler 不包含执行逻辑时，
    //   此处的包装确保事件触发时能通过 IEventExecutionService 执行函数
    var executionService = _eventExecutionService;
    EventHandler wrappedHandler = (sender, args) =>
    {
        // 先执行原始 handler（EventBinderTransformer 的日志逻辑）
        handler.DynamicInvoke(sender, args);

        // ★ 新增：通过 IEventExecutionService 执行函数
        if (executionService != null && sender is Control ctrl && !string.IsNullOrEmpty(ctrl.Name))
        {
            var value = GetControlValue(ctrl);
            executionService.Execute(ctrl.Name, eventName, value);
        }
    };

    // 绑定事件（使用缓存避免重复反射）
    var eventInfo = GetOrAddEventInfo(control.GetType(), eventName);
    if (eventInfo == null)
    {
        FileDrivenLogPlaceholderHandler.Log("MainForm_log_message",
            new { message = $"[WinFormsEventBinder] 警告: 控件 '{control.Name}' 上未找到事件 '{eventName}'" });
        return;
    }

    eventInfo.AddEventHandler(control, wrappedHandler);  // ★ 使用包装后的 handler
}
```

### 4.4 更新 EventBindingPipelineRunner 默认管线

#### 4.4.1 问题

`EventBindingPipelineRunner` 默认使用旧版 `CreateFullBindingPipeline()`。

#### 4.4.2 方案

支持通过构造函数注入 `IEventExecutionService`。

**修改文件**: `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs`

```csharp
public class EventBindingPipelineRunner
{
    private readonly ITransformer<EventBindingContext, EventBindingContext> _pipeline;

    // ★ 重构：默认使用统一管线，支持注入 IEventExecutionService
    public EventBindingPipelineRunner()
        : this(EventBindingPipeline.CreateFullBindingPipeline())
    {
    }

    // ★ 新增：支持注入 IEventExecutionService
    public EventBindingPipelineRunner(IEventExecutionService eventExecutionService)
        : this(EventBindingPipeline.CreateFullBindingPipeline(eventExecutionService))
    {
    }

    public EventBindingPipelineRunner(ITransformer<EventBindingContext, EventBindingContext> pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    // ... 其余方法不变 ...
}
```

### 4.5 更新 DefaultEventBindingService

#### 4.5.1 问题

`DefaultEventBindingService` 创建 `EventBindingPipelineRunner` 时未传入 `IEventExecutionService`。

#### 4.5.2 方案

**修改文件**: `CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs`

```csharp
public class DefaultEventBindingService : UniversalMiddleware.Abstractions.IEventBindingService
{
    // ★ 新增：IEventExecutionService 引用
    private IEventExecutionService? _eventExecutionService;

    // ★ 新增：延迟注入 IEventExecutionService
    public void SetEventExecutionService(IEventExecutionService eventExecutionService)
    {
        _eventExecutionService = eventExecutionService
            ?? throw new ArgumentNullException(nameof(eventExecutionService));
        // ★ 重建管线执行器，传入 IEventExecutionService
        _pipelineRunner = new EventBindingPipelineRunner(eventExecutionService);
    }

    // ★ 修改构造函数：支持注入 IEventExecutionService
    public DefaultEventBindingService(
        IEventExecutionService? eventExecutionService = null,
        IEventTriggerService? eventTriggerService = null,
        WinForms.WinFormsEventBus? fallbackEventBus = null,
        IWinFormsEventBinder? winFormsEventBinder = null)
    {
        _eventExecutionService = eventExecutionService;
        _eventTriggerService = eventTriggerService;
        _fallbackEventBus = fallbackEventBus;
        _winFormsEventBinder = winFormsEventBinder;
        // ★ 如果传入了 IEventExecutionService，使用带执行服务的管线
        _pipelineRunner = eventExecutionService != null
            ? new EventBindingPipelineRunner(eventExecutionService)
            : new EventBindingPipelineRunner();
    }
}
```

### 4.6 更新 FrameworkInitializer 注入链路

#### 4.6.1 问题

`FrameworkInitializer` 初始化各组件时，未将 `IEventExecutionService` 注入到 `DefaultEventBindingService`。

#### 4.6.2 方案

**修改文件**: `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs`

```csharp
// 在 FrameworkInitializer 的初始化方法中，增加注入链路：
// 1. 创建 EventPipelineExecutor（实现 IEventPipelineExecutor）
// 2. 创建 EventExecutionService（实现 IEventExecutionService）
// 3. 将 IEventExecutionService 注入到 DefaultEventBindingService
// 4. DefaultEventBindingService 将 IEventExecutionService 传入 EventBindingPipelineRunner
// 5. EventBindingPipelineRunner 将 IEventExecutionService 传入 EventBinderTransformer
// 6. EventBinderTransformer 将 IEventExecutionService 封装到 EventHandler 中
```

---

## 5. 实施计划

### 5.1 阶段一：修复核心 Bug（P0）

| 步骤 | 文件 | 修改内容 | 风险 |
|------|------|---------|------|
| 1.1 | `EventBindingTransformers.cs` | `EventBinderTransformer` 增加 `IEventExecutionService` 注入，handler 调用 `Execute` | 低 |
| 1.2 | `WinFormsEventBinder.cs` | `BindControlEvent` 将 `_eventExecutionService` 封装到 handler | 低 |
| 1.3 | `EventBindingPipeline.cs` | `EventBindingPipelineRunner` 支持注入 `IEventExecutionService` | 低 |

### 5.2 阶段二：统一管线定义（P1）

| 步骤 | 文件 | 修改内容 | 风险 |
|------|------|---------|------|
| 2.1 | `EventBindingPipeline.cs` | 删除 3 个冗余管线定义，只保留 1 个 | 中（需确认调用方） |
| 2.2 | `EventBindingPipeline.cs` | 统一使用 `WinFormsControlFinderTransformer` + `ControlTreeWalkerTransformer` | 低 |

### 5.3 阶段三：完善注入链路（P1）

| 步骤 | 文件 | 修改内容 | 风险 |
|------|------|---------|------|
| 3.1 | `DefaultEventBindingService.cs` | 增加 `IEventExecutionService` 注入支持 | 低 |
| 3.2 | `FrameworkInitializer.cs` | 完善 `IEventExecutionService` → `DefaultEventBindingService` 注入链路 | 中 |

### 5.4 阶段四：增强诊断（P2）

| 步骤 | 文件 | 修改内容 | 风险 |
|------|------|---------|------|
| 4.1 | `EventBindingTransformers.cs` | handler 增加执行结果日志 | 低 |
| 4.2 | `WinFormsEventBinder.cs` | 增加事件触发和执行的全链路日志 | 低 |

### 5.5 依赖关系图

```
FrameworkInitializer
    ↓ 创建
EventPipelineExecutor (implements IEventPipelineExecutor)
    ↓ 内部创建
EventExecutionService (implements IEventExecutionService)
    ↓ 注入到
DefaultEventBindingService
    ↓ 传入
EventBindingPipelineRunner
    ↓ 传入
EventBinderTransformer
    ↓ 封装到
EventHandler (用户点击时调用)
    ↓ 调用
IEventExecutionService.Execute(controlName, eventType, value)
    ↓
EventPipelineExecutor.ExecutePipelineInternal(command, context)
    ↓
单命令管线 → 执行函数
```

---

## 6. 风险与回退

### 6.1 风险评估

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|---------|
| `IEventExecutionService` 在 handler 触发时尚未初始化 | 低 | 高 | 增加 null 检查，日志警告 |
| 事件触发死循环（set 命令触发事件 → 事件触发 set → ...） | 中 | 高 | `IsScriptExecutionMode` 标记已存在，handler 中检查 |
| 多线程竞争（多个事件同时触发） | 低 | 中 | `EventExecutionService.Execute` 内部通过 `TaskExecutorPool` 串行化 |
| 控件值获取不准确 | 低 | 低 | `GetControlValue` 覆盖常见控件类型 |

### 6.2 回退方案

1. **代码级回退**：保留旧版 `EventBindingPipeline.CreateFullBindingPipeline()` 作为 `Obsolete` 方法，调用方可指定使用旧版
2. **配置级回退**：在 `framework.config.json` 中增加 `UseLegacyEventBinding` 开关
3. **运行时回退**：`EventBinderTransformer` 中 `_eventExecutionService` 为 null 时，保持现有行为（只记录日志）

### 6.3 验证方法

1. **单元测试**：验证 `EventBinderTransformer` 创建的 handler 能正确调用 `IEventExecutionService`
2. **集成测试**：验证 sideContainer 中按钮点击能正确执行 JSON 配置中定义的函数
3. **回归测试**：验证现有脚本执行路径不受影响（脚本模式通过 `IsScriptExecutionMode` 跳过）

---

## 附录 A：关键文件索引

| 文件路径 | 职责 | 修改类型 |
|---------|------|---------|
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | EventBinderTransformer（handler 空壳修复 + IEventExecutionService→Func 委托） | **修改** |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs` | 管线定义 + EventBindingPipelineRunner（IEventExecutionService→Func 委托） | **修改** |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingModels.cs` | EventBindingContext 数据模型 | 不修改 |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/ControlTraversalTransformers.cs` | ControlFinderTransformer 等 | 不修改 |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/WinFormsControlFinderTransformer.cs` | WinFormsControlFinderTransformer | 不修改 |
| `CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs` | 默认事件绑定服务（IEventExecutionService→Func 委托 + SetEventExecuteFunc） | **修改** |
| `CsharpPrj/Operation/EventTriggerFramework/EventTriggerFramework.csproj` | 移除 BackEndDriven 引用（消除循环依赖） | **修改** |
| `FileDriven/FileDriven.Execution/EventBinding/WinFormsEventBinder.cs` | WinForms 事件绑定器（保留 IEventExecutionService，FileDriven.Execution 层使用） | 不修改 |
| `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` | 框架初始化（注释更新，说明延迟注入时机） | **修改** |
| `CsharpPrj/Operation/BackEndDriven/Abstractions/IEventExecutionService.cs` | 事件执行服务接口 | 不修改 |
| `CsharpPrj/Operation/BackEndDriven/Execution/EventExecutionService.cs` | 事件执行服务实现 | 不修改 |
| `FileDriven/FileDriven.Execution/EventPipeline/EventPipelineExecutor.cs` | 事件管线执行器 | 不修改 |

## 附录 C：循环依赖解决方案

### 问题

`EventTriggerFramework` 引用了 `BackEndDriven`（为了 `IEventExecutionService` 接口），
而 `BackEndDriven` 也引用了 `EventTriggerFramework`（为了 `IEventBindingInfo` 等抽象接口），
形成了双向引用循环。

### 解决方案

**EventTriggerFramework 层使用 `Func<string, string, string, bool>?` 委托替代 `IEventExecutionService` 接口。**

- `EventBinderTransformer`：`_eventExecutionService` → `_eventExecuteFunc`（Func 委托）
- `EventBindingPipeline.CreateBindingPipeline/CreateFullBindingPipeline`：参数从 `IEventExecutionService?` → `Func<string, string, string, bool>?`
- `EventBindingPipelineRunner`：构造函数参数从 `IEventExecutionService?` → `Func<string, string, string, bool>?`
- `DefaultEventBindingService`：`_eventExecutionService` → `_eventExecuteFunc`，`SetEventExecutionService` → `SetEventExecuteFunc`
- `EventTriggerFramework.csproj`：移除对 `BackEndDriven.csproj` 的引用

**FileDriven.Execution 层保留 `IEventExecutionService` 接口**，因为该层已经引用了 `BackEndDriven`。

### 调用链

```
EventPipelineExecutor (FileDriven.Execution)
  ↓ 创建
EventExecutionService (implements IEventExecutionService)
  ↓ 注入到
WinFormsEventBinder (FileDriven.Execution 中的新版)
  ↓ BindControlEvent 包装 handler
EventHandler (用户点击时调用)
  ↓ 调用
IEventExecutionService.Execute(controlName, eventType, value)
  ↓
EventPipelineExecutor.ExecutePipelineInternal(command, context)
  ↓
单命令管线 → 执行函数
```

> **说明**：`EventBinderTransformer` 的 `_eventExecuteFunc` 作为可选的双重保险，
> 主要执行路径通过 `WinFormsEventBinder` 的 `_eventExecutionService`（`IEventExecutionService`）实现。
> `SetEventExecuteFunc` 方法保留供未来使用，当前阶段 `EventPipelineExecutor` 在 DI 容器中创建，
> `FrameworkInitializer` 无法直接获取，因此不在此处注入。

## 附录 D：2026-07-01 集中整理实际修改记录

### 修改 1：EventBindingPipeline.cs — 统一管线定义

**文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs`

**修改内容**：
- `CreateFullBindingPipeline()` 和 `CreateFullBindingPipeline(Func<...>?)` 中：
  - `ControlFinderTransformer` → `WinFormsControlFinderTransformer`
  - `RecursiveTraversalTransformer` → `ControlTreeWalkerTransformer`

**原因**：统一使用 WinForms 专用的控件查找和遍历 Transformer，消除旧版 Transformer 的使用。

### 修改 2：WinFormsEventBinder.cs — 包装 handler 调用 IEventExecutionService

**文件**：`FileDriven/FileDriven.Execution/EventBinding/WinFormsEventBinder.cs`

**修改内容**：
- `BindControlEvent` 方法中，将传入的 `handler` 包装为 `wrappedHandler`
- `wrappedHandler` 先执行原始 handler（EventBinderTransformer 的逻辑），再调用 `_eventExecutionService.Execute`
- 增加全链路日志（执行前、执行结果、异常）

**原因**：集中整理事件执行链路，确保 `IEventExecutionService` 在事件触发时被调用。
`EventBinderTransformer` 的 `_eventExecuteFunc` 作为可选的双重保险，
主要执行路径通过 `WinFormsEventBinder` 的 `_eventExecutionService` 实现。

### 修改 3：FrameworkInitializer.cs — 更新注释说明事件执行链路

**文件**：`FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs`

**修改内容**：
- 更新 `DefaultEventBindingService` 创建处的注释
- 说明事件执行链路：`EventBinderTransformer` → `WinFormsEventBinder.BindControlEvent` → `wrappedHandler` → `_eventExecutionService.Execute`
- 说明 `EventPipelineExecutor` 在 DI 容器中创建，`FrameworkInitializer` 阶段无法获取

**原因**：明确当前架构的事件执行链路，便于后续维护。

## 附录 B：调用链时序图

```
MainForm.LoadSideContainerAsync
  │
  ├─ ConfigLoader.LoadSideContainerAsync
  │   └─ 加载 JSON → 渲染到 _sideContainer
  │
  ├─ SideContainerBinder.RegisterSideContainerControls
  │   └─ 注册控件到 ControlRegistry
  │
  └─ SideContainerBinder.BindSideContainerEventsAsync
      │
      ├─ pipeline.RenderSessionOnly(sideContainer, config)
      │   └─ 创建轻量级渲染会话
      │
      └─ pipeline.BindEventsAsync(sideContainer)
          │
          ├─ ExecuteBindEventsStage
          │   └─ defaultBinder.BindEvents(form, config, searchRoot: sideContainer)
          │       └─ EventBindingPipelineRunner.Execute()
          │           └─ ProcessView → ProcessControls
          │               └─ _pipeline.Transform(context)
          │                   ├─ ViewExtractorTransformer
          │                   ├─ ControlListExtractorTransformer
          │                   ├─ WinFormsControlFinderTransformer
          │                   │   └─ 策略1-4 查找控件
          │                   ├─ ScriptModeGuardTransformer
          │                   ├─ BindingMetadataTransformer
          │                   ├─ EventBinderTransformer  ← ★ 修复点
          │                   │   └─ BindControlEvent(control, eventName, handler)
          │                   │       └─ eventInfo.AddEventHandler(control, wrappedHandler)
          │                   │           └─ handler 内部调用 IEventExecutionService.Execute()
          │                   ├─ ControlTreeWalkerTransformer
          │                   └─ BindingStatisticsTransformer
          │
          └─ [用户点击按钮时]
              └─ EventHandler 触发
                  ├─ 日志记录
                  └─ IEventExecutionService.Execute(ctrl.Name, eventName, value)  ← ★ 修复后
                      └─ EventPipelineExecutor.ExecutePipelineInternal
                          └─ 单命令管线 → 执行函数
```
