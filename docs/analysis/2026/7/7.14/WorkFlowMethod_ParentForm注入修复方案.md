# WorkFlowMethod ParentForm 注入修复方案

## 问题描述

### 日志证据

```
[WARNING] [ParameterBuilder] 父窗体为 null，无法查找 source 控件: target=value, source=valueATextBox
[WARNING] [ParameterBuilder] 参数值为空: target=value
```

### 根因分析

`ParameterBuilder` 管线中的 `ParentFormLookupStage` 通过 `ParentFormLookupHelper.FindParentForm(ctx.Sender)` 查找父窗体失败，返回 null。导致后续 `ParameterBindingResolveStage.ResolveControlValue` 无法通过 `FindControlByName(form, binding.Source)` 找到目标控件 `valueATextBox`，最终参数值始终为空。

`FindParentForm` 失败的原因：在 sideload 场景（`LoadSideContainerAsync`）中，控件被动态创建并添加到 `_sideContainer`，但控件树可能尚未完全挂载到 Form，或者 `FindForm()` 方法在特定条件下返回 null。

### 修复原则

**不构建回退逻辑** — 不在 `ParameterBuilder` 层打补丁（如从 sender 的 Parent 链向上查找），而是从源头修复：确保 `ParameterBuilder` 在事件触发时能获取到正确的 Form 引用。

---

## 修复方案（方案B）：在事件绑定阶段显式传递 Form 引用

### 核心思路

`EventBindingStageBase.CreateEventHandler` 在创建事件处理器时，持有当前 Form 的引用，将其注入到 `ParameterBuildContext` 中，替代从 sender 反向查找 `FindParentForm`。

### 修改点

#### 1. `EventBindingStageBase` — 持有 Form 引用

在 `EventBindingStageOptions` 中新增 `ParentForm` 属性：

```csharp
public class EventBindingStageOptions
{
    // ... 现有属性 ...

    /// <summary>
    /// 父窗体引用（可选）
    /// <para>★ 2026-07-14 新增：在事件绑定阶段显式传入 Form 引用，</para>
    /// <para>   替代从 sender 反向查找 FindParentForm。</para>
    /// <para>   解决 sideload 场景中控件树未完全挂载导致 FindForm() 失败的问题。</para>
    /// </summary>
    public object? ParentForm { get; set; }
}
```

#### 2. `EventBindingStageBase` — 将 Form 注入到参数构建委托

修改 `CreateEventHandler` 方法，在调用 `_parameterBuilder` 委托时，将 Form 引用作为额外参数传入：

```csharp
public EventHandler CreateEventHandler(
    object control, string eventName, string functionName,
    IReadOnlyList<ParameterBinding>? parameterBindings = null,
    string? output = null,
    Action<string, string, object?>? onResult = null,
    EventBindingContext? bindingContext = null)
{
    // 捕获 Form 引用（在事件处理器创建时确定）
    var parentForm = _parentForm;

    return (sender, args) =>
    {
        // ... 现有重入保护逻辑 ...

        var task = Task.Run(async () =>
        {
            try
            {
                if (_parameterBuilder == null)
                    throw new InvalidOperationException(...);

                // ★ 关键修改：将 parentForm 通过 sender 的上下文传递
                //   方案A：修改委托签名（破坏性变更）
                //   方案B：将 Form 设置到 sender 的某个可访问属性中
                //   方案C：使用 AsyncLocal<object?> 传递 Form 引用

                var parameters = _parameterBuilder(sender, parameterBindings);
                // ...
            }
        });
    };
}
```

#### 3. 委托签名变更（推荐方案）

修改 `_parameterBuilder` 委托签名，增加 Form 参数：

```csharp
// 旧签名
Func<object, IReadOnlyList<ParameterBinding>?, Dictionary<string, object>>? _parameterBuilder;

// 新签名
Func<object, IReadOnlyList<ParameterBinding>?, object?, Dictionary<string, object>>? _parameterBuilder;
//                                                       ^^^^^^^^
//                                                       新增 parentForm 参数
```

对应修改 `EventBindingStageOptions` 中的委托类型：

```csharp
public Func<object, IReadOnlyList<ParameterBinding>?, object?, Dictionary<string, object>>? ParameterBuilder { get; set; }
//                                                     ^^^^^^^^
```

#### 4. `ParameterBuilder.Build` — 接收 Form 引用

```csharp
// 新增重载
public Dictionary<string, object> Build(object sender, List<ParameterBinding>? parameterBindings, object? parentForm)
{
    var ctx = new ParameterBuildContext
    {
        Sender = sender,
        ParameterBindings = parameterBindings,
        ParentForm = parentForm,  // ★ 直接注入，不再通过 FindParentForm 查找
        TypeConversionService = _typeConversionService
    };

    _buildPipeline.ExecuteAsync(ctx).GetAwaiter().GetResult();
    return ctx.Parameters;
}
```

#### 5. `ParentFormLookupStage` — 优先使用注入的 Form

```csharp
protected override Task ExecuteCoreAsync(ParameterBuildContext ctx, CancellationToken cancellationToken)
{
    // ★ 优先使用上下文中已注入的 ParentForm
    if (ctx.ParentForm != null)
        return Task.CompletedTask;

    // ★ 回退到从 sender 查找（仅当注入的 Form 为 null 时）
    ctx.ParentForm = ParentFormLookupHelper.FindParentForm(ctx.Sender);

    // ★ 如果仍然为 null，直接抛出异常，不再静默返回
    if (ctx.ParentForm == null)
    {
        throw new InvalidOperationException(
            $"ParentForm 查找失败：sender={ctx.Sender}。"
            + " 请确保事件绑定阶段已正确注入 Form 引用。");
    }

    return Task.CompletedTask;
}
```

