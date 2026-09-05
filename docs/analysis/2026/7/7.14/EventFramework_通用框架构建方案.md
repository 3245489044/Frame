# EventFramework 通用事件框架构建方案

> **分析来源**：`CsharpPrj\Share\EventBindingOrchestrator\Invoke\` → 提取通用模式到 `CsharpPrj\Modules\EventFramework\`
> **创建日期**：2026-07-13
> **状态**：规划中

---

## 一、概述

### 1.1 目标

从 `Share.EventBindingOrchestrator.Invoke` 的具体实现中，抽象出 **10 种通用事件处理模式**，提取到 `Modules.EventFramework`，构建一个不依赖任何 UI 框架的通用事件框架。

### 1.2 设计原则

| 原则 | 说明 |
|------|------|
| **零 UI 依赖** | 不依赖 WinForms/WPF/Avalonia 等任何 UI 框架类型 |
| **策略化** | 可变行为通过策略接口注入，框架提供默认实现 |
| **模型化** | 数据模型与行为分离，模型可序列化 |
| **声明式** | 支持声明式验证和绑定配置 |
| **可扩展** | 通过接口 + 委托支持自定义扩展 |

### 1.3 架构分层

```
┌─────────────────────────────────────────────────┐
│             应用层 (Operation/Plate)              │
│   WinFormsEventBus, WinFormsValueAccessor, ...   │
├─────────────────────────────────────────────────┤
│         Share.EventBindingOrchestrator           │
│   管线阶段, 上下文, WinForms 反射辅助, 输出写入   │
├─────────────────────────────────────────────────┤
│         Modules.EventFramework ★ 通用框架        │
│   策略接口, 数据模型, 通用工具, 事件总线核心       │
├─────────────────────────────────────────────────┤
│           BasicMethod / TConfig / CSG            │
│              基础库和配置模型                      │
└─────────────────────────────────────────────────┘
```

---

## 二、10 种通用模式详解

### 模式 1：事件绑定策略（Event Binding Strategy）

**问题**：不同 UI 框架（WinForms/WPF/Avalonia）绑定事件的方式不同，Invoke 中 `ReflectionEventBinderStrategy` 使用反射绑定 WinForms 事件。

**通用化方案**：定义策略接口，框架不提供默认实现（由具体 UI 层提供）。

```csharp
// EventFramework/Abstractions/IEventBinderStrategy.cs
namespace Modules.EventFramework.Abstractions;

/// <summary>
/// 通用事件绑定策略接口
/// <para>将事件绑定逻辑抽象为策略，支持不同的 UI 框架。</para>
/// <para>方法签名完全通用（object, string, Delegate），不依赖任何 UI 框架类型。</para>
/// </summary>
public interface IEventBinderStrategy
{
    /// <summary>绑定事件到目标对象</summary>
    bool BindEvent(object target, string eventName, Delegate handler);

    /// <summary>从目标对象解绑事件</summary>
    bool UnbindEvent(object target, string eventName, Delegate handler);

    /// <summary>判断是否可处理指定目标对象</summary>
    bool CanHandle(object target);
}
```

**使用场景**：
- WinForms 实现：通过反射调用 `control.GetType().GetEvent(eventName).AddEventHandler()`
- WPF 实现：通过 `UIElement.AddHandler(RoutedEvent, Delegate)`
- 测试实现：直接维护 `Dictionary<string, Delegate>` 字典

---

### 模式 2：控件查找策略（Control Finder Strategy）

**问题**：不同 UI 框架查找子控件的方式不同，Invoke 中 `ReflectionControlFinderStrategy` 使用反射查找 WinForms 子控件。

**通用化方案**：定义策略接口，框架不提供默认实现。

```csharp
// EventFramework/Abstractions/IControlFinderStrategy.cs
namespace Modules.EventFramework.Abstractions;

