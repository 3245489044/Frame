# 重构方案：DefaultEventBindingService 依赖注入彻底重构

## 1. 问题分析

### 1.1 现象

MathFunctions.json 事件绑定失败，日志显示 `boundCount = 0`，`ControlRegistry` 注册失败。

### 1.2 根因

`DefaultEventBindingService` 构造函数中使用**服务定位器反模式**从 `AppServiceProvider` 隐式解析 `IEventBus`：

```csharp
// DefaultEventBindingService.cs 第 67 行
var resolvedEventBus = AppServiceProvider.Resolve(typeof(IEventBus)) as IEventBus;
if (_options.EventBus == null && resolvedEventBus != null)
{
    _options.EventBus = resolvedEventBus;
}
```

由于初始化顺序问题，`DefaultEventBindingService` 在 `FrameworkInitializer.Initialize` 步骤 2 被创建时，`IEventBus` 尚未注册到 `AppServiceProvider`（`IEventBus` 在 `EventTriggerFrameworkInitializer.InitializeWinForms` → `WinFormsInitializer.Initialize` 中注册），导致 `Resolve` 返回 null，`_options.EventBus` 保持为 null。

随后 `EventBinderTransformer.Transform` 第 154 行检查 `input.EventBus != null` 为 false，跳过所有事件绑定。

### 1.3 初始化顺序（当前）

```
FrameworkInitializer.Initialize (FileDriven.Execution)
  ├─ 步骤 2: new DefaultEventBindingService()  ← IEventBus 尚未注册，Resolve 返回 null
  ├─ 步骤 6: InitializeAppServiceProvider()   ← 注册 IEventExecutionService 等
  └─ ...后续...
      └─ EventTriggerFrameworkInitializer.Initialize
           ├─ InitializeEventBus()             ← 创建 IEventBus
           └─ InitializeWinForms()
                └─ WinFormsInitializer.Initialize  ← 注册 IEventBus 到 AppServiceProvider
```

### 1.4 涉及的服务定位器调用

`DefaultEventBindingService` 构造函数中所有从 `AppServiceProvider` 隐式解析的调用：

| 行号 | 解析内容 | 问题 |
|------|---------|------|
| 67 | `AppServiceProvider.Resolve(typeof(IEventBus))` | 初始化顺序问题，返回 null |
| 113 | `AppServiceProvider.Resolve<IExecutionEngine>()` | 同上 |
| 124-126 | `() => AppServiceProvider.Resolve<IEventExecutionService>()` | 延迟解析，问题较小但仍是隐式依赖 |

---

## 2. 重构方案

### 2.1 核心原则

> **谁创建，谁注入。** 所有依赖通过构造函数参数显式传入，消除所有 `AppServiceProvider.Resolve` 调用。

### 2.2 修改点清单

#### 2.2.1 `DefaultEventBindingService.cs` — 构造函数签名变更

**当前**（所有参数可选，内部偷偷解析）：
```csharp
public DefaultEventBindingService(
    IEventTriggerService? eventTriggerService = null,
    IEventBusFallback? fallbackEventBus = null,
    EventBindingOptions? options = null)
```

**改为**（`IEventBus` 为强制参数，移除所有 `AppServiceProvider.Resolve`）：
```csharp
public DefaultEventBindingService(
    IEventBus eventBus,
    IExecutionEngine? executionEngine = null,
    Func<IEventExecutionService?>? executionServiceResolver = null,
    IEventTriggerService? eventTriggerService = null,
    IEventBusFallback? fallbackEventBus = null,
    EventBindingOptions? options = null)
```

**移除的代码块**：
- 第 60-73 行：`AppServiceProvider.Resolve(typeof(IEventBus))` 及后续赋值
- 第 113-119 行：`AppServiceProvider.Resolve<IExecutionEngine>()` 及后续赋值
- 第 122-126 行：`_options.ExecutionServiceResolver` 的默认值设置

**新增的代码**：
```csharp
_options.EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
if (executionEngine != null) _options.ExecutionEngine = executionEngine;
if (executionServiceResolver != null) _options.ExecutionServiceResolver = executionServiceResolver;
```

