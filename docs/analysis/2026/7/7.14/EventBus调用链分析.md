# EventBus 调用链分析与重构方案

> **编写日期：** 2026-07-13
> **问题现象：** Sideload 场景下按钮点击无响应，事件绑定被静默跳过
> **根本原因：** `WinFormsRenderPipeline` 单例 fallback 路径创建了没有 `EventBus` 的 `EventBindingOrchestrator`，导致 `ConfigBindingStage` 跳过所有事件绑定

---

## 一、问题定位

### 1.1 错误日志

```
[ERROR] 事件绑定失败: Object reference not set to an instance of an object.
```

### 1.2 调用链追踪

```
MainForm.LoadSideContainerAsync(configPath)
  └── WinFormsRenderPipeline.RenderFullAsync(target, config)     // 渲染 UI
       └── EventBindingOrchestrator.ExecuteWithWalkAsync(...)     // 绑定事件
            └── ExecuteWithWalk(target, config, options)
                 ├── context.ApplyOptions(options)                // EventBus 写入 Config 层
                 └── GenericEventBindingPipeline.ExecuteWithWalk
                      └── WalkAndBind → _pipelineChain.Execute
                           ├── ControlFinderTransformer           // 查找控件 ✓
                           ├── BindingMetadataTransformer         // 提取元数据 ✓
                           └── EventBinderTransformer
                                └── ConfigBindingStage.Process
                                     └── ctx.EventBus == null     // ★ 跳过所有绑定！
```

### 1.3 根本原因

`WinFormsRenderPipeline` 单例的 fallback 路径：

```csharp
// CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs:107-121
private static readonly Lazy<WinFormsRenderPipeline> _instance = new(() =>
{
    var orchestrator = AppServiceProvider.Resolve<EventBindingOrchestrator>();
    if (orchestrator != null)
        return new WinFormsRenderPipeline(context, orchestrator);
    // ★ fallback：EventBus = null！
    return new WinFormsRenderPipeline(context, new EventBindingOrchestrator(new EventBindingOptions()));
});
```

**问题链：** `new EventBindingOptions()` → `EventBus = null` → `ApplyOptions` 跳过 → `ConfigBindingStage` 跳过 → **按钮无响应**

---

## 二、整合调用链

### 2.1 EventBus 完整生命周期

```
┌──────────────────────────────────────────────────────────────────────────┐
│ 1. 创建与注册（WinFormsInitializer → WdUI）                              │
│    new WinFormsEventBus()                                                │
│    AppServiceProvider.Register<IEventBus>(eventBus)                      │
└──────────────────────────────────┬───────────────────────────────────────┘
                                   │
                                   ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ 2. 注入到 Orchestrator（FrameworkInitializer → FileDriven）              │
│    new EventBindingOptions { EventBus = eventBus }                       │
│    new EventBindingOrchestrator(options)                                 │
│    AppServiceProvider.Register(orchestrator)                             │
└──────────────────────────────────┬───────────────────────────────────────┘
                                   │
                                   ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ 3. 传递到 Context（EventBindingOrchestrator → Share）                    │
│    options = _options.Clone()  // 继承 EventBus                         │
│    context.ApplyOptions(options)                                         │
│    └── _cell.Set("EventBus", eventBus, Config)  // Config 层            │
└──────────────────────────────────┬───────────────────────────────────────┘
                                   │
                                   ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ 4. 消费（ConfigBindingStage → Share）                                    │
│    ctx.EventBus.BindControlEvent(control, eventName, handler)            │
└──────────────────────────────────────────────────────────────────────────┘
```

### 2.2 三层存储结构

```
EventBindingContext._cell (IStorageCell)
  ├── Config 层（不受快照影响）← EventBus / TargetObject / Config
  ├── Session 层（受快照管理）← CurrentControlConfig / CurrentControl
  └── Transient 层（临时）    ← SkipCurrentControl
```

### 2.3 消费点汇总

| 消费点 | 位置 | 获取方式 | 用途 |
|--------|------|---------|------|
| ConfigBindingStage.Process | `Share/.../Stages/ConfigBindingStage.cs` | `ctx.EventBus` | 绑定控件事件 |
| UnbindEventsAsync | `Share/.../EventBindingOrchestrator.cs:389` | `_options.EventBus` | 解绑控件事件 |
| TryBindEvent | `Share/.../Stages/EventBindingStageBase.cs` | 参数传入 | 执行绑定 |

---

## 三、消除冗余包装

### 3.1 重复的 WinFormsEventBus

当前存在 **两个** `WinFormsEventBus`：