/// <summary>
/// 通用控件查找策略接口
/// <para>将控件查找逻辑抽象为策略，支持不同的 UI 框架。</para>
/// </summary>
public interface IControlFinderStrategy
{
    /// <summary>获取父对象的子对象列表</summary>
    IEnumerable<object> GetChildObjects(object parent);

    /// <summary>判断是否可处理指定对象</summary>
    bool CanHandle(object target);
}
```

**使用场景**：
- WinForms 实现：通过反射读取 `control.Controls` 属性
- WPF 实现：通过 `VisualTreeHelper.GetChildrenCount()` / `GetChild()`
- 测试实现：直接返回预定义的子对象列表

---

### 模式 3：事件映射注册中心（Event Mapping Registry）

**问题**：Invoke 中 `DefaultEventMappingRegistry` 管理"事件名 → 事件访问器"的映射，结合 `IEventNameNormalizer` 做事件名规范化。这是一个通用需求。

**通用化方案**：提取为通用接口和实现，依赖 `IEventNameNormalizer`（已在 EventFramework）。

```csharp
// EventFramework/Abstractions/IEventMappingRegistry.cs
namespace Modules.EventFramework.Abstractions;

/// <summary>
/// 事件访问器 — 封装事件绑定的具体行为
/// </summary>
/// <param name="Handler">事件处理委托实例</param>
/// <param name="EventDelegateType">事件委托类型，null 时使用反射推断</param>
public record EventAccessor(Delegate Handler, Type? EventDelegateType = null);

/// <summary>
/// 事件映射条目
/// </summary>
public record EventMappingEntry(string EventName, EventAccessor Accessor, int Priority = 100);

/// <summary>
/// 通用事件映射注册中心
/// <para>管理事件名到事件访问器的映射关系。</para>
/// <para>内部依赖 <see cref="IEventNameNormalizer"/> 完成事件名规范化。</para>
/// </summary>
public interface IEventMappingRegistry
{
    void RegisterMapping(string eventName, EventAccessor accessor, int priority = 100);
    void RegisterMappings(IEnumerable<EventMappingEntry> entries);
    EventAccessor? GetAccessor(string eventName);
    IEnumerable<string> GetRegisteredEvents();
    bool UnregisterMapping(string eventName);
    void Clear();
}
```

```csharp
// EventFramework/DefaultEventMappingRegistry.cs
namespace Modules.EventFramework;

/// <summary>
/// 默认事件映射注册中心实现
/// <para>基于 ConcurrentDictionary 实现，线程安全。</para>
/// </summary>
public class DefaultEventMappingRegistry : IEventMappingRegistry
{
    private readonly IEventNameNormalizer _normalizer;
    private readonly ConcurrentDictionary<string, List<EventMappingEntry>> _mappings = new(StringComparer.OrdinalIgnoreCase);

    public DefaultEventMappingRegistry(IEventNameNormalizer? normalizer = null)
    {
        _normalizer = normalizer ?? new DefaultEventNameNormalizer();
    }

    public void RegisterMapping(string eventName, EventAccessor accessor, int priority = 100)
    {
        var normalizedName = _normalizer.ToPascalCase(eventName);
        var entry = new EventMappingEntry(normalizedName, accessor, priority);
        _mappings.AddOrUpdate(normalizedName,
            _ => new List<EventMappingEntry> { entry },
            (_, list) => { list.Add(entry); return list; });
    }

    public EventAccessor? GetAccessor(string eventName)
    {
        var normalizedName = _normalizer.ToPascalCase(eventName);
        if (_mappings.TryGetValue(normalizedName, out var entries))
            return entries.OrderBy(e => e.Priority).First().Accessor;
        return null;
    }

    // ... 其他实现
}
```

---

### 模式 4：事件绑定信息模型（Event Binding Info）

**问题**：Invoke 中 `IEventBindingInfo` + `EventBindingInfo` 描述"控件事件 → 函数"的绑定关系，这是一个通用数据模型。

**通用化方案**：提取为通用接口和模型类。

```csharp
// EventFramework/Abstractions/IEventBindingInfo.cs
namespace Modules.EventFramework.Abstractions;

