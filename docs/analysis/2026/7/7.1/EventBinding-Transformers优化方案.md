# EventBinding 管线化改造 — 基于 Transformers + Pipeline 的优化方案

> **文档状态**：方案设计  
> **目标版本**：FileDriven v2.x  
> **关联模块**：EventTriggerFramework、WdUI、FileDriven.Execution  
> **基础框架**：`BasicMethod.Transformers`（`Modules.Transformers`）+ `Modules.Pipeline`

---

## 1. 问题回顾

### 1.1 当前日志暴露的问题

```
[WARN] RegisterSingleControl: control not found: titleLabel
[WARN] EventBinding_SkipControl → reason: "WinFormsEventBinder not available"
[WARN] MainForm_BindCommandEvents_executeButton_not_found
```

### 1.2 根因分析

| 问题 | 根因 | 代码位置 |
|------|------|---------|
| `RegisterSingleControl` 找不到控件 | `IControlFinder` 未正确注册或无法递归查找 `ConfigContainer` 内的嵌套控件 | `MainForm.cs:748` |
| `EventBinding_SkipControl` | `DefaultEventBindingService._winFormsEventBinder` 为 `null`，依赖注入断裂 | `DefaultEventBindingService.cs:155` |
| `executeButton_not_found` | MainLayout 配置中没有名为 `"executeButton"` 的控件 | `MainForm.cs:425` |
| sideload 配置按钮无响应 | 上述两个问题的综合结果 | — |

### 1.3 当前架构的痛点

```
手写递归遍历（BindControlsRecursive）
    ↓
if-else 分支判断（Events == null / Binder == null）
    ↓
静默跳过（日志级别 WARN，不抛异常）
    ↓
问题难以诊断
```

**核心问题**：事件绑定逻辑是**过程式代码**，没有统一的抽象层。每个步骤（查找控件→检查事件→检查绑定器→绑定事件→递归子控件）都是手写的 if-else，错误被静默吞没。

---

## 2. 改造目标

### 2.1 核心原则

1. **保持 JSON 驱动的事件→函数映射架构不变** — 这是正确的设计
2. **用 Transformers 框架替代手写的过程式逻辑** — 将绑定流程管线化
3. **错误显式化** — 管线失败时抛出明确异常，而非静默跳过
4. **可组合性** — 每个步骤独立可测试，可复用

### 2.2 改造范围

```
DefaultEventBindingService.BindControlsRecursive  →  Pipeline<ControlConfig, BindingResult>
WinFormsEventBinder.BindControlEvents              →  ITransformer<Control, int>
ControlFinderService.FindControl                   →  CompositeTransformer 链
EventTriggerEngine.TriggerEventAsync               →  Pipeline + Decorator
```

---

## 3. 详细改造方案

### 3.1 控件查找链 — `OrElse()` 回退组合

**当前代码**（`DefaultEventBindingService.cs:186-190`）：

```csharp
private static Control? FindControl(Form form, string name)
{
    if (form == null || string.IsNullOrWhiteSpace(name)) return null;
    return form.Controls.Find(name, true).FirstOrDefault();
}
```

**问题**：只用了 `form.Controls.Find` 一种策略，没有 fallback 机制。`ConfigContainer` 中的控件是通过渲染管线动态创建的，可能不在 `form.Controls` 的直接子树中。

**改造后**：

