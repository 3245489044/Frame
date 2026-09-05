# IoC 重构计划 — EventBindingIoC 接口上提 + 实现下沉

> **目标**：将 `EventBindingIoC` 从静态 Service Locator 改造为接口驱动的 IoC 容器，消除对具体类型的直接引用，实现控制反转。
>
> **原则**：接口定义在 Share 层（上层），实现在 BackEndDriven（下层），消费方通过接口编程。

---

## 1. 现状分析

### 1.1 当前问题

`EventBindingIoC.cs` 位于 `CsharpPrj/Share/EventBindingOrchestrator/`，是一个**静态类**，内部直接引用了以下具体类型：

| 引用类型 | 所在位置 | 问题 |
|---------|---------|------|
| `EventBindingDiagnosticContext` | Share.EventBindingOrchestrator.Diagnostics | 静态类引用静态类，紧耦合 |
| `EventNames` | Share.EventBindingOrchestrator.Diagnostics | 常量类，紧耦合 |
| `ReentryGuard` | Modules.Threading | 跨模块直接引用 |
| `ReflectionEventBinderStrategy` | Share.EventBindingOrchestrator.Invoke | 直接 new 实例 |
| `TConfig.EventContext` | Plate.TConfig | 跨层直接引用 |
| `ExecutionResult` | 未明确定义 | 类型引用不清晰 |

### 1.2 当前调用链

```
WinFormsRenderPipeline (注入方)
  ↓ 注册委托到
EventBindingIoC (静态 Service Locator)
  ↓ 被消费方调用
ConfigBindingStage / DeclarationBindingStage / EventBindingPipelineFactory
EventBindingOrchestrator / ParameterBuilder / OutputPipelineRunner
```

### 1.3 当前注入点分布

注入逻辑散落在 `WdUI.RenderPipeline.cs` 的 fallback 路径中（第 129-196 行），与渲染管线逻辑耦合。

---

## 2. 目标架构

```
┌─────────────────────────────────────────────────┐
│  Share/EventBindingOrchestrator/Abstractions/   │
│  IEventBindingIoC (接口)                        │ ← 只定义契约，无实现
├─────────────────────────────────────────────────┤
│  Operation/BackEndDriven/IoC/                   │
│  ├── EventBindingIoC.cs (实现类)                │ ← 实现 IEventBindingIoC
│  ├── EventBindingIoCInitializer.cs (初始化器)    │ ← 集中注册所有委托
│  └── EventBindingIoCConfig.cs (配置模型)         │ ← 配置 DTO
├─────────────────────────────────────────────────┤
│  消费方通过 IEventBindingIoC 接口编程            │ ← 构造函数注入
└─────────────────────────────────────────────────┘
```

### 2.1 分层关系

```
Share 层 (Layer 3)
  └── IEventBindingIoC 接口定义
  └── ConfigBindingStage / DeclarationBindingStage 等消费方
        └── 通过构造函数注入 IEventBindingIoC

Operation 层 (Layer 4)
  └── BackEndDriven
        └── EventBindingIoC : IEventBindingIoC (实现)
        └── EventBindingIoCInitializer (初始化)
```

---

## 3. 接口定义

### 3.1 IEventBindingIoC 接口

```csharp
// Share/EventBindingOrchestrator/Abstractions/IEventBindingIoC.cs
namespace Share.EventBindingOrchestrator.Abstractions;

public interface IEventBindingIoC
{
    // ========== 委托注册 ==========
    Func<object, IReadOnlyList<object>?, Dictionary<string, object>>? ParameterBuilder { get; set; }
    IEventExecutionService? ExecutionService { get; set; }
    IFunctionRegistry? Registry { get; set; }
    IEventBus? EventBus { get; set; }
    Action<object, string, object?>? WriteOutputToControl { get; set; }
    Action<string, string, object?>? OnEventResult { get; set; }
    IEventNameNormalizer Normalizer { get; set; }
    ILogger? Logger { get; set; }

    // ========== 诊断委托（替代对 EventBindingDiagnosticContext 的直接引用）==========
    Action<string, object?>? RecordDiagnostic { get; set; }
    Func<IDisposable>? BeginDiagnosticScope { get; set; }
    Func<object?>? CaptureDiagnosticSnapshot { get; set; }
    Func<object?, IDisposable>? RestoreDiagnosticScope { get; set; }

    // ========== 重入保护委托（替代对 ReentryGuard 的直接引用）==========
    Func<object, bool>? CheckReentry { get; set; }

    // ========== 反射绑定委托（替代对 ReflectionEventBinderStrategy 的直接引用）==========
    Func<object, string, Delegate, bool>? BindEventFallback { get; set; }

    // ========== 异步上下文 ==========
    object? CurrentContext { get; }
    IDisposable SetCurrentContext(object ctx);

    // ========== 待处理任务 ==========
    void TrackPendingTask(object sender, Task task);

    // ========== 事件处理器 ==========
    EventHandler CreateEventHandler(
        object control,
        string eventName,
        string functionName,
        IReadOnlyList<ParameterBinding>? parameterBindings = null,
        string? outputTarget = null,
        string? outputKey = null,
        Action<string, string, object?>? onResult = null);

    bool TryBindEvent(
        IEventBus eventBus,
        object control,
        string normalizedEventName,
        Delegate handler,
        object errors);

    void RecordBoundControl(
        object input,
        object control,
        int boundEventCount,
        string diagnosticKey,
        object? eventsConfig = null,
        string? eventName = null,
        string? functionName = null);

    // ========== 执行结果处理 ==========
    Task HandleExecutionResult(object result);

    // ========== 辅助方法 ==========
    string? GetControlName(object control);
    (string? outputTarget, string? outputKey) ResolveOutput(
        object? bindings, string functionName);

    // ========== 生命周期 ==========
    bool Validate();
    void Reset();
}
```