/// <summary>
/// 通用事件绑定信息接口
/// <para>描述事件与函数之间的绑定关系。</para>
/// </summary>
public interface IEventBindingInfo
{
    /// <summary>绑定的函数名（取 FunctionNames 的第一个元素）</summary>
    string FunctionName { get; }

    /// <summary>事件类型（如 "click", "textchanged"）</summary>
    string EventType { get; }

    /// <summary>输入参数列表</summary>
    IReadOnlyList<string> Inputs { get; }

    /// <summary>输出目标</summary>
    string? Output { get; }

    /// <summary>所有绑定的函数名列表（支持多函数绑定）</summary>
    IReadOnlyList<string> FunctionNames { get; }
}
```

```csharp
// EventFramework/Models/EventBindingInfo.cs
namespace Modules.EventFramework.Models;

public class EventBindingInfo : IEventBindingInfo
{
    public string FunctionName => FunctionNames is { Count: > 0 } ? FunctionNames[0] : "";
    public string EventType { get; set; } = "";
    public IReadOnlyList<string> Inputs { get; set; } = Array.Empty<string>();
    public string? Output { get; set; }
    public IReadOnlyList<string> FunctionNames { get; set; } = Array.Empty<string>();
}
```

---

### 模式 5：事件请求模型（Event Request）

**问题**：Invoke 中 `EventRequest` 作为统一事件请求 DTO，支持多种事件来源。这是一个通用模型。

**通用化方案**：提取为通用模型类，移除 Share 层特定依赖。

```csharp
// EventFramework/Models/EventRequest.cs
namespace Modules.EventFramework.Models;

/// <summary>
/// 通用事件请求
/// <para>所有事件处理逻辑通过此模型统一输入。</para>
/// <para>支持多种事件来源：config/code/command/resize/binding。</para>
/// </summary>
public class EventRequest
{
    /// <summary>事件来源标识</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>事件类型（如 "click"/"resize"/"textchanged"）</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>目标对象名称</summary>
    public string? TargetName { get; init; }

    /// <summary>事件数据</summary>
    public object? Data { get; init; }

    /// <summary>事件参数字典</summary>
    public Dictionary<string, object>? Parameters { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    // 静态工厂方法
    public static EventRequest FromConfig(string eventType, string? targetName = null, object? data = null)
        => new() { Source = "config", EventType = eventType, TargetName = targetName, Data = data };

    public static EventRequest FromCode(string eventType, string? targetName = null, object? data = null)
        => new() { Source = "code", EventType = eventType, TargetName = targetName, Data = data };

    public static EventRequest FromCommand(string eventType, string? targetName = null, Dictionary<string, object>? parameters = null)
        => new() { Source = "command", EventType = eventType, TargetName = targetName, Parameters = parameters };
}
```

---

### 模式 6：事件绑定声明（Event Binding Declaration）

**问题**：Invoke 中 `EventBindingDeclaration` 作为声明式事件绑定配置，不依赖具体配置模型。这是一个通用模型。

**通用化方案**：提取为通用模型类，实现 `IDeclaration` 接口。

```csharp
// EventFramework/Models/EventBindingDeclaration.cs
namespace Modules.EventFramework.Models;

/// <summary>
/// 通用事件绑定声明
/// <para>声明式事件绑定配置，不依赖具体配置模型。</para>
/// </summary>
public class EventBindingDeclaration : IDeclaration
{
    public string Name => $"{TargetName}.{EventName}";
    public string Description => $"绑定 {TargetName}.{EventName} → {FunctionName}";
    public string DeclarationType => "event-binding";

    /// <summary>目标对象名称（如控件名）</summary>
    public string TargetName { get; init; } = "";

    /// <summary>事件名称</summary>
    public string EventName { get; init; } = "";

    /// <summary>绑定的函数名</summary>
    public string FunctionName { get; init; } = "";

    /// <summary>参数映射列表</summary>
    public IReadOnlyList<ParameterMapping> Parameters { get; init; } = Array.Empty<ParameterMapping>();