```csharp
// ============================================================
// 控件查找转换器链
// ============================================================

/// <summary>
/// 从 Form.Controls 中查找控件
/// </summary>
public class FormControlsFinder : ITransformer<(Form form, string name), Control?>
{
    public string Name => "FormControlsFinder";
    
    public Control? Transform((Form form, string name) input)
    {
        var (form, name) = input;
        return form.Controls.Find(name, true).FirstOrDefault();
    }
}

/// <summary>
/// 从 IControlRegistry 中查找控件
/// </summary>
public class RegistryControlFinder : ITransformer<(Form form, string name), Control?>
{
    public string Name => "RegistryControlFinder";
    
    public Control? Transform((Form form, string name) input)
    {
        var registry = ComponentRegistry.Instance.Resolve<IControlRegistry>();
        return registry?.Find(input.name) as Control;
    }
}

/// <summary>
/// 从 IControlFinder 中递归查找控件（支持 ConfigContainer 嵌套）
/// </summary>
public class RecursiveControlFinder : ITransformer<(Form form, string name), Control?>
{
    public string Name => "RecursiveControlFinder";
    
    public Control? Transform((Form form, string name) input)
    {
        var finder = ComponentRegistry.Instance.Resolve<IControlFinder>();
        return finder?.FindByName(input.form, input.name);
    }
}

/// <summary>
/// 控件查找回退链：Registry → RecursiveFinder → FormControls
/// 使用 OrElse() 组合，前一个返回 null 时自动尝试下一个
/// </summary>
public class FallbackControlFinder : ITransformer<(Form form, string name), Control?>
{
    private readonly ITransformer<(Form form, string name), Control?> _chain;
    
    public string Name => "FallbackControlFinder";
    
    public FallbackControlFinder()
    {
        // 构建回退链：Registry → Recursive → FormControls
        _chain = new RegistryControlFinder()
            .OrElse(new RecursiveControlFinder())
            .OrElse(new FormControlsFinder());
    }
    
    public Control? Transform((Form form, string name) input)
        => _chain.Transform(input);
}
```

> **说明**：`OrElse()` 是新增的 OR 语义组合子（见第 4.1 节），数学语义为 `f(x) ?? g(x)`，先尝试第一个转换器，返回 null 时自动尝试下一个。

---

### 3.2 事件绑定管线 — Pipeline<ControlConfig, BindingResult>

**当前代码**（`DefaultEventBindingService.cs:126-180`）：

```csharp
private void BindControlsRecursive(Form form, List<ControlConfig> controls)
{
    foreach (var controlConfig in controls)
    {
        // if-else 分支判断
        if (string.IsNullOrEmpty(controlConfig.Name)) { _skippedCount++; continue; }
        var control = FindControl(form, controlConfig.Name);
        if (control == null) { _skippedCount++; continue; }
        if (controlConfig.Events != null && _winFormsEventBinder != null)
        {
            _winFormsEventBinder.BindControlEvents(control, controlConfig.Events);
            _boundCount++;
        }
        else { _skippedCount++; }
        
        // 递归子控件
        if (controlConfig.Controls != null)
            BindControlsRecursive(form, controlConfig.Controls);
    }
}
```

**改造后**：