> **注意**：`EventBindingContext`、`ExecutionResult`、`ParameterBinding`、`EventConfig`、`ErrorCollector` 等类型在接口中统一使用 `object` 替代，避免接口层依赖具体类型。消费方在使用时自行转换。

---

## 4. 实现类设计

### 4.1 EventBindingIoC（BackEndDriven）

```csharp
// Operation/BackEndDriven/IoC/EventBindingIoC.cs
namespace BackEndDriven.IoC;

public class EventBindingIoC : IEventBindingIoC
{
    // 实现 IEventBindingIoC 所有成员
    // 将原静态类中的业务逻辑迁移过来
    // 所有对具体类型的引用改为委托调用
}
```

**关键改造点：**

| 原代码 | 改造后 |
|--------|--------|
| `EventBindingDiagnosticContext.BeginScope()` | `BeginDiagnosticScope?.Invoke()` |
| `EventBindingDiagnosticContext.Record(key, data)` | `RecordDiagnostic?.Invoke(key, data)` |
| `EventBindingDiagnosticContext.CaptureSnapshot()` | `CaptureDiagnosticSnapshot?.Invoke()` |
| `EventBindingDiagnosticContext.RestoreScope(snapshot)` | `RestoreDiagnosticScope?.Invoke(snapshot)` |
| `ReentryGuard.IsReentering(sender)` | `CheckReentry?.Invoke(sender)` |
| `new ReflectionEventBinderStrategy().BindEvent(...)` | `BindEventFallback?.Invoke(control, name, handler)` |
| `EventNames.EventBinderReentrySkip` | 直接使用字符串常量 `"eventbinder.reentry.skip"` |
| `TConfig.EventContext` | 改为 `object` |
| `ExecutionResult` | 改为 `object` |
| `static class` | 改为 `class : IEventBindingIoC` |
| `static AsyncLocal<>` | 改为实例字段 |
| `static ConditionalWeakTable<>` | 改为实例字段 |

### 4.2 EventBindingIoCInitializer（BackEndDriven）

```csharp
// Operation/BackEndDriven/IoC/EventBindingIoCInitializer.cs
namespace BackEndDriven.IoC;

public static class EventBindingIoCInitializer
{
    /// <summary>
    /// 初始化 EventBindingIoC 实例，注册所有委托
    /// </summary>
    public static EventBindingIoC Initialize(
        IEventBus eventBus,
        IFunctionRegistry registry,
        ILogger? logger = null)
    {
        var ioc = new EventBindingIoC();

        // 注册核心委托
        ioc.EventBus = eventBus;
        ioc.Registry = registry;
        ioc.ParameterBuilder = (sender, bindings) =>
            new WorkFlowMethod.Parameters.ParameterBuilder().Build(
                sender, bindings as List<TConfig.ParameterBinding>);

        // 注册诊断委托
        ioc.RecordDiagnostic = (key, data) =>
            EventBindingDiagnosticContext.Record(key, data);
        ioc.BeginDiagnosticScope = () =>
            EventBindingDiagnosticContext.BeginScope();
        ioc.CaptureDiagnosticSnapshot = () =>
            EventBindingDiagnosticContext.CaptureSnapshot();
        ioc.RestoreDiagnosticScope = (s) =>
            EventBindingDiagnosticContext.RestoreScope(s);

        // 注册重入保护委托
        ioc.CheckReentry = (sender) =>
            ReentryGuard.IsReentering(sender);

        // 注册反射绑定委托
        ioc.BindEventFallback = (control, name, handler) =>
            new ReflectionEventBinderStrategy().BindEvent(control, name, handler);

        // 注册输出委托
        ioc.WriteOutputToControl = (sender, outputTarget, value) =>
        {
            // ... 从 WinFormsRenderPipeline 迁移过来的逻辑
        };

        ioc.Logger = logger;
        return ioc;
    }
}
```