| # | 位置 | 基类 | 接口 | 状态 |
|---|------|------|------|------|
| 1 | `CsharpPrj/Share/EventBindingOrchestrator/Controls/WinFormsEventBus.cs` | `DefaultEventBus` | `IEventBus` | ✅ 保留 |
| 2 | `CsharpPrj/Operation/WdUI/WinForms/WinFormsEventBus.cs` | `DefaultEventBusCore` | `IEventBusFallback` | ❌ 废弃 |

**问题：** WdUI 层的 `WinFormsEventBus` 是旧实现，Share 层已有功能等价的新实现。WdUI 层的不应持有 EventBus 创建权。

### 3.2 WinFormsRenderPipeline 单例的冗余 fallback

```csharp
// 当前：fallback 路径创建无 EventBus 的 Orchestrator
return new WinFormsRenderPipeline(context, new EventBindingOrchestrator(new EventBindingOptions()));

// 修复后：从 AppServiceProvider 获取 EventBus
var eventBus = AppServiceProvider.Resolve<TConfig.IEventBus>();
var options = new EventBindingOptions { EventBus = eventBus };
return new WinFormsRenderPipeline(context, new EventBindingOrchestrator(options));
```

### 3.3 冗余的静默跳过逻辑

| 位置 | 当前行为 | 修复后行为 |
|------|---------|-----------|
| `ApplyOptions` | `if (EventBus != null)` 静默跳过 | 保持（由上游保证不为 null） |
| `ConfigBindingStage` | `if (ctx.EventBus != null)` 静默跳过 | 抛 `InvalidOperationException` |
| `UnbindEventsAsync` | `if (eventBus == null)` 静默跳过 | 抛 `InvalidOperationException` |

---

## 四、重构方案（先重构，后 Debug）

### 4.1 第一阶段：IoC 重构（EventBus 创建权迁移）

**目标：** EventBus 由 FileDriven 层创建，WdUI 层只注册 WinForms 特有组件

#### 步骤 1：修改 `FrameworkInitializer` — 在 FileDriven 层创建 EventBus

**文件：** `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs`

```csharp
private static void InitializeWinFormsSubsystem()
{
    var ctx = EventTriggerFrameworkInitializer.Context;
    if (ctx == null) return;

    // ★ 新增：在 FileDriven 层创建 EventBus 并注册
    var eventBus = new Share.EventBindingOrchestrator.Adapters.WinFormsEventBus();
    ctx.UISubsystemEventBus = eventBus;
    AppServiceProvider.Register<TConfig.IEventBus>(eventBus);

    // WinFormsInitializer 不再创建 EventBus
    var initializer = AppServiceProvider.Resolve<IUISubsystemInitializer>();
    initializer?.Initialize(ctx);

    // 创建 EventBindingOrchestrator（不变）
    var executionEngine = AppServiceProvider.Resolve<IExecutionEngine>();
    var options = new EventBindingOptions
    {
        EventBus = eventBus,
        ExecutionEngine = executionEngine,
        // ...
    };
    var orchestrator = new EventBindingOrchestrator(options);
    AppServiceProvider.Register(typeof(EventBindingOrchestrator), orchestrator);
}
```

#### 步骤 2：修改 `WinFormsInitializer` — 移除 EventBus 创建

**文件：** `CsharpPrj/Operation/WdUI/WinForms/WinFormsInitializer.cs`

```csharp
public void Initialize(FrameworkContext ctx)
{
    ctx.ControlValueAccessor = new WinFormsControlValueAccessor();
    ctx.EventNameResolver = new WinFormsEventNameResolver();
    ctx.EventBinderStrategy = new ReflectionEventBinder<Control>(new WinFormsEventNameResolver());
    
    // ★ 移除：EventBus 创建权已迁移到 FileDriven 层
    // ctx.UISubsystemEventBus = new WdUI.WinForms.WinFormsEventBus() as TConfig.IEventBus;
    
    AppServiceProvider.Register<IControlFinder>(new WinFormsControlFinderService());
    AppServiceProvider.Register<IControlFinderStrategy>(new WinFormsControlFinderStrategy());
    
    // ★ 移除：EventBus 注册已迁移到 FileDriven 层
    // AppServiceProvider.Register<TConfig.IEventBus>(ctx.UISubsystemEventBus!);
}
```

#### 步骤 3：废弃 WdUI 层的 `WinFormsEventBus`

**文件：** `CsharpPrj/Operation/WdUI/WinForms/WinFormsEventBus.cs`

```csharp
[Obsolete("EventBus 创建权已迁移到 FileDriven 层，请使用 Share.EventBindingOrchestrator.Adapters.WinFormsEventBus")]
public class WinFormsEventBus : DefaultEventBusCore, IEventBusFallback { ... }
```