```csharp
// ============================================================
// 事件绑定管线 — 基于 Modules.Pipeline 的 Pipeline<TInput, TOutput>
// ============================================================

using Modules.Pipeline;

/// <summary>
/// 单个控件的事件绑定结果
/// </summary>
public class BindingResult
{
    public string ControlName { get; init; } = "";
    public bool Success { get; init; }
    public int EventCount { get; init; }
    public string? Error { get; init; }
    
    public static BindingResult Skipped(string name, string reason)
        => new() { ControlName = name, Success = false, Error = reason };
    
    public static BindingResult Bound(string name, int count)
        => new() { ControlName = name, Success = true, EventCount = count };
}

/// <summary>
/// 步骤1：验证控件名称
/// </summary>
public class ValidateControlNameStep : ITransformer<ControlConfig, ControlConfig>
{
    public string Name => "ValidateControlName";
    
    public ControlConfig Transform(ControlConfig input)
    {
        if (string.IsNullOrEmpty(input.Name))
            throw new ArgumentException("Control name is empty");
        return input;
    }
}

/// <summary>
/// 步骤2：查找控件（使用 OrElse 回退链）
/// </summary>
public class FindControlStep : ITransformer<(Form form, ControlConfig config), (Form form, Control control, ControlConfig config)>
{
    private readonly ITransformer<(Form form, string name), Control?> _finder;
    
    public string Name => "FindControl";
    
    public FindControlStep(ITransformer<(Form form, string name), Control?> finder)
    {
        _finder = finder;
    }
    
    public (Form form, Control control, ControlConfig config) Transform(
        (Form form, ControlConfig config) input)
    {
        var control = _finder.Transform((input.form, input.config.Name!));
        if (control == null)
            throw new KeyNotFoundException($"Control '{input.config.Name}' not found");
        return (input.form, control, input.config);
    }
}

/// <summary>
/// 步骤3：绑定事件
/// </summary>
public class BindEventsStep : ITransformer<(Form form, Control control, ControlConfig config), BindingResult>
{
    private readonly IWinFormsEventBinder? _binder;
    
    public string Name => "BindEvents";
    
    public BindEventsStep(IWinFormsEventBinder? binder)
    {
        _binder = binder;
    }
    
    public BindingResult Transform((Form form, Control control, ControlConfig config) input)
    {
        var (_, control, config) = input;
        
        if (config.Events == null)
            return BindingResult.Skipped(control.Name, "No events defined");
        
        if (_binder == null)
            return BindingResult.Skipped(control.Name, "WinFormsEventBinder not available");
        
        var eventCount = CountEvents(config.Events);
        _binder.BindControlEvents(control, config.Events);
        return BindingResult.Bound(control.Name, eventCount);
    }
}

/// <summary>
/// 事件绑定管线（完整流程）
/// 使用 Modules.Pipeline 的 Pipeline<TInput, TOutput> + IPipelineStage
/// </summary>
public class ControlBindingPipeline : ITransformer<(Form form, ControlConfig config), BindingResult>
{
    private readonly Pipeline<(Form form, ControlConfig config), BindingResult> _pipeline;
    
    public string Name => "ControlBindingPipeline";
    
    public ControlBindingPipeline(IWinFormsEventBinder? binder, 
        ITransformer<(Form form, string name), Control?>? finder = null)
    {
        finder ??= new FallbackControlFinder();
        
        // 使用 Modules.Pipeline 的 Pipeline<TInput, TOutput>
        // 每个步骤通过 ToPipelineStep() 适配为 IPipelineStage
        _pipeline = new Pipeline<(Form form, ControlConfig config), BindingResult>();
        _pipeline.RegisterStage(new ValidateControlNameStep().ToPipelineStep());
        _pipeline.RegisterStage(new FindControlStep(finder).ToPipelineStep());
        _pipeline.RegisterStage(new BindEventsStep(binder).ToPipelineStep());
    }
    
    public BindingResult Transform((Form form, ControlConfig config) input)
    {
        var result = _pipeline.Execute(input);
        if (result.IsSuccess && result.Data != null)
            return result.Data;
        if (result.HasErrors)
            throw new InvalidOperationException(
                $"Binding failed: {string.Join("; ", result.Errors.GetAll().Select(e => e.Message))}");
        return BindingResult.Skipped(input.config.Name ?? "unknown", "Pipeline returned no data");
    }
}
```

**改造后的 `DefaultEventBindingService`**：

```csharp
public class DefaultEventBindingService : IEventBindingService
{
    private readonly ControlBindingPipeline _pipeline;
    private readonly List<BindingResult> _results = new();
    
    public IWinFormsEventBinder? WinFormsEventBinder { get; set; }
    
    public DefaultEventBindingService(...)
    {
        // 延迟初始化管线（WinFormsEventBinder 可能尚未注入）
        _pipeline = new ControlBindingPipeline(
            () => WinFormsEventBinder,
            new FallbackControlFinder());
    }
    
    public void BindEvents(Form form, UIConfiguration config)
    {
        _results.Clear();
        
        foreach (var view in config.Views)
        {
            BindControlsRecursive(form, view.Controls);
        }
        
        // 输出汇总
        var successCount = _results.Count(r => r.Success);
        var failCount = _results.Count(r => !r.Success);
        Log($"绑定完成: {successCount} 成功, {failCount} 失败");
        
        // 如果有失败，记录详细原因
        foreach (var failed in _results.Where(r => !r.Success))
        {
            Log($"  [{failed.ControlName}] {failed.Error}");
        }
    }
    
    private void BindControlsRecursive(Form form, List<ControlConfig> controls)
    {
        foreach (var config in controls)
        {
            var result = _pipeline.Transform((form, config));
            _results.Add(result);
            
            // 递归子控件
            if (config.Controls != null)
                BindControlsRecursive(form, config.Controls);
        }
    }
}
```

---

### 3.3 WinFormsEventBinder 装饰器增强

**当前代码**（`WinFormsEventBinder.cs:100-147`）：