### 4.3 EventBindingIoCConfig（BackEndDriven）

```csharp
// Operation/BackEndDriven/IoC/EventBindingIoCConfig.cs
namespace BackEndDriven.IoC;

public class RegistrationConfig { ... }
public class RegistrationEntry { ... }
public class ParameterConfig { ... }
```

---

## 5. 消费方改造

### 5.1 Share 层消费方

所有 Share 层的消费方改为通过构造函数注入 `IEventBindingIoC`：

```csharp
// ConfigBindingStage.cs — 改造前
using Share.EventBindingOrchestrator;
// ...
var handler = EventBindingIoC.CreateEventHandler(control, eventName, functionName, ...);

// ConfigBindingStage.cs — 改造后
using Share.EventBindingOrchestrator.Abstractions;
// ...
public class ConfigBindingStage
{
    private readonly IEventBindingIoC _ioc;
    public ConfigBindingStage(IEventBindingIoC ioc) { _ioc = ioc; }
    // ...
    var handler = _ioc.CreateEventHandler(control, eventName, functionName, ...);
}
```

### 5.2 Operation 层消费方

```csharp
// ParameterBuilder.cs — 改造前
var eventCtx = EventBindingIoC.CurrentContext;

// ParameterBuilder.cs — 改造后
public class ParameterBuilder
{
    private readonly IEventBindingIoC _ioc;
    public ParameterBuilder(IEventBindingIoC ioc) { _ioc = ioc; }
    // ...
    var eventCtx = _ioc.CurrentContext;
}
```

---

## 6. 文件操作清单

| # | 操作 | 文件路径 | 说明 |
|---|------|---------|------|
| 1 | **新建** | `Share/EventBindingOrchestrator/Abstractions/IEventBindingIoC.cs` | 接口定义 |
| 2 | **新建** | `Operation/BackEndDriven/IoC/EventBindingIoC.cs` | 接口实现 |
| 3 | **新建** | `Operation/BackEndDriven/IoC/EventBindingIoCInitializer.cs` | 初始化器 |
| 4 | **新建** | `Operation/BackEndDriven/IoC/EventBindingIoCConfig.cs` | 配置模型 |
| 5 | **删除** | `Share/EventBindingOrchestrator/EventBindingIoC.cs` | 原静态类 |
| 6 | **修改** | `Operation/WdUI/WdUI.RenderPipeline.cs` | 删除第 129-196 行注册逻辑 |
| 7 | **修改** | `Share/EventBindingOrchestrator/Invoke/ConfigBindingStage.cs` | 改为接口注入 |
| 8 | **修改** | `Share/EventBindingOrchestrator/Invoke/DeclarationBindingStage.cs` | 改为接口注入 |
| 9 | **修改** | `Share/EventBindingOrchestrator/Transformers/EventBindingPipelineFactory.cs` | 改为接口注入 |
| 10 | **修改** | `Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` | 改为接口注入 |
| 11 | **修改** | `Operation/WorkFlowMethod/Parameters/ParameterBuilder.cs` | 改为接口注入 |
| 12 | **修改** | `Operation/WorkFlowMethod/Output/OutputPipelineRunner.cs` | 改为接口注入 |
| 13 | **修改** | `Operation/BackEndDriven/BackEndDriven.csproj` | 添加对 Share.EventBindingOrchestrator 的引用（如需要） |

---

## 7. 执行顺序

```
Step 1: 新建 IEventBindingIoC 接口（Share 层）
Step 2: 新建 EventBindingIoC 实现类（BackEndDriven）
Step 3: 新建 EventBindingIoCInitializer（BackEndDriven）
Step 4: 新建 EventBindingIoCConfig（BackEndDriven）
Step 5: 删除原 EventBindingIoC.cs（Share 层）
Step 6: 修改 WinFormsRenderPipeline.cs（删除注册逻辑）
Step 7~12: 逐个修改 6 个消费方文件
Step 13: 更新 .csproj 引用
Step 14: 编译验证
```

---

## 8. 风险与注意事项

1. **Share 层引用 Operation 层**：Share 层的消费方通过 `IEventBindingIoC` 接口编程，不直接引用 BackEndDriven。接口在 Share 层定义，实现由上层注入，符合依赖反转原则。
2. **AsyncLocal 改为实例字段**：原静态 AsyncLocal 改为实例字段后，需要确保 EventBindingIoC 实例在应用生命周期内是单例的。
3. **ConditionalWeakTable 改为实例字段**：同上，需要单例保证。
4. **字符串常量替代 EventNames**：EventNames 中的常量直接替换为字符串字面量，EventNames.cs 文件保留不动（其他文件可能还在使用）。
5. **BackEndDriven.csproj 引用**：需要添加对 `Share.EventBindingOrchestrator.csproj` 的 ProjectReference，因为 EventBindingIoC 实现类需要引用接口所在的程序集。