#### 6. `EventBindingPipelineFactory.CreateGenericPipeline` — 注入 Form

在创建 `EventBindingStageOptions` 时，从 `EventBindingOptions` 获取 `ParentForm`：

```csharp
var stageOptions = new EventBindingStageOptions
{
    EventExecutionService = eventExecutionService,
    OnEventResult = onEventResult,
    BinderStrategy = resolvedBinderStrategy,
    ParameterBuilder = options?.ParameterBuilder,
    OutputControlFinder = options?.OutputControlFinder,
    OutputWriter = options?.OutputWriter,
    ParentForm = options?.ParentForm  // ★ 新增：传递 Form 引用
};
```

#### 7. `EventBindingOptions` — 新增 ParentForm 属性

```csharp
public class EventBindingOptions
{
    // ... 现有属性 ...

    /// <summary>
    /// 父窗体引用（可选）
    /// <para>★ 2026-07-14 新增：在事件绑定阶段显式传入 Form 引用。</para>
    /// </summary>
    public object? ParentForm { get; set; }
}
```

#### 8. `FrameworkInitializer.InitializeWinFormsSubsystem` — 注入 Form

在创建 `EventBindingOptions` 时，从 `FrameworkContext` 获取 Form：

```csharp
var options = new EventBindingOptions
{
    EventBus = winFormsEventBus,
    ExecutionEngine = executionEngine,
    ExecutionServiceResolver = executionServiceResolver,
    IsScriptExecutionMode = false,
    OnEventResult = (functionName, status, detail) => { ... },
    ParameterBuilder = (sender, bindings) =>
        new ParameterBuilder().Build(sender, bindings as List<ParameterBinding>),
    OutputControlFinder = (sender, output) => OutputHelper.FindOutputControl(sender, output),
    OutputWriter = (target, value) => OutputHelper.WriteOutputToControl(target, value),
    // ★ 新增：从 FrameworkContext 获取 MainForm
    ParentForm = ctx.MainForm
};
```

#### 9. `WinFormsRenderPipeline` fallback 路径 — 注入 Form

```csharp
var fallbackOptions = new EventBindingOptions
{
    EventBus = eventBus,
    ParameterBuilder = (sender, bindings) =>
        new ParameterBuilder().Build(sender, bindings as List<ParameterBinding>),
    OutputControlFinder = (sender, output) => OutputHelper.FindOutputControl(sender, output),
    OutputWriter = (target, value) => OutputHelper.WriteOutputToControl(target, value),
    // ★ 新增：从 AppServiceProvider 获取 MainForm
    ParentForm = AppServiceProvider.Resolve<Form>()
};
```

---

## 关键位置直接抛出错误

### 1. `ParentFormLookupStage` — Form 为 null 时抛出

```csharp
if (ctx.ParentForm == null)
{
    throw new InvalidOperationException(
        $"ParentForm 查找失败：sender={ctx.Sender}。"
        + " 请确保事件绑定阶段已正确注入 Form 引用。"
        + " 检查 EventBindingStageOptions.ParentForm 是否已设置，"
        + " 或控件是否已被正确挂载到 Form 的控件树中。");
}
```

### 2. `ParameterBindingResolveStage.ResolveControlValue` — Form 为 null 时抛出

```csharp
if (form == null)
{
    throw new InvalidOperationException(
        $"ParentForm 为 null，无法查找 source 控件: target={binding.Target}, source={binding.Source}。"
        + " 请确保事件绑定阶段已正确注入 Form 引用，或控件已被正确挂载到 Form 的控件树中。");
}
```

### 3. `EventBindingStageBase.CreateEventHandler` — 委托为 null 时抛出（已有）

```csharp
if (_parameterBuilder == null)
    throw new InvalidOperationException(
        $"ParameterBuilder 委托未注入，无法构建函数 '{functionName}' 的参数。"
        + " 请确保 EventBindingStageOptions.ParameterBuilder 已设置。");
```

---

## 调用链总结
```
MainForm.LoadSideContainerAsync
  → WinFormsRenderPipeline.RenderSessionOnly
  → WinFormsRenderPipeline.BindEventsAsync
    → WinFormsRenderPipeline.ExecuteBindEventsStage
      → EventBindingOrchestrator.ExecuteWithWalkAsync
        → GenericEventBindingPipeline.ExecuteAsync
          → EventBindingStageBase.CreateEventHandler  ← 持有 _parentForm
            → (sender, args) => Task.Run
              → _parameterBuilder(sender, bindings, _parentForm)  ← 注入 Form
                → ParameterBuilder.Build(sender, bindings, parentForm)
                  → ParameterBuildContext.ParentForm = parentForm  ← 直接设置
                  → ParentFormLookupStage: ctx.ParentForm != null → 跳过查找
                  → ParameterBindingResolveStage: form != null → 正常查找控件
```


---

## 影响范围

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `EventBindingStageOptions` | 新增属性 | 增加 `ParentForm` 属性 |
| `EventBindingStageBase` | 修改字段+构造函数 | 存储 `_parentForm`，修改委托调用 |
| `EventBindingOptions` | 新增属性 | 增加 `ParentForm` 属性 |
| `EventBindingPipelineFactory` | 修改 | 传递 `ParentForm` |
| `FrameworkInitializer` | 修改 | 从 `ctx.MainForm` 获取 Form |
| `WinFormsRenderPipeline` | 修改 | fallback 路径注入 Form |
| `ParameterBuilder` | 新增重载 | 接收 `parentForm` 参数 |
| `ParameterBuilderStages` | 修改 | `ParentFormLookupStage` 优先使用注入值 |
| `IParameterBuilder` | 修改接口 | 新增 `Build` 重载 |