    /// <summary>输出目标</summary>
    public string? OutputTarget { get; init; }

    /// <summary>输出映射（函数名 → 目标）</summary>
    public IReadOnlyDictionary<string, string>? OutputMapping { get; init; }
}
```

---

### 模式 7：事件绑定验证器（Event Binding Validator）

**问题**：Invoke 中 `EventBindingValidator` 对事件绑定声明进行声明式验证，这是一个通用需求。

**通用化方案**：提取为通用验证器，基于 `BasicMethod.Declarative` 框架。

```csharp
// EventFramework/EventBindingValidator.cs
namespace Modules.EventFramework;

/// <summary>
/// 通用事件绑定声明验证器
/// <para>使用声明式约束验证 EventBindingDeclaration。</para>
/// </summary>
public class EventBindingValidator
{
    private readonly DeclarationValidator<EventBindingDeclaration> _validator;

    /// <summary>创建默认验证器（TargetName/EventName/FunctionName 非空检查）</summary>
    public EventBindingValidator()
    {
        _validator = new DeclarationValidator<EventBindingDeclaration>("EventBindingValidator")
            .AddConstraint(new RequiredConstraint("TargetName", "目标对象名称不能为空"))
            .AddConstraint(new PredicateConstraint<EventBindingDeclaration>(
                "EventName", "事件名称不能为空",
                d => !string.IsNullOrWhiteSpace(d.EventName)))
            .AddConstraint(new PredicateConstraint<EventBindingDeclaration>(
                "FunctionName", "函数名称不能为空",
                d => !string.IsNullOrWhiteSpace(d.FunctionName)));
    }

    /// <summary>创建自定义验证器</summary>
    public EventBindingValidator(DeclarationValidator<EventBindingDeclaration> validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public ValidationResult Validate(EventBindingDeclaration declaration)
        => _validator.Validate(declaration);

    public IReadOnlyList<ValidationResult> ValidateAll(IEnumerable<EventBindingDeclaration> declarations)
        => _validator.ValidateAll(declarations);

    public void ValidateOrThrow(EventBindingDeclaration declaration)
    {
        var result = Validate(declaration);
        if (!result.IsValid)
            throw new InvalidOperationException(
                $"事件绑定声明验证失败 [{declaration.Name}]: {string.Join("; ", result.FailureReasons)}");
    }
}
```

---

### 模式 8：参数构建器（Parameter Builder）

**问题**：Invoke 中 `ParameterBuilder` 从参数绑定信息构建函数执行参数字典，支持 control/literal/expression 三种 SourceType。但硬编码了 WinForms 反射调用。

**通用化方案**：通过委托注入值解析逻辑，消除 WinForms 依赖。

```csharp
// EventFramework/ParameterBuilder.cs
namespace Modules.EventFramework;

/// <summary>
/// 通用参数构建器
/// <para>从参数绑定信息构建函数执行参数字典。</para>
/// <para>支持 control/literal/expression 三种 SourceType。</para>
/// <para>值解析逻辑通过委托注入，不依赖任何 UI 框架。</para>
/// </summary>
public class ParameterBuilder
{
    private readonly ITypeConversionService? _typeConversionService;

    /// <summary>
    /// 创建参数构建器
    /// </summary>
    /// <param name="typeConversionService">类型转换服务（可选）</param>
    public ParameterBuilder(ITypeConversionService? typeConversionService = null)
    {
        _typeConversionService = typeConversionService;
    }