### 4.2 第二阶段：防御性增强（消除静默失败）

#### 步骤 4：`ConfigBindingStage` 抛错替代静默跳过

**文件：** `CsharpPrj/Share/EventBindingOrchestrator/Stages/ConfigBindingStage.cs`

```csharp
if (config.Events != null)
{
    if (ctx.EventBus == null)
        throw new InvalidOperationException(
            $"EventBus 未设置，无法绑定控件 '{ctx.CurrentControlConfig?.Name}' 的事件。");
    // ... 现有绑定逻辑 ...
}
```

#### 步骤 5：`ExecuteWithWalk` 入口验证

**文件：** `CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs`

```csharp
context.ApplyOptions(mergedOptions);
if (context.EventBus == null)
    throw new InvalidOperationException(
        $"EventBus 未设置。options.EventBus={mergedOptions.EventBus != null}");
```

#### 步骤 6：构造函数验证

**文件：** `CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs`

```csharp
public EventBindingOrchestrator(EventBindingOptions options)
    : this(NullLogger<EventBindingOrchestrator>.Instance,
           EventBindingPipelineFactory.CreateGenericPipeline(options), options)
{
    if (options.EventBus == null)
        throw new ArgumentNullException(nameof(options.EventBus));
}
```

### 4.3 第三阶段：修复 WinFormsRenderPipeline fallback

#### 步骤 7：fallback 路径注入 EventBus

**文件：** `CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs`

```csharp
private static readonly Lazy<WinFormsRenderPipeline> _instance = new(() =>
{
    var context = WdUIComposer.LayoutContext;
    var orchestrator = AppServiceProvider.Resolve<EventBindingOrchestrator>();
    if (orchestrator != null)
        return new WinFormsRenderPipeline(context, orchestrator);
    
    // ★ 修复：从 AppServiceProvider 获取 EventBus
    var eventBus = AppServiceProvider.Resolve<TConfig.IEventBus>();
    if (eventBus == null)
        throw new InvalidOperationException("EventBus 未注册到 AppServiceProvider。");
    var options = new EventBindingOptions { EventBus = eventBus };
    return new WinFormsRenderPipeline(context, new EventBindingOrchestrator(options));
});
```

### 4.4 第四阶段：Debug 验证

#### 步骤 8：构建并运行

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\FileDriven\FileDriven.sln"
```

#### 步骤 9：验证场景

| 场景 | 预期行为 | 验证方式 |
|------|---------|---------|
| 正常启动 | 按钮点击有响应 | 手动点击 |
| Sideload 加载 | 按钮点击有响应 | 手动点击 |
| EventBus 未注册 | 启动时抛 `InvalidOperationException` | 观察日志 |
| Orchestrator 未注册 | fallback 路径自动注入 EventBus | 观察日志 |

---

## 五、影响范围

| 文件 | 变更类型 | 阶段 |
|------|---------|------|
| `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` | 修改：新增 EventBus 创建 | 一 |
| `CsharpPrj/Operation/WdUI/WinForms/WinFormsInitializer.cs` | 修改：移除 EventBus 创建 | 一 |
| `CsharpPrj/Operation/WdUI/WinForms/WinFormsEventBus.cs` | 废弃 | 一 |
| `CsharpPrj/Share/EventBindingOrchestrator/Stages/ConfigBindingStage.cs` | 修改：抛错替代静默跳过 | 二 |
| `CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` | 修改：入口验证 + 构造函数验证 | 二 |
| `CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs` | 修改：fallback 注入 EventBus | 三 |

---

## 六、依赖关系变化

```
重构前：
WdUI (Operation) ──创建──► WinFormsEventBus
FileDriven (应用) ──依赖──► WdUI（间接创建 EventBus）

重构后：
FileDriven (应用) ──创建──► WinFormsEventBus（Share 层实现）
WdUI (Operation) ──只注册──► IControlFinder / IControlFinderStrategy
```

---

## 七、风险与注意事项

1. **`WinFormsInitializer.Initialize()` 签名不变** — 对外接口不变，内部移除 EventBus 创建
2. **`FrameworkContext.UISubsystemEventBus` 设置时机提前** — 从 `WinFormsInitializer` 内部提前到调用之前
3. **Share 层 `WinFormsEventBus` 功能等价性** — 需确认不继承 `DefaultEventBusCore` 是否影响功能
4. **`IEventBusFallback` 接口** — 如果旧实现有 `IEventBusFallback` 的消费者，需迁移到 Share 层实现