```csharp
public void BindControlEvents(Control control, EventConfig eventsConfig)
{
    // 手写遍历标准事件映射
    var standardEvents = new (string name, Func<EventConfig, List<string>?> getter)[]
    {
        ("click", cfg => cfg.Click),
        ("doubleclick", cfg => cfg.DoubleClick),
        // ...
    };
    
    foreach (var (name, getter) in standardEvents)
    {
        var funcNames = getter(eventsConfig);
        if (funcNames is { Count: > 0 })
        {
            BindSingleEvent(control, name, funcNames[0], eventsConfig);
            boundCount++;
        }
    }
}
```

**改造后**：

```csharp
// ============================================================
// 事件绑定转换器
// ============================================================

/// <summary>
/// 事件绑定转换器 — 将 EventConfig 绑定到 Control
/// </summary>
public class EventBinderTransformer : ITransformer<(Control control, EventConfig config), int>
{
    public string Name => "EventBinder";
    
    public int Transform((Control control, EventConfig config) input)
    {
        var (control, config) = input;
        var boundCount = 0;
        
        // 标准事件映射表（可配置）
        var standardEvents = new (string name, Func<EventConfig, List<string>?> getter)[]
        {
            ("click",               cfg => cfg.Click),
            ("doubleclick",         cfg => cfg.DoubleClick),
            ("textchanged",         cfg => cfg.TextChanged),
            ("checkedchanged",      cfg => cfg.CheckedChanged),
            ("selectedindexchanged",cfg => cfg.SelectedIndexChanged),
            ("valuechanged",        cfg => cfg.ValueChanged),
        };
        
        foreach (var (name, getter) in standardEvents)
        {
            var funcNames = getter(config);
            if (funcNames is { Count: > 0 })
            {
                BindSingleEvent(control, name, funcNames[0], config);
                boundCount++;
            }
        }
        
        // CustomEvents
        if (config.CustomEvents is { Count: > 0 })
        {
            foreach (var kvp in config.CustomEvents)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    BindSingleEvent(control, kvp.Key, kvp.Value, config);
                    boundCount++;
                }
            }
        }
        
        return boundCount;
    }
    
    private void BindSingleEvent(Control control, string eventName, 
        string functionName, EventConfig config)
    {
        // ... 反射绑定逻辑（与当前实现相同）
    }
}

// ============================================================
// 使用装饰器增强
// ============================================================

// 带日志的绑定
var loggedBinder = new EventBinderTransformer()
    .Decorate(new LogDecorator());

// 带重试的绑定（反射偶尔失败时自动重试）
var resilientBinder = new EventBinderTransformer()
    .Decorate(new DecoratorChain()
        .Add(new LogDecorator())
        .Add(new RetryDecorator(2)));

// 在 WinFormsEventBinder 中使用
public class WinFormsEventBinder : IWinFormsEventBinder
{
    private readonly ITransformer<(Control control, EventConfig config), int> _binder;
    
    public WinFormsEventBinder()
    {
        _binder = new EventBinderTransformer()
            .Decorate(new LogDecorator());
    }
    
    public void BindControlEvents(Control control, EventConfig eventsConfig)
    {
        var count = _binder.Transform((control, eventsConfig));
        TransformContext.Current?.Log(
            $"已绑定 {count} 个事件到控件 '{control.Name}'");
    }
}
```

---

### 3.4 事件触发管线 — Pipeline + Decorator

**当前代码**（`EventTriggerEngine.cs` 的模板方法）：

```
查找绑定 → 提取输入值 → 执行函数 → 写入输出
```

**改造后**：

