# EventBindingOrchestrator 上下文体系重构方案

> 基于 `BasicMethod/ContextFrame` 优化现有上下文体系
>
> 日期：2026-07-13
> 状态：方案设计

---

## 目录

1. [背景与目标](#一背景与目标)
2. [当前架构分析](#二当前架构分析)
3. [ContextFrame 能力总览](#三contextframe-能力总览)
4. [逐项优化方案](#四逐项优化方案)
5. [优化后架构](#五优化后架构)
6. [实施计划](#六实施计划)
7. [附录：文件变更清单](#七附录文件变更清单)

---

## 一、背景与目标

### 1.1 背景

`EventBindingOrchestrator` 是 Share 层的事件绑定编排模块，管理着 6 个上下文类型。在 2026-07-09 的重构中，已部分迁移到 `BasicMethod/ContextFrame` 框架（`EventBindingContext` 继承 `PipelineContextBase`，`FrameworkContext` 继承 `ContextBase`，`EventBindingDiagnosticContext` 基于 `AsyncContext<T>`）。

但当前实现存在以下问题：

- **样板代码过多**：`EventBindingContext` 的 20+ 个属性每个都通过 `GetData<T>()` / `SetData<T>()` 包装
- **手动状态重置**：`ResetCurrent()` 手动调用 5 次 `RemoveData<T>()`，容易遗漏
- **无父子关系**：`EventBindingContext` 和 `FrameworkContext` 独立，子系统需重复传递
- **类型不安全**：`FrameworkContext` 的 WinForms 适配部分使用 `object` 类型
- **自定义释放逻辑**：`EventBindingDiagnosticContext.BeginScope()` 需要自定义 `CompositeDisposable`

### 1.2 目标

1. **消除样板代码**：减少约 200 行属性包装代码
2. **利用快照机制**：用 `ISnapshotable` 替代手动 `ResetCurrent()`
3. **建立父子关系**：`EventBindingContext` 作为 `FrameworkContext` 的子上下文
4. **统一子系统管理**：使用 `IStateStore` 替代 `GetOrAddService<T>()` 包装
5. **简化诊断作用域**：使用 `ContextScope` 替代自定义 `CompositeDisposable`
6. **统一锁工具**：使用 `Lock.Monitor` 替代原生 `lock` 语句

---

## 二、当前架构分析

### 2.1 上下文类型总览

| 上下文 | 当前基类 | 文件 | 行数 | 角色 |
|--------|---------|------|------|------|
| `EventBindingContext` | `PipelineContextBase` | `Contexts/EventBindingContext.cs` | 335 | 核心状态容器 |
| `FrameworkContext` | `ContextBase` | `Contexts/FrameworkContext.cs` | 279 | 子系统注册中心 |
| `EventBindingDiagnosticContext` | `AsyncContext<T>` | `Contexts/EventBindingDiagnosticContext.cs` | 205 | 诊断上下文 |
| `BindingResult` | POCO | `Contexts/BindingResult.cs` | 84 | 绑定结果模型 |
| `FrameworkOptions` | POCO | `Contexts/FrameworkOptions.cs` | 141 | 配置选项 |
| `EventBindingFrameworkOptions` | POCO（已废弃） | `Contexts/EventBindingFrameworkOptions.cs` | 27 | 已废弃 |

### 2.2 当前状态流转

```
EventBindingOrchestrator
    │
    ├── ExecuteWithWalk()
    │   ├── new EventBindingContext { TargetObject = ..., Config = ..., EventBus = ... }
    │   │   └── 手动初始化 10+ 个属性
    │   ├── _genericPipeline.ExecuteWithWalk(context, config)
    │   │   └── 各 Transformer 通过 context.GetData/SetData 读写
    │   └── context.ResetCurrent()  ← 手动清除 5 个属性
    │
    └── LoadAndBindDeclarations()
        ├── new EventBindingContext { CurrentDeclaration = ..., TargetObject = ... }
        │   └── context.ApplyOptions(_options)  ← 手动设置 7 个属性
        └── _genericPipeline.Execute(context)
```

### 2.3 当前问题明细

#### 问题 1：EventBindingContext 属性样板代码

每个属性重复以下模式，共 20+ 次：

```csharp
public object? TargetObject
{
    get => GetData<object>(nameof(TargetObject));
    set => SetData(value!, nameof(TargetObject));
}
```

**影响**：约 200 行纯样板代码，新增属性时容易遗漏 `GetData/SetData` 包装。

#### 问题 2：ResetCurrent() 手动维护

```csharp
public void ResetCurrent()
{
    RemoveData<object>(nameof(CurrentControl));
    RemoveData<List<ControlConfig>>(nameof(CurrentControls));
    RemoveData<List<ParameterBinding>>(nameof(CurrentParameterBindings));
    RemoveData<string>(nameof(CurrentOutput));
    RemoveData<bool>(nameof(SkipCurrentControl));
}
```

**影响**：新增"每控件"属性时，必须同步更新 `ResetCurrent()`，否则会导致状态泄漏。

#### 问题 3：无父子上下文级联

`EventBindingContext` 和 `FrameworkContext` 是独立的，导致：

```csharp
// EventBindingOrchestrator 中需要重复设置
var context = new EventBindingContext
{
    TargetObject = targetObject,
    Config = config,
    EventBus = mergedOptions.EventBus,          // 从 FrameworkContext 复制
    MappingRegistry = mergedOptions.MappingRegistry,  // 从 FrameworkContext 复制
    BinderStrategy = mergedOptions.BinderStrategy,    // 从 FrameworkContext 复制
    // ...
};
```

**影响**：`EventBindingOptions` 中维护了与 `FrameworkContext` 重复的子系统引用。

#### 问题 4：FrameworkContext 类型不安全

```csharp
public object? WinFormsAccessor
{
    get { TryGetService<object>(out var svc); return svc; }
    set => GetOrAddService<object>(() => value!);
}
```

**影响**：调用方需要强制类型转换，失去编译时类型检查。

#### 问题 5：自定义 CompositeDisposable

```csharp
private sealed class CompositeDisposable : IDisposable
{
    private readonly DiagnosticContext _diagCtx;
    private readonly IDisposable _asyncScope;
    // ... 自定义释放逻辑
}
```

**影响**：维护额外的自定义类型，与 ContextFrame 的 `ContextScope` 功能重叠。

---

## 三、ContextFrame 能力总览

`BasicMethod/ContextFrame` 提供以下能力：

| 组件 | 能力 | 当前使用情况 |
|------|------|-------------|
| `IContext` / `ContextBase` | 数据存储、服务定位、父子层级、事件通知、生命周期 | ✅ `FrameworkContext` 已使用 |
| `IPipelineContext` / `PipelineContextBase` | 阶段状态跟踪、共享数据、错误聚合 | ✅ `EventBindingContext` 已使用 |
| `IScopedContext<T>` / `AsyncContext<T>` | AsyncLocal 作用域管理 | ✅ `EventBindingDiagnosticContext` 已使用 |
| `IContextSnapshot` / `ISnapshotable` | 快照创建/恢复、差异比较 | ❌ 未使用 |
| `IStateStore` / `StateStoreBase` | 类型安全键值存储 | ❌ 未使用 |
| `ContextBuilder<T>` | Fluent API 构建上下文 | ❌ 未使用（仅 `FrameworkOptions` 有扩展方法） |
| `ContextAdapter<TSource, TTarget>` | 上下文适配转换 | ❌ 未使用 |
| `ContextScope` | 轻量级作用域管理 | ❌ 未使用 |
| `Lock` | Monitor/ReaderWriterLockSlim 锁作用域 | ❌ 未使用 |
| `ContextExtensions` | 批量操作、子上下文创建、快照 | ❌ 未使用 |

---

## 四、逐项优化方案

### 优化 1：EventBindingContext — 消除属性样板代码

#### 当前代码

```csharp
public class EventBindingContext : PipelineContextBase, IEventBindingContext
{
    public object? TargetObject
    {
        get => GetData<object>(nameof(TargetObject));
        set => SetData(value!, nameof(TargetObject));
    }

    public UIConfiguration Config
    {
        get => GetData<UIConfiguration>(nameof(Config))!;
        set => SetData(value!, nameof(Config));
    }

    // ... 20+ 个类似属性
}
```

#### 优化方案

**方案 A（推荐）**：保留强类型属性，使用 `ContextExtensions.SetDataRange` 批量初始化，消除调用方的零散赋值。

```csharp
// EventBindingContext.cs — 保留属性定义（调用方受益）
// 属性本身保持不变，但调用方使用批量初始化

// EventBindingOrchestrator.cs — 调用方
var context = new EventBindingContext(parent: frameworkCtx);
context.SetDataRange(new Dictionary<string, object?>
{
    [nameof(EventBindingContext.TargetObject)] = targetObject,
    [nameof(EventBindingContext.Config)] = config,
    [nameof(EventBindingContext.IsScriptExecutionMode)] = mergedOptions.IsScriptExecutionMode,
    // ...
});
```

**方案 B（激进）**：完全消除属性，调用方直接使用 `GetData<T>()` / `SetData<T>()`。

```csharp
// 调用方
context.SetData(targetObject, nameof(EventBindingContext.TargetObject));
var target = context.GetData<object>(nameof(EventBindingContext.TargetObject));
```

**选择建议**：采用方案 A，保留属性定义以维持编译时类型安全，但调用方使用 `SetDataRange` 批量初始化。

---

### 优化 2：ResetCurrent() — 使用快照替代手动清除

#### 当前代码

```csharp
public void ResetCurrent()
{
    RemoveData<object>(nameof(CurrentControl));
    RemoveData<List<ControlConfig>>(nameof(CurrentControls));
    RemoveData<List<ParameterBinding>>(nameof(CurrentParameterBindings));
    RemoveData<string>(nameof(CurrentOutput));
    RemoveData<bool>(nameof(SkipCurrentControl));
}
```

#### 优化方案

利用 ContextFrame 的 `ISnapshotable` 接口和 `ContextSnapshot` 实现快照/恢复。

```csharp
// EventBindingContext.cs — 实现 ISnapshotable
public class EventBindingContext : PipelineContextBase, IEventBindingContext, ISnapshotable
{
    // 管线开始时创建快照
    public IContextSnapshot CreateSnapshot()
    {
        return new ContextSnapshot(Id, new Dictionary<string, object?>
        {
            [nameof(CurrentControl)] = CurrentControl,
            [nameof(CurrentControls)] = CurrentControls,
            [nameof(CurrentParameterBindings)] = CurrentParameterBindings,
            [nameof(CurrentOutput)] = CurrentOutput,
            [nameof(SkipCurrentControl)] = SkipCurrentControl,
        });
    }

    // 从快照恢复（替代 ResetCurrent）
    public void RestoreSnapshot(IContextSnapshot snapshot)
    {
        if (snapshot.ContextId != Id)
            throw new InvalidOperationException("快照上下文标识不匹配");

        foreach (var kvp in snapshot.Data)
        {
            if (kvp.Value != null)
            {
                var valueType = kvp.Value.GetType();
                var setDataMethod = typeof(IContext).GetMethod(nameof(IContext.SetData))
                    ?.MakeGenericMethod(valueType);
                setDataMethod?.Invoke(this, new[] { kvp.Value, kvp.Key });
            }
        }
    }
}
```

或者使用 ContextFrame 提供的扩展方法：

```csharp
// GenericEventBindingPipeline.cs — 管线中
// 处理每个控件前创建快照
var snapshot = context.CreateDataSnapshot();

try
{
    ProcessControl(context, controlConfig);
}
finally
{
    // 恢复快照（替代 ResetCurrent）
    context.RestoreDataFromSnapshot(snapshot);
}
```

**优势**：
- 自动包含所有"每控件"属性，不会遗漏
- 支持嵌套快照（子控件处理时创建子快照）
- 消除手动维护 `ResetCurrent()` 的负担
- `ContextExtensions` 已提供 `CreateDataSnapshot()` 和 `RestoreDataFromSnapshot()` 扩展方法

---

### 优化 3：建立父子上下文级联

#### 当前代码

```csharp
// EventBindingOrchestrator.cs
var context = new EventBindingContext
{
    TargetObject = targetObject,
    Config = config,
    EventBus = mergedOptions.EventBus,          // 从 Options 复制
    MappingRegistry = mergedOptions.MappingRegistry,  // 从 Options 复制
    BinderStrategy = mergedOptions.BinderStrategy,    // 从 Options 复制
    ControlFinderStrategy = mergedOptions.ControlFinderStrategy,  // 从 Options 复制
};
```

#### 优化方案

将 `EventBindingContext` 作为 `FrameworkContext` 的子上下文，利用父子级联自动查找子系统。

```csharp
// EventBindingOrchestrator.cs
// 1. 创建 FrameworkContext（应用生命周期）
var frameworkCtx = new FrameworkContext(options);

// 2. 创建 EventBindingContext 作为子上下文
var context = new EventBindingContext(parent: frameworkCtx)
{
    TargetObject = targetObject,
    Config = config,
    // EventBus / MappingRegistry / BinderStrategy 不再需要设置
    // 它们会通过父子级联从 FrameworkContext 自动查找
};

// EventBindingContext 中
public IEventBus? EventBus
{
    // GetData 会自动级联到父上下文（FrameworkContext）
    get => GetData<IEventBus>(nameof(EventBus));
    set => SetData(value!, nameof(EventBus));
}
```

**优势**：
- 消除 `EventBindingOptions` 中与 `FrameworkContext` 重复的子系统引用
- `EventBindingContext` 可以直接访问 `FrameworkContext` 中的任何子系统
- 数据查找自动级联，无需手动传递

**注意**：`FrameworkContext` 当前是每次绑定调用创建的，需要改为应用生命周期管理。

---

### 优化 4：FrameworkContext — 使用 IStateStore 管理子系统

#### 当前代码

```csharp
public class FrameworkContext : ContextBase
{
    public DefaultEventBusCore? EventBusCore
    {
        get { TryGetService<DefaultEventBusCore>(out var svc); return svc; }
        set => GetOrAddService<DefaultEventBusCore>(() => value!);
    }

    public object? WinFormsAccessor
    {
        get { TryGetService<object>(out var svc); return svc; }
        set => GetOrAddService<object>(() => value!);
    }
    // ... 15+ 个类似属性
}
```

#### 优化方案

```csharp
public class FrameworkContext : ContextBase
{
    /// <summary>子系统存储</summary>
    public IStateStore SubsystemStore { get; }

    public FrameworkContext(FrameworkOptions options) : base()
    {
        Options = options;
        SubsystemStore = new StateStoreBase();
    }

    /// <summary>获取子系统（不存在时抛出异常）</summary>
    public T GetSubsystem<T>(string key) where T : notnull
        => SubsystemStore.Get<T>(key)
           ?? throw new InvalidOperationException($"子系统 '{key}' 未注册");

    /// <summary>注册子系统</summary>
    public void RegisterSubsystem<T>(string key, T instance) where T : notnull
        => SubsystemStore.Set(key, instance);

    /// <summary>尝试获取子系统</summary>
    public bool TryGetSubsystem<T>(out T? instance, string key) where T : notnull
    {
        instance = SubsystemStore.Get<T>(key);
        return instance != null;
    }
}
```

调用方：

```csharp
// 注册
frameworkCtx.RegisterSubsystem(nameof(FrameworkContext.EventBus), eventBusFacade);
frameworkCtx.RegisterSubsystem("WinFormsAccessor", winFormsAccessor);

// 获取
var eventBus = frameworkCtx.GetSubsystem<EventBusFacade>(nameof(FrameworkContext.EventBus));
```

**优势**：
- 消除 15+ 个属性的样板代码
- 类型安全（不再使用 `object`）
- 统一的子系统生命周期管理
- 新增子系统时无需修改 `FrameworkContext` 类

---

### 优化 5：EventBindingDiagnosticContext — 使用 ContextScope

#### 当前代码

```csharp
public static IDisposable BeginScope()
{
    var diagCtx = DiagnosticManager.BeginScope(ScopeName);
    var asyncScope = AsyncContext<DiagnosticContext>.CreateScope(diagCtx);
    diagCtx.Record(EventNames.EventBinderScopeBegan, new { ... });
    return new CompositeDisposable(diagCtx, asyncScope);
}

private sealed class CompositeDisposable : IDisposable
{
    private readonly DiagnosticContext _diagCtx;
    private readonly IDisposable _asyncScope;

    public void Dispose()
    {
        _asyncScope.Dispose();
        _diagCtx.Dispose();
    }
}
```

#### 优化方案

```csharp
public static IDisposable BeginScope()
{
    var diagCtx = DiagnosticManager.BeginScope(ScopeName);
    var asyncScope = AsyncContext<DiagnosticContext>.CreateScope(diagCtx);

    // 使用 ContextScope 替代 CompositeDisposable
    return new ContextScope(
        onCreate: () => diagCtx.Record(EventNames.EventBinderScopeBegan, new
        {
            scopeName = ScopeName,
            createdAt = diagCtx.CreatedAt
        }),
        onDispose: () =>
        {
            asyncScope.Dispose();  // 先恢复 AsyncContext
            diagCtx.Dispose();     // 再恢复 DiagnosticContext
        }
    );
}
```

**优势**：
- 消除自定义 `CompositeDisposable` 类
- 复用 ContextFrame 的 `ContextScope`
- 代码更简洁

---

### 优化 6：EventBindingOrchestrator — 使用 Lock 工具

#### 当前代码

```csharp
private readonly object _statsLock = new();

lock (_statsLock)
{
    _bindingResults.Add(result);
}
```

#### 优化方案

```csharp
using BasicMethod.ContextFrame.Locking;

using (Lock.Monitor(_statsLock))
{
    _bindingResults.Add(result);
}
```

**优势**：
- 与 ContextFrame 的锁体系保持一致
- 支持后续升级到 `ReaderWriterLockSlim`（`Lock.Read` / `Lock.Write`）
- `ref struct` 零分配开销

---

## 五、优化后架构

### 5.1 类关系图

```
┌─────────────────────────────────────────────────────────────┐
│  FrameworkContext : ContextBase                              │
│  ├── Options: FrameworkOptions                               │
│  ├── SubsystemStore: IStateStore                             │
│  │   ├── EventBusFacade                                      │
│  │   ├── CommandFrameworkHost                                │
│  │   ├── IStagePipeline<CommandContext>                      │
│  │   ├── IWinFormsValueAccessor  (类型安全)                  │
│  │   ├── IWinFormsEventBinder     (类型安全)                 │
│  │   └── ...                                                  │
│  │                                                            │
│  └── [child] EventBindingContext : PipelineContextBase        │
│       ├── TargetObject / Config / CurrentView / ...           │
│       ├── BoundControls / SkippedControls                     │
│       ├── CurrentDeclaration                                  │
│       ├── ISnapshotable                                       │
│       │   ├── CreateSnapshot()  → 管线开始时                  │
│       │   └── RestoreSnapshot() → 替代 ResetCurrent()         │
│       └── 级联查找: EventBus → 父上下文 FrameworkContext      │
│                                                                 │
│  EventBindingDiagnosticContext (static)                        │
│  ├── AsyncContext<DiagnosticContext>                           │
│  ├── BeginScope() → ContextScope                               │
│  ├── Record() / GetSnapshotString()                            │
│  └── CaptureSnapshot() / RestoreScope()                        │
│                                                                 │
│  BindingResult (POCO)                                          │
│  FrameworkOptions (POCO)                                       │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 优化后状态流转

```
EventBindingOrchestrator
    │
    ├── ExecuteWithWalk()
    │   ├── frameworkCtx = new FrameworkContext(options)  ← 应用生命周期
    │   ├── context = new EventBindingContext(parent: frameworkCtx)
    │   │   └── context.SetDataRange({ TargetObject, Config, ... })  ← 批量初始化
    │   ├── snapshot = context.CreateDataSnapshot()  ← 快照
    │   ├── _genericPipeline.ExecuteWithWalk(context, config)
    │   │   └── 各 Transformer 通过 context.GetData/SetData 读写
    │   │   └── 子控件处理: context.RestoreDataFromSnapshot(snapshot)  ← 快照恢复
    │   └── 无需 ResetCurrent()
    │
    └── LoadAndBindDeclarations()
        ├── context = new EventBindingContext(parent: frameworkCtx)
        │   └── context.SetDataRange({ CurrentDeclaration, TargetObject, ... })
        └── _genericPipeline.Execute(context)
```

### 5.3 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Contexts/EventBindingContext.cs` | 修改 | 实现 ISnapshotable，添加快照方法 |
| `Contexts/FrameworkContext.cs` | 修改 | 使用 IStateStore 替代属性包装 |
| `Contexts/EventBindingDiagnosticContext.cs` | 修改 | 使用 ContextScope 替代 CompositeDisposable |
| `EventBindingOrchestrator.cs` | 修改 | 建立父子上下文，使用 Lock.Monitor，使用 SetDataRange |
| `Abstractions/EventBindingOptions.cs` | 修改 | 移除与 FrameworkContext 重复的子系统属性 |
| `Transformers/GenericEventBindingPipeline.cs` | 修改 | 使用快照替代 ResetCurrent |
| `Transformers/ControlFinderTransformer.cs` | 修改 | 适配快照机制 |
| `Transformers/BindingMetadataTransformer.cs` | 修改 | 适配快照机制 |
| `Transformers/EventBinderTransformer.cs` | 修改 | 适配快照机制 |

---

## 六、实施计划

### 阶段 1：基础优化（低风险，高收益）

| 步骤 | 内容 | 涉及文件 | 预计工时 |
|------|------|---------|---------|
| 1.1 | EventBindingContext 实现 ISnapshotable | `EventBindingContext.cs` | 1h |
| 1.2 | GenericEventBindingPipeline 使用快照替代 ResetCurrent | `GenericEventBindingPipeline.cs` | 1h |
| 1.3 | 各 Transformer 适配快照机制 | 3 个 Transformer 文件 | 1h |

### 阶段 2：架构优化（中风险，中收益）

| 步骤 | 内容 | 涉及文件 | 预计工时 |
|------|------|---------|---------|
| 2.1 | FrameworkContext 使用 IStateStore | `FrameworkContext.cs` | 2h |
| 2.2 | 建立父子上下文级联 | `EventBindingOrchestrator.cs` | 2h |
| 2.3 | EventBindingOptions 移除重复属性 | `EventBindingOptions.cs` | 1h |
| 2.4 | 调用方适配（BackEndDrivenBuilder 等） | 外部文件 | 2h |

### 阶段 3：代码简化（低风险，低收益）

| 步骤 | 内容 | 涉及文件 | 预计工时 |
|------|------|---------|---------|
| 3.1 | EventBindingDiagnosticContext 使用 ContextScope | `EventBindingDiagnosticContext.cs` | 0.5h |
| 3.2 | EventBindingOrchestrator 使用 Lock.Monitor | `EventBindingOrchestrator.cs` | 0.5h |
| 3.3 | 调用方使用 SetDataRange 批量初始化 | `EventBindingOrchestrator.cs` | 1h |

### 阶段 4：测试验证

| 步骤 | 内容 | 预计工时 |
|------|------|---------|
| 4.1 | 编译验证 | 0.5h |
| 4.2 | 单元测试（快照/恢复、父子级联、IStateStore） | 2h |
| 4.3 | 集成测试（完整绑定流程） | 2h |

---

## 七、附录：文件变更清单

### 7.1 EventBindingContext.cs 变更

```diff
- public class EventBindingContext : PipelineContextBase, IEventBindingContext
+ public class EventBindingContext : PipelineContextBase, IEventBindingContext, ISnapshotable
  {
      // 属性保持不变（保留编译时类型安全）

-     public void ResetCurrent()
-     {
-         RemoveData<object>(nameof(CurrentControl));
-         RemoveData<List<ControlConfig>>(nameof(CurrentControls));
-         RemoveData<List<ParameterBinding>>(nameof(CurrentParameterBindings));
-         RemoveData<string>(nameof(CurrentOutput));
-         RemoveData<bool>(nameof(SkipCurrentControl));
-     }
+     // 使用 ISnapshotable 替代 ResetCurrent
+     // CreateSnapshot() / RestoreSnapshot() 由 ContextExtensions 提供
+     // 或通过 SnapshotableContextBase 基类实现

      public void ApplyOptions(EventBindingOptions options)
      {
-         // 逐个属性设置
-         EventBus = options.EventBus;
-         IsScriptExecutionMode = options.IsScriptExecutionMode;
-         SearchRoot = options.SearchRoot;
-         CustomControlFinder = options.CustomControlFinder;
-         MappingRegistry = options.MappingRegistry;
-         BinderStrategy = options.BinderStrategy;
-         ControlFinderStrategy = options.ControlFinderStrategy;
+         // 使用 SetDataRange 批量设置
+         this.SetDataRange(new Dictionary<string, object?>
+         {
+             [nameof(EventBus)] = options.EventBus,
+             [nameof(IsScriptExecutionMode)] = options.IsScriptExecutionMode,
+             [nameof(SearchRoot)] = options.SearchRoot,
+             [nameof(CustomControlFinder)] = options.CustomControlFinder,
+             [nameof(MappingRegistry)] = options.MappingRegistry,
+             [nameof(BinderStrategy)] = options.BinderStrategy,
+             [nameof(ControlFinderStrategy)] = options.ControlFinderStrategy,
+         });
      }
  }
```

### 7.2 FrameworkContext.cs 变更

```diff
  public class FrameworkContext : ContextBase
  {
+     /// <summary>子系统存储</summary>
+     public IStateStore SubsystemStore { get; }

      public FrameworkContext(FrameworkOptions options) : base()
      {
          Options = options;
+         SubsystemStore = new StateStoreBase();
      }

-     public DefaultEventBusCore? EventBusCore
-     {
-         get { TryGetService<DefaultEventBusCore>(out var svc); return svc; }
-         set => GetOrAddService<DefaultEventBusCore>(() => value!);
-     }
-     // ... 15+ 个类似属性

+     /// <summary>注册子系统</summary>
+     public void RegisterSubsystem<T>(string key, T instance) where T : notnull
+         => SubsystemStore.Set(key, instance);
+
+     /// <summary>获取子系统</summary>
+     public T GetSubsystem<T>(string key) where T : notnull
+         => SubsystemStore.Get<T>(key)
+            ?? throw new InvalidOperationException($"子系统 '{key}' 未注册");
+
+     /// <summary>尝试获取子系统</summary>
+     public bool TryGetSubsystem<T>(out T? instance, string key) where T : notnull
+     {
+         instance = SubsystemStore.Get<T>(key);
+         return instance != null;
+     }

      public override void Dispose()
      {
-         // 释放所有已注册的 IDisposable 子系统
-         foreach (var disposable in GetAllData().OfType<IDisposable>())
-         {
-             disposable.Dispose();
-         }
+         // 通过 IStateStore 释放子系统
+         // 由调用方在注册时决定生命周期策略
          base.Dispose();
      }
  }
```

### 7.3 EventBindingDiagnosticContext.cs 变更

```diff
- private sealed class CompositeDisposable : IDisposable
- {
-     private readonly DiagnosticContext _diagCtx;
-     private readonly IDisposable _asyncScope;
-     // ... 自定义释放逻辑
- }

  public static IDisposable BeginScope()
  {
      var diagCtx = DiagnosticManager.BeginScope(ScopeName);
      var asyncScope = AsyncContext<DiagnosticContext>.CreateScope(diagCtx);

-     diagCtx.Record(EventNames.EventBinderScopeBegan, new { ... });

-     return new CompositeDisposable(diagCtx, asyncScope);
+     return new ContextScope(
+         onCreate: () => diagCtx.Record(EventNames.EventBinderScopeBegan, new
+         {
+             scopeName = ScopeName,
+             createdAt = diagCtx.CreatedAt
+         }),
+         onDispose: () =>
+         {
+             asyncScope.Dispose();
+             diagCtx.Dispose();
+         }
+     );
  }
```

### 7.4 EventBindingOrchestrator.cs 变更

```diff
  public class EventBindingOrchestrator : IEventBindingService
  {
-     private readonly object _statsLock = new();

      public EventBindingContext ExecuteWithWalk(
          object targetObject,
          UIConfiguration config,
          EventBindingOptions? options = null)
      {
-         var context = new EventBindingContext
-         {
-             TargetObject = targetObject,
-             Config = config,
-             IsScriptExecutionMode = mergedOptions.IsScriptExecutionMode,
-             SearchRoot = mergedOptions.SearchRoot,
-             CustomControlFinder = mergedOptions.CustomControlFinder,
-             EventBus = mergedOptions.EventBus,
-             MappingRegistry = mergedOptions.MappingRegistry,
-             BinderStrategy = mergedOptions.BinderStrategy,
-             ControlFinderStrategy = mergedOptions.ControlFinderStrategy
-         };
+         // 使用 FrameworkContext 作为父上下文
+         var frameworkCtx = GetOrCreateFrameworkContext();
+         var context = new EventBindingContext(parent: frameworkCtx);
+         context.SetDataRange(new Dictionary<string, object?>
+         {
+             [nameof(EventBindingContext.TargetObject)] = targetObject,
+             [nameof(EventBindingContext.Config)] = config,
+             [nameof(EventBindingContext.IsScriptExecutionMode)] = mergedOptions.IsScriptExecutionMode,
+             [nameof(EventBindingContext.SearchRoot)] = mergedOptions.SearchRoot,
+             [nameof(EventBindingContext.CustomControlFinder)] = mergedOptions.CustomControlFinder,
+         });
+         // EventBus / MappingRegistry / BinderStrategy 通过父子级联自动查找

-         lock (_statsLock)
+         using (Lock.Monitor(_statsLock))
          {
              _bindingResults.Add(new BindingResult { ... });
          }
      }
  }
```

---

## 附录：ContextFrame 未覆盖的部分

以下逻辑是事件绑定特有的业务逻辑，不属于通用上下文框架的范畴，保持不变：

1. **Transformer 管线**（`GenericEventBindingPipeline`）— 3 步 Transformer 链
2. **控件查找**（`ControlFinderTransformer`）— 反射查找 WinForms 控件
3. **绑定元数据提取**（`BindingMetadataTransformer`）— 从配置提取参数/输出
4. **事件绑定**（`EventBinderTransformer`）— 创建 EventHandler 并注册到 IEventBus
5. **参数构建**（`ParameterBuilder`）— 从 parameterBindings 构建函数参数字典
6. **声明验证**（`EventBindingValidator`）— 基于 BasicMethod.Declarative 框架
7. **配置映射**（`ConfigToDeclarationMapper`）— ControlConfig → EventBindingDeclaration
8. **初始化编排**（`InitializationOrchestrator<T>`）— 阶段编排 + 依赖注入 + 脚本执行