    /// <summary>
    /// 构建参数字典
    /// </summary>
    /// <param name="sender">事件发送者</param>
    /// <param name="parameterBindings">参数绑定列表</param>
    /// <param name="controlValueResolver">控件值解析委托 (sender, sourceName) → value</param>
    /// <returns>参数字典</returns>
    public Dictionary<string, object> Build(
        object sender,
        List<ParameterBinding>? parameterBindings,
        Func<object, string, object?>? controlValueResolver = null)
    {
        var parameters = new Dictionary<string, object>();

        if (parameterBindings is { Count: > 0 })
        {
            foreach (var binding in parameterBindings)
            {
                object? resolvedValue = null;

                switch (binding.SourceType)
                {
                    case "control" when !string.IsNullOrEmpty(binding.Source):
                        resolvedValue = controlValueResolver?.Invoke(sender, binding.Source);
                        break;
                    case "literal" when !string.IsNullOrEmpty(binding.Source):
                        resolvedValue = binding.Source;
                        break;
                    case "expression" when !string.IsNullOrEmpty(binding.Source):
                        resolvedValue = binding.Source;
                        break;
                }

                parameters[binding.Target] = ConvertValue(binding, resolvedValue);
            }

            // 自动添加 sender 的当前值
            if (!parameters.ContainsKey("value") && controlValueResolver != null)
                parameters["value"] = controlValueResolver(sender, "");
        }
        else
        {
            // 回退：没有绑定信息时传 sender 自身的值
            parameters["value"] = controlValueResolver?.Invoke(sender, "") ?? sender.ToString();
        }

        return parameters;
    }

    private object ConvertValue(ParameterBinding binding, object? resolvedValue)
    {
        if (string.IsNullOrEmpty(binding.Type) || resolvedValue == null)
            return resolvedValue?.ToString() ?? string.Empty;

        var targetType = TypeResolver.Resolve(binding.Type);
        if (targetType == null || targetType == typeof(string))
            return resolvedValue;

        if (_typeConversionService != null)
        {
            var result = _typeConversionService.Convert(resolvedValue, targetType);
            if (result.IsSuccess && result.Value != null)
                return result.Value;
        }

        // 内置 TypeConverter 回退
        try
        {
            return System.ComponentModel.TypeDescriptor
                .GetConverter(targetType)
                .ConvertFromString(resolvedValue?.ToString() ?? string.Empty);
        }
        catch
        {
            return resolvedValue?.ToString() ?? string.Empty;
        }
    }
}
```

---

### 模式 9：控件树遍历器（Control Tree Walker）

**问题**：Invoke 中 `ControlTreeWalker` 遍历配置模型中的控件树，基于 `Framework.Tree.TreeWalker`。这是一个通用工具。

**通用化方案**：提取为通用工具类，依赖 `Modules.ConfigurationModels`。

```csharp
// EventFramework/ControlTreeWalker.cs
namespace Modules.EventFramework;

/// <summary>
/// 通用控件树遍历器
/// <para>基于 TreeWalker 实现，统一控件树遍历逻辑。</para>
/// <para>零 WinForms 依赖，纯配置逻辑。</para>
/// </summary>
public static class ControlTreeWalker
{
    private static readonly Lazy<TreeWalker<ControlConfig>> _walker =
        new(() => new TreeWalker<ControlConfig>(GetDirectChildren));

    /// <summary>获取 ControlConfig 的直接子控件列表</summary>
    public static IEnumerable<ControlConfig> GetDirectChildren(ControlConfig config)
    {
        if (config == null) yield break;

        if (config.Controls is { Count: > 0 })
            foreach (var child in config.Controls) yield return child;

        if (config.Panel1Controls is { Count: > 0 })
            foreach (var child in config.Panel1Controls) yield return child;

        if (config.Panel2Controls is { Count: > 0 })
            foreach (var child in config.Panel2Controls) yield return child;
    }

    /// <summary>遍历 UIConfiguration 中所有 View 的控件树</summary>
    public static void WalkAllViews(UIConfiguration config, Action<ControlConfig> action)
    {
        if (config?.Views == null) return;
        foreach (var view in config.Views)
            if (view.Controls != null)
                WalkControls(view.Controls, action);
    }

    /// <summary>递归遍历控件列表</summary>
    public static void WalkControls(List<ControlConfig> controls, Action<ControlConfig> action)
    {
        if (controls == null) return;
        foreach (var control in controls)
            _walker.Value.Walk(control, (node, _) => action(node));
    }

