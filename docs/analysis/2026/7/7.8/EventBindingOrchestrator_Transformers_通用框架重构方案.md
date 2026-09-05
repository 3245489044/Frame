# EventBindingOrchestrator.Transformers 通用框架重构方案

> **分析日期**：2026-07-07
> **分析范围**：`CsharpPrj/Share/EventBindingOrchestrator/Transformers/`
> **目标**：将当前以 WinForms 为中心的 Transformers 管线重构为 UI 技术无关的通用事件绑定框架
> **前置文档**：[EventTriggerFramework → EventBindingOrchestrator 迁移方案](./EventTriggerFramework_EventBindingOrchestrator_迁移方案.md)

---

## 目录

1. [现状分析](#1-现状分析)
2. [通用框架设计目标](#2-通用框架设计目标)
3. [核心抽象层设计](#3-核心抽象层设计)
4. [重构步骤（5 阶段）](#4-重构步骤5-阶段)
5. [阶段一：定义通用抽象接口](#5-阶段一定义通用抽象接口)
6. [阶段二：实现平台适配层（WinFormsAdapter）](#6-阶段二实现平台适配层winformsadapter)
7. [阶段三：重构 Transformer 管线](#7-阶段三重构-transformer-管线)
8. [阶段四：保留兼容层并迁移调用方](#8-阶段四保留兼容层并迁移调用方)
9. [阶段五：清理废弃代码](#9-阶段五清理废弃代码)
10. [文件变更清单](#10-文件变更清单)
11. [影响分析与回退策略](#11-影响分析与回退策略)
12. [附录：新旧接口映射表](#12-附录新旧接口映射表)

---

## 1. 现状分析

### 1.1 当前架构

```
Transformers/
├── EventBinderTransformer.cs          # 808 行 — 事件绑定核心（过于庞大）
├── BindingMetadataTransformer.cs      # 70 行 — 绑定元数据提取
├── ControlFinderTransformer.cs        # 164 行 — 控件查找（含反射回退）
├── ControlTreeWalkerTransformer.cs    # 43 行 — 控件树遍历
├── ScriptModeGuardTransformer.cs      # 49 行 — 脚本模式守卫
├── EventBindingHelper.cs              # 93 行 — 静态辅助方法
├── EventBindingPipelineFactory.cs     # 55 行 — 管线工厂
└── EventBindingPipelineRunner.cs      # 152 行 — 管线执行器
```

### 1.2 核心问题

| # | 问题 | 位置 | 影响 |
|---|------|------|------|
| 1 | **EventBinderTransformer 职责过多**（808行） | 事件创建、参数构建、输出写入、ListView处理、跨线程封送 | 难以维护、扩展 |
| 2 | **接口命名带有 WinForms 痕迹** | `IWinFormsControlStrategy`、`IWinFormsEventBinder` | 非 WinForms 场景语义不匹配 |
| 3 | **反射回退路径与策略接口并存** | `FindParentFormReflection` / `FindControlByNameReflection` / `GetControlValueReflection` | 两套平行实现，增加维护成本 |
| 4 | **ListView 硬编码** | `WriteToListView` + `ParseListViewItems` 共 100+ 行 | 无法扩展支持 DataGridView 等其他控件 |
| 5 | **跨线程封送通过反射** | `InvokeRequired` / `BeginInvoke` 反射检测 | 性能差、脆弱、仅适用于 WinForms |
| 6 | **超时硬编码** | `TimeSpan.FromSeconds(10)` | 不可配置 |
| 7 | **诊断记录散布** | `EventBindingDiagnosticContext.Record` 散布在业务逻辑中 | 横切关注点未分离 |
| 8 | **管线上下文耦合具体模型** | `EventBindingContext` 直接引用 `UIConfiguration`、`ControlConfig` | 无法用于非 JSON 配置场景 |

### 1.3 依赖关系

```
EventBinderTransformer
├── IWinFormsControlStrategy?     ← WinForms 策略接口（可选）
├── IWinFormsEventBinder?         ← WinForms 事件绑定器（可选）
├── IEventExecutionService?       ← 函数执行服务（可选）
├── EventBindingDiagnosticContext ← 诊断记录（静态类）
├── ReentryGuard                  ← 重入守卫（静态类）
├── EventTypeMapperService        ← 事件类型映射（静态类）
├── EventBindingHelper            ← 辅助方法（静态类）
└── System.Reflection             ← 反射回退路径
```

---

## 2. 通用框架设计目标

### 2.1 核心原则

```
UI 技术无关  →  不假设 WinForms/WPF/Avalonia/Web
事件模型无关  →  不假设 .NET EventHandler 委托
控件模型无关  →  不假设 Control/Form 层级结构
值模型无关    →  不假设 Text 属性
执行引擎无关  →  不假设 IEventExecutionService
配置模型无关  →  不假设 UIConfiguration/ControlConfig
```

### 2.2 目标架构

```
┌──────────────────────────────────────────────────────────────────┐
│                     EventBindingFramework                        │
│                    (通用事件绑定框架)                              │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                  核心抽象层 (Abstractions/)                │   │
│  │                                                          │   │
│  │  IControlTree     IEventBus     IValueAccessor           │   │
│  │  IControlNode     IEventSubscription  IValueConverter    │   │
│  │  IFunctionExecutor  IEventBindingContext                 │   │
│  └──────────────────────────┬───────────────────────────────┘   │
│                             │                                    │
│  ┌──────────────────────────┴───────────────────────────────┐   │
│  │                  管线层 (Transformers/)                   │   │
│  │                                                          │   │
│  │  ControlFinderTransformer  (通用: IControlTree.Find)     │   │
│  │  BindingMetadataTransformer (通用: IEventBindingContext)  │   │
│  │  EventHandlerFactoryTransformer (通用: IEventBus)        │   │
│  │  ParameterBuilderTransformer   (通用: IValueAccessor)    │   │
│  │  ControlValueWriterTransformer (通用: IValueAccessor)    │   │
│  │  ScriptModeGuardTransformer    (通用: 无变化)            │   │
│  │  ControlTreeWalkerTransformer  (通用: 无变化)            │   │
│  │  DiagnosticTransformer         (通用: 装饰器)            │   │
│  └──────────────────────────┬───────────────────────────────┘   │
│                             │                                    │
│  ┌──────────────────────────┴───────────────────────────────┐   │
│  │                  平台适配层 (Adapters/)                   │   │
│  │                                                          │   │
│  │  WinFormsAdapter  │  WpfAdapter  │  WebAdapter  │ ...   │   │
│  │  (WinFormsControl  │  (WpfControl  │  (JsControl         │   │
│  │   Tree / EventBus  │   Tree /      │   Tree / EventBus)  │   │
│  │   / ValueAccessor) │   EventBus)   │                     │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

---

## 3. 核心抽象层设计

### 3.1 控件树抽象

```csharp
/// <summary>
/// 控件节点 — 通用控件表示
/// 不依赖任何具体 UI 技术，通过 NativeControl 属性暴露平台原生引用。
/// </summary>
public interface IControlNode
{
    /// <summary>控件名称</summary>
    string Name { get; }

    /// <summary>平台原生控件引用（WinForms 下为 Control，WPF 下为 DependencyObject）</summary>
    object? NativeControl { get; }

    /// <summary>父节点</summary>
    IControlNode? Parent { get; }

    /// <summary>子节点集合</summary>
    IReadOnlyList<IControlNode> Children { get; }

    /// <summary>安全向下转型</summary>
    T? As<T>() where T : class;
}

/// <summary>
/// 控件树 — 通用控件层级结构
/// 封装控件的查找和遍历逻辑，不依赖具体 UI 框架。
/// </summary>
public interface IControlTree
{
    /// <summary>根节点</summary>
    IControlNode? Root { get; }

    /// <summary>按名称查找控件</summary>
    IControlNode? Find(string name);

    /// <summary>按条件查找控件</summary>
    IEnumerable<IControlNode> FindAll(Func<IControlNode, bool> predicate);

    /// <summary>遍历所有控件</summary>
    void Walk(Action<IControlNode> action);
}
```

### 3.2 事件模型抽象

```csharp
/// <summary>
/// 事件上下文 — 事件触发时的上下文信息
/// </summary>
public class EventContext
{
    /// <summary>事件名称</summary>
    public string EventName { get; init; } = "";

    /// <summary>事件源控件</summary>
    public IControlNode? Source { get; init; }

    /// <summary>事件参数（平台原生 EventArgs）</summary>
    public object? EventArgs { get; init; }

    /// <summary>自定义数据</summary>
    public Dictionary<string, object> Data { get; } = new();
}

/// <summary>
/// 事件订阅令牌 — 取消订阅时释放
/// </summary>
public interface IEventSubscription : IDisposable
{
    /// <summary>是否已释放</summary>
    bool IsDisposed { get; }
}

/// <summary>
/// 事件总线 — 通用事件发布/订阅
/// 不依赖 .NET EventHandler 委托模型，支持异步事件处理。
/// </summary>
public interface IEventBus
{
    /// <summary>订阅事件</summary>
    IEventSubscription Subscribe(string eventName, Func<EventContext, Task> handler);

    /// <summary>发布事件</summary>
    Task PublishAsync(string eventName, EventContext context);

    /// <summary>取消所有订阅</summary>
    void Clear();
}
```

### 3.3 值访问抽象

```csharp
/// <summary>
/// 值访问器 — 通用控件值读写
/// 不依赖 Text 属性，支持任意属性/状态读写。
/// </summary>
public interface IValueAccessor
{
    /// <summary>是否支持读取</summary>
    bool CanRead(IControlNode control);

    /// <summary>是否支持写入</summary>
    bool CanWrite(IControlNode control);

    /// <summary>读取控件值</summary>
    object? GetValue(IControlNode control);

    /// <summary>写入控件值</summary>
    void SetValue(IControlNode control, object? value);
}

/// <summary>
/// 值转换器 — 值格式转换
/// 用于将函数执行结果转换为控件可接受的值格式。
/// </summary>
public interface IValueConverter
{
    /// <summary>是否支持该类型转换</summary>
    bool CanConvert(Type sourceType, Type targetType);

    /// <summary>执行转换</summary>
    object? Convert(object? value, Type targetType);
}
```

### 3.4 函数执行抽象

```csharp
/// <summary>
/// 函数执行器 — 通用函数执行
/// 不依赖 IEventExecutionService，支持任意函数执行引擎。
/// </summary>
public interface IFunctionExecutor
{
    /// <summary>执行函数（命名参数）</summary>
    Task<ExecutionResult> ExecuteAsync(string functionName, IReadOnlyDictionary<string, object> parameters);

    /// <summary>检查函数是否存在</summary>
    bool HasFunction(string functionName);
}
```

### 3.5 绑定声明模型

```csharp
/// <summary>
/// 事件绑定声明 — 声明式事件绑定配置
/// 不依赖 UIConfiguration/ControlConfig，使用纯模型。
/// </summary>
public class EventBindingDeclaration
{
    /// <summary>控件名称</summary>
    public string ControlName { get; init; } = "";

    /// <summary>事件名称</summary>
    public string EventName { get; init; } = "";

    /// <summary>绑定的函数名</summary>
    public string FunctionName { get; init; } = "";

    /// <summary>参数映射列表</summary>
    public IReadOnlyList<ParameterMapping> Parameters { get; init; } = Array.Empty<ParameterMapping>();

    /// <summary>输出目标控件名</summary>
    public string? OutputTarget { get; init; }

    /// <summary>输出映射（函数名 → 目标控件名）</summary>
    public IReadOnlyDictionary<string, string>? OutputMapping { get; init; }
}

/// <summary>
/// 参数映射 — 声明函数参数从何处获取
/// </summary>
public class ParameterMapping
{
    /// <summary>函数形参名</summary>
    public string Target { get; init; } = "";

    /// <summary>源类型：control | literal | expression</summary>
    public string SourceType { get; init; } = "";

    /// <summary>源值：控件名 / 字面量 / 表达式</summary>
    public string Source { get; init; } = "";
}
```

### 3.6 通用管线上下文

```csharp
/// <summary>
/// 事件绑定上下文 — 通用管线上下文
/// 承载整个绑定过程的状态，不依赖任何具体 UI 技术或配置模型。
/// </summary>
public interface IEventBindingContext
{
    /// <summary>控件树</summary>
    IControlTree ControlTree { get; }

    /// <summary>事件总线</summary>
    IEventBus EventBus { get; }

    /// <summary>值访问器</summary>
    IValueAccessor ValueAccessor { get; }

    /// <summary>函数执行器</summary>
    IFunctionExecutor FunctionExecutor { get; }

    /// <summary>绑定声明列表</summary>
    IReadOnlyList<EventBindingDeclaration> Declarations { get; }

    /// <summary>当前正在处理的绑定声明</summary>
    EventBindingDeclaration? CurrentDeclaration { get; set; }

    /// <summary>当前找到的控件节点</summary>
    IControlNode? CurrentControl { get; set; }

    /// <summary>当前控件的子控件列表</summary>
    IReadOnlyList<IControlNode>? CurrentChildren { get; set; }

    /// <summary>是否跳过当前控件</summary>
    bool SkipCurrentControl { get; set; }

    /// <summary>运行时状态存储</summary>
    Dictionary<string, object> State { get; }

    /// <summary>重置当前控件状态</summary>
    void ResetCurrent();
}
```

### 3.7 框架配置

```csharp
/// <summary>
/// 事件绑定框架选项
/// </summary>
public class EventBindingFrameworkOptions
{
    /// <summary>函数执行超时（毫秒），默认 10000</summary>
    public int FunctionTimeoutMs { get; set; } = 10000;

    /// <summary>是否启用重入守卫，默认 true</summary>
    public bool EnableReentryGuard { get; set; } = true;

    /// <summary>是否启用诊断记录，默认 true</summary>
    public bool EnableDiagnostics { get; set; } = true;

    /// <summary>是否启用跨线程调度，默认 true</summary>
    public bool EnableCrossThreadDispatching { get; set; } = true;

    /// <summary>函数执行失败最大重试次数，默认 0</summary>
    public int MaxRetryCount { get; set; } = 0;

    /// <summary>重试间隔（毫秒），默认 200</summary>
    public int RetryDelayMs { get; set; } = 200;
}
```

---

## 4. 重构步骤（5 阶段）

```
阶段一：定义通用抽象接口
  ├── 新建 Abstractions/IControlNode.cs
  ├── 新建 Abstractions/IControlTree.cs
  ├── 新建 Abstractions/IEventBus.cs
  ├── 新建 Abstractions/IValueAccessor.cs
  ├── 新建 Abstractions/IValueConverter.cs
  ├── 新建 Abstractions/IFunctionExecutor.cs
  ├── 新建 Models/EventBindingDeclaration.cs
  ├── 新建 Models/ParameterMapping.cs
  ├── 新建 Models/EventContext.cs
  ├── 新建 Models/EventBindingFrameworkOptions.cs
  └── 新建 Models/IEventBindingContext.cs

阶段二：实现平台适配层（WinFormsAdapter）
  ├── 新建 Adapters/WinFormsControlNode.cs
  ├── 新建 Adapters/WinFormsControlTree.cs
  ├── 新建 Adapters/WinFormsEventBus.cs
  ├── 新建 Adapters/WinFormsValueAccessor.cs
  ├── 新建 Adapters/WinFormsValueConverter.cs
  └── 新建 Adapters/WinFormsThreadDispatcher.cs

阶段三：重构 Transformer 管线
  ├── 拆分 EventBinderTransformer → 3 个子 Transformer
  ├── 重构现有 Transformer 使用新接口
  ├── 新建 DiagnosticTransformer（装饰器）
  └── 更新 EventBindingPipelineFactory

阶段四：保留兼容层并迁移调用方
  ├── 保留旧接口作为 deprecated 包装
  ├── 迁移 EventBindingPipelineRunner
  ├── 迁移 ContainerBinderPipeline
  └── 迁移外部调用方

阶段五：清理废弃代码
  ├── 删除反射回退方法
  ├── 删除旧接口
  └── 更新 .csproj 依赖
```

---

## 5. 阶段一：定义通用抽象接口

### 5.1 新建文件清单

| 文件路径 | 内容 | 行数预估 |
|---------|------|---------|
| `Abstractions/IControlNode.cs` | 控件节点接口 | ~30 |
| `Abstractions/IControlTree.cs` | 控件树接口 | ~25 |
| `Abstractions/IEventBus.cs` | 事件总线接口 + EventContext | ~50 |
| `Abstractions/IValueAccessor.cs` | 值访问器接口 | ~25 |
| `Abstractions/IValueConverter.cs` | 值转换器接口 | ~15 |
| `Abstractions/IFunctionExecutor.cs` | 函数执行器接口 | ~20 |
| `Models/EventBindingDeclaration.cs` | 绑定声明模型 | ~50 |
| `Models/ParameterMapping.cs` | 参数映射模型 | ~20 |
| `Models/EventContext.cs` | 事件上下文 | ~25 |
| `Models/EventBindingFrameworkOptions.cs` | 框架选项 | ~30 |
| `Models/IEventBindingContext.cs` | 通用管线上下文接口 | ~50 |

### 5.2 接口设计原则

1. **最小接口** — 每个接口只暴露必要方法，避免胖接口
2. **平台无关** — 参数和返回值使用通用类型（string, object, Task）
3. **可空性** — 使用 nullable 注解，明确表示可能为 null
4. **异步优先** — 事件处理和函数执行使用 async Task
5. **不可变模型** — 绑定声明使用 init-only 属性

### 5.3 与现有接口的关系

```
现有接口                          新接口
──────────────────────────────────────────────────
IWinFormsControlStrategy  ──→   IValueAccessor
                                   + IControlTree
                                   + WinFormsThreadDispatcher

IWinFormsEventBinder      ──→   IEventBus

IEventExecutionService    ──→   IFunctionExecutor

IEventBindingContext      ──→   IEventBindingContext (重写)

EventBindingContext       ──→   EventBindingContext (实现新接口)
```

---

## 6. 阶段二：实现平台适配层（WinFormsAdapter）

### 6.1 新建文件清单

| 文件路径 | 内容 | 行数预估 |
|---------|------|---------|
| `Adapters/WinFormsControlNode.cs` | WinForms 控件节点包装 | ~60 |
| `Adapters/WinFormsControlTree.cs` | WinForms 控件树（包装 Form） | ~80 |
| `Adapters/WinFormsEventBus.cs` | WinForms 事件总线（反射绑定） | ~120 |
| `Adapters/WinFormsValueAccessor.cs` | WinForms 值读写器 | ~50 |
| `Adapters/WinFormsValueConverter.cs` | WinForms 值转换器（含 ListView） | ~80 |
| `Adapters/WinFormsThreadDispatcher.cs` | WinForms UI 线程调度器 | ~40 |

### 6.2 WinFormsControlNode 设计

```csharp
public class WinFormsControlNode : IControlNode
{
    private readonly Control _control;
    private readonly Lazy<IReadOnlyList<IControlNode>> _children;

    public WinFormsControlNode(Control control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _children = new Lazy<IReadOnlyList<IControlNode>>(InitializeChildren);
    }

    public string Name => _control.Name ?? _control.GetType().Name;
    public object? NativeControl => _control;
    public IControlNode? Parent => _control.Parent != null ? new WinFormsControlNode(_control.Parent) : null;
    public IReadOnlyList<IControlNode> Children => _children.Value;

    public T? As<T>() where T : class => _control as T;

    private IReadOnlyList<IControlNode> InitializeChildren()
    {
        return _control.Controls
            .Cast<Control>()
            .Select(c => new WinFormsControlNode(c))
            .ToList();
    }
}
```

### 6.3 WinFormsEventBus 设计

```csharp
public class WinFormsEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<EventContext, Task>>> _handlers = new();
    private readonly IWinFormsEventBinder? _binder;  // 复用现有反射绑定器

    public WinFormsEventBus(IWinFormsEventBinder? binder = null)
    {
        _binder = binder;
    }

    public IEventSubscription Subscribe(string eventName, Func<EventContext, Task> handler)
    {
        var handlers = _handlers.GetOrAdd(eventName, _ => new List<Func<EventContext, Task>>());
        lock (handlers) { handlers.Add(handler); }
        return new EventSubscription(() => Unsubscribe(eventName, handler));
    }

    public async Task PublishAsync(string eventName, EventContext context)
    {
        if (_handlers.TryGetValue(eventName, out var handlers))
        {
            List<Func<EventContext, Task>> snapshot;
            lock (handlers) { snapshot = handlers.ToList(); }
            foreach (var handler in snapshot)
            {
                await handler(context);
            }
        }
    }

    public void Clear() => _handlers.Clear();
}
```

### 6.4 WinFormsValueAccessor 设计

```csharp
public class WinFormsValueAccessor : IValueAccessor
{
    public bool CanRead(IControlNode control)
    {
        var native = control.As<Control>();
        return native != null && native.GetType().GetProperty("Text") != null;
    }

    public bool CanWrite(IControlNode control)
    {
        var native = control.As<Control>();
        return native != null && native.GetType().GetProperty("Text")?.CanWrite == true;
    }

    public object? GetValue(IControlNode control)
    {
        var native = control.As<Control>();
        return native?.Text;
    }

    public void SetValue(IControlNode control, object? value)
    {
        var native = control.As<Control>();
        if (native == null) return;

        using (ReentryGuard.Enter(native))
        {
            if (native.InvokeRequired)
            {
                native.BeginInvoke(new Action(() => SetValue(control, value)));
                return;
            }
            native.Text = value?.ToString() ?? "";
        }
    }
}
```

---

## 7. 阶段三：重构 Transformer 管线

### 7.1 拆分 EventBinderTransformer

```
EventBinderTransformer (808行)
    ↓
├── EventHandlerFactoryTransformer  (~150行)  — 创建事件处理器委托
│   ├── 依赖: IEventBus, IFunctionExecutor, IValueAccessor
│   ├── 职责: 订阅事件 → 构建参数 → 执行函数 → 写回结果
│   └── 提取自: CreateEventHandler()
│
├── ParameterBuilderTransformer     (~100行)  — 构建参数字典
│   ├── 依赖: IValueAccessor, IControlTree
│   ├── 职责: 从控件/字面量/表达式解析参数值
│   └── 提取自: BuildParametersFromBindings()
│
└── ControlValueWriterTransformer   (~120行)  — 写回控件值
    ├── 依赖: IValueAccessor, IValueConverter
    ├── 职责: 将函数结果写回目标控件（含类型转换）
    └── 提取自: WriteOutputToControl() + WriteToListView()
```

### 7.2 EventHandlerFactoryTransformer 核心逻辑

```csharp
public class EventHandlerFactoryTransformer : ITransformer<IEventBindingContext, IEventBindingContext>
{
    private readonly EventBindingFrameworkOptions _options;

    public string Name => "EventHandlerFactory";

    public IEventBindingContext Transform(IEventBindingContext input)
    {
        if (input.SkipCurrentControl || input.CurrentDeclaration == null)
            return input;

        var declaration = input.CurrentDeclaration;
        var control = input.CurrentControl;
        if (control == null) return input;

        // 通过事件总线订阅事件
        input.EventBus.Subscribe(declaration.EventName, async ctx =>
        {
            // 1. 检查重入
            if (_options.EnableReentryGuard && IsReentering(ctx))
                return;

            // 2. 构建参数
            var parameters = await BuildParametersAsync(input, declaration);

            // 3. 执行函数
            var result = await ExecuteFunctionWithTimeoutAsync(
                input.FunctionExecutor, declaration.FunctionName, parameters);

            // 4. 写回结果
            if (result.Success && declaration.OutputTarget != null)
            {
                var target = input.ControlTree.Find(declaration.OutputTarget);
                if (target != null)
                    input.ValueAccessor.SetValue(target, result.Value);
            }
        });

        return input;
    }
}
```

### 7.3 新增 DiagnosticTransformer（装饰器）

```csharp
/// <summary>
/// 诊断记录装饰器 — 将诊断记录从业务逻辑中分离
/// 包装任意 ITransformer，自动记录进入/退出/异常。
/// </summary>
public class DiagnosticTransformer : ITransformer<IEventBindingContext, IEventBindingContext>
{
    private readonly ITransformer<IEventBindingContext, IEventBindingContext> _inner;

    public DiagnosticTransformer(ITransformer<IEventBindingContext, IEventBindingContext> inner)
    {
        _inner = inner;
    }

    public string Name => $"Diagnostic({_inner.Name})";

    public IEventBindingContext Transform(IEventBindingContext input)
    {
        EventBindingDiagnosticContext.Record($"transformer.{_inner.Name}.enter", new
        {
            controlName = input.CurrentControl?.Name,
            declaration = input.CurrentDeclaration?.EventName
        });

        try
        {
            var result = _inner.Transform(input);

            EventBindingDiagnosticContext.Record($"transformer.{_inner.Name}.exit", new
            {
                skipped = result.SkipCurrentControl
            });

            return result;
        }
        catch (Exception ex)
        {
            EventBindingDiagnosticContext.Record($"transformer.{_inner.Name}.error", new
            {
                error = ex.Message
            });
            throw;
        }
    }
}
```

### 7.4 更新 EventBindingPipelineFactory

```csharp
public static class EventBindingPipelineFactory
{
    /// <summary>创建单控件绑定管线（通用版本）</summary>
    public static ITransformer<IEventBindingContext, IEventBindingContext> CreateBindingPipeline(
        EventBindingFrameworkOptions? options = null)
    {
        options ??= new EventBindingFrameworkOptions();

        var pipeline = new ScriptModeGuardTransformer()
            .Then(new BindingMetadataTransformer())
            .Then(new EventHandlerFactoryTransformer(options))
            .Then(new ParameterBuilderTransformer())
            .Then(new ControlValueWriterTransformer());

        return options.EnableDiagnostics
            ? new DiagnosticTransformer(pipeline)
            : pipeline;
    }

    /// <summary>创建完整绑定管线（含控件遍历）</summary>
    public static ITransformer<IEventBindingContext, IEventBindingContext> CreateFullBindingPipeline(
        EventBindingFrameworkOptions? options = null)
    {
        return new ControlFinderTransformer()
            .Then(CreateBindingPipeline(options))
            .Then(new ControlTreeWalkerTransformer());
    }

    /// <summary>
    /// 创建兼容管线（使用旧接口，内部适配到新接口）
    /// 用于逐步迁移期间保持向后兼容
    /// </summary>
    [Obsolete("请迁移到 CreateBindingPipeline(EventBindingFrameworkOptions)")]
    public static ITransformer<EventBindingContext, EventBindingContext> CreateLegacyBindingPipeline(
        IWinFormsControlStrategy? winFormsStrategy = null)
    {
        // 内部使用适配器将旧接口映射到新接口
        var adapter = new WinFormsContextAdapter(winFormsStrategy);
        var pipeline = CreateBindingPipeline();
        return new LegacyPipelineAdapter(pipeline, adapter);
    }
}
```

---

## 8. 阶段四：保留兼容层并迁移调用方

### 8.1 兼容层策略

1. **旧接口标记 `[Obsolete]`** — 添加警告信息，提示迁移到新接口
2. **内部适配器** — 旧接口实现委托给新接口
3. **逐步迁移** — 每个调用方独立迁移，不阻塞其他模块

### 8.2 迁移顺序

```
Step 1: EventBindingPipelineRunner
  └── 改为使用 IEventBindingContext 和 EventBindingFrameworkOptions

Step 2: ContainerBinderPipeline
  └── 阶段 2a/2b 改为使用新管线

Step 3: EventBindingOrchestrator
  └── 改为使用新管线工厂

Step 4: 外部调用方（WdUI、BackEndDriven 等）
  └── 逐个迁移到新接口
```

### 8.3 兼容适配器示例

```csharp
[Obsolete("请迁移到 IEventBindingContext 和 EventBindingFrameworkOptions")]
public class LegacyPipelineAdapter : ITransformer<EventBindingContext, EventBindingContext>
{
    private readonly ITransformer<IEventBindingContext, IEventBindingContext> _inner;
    private readonly WinFormsContextAdapter _adapter;

    public string Name => "LegacyAdapter";

    public EventBindingContext Transform(EventBindingContext input)
    {
        var newContext = _adapter.ToNew(input);
        var result = _inner.Transform(newContext);
        _adapter.CopyBack(result, input);
        return input;
    }
}
```

---

## 9. 阶段五：清理废弃代码

### 9.1 删除清单

| 文件/方法 | 删除条件 | 替代 |
|----------|---------|------|
| `EventBinderTransformer.cs` 中的反射回退方法 | WinFormsAdapter 完成 | `WinFormsValueAccessor` |
| `ControlFinderTransformer.cs` 中的反射缓存 | WinFormsAdapter 完成 | `WinFormsControlTree` |
| `IWinFormsControlStrategy` 接口 | 所有调用方迁移 | `IValueAccessor` + `IControlTree` |
| `IWinFormsEventBinder` 接口 | 所有调用方迁移 | `IEventBus` |
| `EventBindingHelper.cs` 中的部分方法 | 功能迁移完成 | 新接口方法 |

### 9.2 清理顺序

```
1. 删除反射回退方法（内部使用，无外部依赖）
2. 删除旧接口（需确认所有调用方已迁移）
3. 删除兼容适配器（所有调用方迁移完成后）
4. 更新 .csproj（移除不再需要的依赖）
```

---

## 10. 文件变更清单

### 10.1 新增文件（18 个）

```
CsharpPrj/Share/EventBindingOrchestrator/
├── Abstractions/
│   ├── IControlNode.cs              # 控件节点接口
│   ├── IControlTree.cs              # 控件树接口
│   ├── IEventBus.cs                 # 事件总线接口
│   ├── IValueAccessor.cs            # 值访问器接口
│   ├── IValueConverter.cs           # 值转换器接口
│   └── IFunctionExecutor.cs         # 函数执行器接口
├── Models/
│   ├── EventBindingDeclaration.cs   # 绑定声明模型
│   ├── ParameterMapping.cs          # 参数映射模型
│   ├── EventContext.cs              # 事件上下文
│   ├── EventBindingFrameworkOptions.cs  # 框架选项
│   └── IEventBindingContext.cs      # 通用管线上下文接口
├── Adapters/
│   ├── WinFormsControlNode.cs       # WinForms 控件节点
│   ├── WinFormsControlTree.cs       # WinForms 控件树
│   ├── WinFormsEventBus.cs          # WinForms 事件总线
│   ├── WinFormsValueAccessor.cs     # WinForms 值读写器
│   ├── WinFormsValueConverter.cs    # WinForms 值转换器
│   └── WinFormsThreadDispatcher.cs  # WinForms UI 线程调度器
└── Transformers/
    └── DiagnosticTransformer.cs     # 诊断记录装饰器
```

### 10.2 修改文件（8 个）

```
CsharpPrj/Share/EventBindingOrchestrator/
├── Transformers/
│   ├── EventBinderTransformer.cs        # 拆分为 3 个子 Transformer
│   ├── EventHandlerFactoryTransformer.cs # 新增（从 EventBinderTransformer 拆分）
│   ├── ParameterBuilderTransformer.cs    # 新增（从 EventBinderTransformer 拆分）
│   ├── ControlValueWriterTransformer.cs  # 新增（从 EventBinderTransformer 拆分）
│   ├── ControlFinderTransformer.cs       # 使用 IControlTree 替代反射
│   ├── EventBindingPipelineFactory.cs    # 支持新接口 + 兼容层
│   └── EventBindingPipelineRunner.cs     # 支持新接口
└── Models/
    └── EventBindingContext.cs            # 实现 IEventBindingContext
```

### 10.3 删除文件（阶段五执行）

```
CsharpPrj/Share/EventBindingOrchestrator/
├── Abstractions/
│   ├── IWinFormsControlStrategy.cs   # 由 IValueAccessor + IControlTree 替代
│   └── IWinFormsEventBinder.cs       # 由 IEventBus 替代
└── Transformers/
    └── EventBindingHelper.cs         # 功能迁移到新接口
```

---

## 11. 影响分析与回退策略

### 11.1 影响范围

| 模块 | 影响级别 | 说明 |
|------|---------|------|
| `Share.EventBindingOrchestrator` | 🔴 高 | 核心重构，接口变更 |
| `Operation/WdUI` | 🟡 中 | 使用 IWinFormsControlStrategy 实现 |
| `Operation/BackEndDriven` | 🟡 中 | 使用 EventBindingPipelineRunner |
| `FileDriven` | 🟢 低 | 仅通过 IEventExecutionService 间接依赖 |
| `Modules` | 🟢 低 | 仅依赖 ITransformer 接口 |

### 11.2 回退策略

```
阶段一（接口定义）→ 纯新增，无影响，无需回退
阶段二（适配器）  → 纯新增，无影响，无需回退
阶段三（管线重构）→ 保留旧管线工厂重载，可切回旧管线
阶段四（迁移调用方）→ 保留旧接口标记 [Obsolete]，调用方可回退到旧接口
阶段五（清理废弃）→ 删除前确保无调用方，可回滚 git commit
```

### 11.3 风险缓解

| 风险 | 缓解措施 |
|------|---------|
| 新接口设计不合理 | 阶段一为纯新增，可在阶段二/三中调整接口签名 |
| 适配器性能问题 | WinFormsAdapter 使用 Lazy 初始化，缓存反射结果 |
| 管线行为不一致 | 新旧管线并行运行对比测试，确保输出一致 |
| 迁移遗漏调用方 | 使用 `[Obsolete]` 标记触发编译警告，全局搜索旧接口引用 |

---

## 12. 附录：新旧接口映射表

### 12.1 接口映射

| 旧接口/类 | 新接口/类 | 映射方式 |
|-----------|----------|---------|
| `IWinFormsControlStrategy` | `IValueAccessor` + `IControlTree` | 拆分 |
| `IWinFormsEventBinder` | `IEventBus` | 重命名 + 泛化 |
| `IEventExecutionService` | `IFunctionExecutor` | 重命名 |
| `IEventBindingContext` | `IEventBindingContext` | 重写 |
| `EventBindingContext` | `EventBindingContext` (实现新接口) | 扩展 |
| `ControlFinderTransformer` | `ControlFinderTransformer` (使用 IControlTree) | 内部重构 |
| `EventBinderTransformer` | `EventHandlerFactoryTransformer` + `ParameterBuilderTransformer` + `ControlValueWriterTransformer` | 拆分 |
| `EventBindingHelper` | 分散到各 Adapter/Transformer | 内联 |
| `ReentryGuard` | `EventBindingFrameworkOptions.EnableReentryGuard` | 配置化 |
| `EventBindingDiagnosticContext` | `DiagnosticTransformer` | 装饰器模式 |

### 12.2 配置映射

| 旧配置（硬编码） | 新配置（EventBindingFrameworkOptions） |
|-----------------|--------------------------------------|
| `TimeSpan.FromSeconds(10)` | `FunctionTimeoutMs = 10000` |
| `ReentryGuard.Enter()` | `EnableReentryGuard = true` |
| `EventBindingDiagnosticContext.Record()` | `EnableDiagnostics = true` |
| `InvokeRequired` / `BeginInvoke` | `EnableCrossThreadDispatching = true` |

### 12.3 目录结构变更

```
旧结构：                             新结构：
EventBindingOrchestrator/            EventBindingOrchestrator/
├── Abstractions/                    ├── Abstractions/
│   ├── IWinFormsControlStrategy.cs  │   ├── IControlNode.cs          ← 新增
│   ├── IWinFormsEventBinder.cs      │   ├── IControlTree.cs          ← 新增
│   └── IEventBindingContext.cs      │   ├── IEventBus.cs             ← 新增
├── Models/                          │   ├── IValueAccessor.cs        ← 新增
│   ├── EventBindingContext.cs       │   ├── IValueConverter.cs       ← 新增
│   └── ...                          │   └── IFunctionExecutor.cs     ← 新增
├── Transformers/                    ├── Models/
│   ├── EventBinderTransformer.cs    │   ├── EventBindingContext.cs   ← 重构
│   ├── ControlFinderTransformer.cs  │   ├── EventBindingDeclaration.cs ← 新增
│   ├── ...                          │   ├── ParameterMapping.cs      ← 新增
│   └── EventBindingPipelineFactory  │   ├── EventContext.cs          ← 新增
└── ContainerBinderPipeline.cs       │   └── EventBindingFrameworkOptions.cs ← 新增
                                     ├── Adapters/                   ← 新增
                                     │   ├── WinFormsControlNode.cs
                                     │   ├── WinFormsControlTree.cs
                                     │   ├── WinFormsEventBus.cs
                                     │   ├── WinFormsValueAccessor.cs
                                     │   ├── WinFormsValueConverter.cs
                                     │   └── WinFormsThreadDispatcher.cs
                                     ├── Transformers/
                                     │   ├── EventBinderTransformer.cs    ← 精简
                                     │   ├── EventHandlerFactoryTransformer.cs ← 新增
                                     │   ├── ParameterBuilderTransformer.cs  ← 新增
                                     │   ├── ControlValueWriterTransformer.cs ← 新增
                                     │   ├── DiagnosticTransformer.cs   ← 新增
                                     │   ├── ControlFinderTransformer.cs ← 重构
                                     │   ├── ...
                                     │   └── EventBindingPipelineFactory.cs ← 重构
                                     └── ContainerBinderPipeline.cs
```

---

> **文档版本**：v1.0
> **最后更新**：2026-07-07