```csharp
// ============================================================
// 事件触发管线 — 基于 Modules.Pipeline
// ============================================================

using Modules.Pipeline;

/// <summary>
/// 步骤1：查找事件绑定
/// </summary>
public class FindEventBindingStep : ITransformer<string, EventBinding>
{
    private readonly IEventBindingRepository _repository;
    public string Name => "FindEventBinding";
    
    public EventBinding Transform(string controlName)
    {
        var binding = _repository.Find(controlName);
        if (binding == null)
            throw new KeyNotFoundException($"No binding found for '{controlName}'");
        return binding;
    }
}

/// <summary>
/// 步骤2：提取输入值
/// </summary>
public class ExtractInputsStep : ITransformer<EventBinding, FunctionCall>
{
    private readonly IControlService _controlService;
    public string Name => "ExtractInputs";
    
    public FunctionCall Transform(EventBinding binding)
    {
        var inputs = binding.Inputs?
            .Select(name => _controlService.GetControlValue(name))
            .ToList();
        return new FunctionCall(binding.FunctionName, inputs);
    }
}

/// <summary>
/// 步骤3：执行函数
/// </summary>
public class ExecuteFunctionStep : ITransformer<FunctionCall, FunctionResult>
{
    private readonly IFunctionRegistry _registry;
    public string Name => "ExecuteFunction";
    
    public FunctionResult Transform(FunctionCall call)
    {
        var func = _registry.Find(call.FunctionName);
        if (func == null)
            throw new KeyNotFoundException($"Function '{call.FunctionName}' not found");
        return func.Execute(call.Inputs);
    }
}

/// <summary>
/// 步骤4：写入输出
/// </summary>
public class WriteOutputStep : ITransformer<FunctionResult, bool>
{
    private readonly IControlService _controlService;
    public string Name => "WriteOutput";
    
    public bool Transform(FunctionResult result)
    {
        if (string.IsNullOrEmpty(result.OutputTarget))
            return false;
        _controlService.SetControlValue(result.OutputTarget, result.Value?.ToString() ?? "");
        return true;
    }
}

/// <summary>
/// 完整事件触发管线
/// 使用 Modules.Pipeline 的 Pipeline<TInput, TOutput> + IPipelineStage
/// </summary>
public class EventTriggerPipeline : IAsyncTransformer<string, bool>
{
    private readonly Pipeline<string, bool> _pipeline;
    
    public string Name => "EventTriggerPipeline";
    
    public EventTriggerPipeline(
        IEventBindingRepository repository,
        IControlService controlService,
        IFunctionRegistry registry)
    {
        _pipeline = new Pipeline<string, bool>();
        _pipeline.RegisterStage(new FindEventBindingStep(repository).ToPipelineStep());
        _pipeline.RegisterStage(new ExtractInputsStep(controlService).ToPipelineStep());
        _pipeline.RegisterStage(new ExecuteFunctionStep(registry).ToPipelineStep());
        _pipeline.RegisterStage(new WriteOutputStep(controlService).ToPipelineStep());
    }
    
    public Task<bool> TransformAsync(string controlName, 
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var result = _pipeline.Execute(controlName);
            return result.IsSuccess && result.Data != null;
        }, ct);
    }
}
```

---

## 4. 新增基础组件

### 4.1 OrElseTransformer（OR 语义组合子）

> **位置**：`BasicMethod.Transformers`（`TransformerExtensions.cs`）
> **理由**：这是基础组合子，与 `Then()` 平级，属于 Transformers 框架的核心能力。

```csharp
/// <summary>
/// OR 语义组合子：f(x) ?? g(x)
/// 先尝试 first，返回 null 时尝试 second
/// </summary>
public class OrElseTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>
    where TOutput : class
{
    private readonly ITransformer<TInput, TOutput> _first;
    private readonly ITransformer<TInput, TOutput> _second;
    
    public string Name => $"{_first.Name} ?? {_second.Name}";
    
    public OrElseTransformer(
        ITransformer<TInput, TOutput> first,
        ITransformer<TInput, TOutput> second)
    {
        _first = first;
        _second = second;
    }
    
    public TOutput Transform(TInput input)
    {
        var result = _first.Transform(input);
        if (result != null) return result;
        
        TransformContext.Current?.Log(
            $"[{_first.Name}] → null, fallback to [{_second.Name}]");
        return _second.Transform(input);
    }
}

// 扩展方法
public static class OrElseExtensions
{
    public static ITransformer<TInput, TOutput> OrElse<TInput, TOutput>(
        this ITransformer<TInput, TOutput> first,
        ITransformer<TInput, TOutput> second)
        where TOutput : class
        => new OrElseTransformer<TInput, TOutput>(first, second);
}
```

### 4.2 Transformer → PipelineStage 适配器

> **位置**：`Modules.Pipeline`（新建 `TransformerAdapter.cs`）
> **理由**：`IPipelineStage` 定义在 `Modules.Pipeline` 中，适配器应与其适配的目标接口放在同一模块。