    /// <summary>异步遍历控件树</summary>
    public static async Task WalkControlsAsync(List<ControlConfig> controls, Func<ControlConfig, Task> asyncAction)
    {
        if (controls == null) return;
        foreach (var control in controls)
            await _walker.Value.WalkAsync(control, async (node, _) => await asyncAction(node));
    }

    /// <summary>按名称查找控件</summary>
    public static ControlConfig? FindControl(UIConfiguration config, string name)
    {
        ControlConfig? found = null;
        WalkAllViews(config, ctrl =>
        {
            if (found == null && string.Equals(ctrl.Name, name, StringComparison.OrdinalIgnoreCase))
                found = ctrl;
        });
        return found;
    }

    /// <summary>构建控件名称到配置的字典</summary>
    public static Dictionary<string, ControlConfig> BuildControlMap(UIConfiguration config)
    {
        var map = new Dictionary<string, ControlConfig>(StringComparer.OrdinalIgnoreCase);
        WalkAllViews(config, ctrl =>
        {
            if (!string.IsNullOrWhiteSpace(ctrl.Name) && !map.ContainsKey(ctrl.Name))
                map[ctrl.Name] = ctrl;
        });
        return map;
    }
}
```

---

### 模式 10：工作流定义模型（Workflow Definition）

**问题**：Invoke 中 `WorkflowDefinitionEntry` + `WorkflowNodeEntry` 作为工作流定义的数据模型，与 `TConfig.StepDefinition` 互转。

**通用化方案**：提取为通用模型类。

```csharp
// EventFramework/Models/WorkflowDefinitionEntry.cs
namespace Modules.EventFramework.Models;

/// <summary>
/// 通用工作流定义条目
/// </summary>
public class WorkflowDefinitionEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public List<WorkflowNodeEntry> Nodes { get; init; } = new();

    /// <summary>转换为执行计划</summary>
    public ExecutionPlan ToExecutionPlan() => new()
    {
        Id = Id,
        Name = Name,
        Steps = Nodes.Select(n => n.ToStepDefinition()).ToList()
    };
}

/// <summary>
/// 通用工作流节点条目
/// </summary>
public class WorkflowNodeEntry
{
    public string Id { get; init; } = string.Empty;
    public string? FunctionName { get; init; }
    public object? Parameters { get; init; }
    public string? OutputKey { get; init; }
    public string? OutputTarget { get; init; }