#### 2.2.2 `EventTriggerFrameworkInitializer.cs` — 创建时机调整

**`InitializeCore` 方法（第 251 行）**：
- 当前：`ctx.EventBindingService = new DefaultEventBindingService();`
- 改为：**移除**此处的创建逻辑，因为此时 `IEventBus` 尚未初始化

**`InitializeWinForms` 方法（第 262 行）**：
- 在 `WinFormsInitializer.Initialize(ctx)` 调用之后，创建 `DefaultEventBindingService`
- 此时 `ctx.WinFormsEventBus` 已可用，`IEventBus` 已注册到 `AppServiceProvider`

```csharp
private static void InitializeWinForms(FrameworkContext ctx)
{
    // ... 现有逻辑 ...
    WinFormsInitializer winFormsInit = new WinFormsInitializer();
    winFormsInit.Initialize(ctx);

    // ★ 重构：在 IEventBus 就绪后创建 DefaultEventBindingService
    var eventBus = ctx.WinFormsEventBus as IEventBus
        ?? throw new InvalidOperationException("WinFormsEventBus 未初始化");
    var executionEngine = AppServiceProvider.Resolve<IExecutionEngine>();
    ctx.EventBindingService = new DefaultEventBindingService(
        eventBus,
        executionEngine: executionEngine,
        executionServiceResolver: () => AppServiceProvider.Resolve<IEventExecutionService>());
}
```

#### 2.2.3 `FrameworkInitializer.cs` — 移除早期创建

**步骤 2（第 137-142 行）**：
- 当前：创建 `DefaultEventBindingService` 并注入到渲染管线
- 改为：**移除**此处的创建逻辑，改为注入一个**占位符**或**延迟创建委托**

```csharp
// 步骤 2：注入 EventBindingService 到渲染管线
// ★ 重构：DefaultEventBindingService 延迟到 EventTriggerFrameworkInitializer 中创建
//   此处注入一个延迟创建委托，确保渲染管线 BindEventsSkeleton 时能获取到实例
Lazy<DefaultEventBindingService> lazyBindingService = new(() =>
{
    var eventBus = AppServiceProvider.Resolve(typeof(IEventBus)) as IEventBus
        ?? throw new InvalidOperationException("IEventBus 未注册");
    var engine = AppServiceProvider.Resolve<IExecutionEngine>();
    return new DefaultEventBindingService(eventBus, executionEngine: engine);
});
FileDrivenWdUIBridge.InjectEventBinder(lazyBindingService.Value);
```

或者更彻底：**将步骤 2 整体移到初始化流程的最后阶段**，确保所有依赖都已就绪。

#### 2.2.4 `FileDrivenDependencyInjector.cs` — 适配新构造函数

> 需要查看此文件确认具体改动。

如果 `FileDrivenDependencyInjector` 也创建了 `DefaultEventBindingService`，需要适配新的构造函数签名，传入 `IEventBus`。

---

## 3. 影响范围

| 文件 | 改动类型 | 风险 |
|------|---------|------|
| `DefaultEventBindingService.cs` | 构造函数签名变更 | 中 — 所有调用方需适配 |
| `EventTriggerFrameworkInitializer.cs` | 创建时机调整 | 中 — 需确保初始化顺序正确 |
| `FrameworkInitializer.cs` | 移除步骤 2 的创建逻辑 | 低 — 改为延迟创建或后移 |
| `FileDrivenDependencyInjector.cs` | 适配新构造函数 | 低 — 需确认是否存在调用 |

---

## 4. 验证标准

1. **编译通过**：所有调用方适配新构造函数签名
2. **启动正常**：初始化流程不报错，`DefaultEventBindingService` 在 `IEventBus` 就绪后创建
3. **事件绑定正常**：MathFunctions.json 的 `boundCount > 0`
4. **无服务定位器调用**：`DefaultEventBindingService` 中不再有 `AppServiceProvider.Resolve`