```csharp
// ============================================================
// TransformerAdapter.cs — Transformer → PipelineStage 适配器
// 将 BasicMethod.Transformers 的 ITransformer 适配为
// Modules.Pipeline 的 IPipelineStage，实现两层框架的桥接
// ============================================================

using Modules.Transformers;

namespace Modules.Pipeline
{
    /// <summary>
    /// Transformer → IPipelineStage 适配器
    /// 将 ITransformer 包装为 IPipelineStage，使其能注册到 Pipeline 中
    /// </summary>
    public class TransformerPipelineStep<TInput, TOutput> : IPipelineStage<TInput, TOutput>
        where TOutput : class
    {
        private readonly ITransformer<TInput, TOutput> _transformer;
        
        public string Name => _transformer.Name;
        
        /// <summary>执行顺序（默认 100）</summary>
        public int Order { get; set; } = 100;
        
        /// <summary>是否致命错误</summary>
        public bool IsFatal { get; set; } = true;
        
        public TransformerPipelineStep(ITransformer<TInput, TOutput> transformer)
        {
            _transformer = transformer;
        }
        
        public TOutput? Process(TInput input, ErrorCollector errors, PipelineContext context)
        {
            try
            {
                return _transformer.Transform(input);
            }
            catch (Exception ex)
            {
                errors.Add($"STEP_{Name.ToUpper()}_FAILED",
                    $"Transformer step '{Name}' failed: {ex.Message}",
                    IsFatal ? ErrorLevel.Fatal : ErrorLevel.Error,
                    ex);
                return null;
            }
        }
    }
    
    /// <summary>
    /// 扩展方法：将 ITransformer 适配为 IPipelineStage
    /// </summary>
    public static class TransformerPipelineStepExtensions
    {
        public static IPipelineStage<TInput, TOutput> ToPipelineStep<TInput, TOutput>(
            this ITransformer<TInput, TOutput> transformer)
            where TOutput : class
            => new TransformerPipelineStep<TInput, TOutput>(transformer);
    }
}
```

### 4.3 组件归属总表

| 组件 | 说明 | 归属模块 | 建议文件 |
|------|------|---------|---------|
| `OrElseTransformer` | OR 语义组合子 | `BasicMethod.Transformers` | `TransformerExtensions.cs` |
| `OrElse()` 扩展方法 | 便捷调用 | `BasicMethod.Transformers` | `TransformerExtensions.cs` |
| `TransformerPipelineStep` | Transformer → PipelineStage 适配器 | `Modules.Pipeline` | 新建 `TransformerAdapter.cs` |
| `ToPipelineStep()` | 扩展方法 | `Modules.Pipeline` | 新建 `TransformerAdapter.cs` |

---

## 5. 改造路线图

### Phase 1：基础组件（1-2 天）

1. 在 `BasicMethod.Transformers` 中添加 `OrElseTransformer` + `OrElse()` 扩展方法
2. 在 `Modules.Pipeline` 中添加 `TransformerPipelineStep` + `ToPipelineStep()` 适配器
3. 验证：单元测试覆盖新增组件

### Phase 2：控件查找链（1 天）

1. 实现 `FormControlsFinder`、`RegistryControlFinder`、`RecursiveControlFinder`
2. 实现 `FallbackControlFinder`（OrElse 组合）
3. 替换 `DefaultEventBindingService.FindControl` 方法
4. 验证：`RegisterSingleControl` 不再报 `control not found`

### Phase 3：事件绑定管线（2-3 天）

1. 实现 `ValidateControlNameStep`、`FindControlStep`、`BindEventsStep`
2. 实现 `ControlBindingPipeline`
3. 重构 `DefaultEventBindingService.BindControlsRecursive`
4. 实现 `EventBinderTransformer` + 装饰器
5. 验证：sideload 配置的按钮点击正常响应

### Phase 4：事件触发管线（2-3 天）

1. 实现 `FindEventBindingStep`、`ExtractInputsStep`、`ExecuteFunctionStep`、`WriteOutputStep`
2. 实现 `EventTriggerPipeline`
3. 替换 `EventTriggerEngine` 的模板方法
4. 验证：全链路事件触发正常

### Phase 5：清理与文档（1 天）

1. 删除废弃的 `EventTriggerEngine` 模板方法
2. 更新依赖注入配置
3. 更新文档

---

## 6. 预期效果