    /// <summary>转换为 StepDefinition</summary>
    public StepDefinition ToStepDefinition() => new()
    {
        Id = $"step_{FunctionName ?? Id}",
        Name = FunctionName ?? Id,
        StepType = "Function",
        FunctionName = FunctionName,
        Parameters = Parameters
    };
}
```

---

## 三、提取后的 EventFramework 完整结构

```
EventFramework/
├── EventFramework.csproj
│   ├── 已有引用：BasicMethod, TConfig, Microsoft.Extensions.Logging
│   └── 新增引用：CSG.Unified, ParameterResolver.Abstractions, Framework.Tree
│
├── Abstractions/
│   ├── IEventBus.cs                    (已有 — 类型别名指向 TConfig.IEventBus)
│   ├── IEventBusCore.cs                (已有)
│   ├── IEventBusFacade.cs              (已有)
│   ├── IEventNameNormalizer.cs         (已有)
│   ├── IEventMappingRegistry.cs        ★ 新增 — 事件映射注册中心
│   ├── IEventBinderStrategy.cs         ★ 新增 — 事件绑定策略
│   ├── IControlFinderStrategy.cs       ★ 新增 — 控件查找策略
│   └── IEventBindingInfo.cs            ★ 新增 — 事件绑定信息
│
├── Bus/
│   ├── DefaultEventBusCore.cs          (已有)
│   ├── DefaultEventBus.cs              (已有)
│   ├── EventBusFacade.cs               (已有)
│   ├── EventQueue.cs                   (已有)
│   ├── EventScope.cs                   (已有)
│   ├── SubscriptionToken.cs            (已有)
│   ├── DefaultCommandExecutor.cs       (已有)
│   ├── EventBusCoreDecoratorBase.cs    (已有)
│   └── Decorators/                     (已有)
│
├── Models/
│   ├── EventBindingInfo.cs             ★ 新增 — 事件绑定信息模型
│   ├── EventRequest.cs                 ★ 新增 — 统一事件请求 DTO
│   ├── EventBindingDeclaration.cs      ★ 新增 — 事件绑定声明
│   └── WorkflowDefinitionEntry.cs      ★ 新增 — 工作流定义模型
│
├── DefaultEventNameNormalizer.cs       (已有)
├── DefaultEventMappingRegistry.cs      ★ 新增 — 默认映射注册中心实现
├── EventBindingValidator.cs            ★ 新增 — 事件绑定验证器
├── ControlTreeWalker.cs                ★ 新增 — 控件树遍历器
└── ParameterBuilder.cs                 ★ 新增 — 参数构建器
```

---

## 四、依赖关系图

```
EventFramework
├── BasicMethod              (已有 — IDeclaration, IConstraint 等)
├── TConfig                  (已有 — IEventBus, EventContext, ExecutionResult 等)
├── Microsoft.Extensions.Logging (已有)
├── CSG.Unified              ★ 新增 — ParameterBinding
├── ParameterResolver.Abstractions ★ 新增 — ITypeConversionService, ParameterMapping
└── Framework.Tree           ★ 新增 — TreeWalker<T>
```

---

## 五、Share 层调整方案

### 5.1 移除的文件

提取到 EventFramework 后，Share 层需移除以下源文件：

| 文件 | 替换方式 |
|------|---------|
| `Abstractions/IEventMappingRegistry.cs` | 引用 `Modules.EventFramework.Abstractions.IEventMappingRegistry` |
| `Abstractions/IControlFinderStrategy.cs` | 引用 `Modules.EventFramework.Abstractions.IControlFinderStrategy` |
| `Abstractions/IWinFormsEventBinderStrategy.cs` | 引用 `Modules.EventFramework.Abstractions.IEventBinderStrategy` |
| `Invoke/DefaultEventMappingRegistry.cs` | 引用 `Modules.EventFramework.DefaultEventMappingRegistry` |
| `Invoke/EventBindingDeclaration.cs` | 引用 `Modules.EventFramework.Models.EventBindingDeclaration` |
| `Invoke/EventRequest.cs` | 引用 `Modules.EventFramework.Models.EventRequest` |
| `Invoke/EventBindingInfo.cs` | 引用 `Modules.EventFramework.Models.EventBindingInfo` |
| `Invoke/EventBindingValidator.cs` | 引用 `Modules.EventFramework.EventBindingValidator` |
| `Invoke/ParameterBuilder.cs` | 引用 `Modules.EventFramework.ParameterBuilder` |
| `Invoke/ControlTreeWalker.cs` | 引用 `Modules.EventFramework.ControlTreeWalker` |
| `Invoke/WorkflowDefinitionEntry.cs` | 引用 `Modules.EventFramework.Models.WorkflowDefinitionEntry` |

### 5.2 保留的文件（WinForms 特定）

| 文件 | 原因 |
|------|------|
| `Invoke/WinFormsEventBus.cs` | WinForms 事件绑定（DynamicMethod 包装非标准委托） |
| `Invoke/WinFormsValueAccessor.cs` | WinForms 值读写器 |
| `Invoke/OutputHelper.cs` | 写回 WinForms 控件（ListView 特殊处理、跨线程调度） |
| `Invoke/ReflectionEventBinderStrategy.cs` | 反射绑定 WinForms 事件 |
| `Invoke/ReflectionControlFinderStrategy.cs` | 反射查找 WinForms 子控件 |
| `Invoke/EventTriggerStage.cs` | 通过反射触发 WinForms 控件事件 |
| `Invoke/ControlLookupStage.cs` | 通过反射查找 WinForms 控件 |
| `Invoke/EventBindingResolver.cs` | 依赖 LoadedConfigInfo/UIConfiguration（Share 层特定） |
| `Invoke/EventBindingHelper.cs` | WinForms 反射辅助方法（FindForm/Controls.Find 等） |
| `Invoke/EventBindingStageBase.cs` | 依赖 OutputHelper/EventBindingContext（Share 层特定） |

### 5.3 命名空间迁移对照

| 原命名空间 | 新命名空间 |
|-----------|-----------|
| `Share.EventBindingOrchestrator.Abstractions.IEventMappingRegistry` | `Modules.EventFramework.Abstractions.IEventMappingRegistry` |
| `Share.EventBindingOrchestrator.Abstractions.IControlFinderStrategy` | `Modules.EventFramework.Abstractions.IControlFinderStrategy` |
| `Share.EventBindingOrchestrator.Abstractions.IEventBinderStrategy` | `Modules.EventFramework.Abstractions.IEventBinderStrategy` |
| `Share.EventBindingOrchestrator.Abstractions.IEventBindingInfo` | `Modules.EventFramework.Abstractions.IEventBindingInfo` |
| `Share.EventBindingOrchestrator.Parameters.EventBindingDeclaration` | `Modules.EventFramework.Models.EventBindingDeclaration` |
| `Share.EventBindingOrchestrator.Parameters.EventRequest` | `Modules.EventFramework.Models.EventRequest` |
| `Share.EventBindingOrchestrator.Models.EventBindingInfo` | `Modules.EventFramework.Models.EventBindingInfo` |
| `Share.EventBindingOrchestrator.Parameters.Binding.WorkflowDefinitionEntry` | `Modules.EventFramework.Models.WorkflowDefinitionEntry` |
| `Share.EventBindingOrchestrator.Parameters.EventBindingValidator` | `Modules.EventFramework.EventBindingValidator` |
| `Share.EventBindingOrchestrator.Parameters.ParameterBuilder` | `Modules.EventFramework.ParameterBuilder` |
| `Share.EventBindingOrchestrator.Utils.ControlTreeWalker` | `Modules.EventFramework.ControlTreeWalker` |
| `Share.EventBindingOrchestrator.Invoke.DefaultEventMappingRegistry` | `Modules.EventFramework.DefaultEventMappingRegistry` |

---

## 六、实施计划

### 第一阶段：接口 + 模型（无副作用）

1. 在 `EventFramework/Abstractions/` 新增 4 个接口文件
2. 在 `EventFramework/Models/` 新增 4 个模型文件
3. 更新 `EventFramework.csproj` 添加新增引用

### 第二阶段：实现类

4. 新增 `DefaultEventMappingRegistry.cs`
5. 新增 `EventBindingValidator.cs`
6. 新增 `ControlTreeWalker.cs`
7. 新增 `ParameterBuilder.cs`

### 第三阶段：Share 层适配

8. 移除 Share 层已提取的源文件
9. 更新 Share 层的 using 引用
10. 编译验证，修复引用断裂

---

## 七、设计决策记录

| 决策 | 选择 | 理由 |
|------|------|------|
| ParameterBuilder 的 WinForms 解耦 | 通过 `Func<object, string, object?>` 委托注入 | 保持通用性，调用方提供值解析逻辑 |
| EventBindingStageBase 是否提取 | 不提取完整基类，只提取通用模式到文档 | 基类强依赖 Share 层上下文和 OutputHelper，提取后反而增加复杂度 |
| EventBindingHelper 的纯逻辑提取 | 只提取 ResolveOutput/GetFirstInput/GetExtensionDataValue | 反射方法强依赖 WinForms 类型名 |
| IEventBinderStrategy 的默认实现 | 框架不提供默认实现 | 不同 UI 框架绑定方式差异大，无法统一 |
| ControlTreeWalker 的依赖 | 依赖 Modules.ConfigurationModels | EventFramework 在模块层，可引用配置模型 |