### 6.1 问题修复

| 当前问题 | 修复方式 | 预期效果 |
|---------|---------|---------|
| `RegisterSingleControl` 找不到控件 | OrElse 回退链覆盖三种查找策略 | 所有控件都能被找到 |
| `EventBinding_SkipControl` | Pipeline 的 `ErrorCollector` + 显式错误收集 | 绑定失败时记录明确错误原因 |
| 按钮无响应 | 上述两个问题修复后自动解决 | 按钮点击正常触发函数 |

### 6.2 架构提升

```
改造前：过程式 if-else + 静默跳过
改造后：声明式 Pipeline + 显式错误传播 + 装饰器横切关注点
```

| 维度 | 改造前 | 改造后 |
|------|--------|--------|
| 可读性 | 手写递归 + 分支判断 | Pipeline 步骤声明 |
| 可测试性 | 需要 mock 整个 Form | 每步独立测试 |
| 可扩展性 | 修改模板方法 | 添加/移除 Pipeline 步骤 |
| 错误诊断 | WARN 日志，静默跳过 | ErrorCollector + 详细错误原因 |
| 横切关注点 | 手写 try-catch | Decorator 链声明式配置 |

---

## 7. 与现有架构的关系

```
┌─────────────────────────────────────────────────────┐
│  JSON 配置层（声明式）                                │
│  { events: { click: ["Math_Add"] } }                │
└──────────────────────┬──────────────────────────────┘
                       ↓
┌─────────────────────────────────────────────────────┐
│  Transformers 管线层（新增编排层）                     │
│  ┌─────────────────────────────────────────────┐    │
│  │ BasicMethod.Transformers（基础组合子）        │    │
│  │  ├─ Then() — 串联组合                        │    │
│  │  ├─ OrElse() — 回退组合（新增）               │    │
│  │  └─ Decorate() — 装饰器                      │    │
│  └──────────────────┬──────────────────────────┘    │
│                     ↓                                │
│  ┌─────────────────────────────────────────────┐    │
│  │ Modules.Pipeline（管线编排器）                │    │
│  │  ├─ Pipeline<TInput, TOutput>               │    │
│  │  ├─ IPipelineStage                          │    │
│  │  └─ TransformerPipelineStep（适配器·新增）    │    │
│  └─────────────────────────────────────────────┘    │
└──────────────────────┬──────────────────────────────┘
                       ↓
┌─────────────────────────────────────────────────────┐
│  WinForms 事件绑定层（保持）                           │
│  WinFormsEventBinder.BindControlEvents               │
└──────────────────────┬──────────────────────────────┘
                       ↓
┌─────────────────────────────────────────────────────┐
│  函数执行层（保持）                                    │
│  FunctionStorageRegistry.Execute                     │
└─────────────────────────────────────────────────────┘
```

**关键点**：
- `BasicMethod.Transformers`（层1）提供基础组合子（`Then`、`OrElse`、`Decorate`），**不包含 Pipeline 编排逻辑**
- `Modules.Pipeline`（层2）提供管线编排器（`Pipeline<TInput, TOutput>`、`IPipelineStage`），**是已有的成熟框架**
- `TransformerPipelineStep` 适配器桥接两层，使 Transformer 能注册到 Pipeline 中
- Transformers 管线层是**新增的编排层**，不替代现有的事件绑定和函数执行逻辑，而是将手写的过程式编排替换为声明式的 Pipeline

---

## 8. 风险与注意事项

1. **Pipeline 性能开销**：每个控件绑定都经过 Pipeline 步骤，会有少量性能开销。对于 UI 初始化场景（毫秒级），可以忽略不计。
2. **递归深度**：`BindControlsRecursive` 的递归逻辑保留在 Pipeline 外部，避免 Pipeline 步骤中嵌套递归导致栈溢出。
3. **向后兼容**：`IEventBindingService` 接口不变，`DefaultEventBindingService` 的公开 API 不变，调用方无需修改。
4. **依赖注入**：`WinFormsEventBinder` 的延迟注入机制保持不变，Pipeline 在构造时接收工厂委托而非实例。
5. **模块依赖**：使用 `TransformerPipelineStep` 适配器的项目需要同时引用 `BasicMethod.Transformers` 和 `Modules.Pipeline`。
