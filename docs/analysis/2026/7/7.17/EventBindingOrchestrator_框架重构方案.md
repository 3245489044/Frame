# EventBindingOrchestrator 框架重构方案

> **日期**：2026-07-16
> **状态**：规划中
> **目标**：从框架层面系统性解决事件绑定流程中的拼凑式问题，消除冗余、统一规范、提升可维护性

---

## 目录

1. [问题全景](#1-问题全景)
2. [重构总览](#2-重构总览)
3. [阶段一：统一事件配置模型](#3-阶段一统一事件配置模型)
4. [阶段二：精简事件绑定管线](#4-阶段二精简事件绑定管线)
5. [阶段三：统一绑定编排](#5-阶段三统一绑定编排)
6. [阶段四：诊断日志集中化](#6-阶段四诊断日志集中化)
7. [文件变更清单](#7-文件变更清单)
8. [风险与注意事项](#8-风险与注意事项)

---

## 1. 问题全景

### 1.1 配置解析层

**涉及文件**：`CsharpPrj/Plate/TConfig/Models/EventConfig.cs`

- `KnownNonEventProperties` 硬编码排除列表（`eventname`, `handlertype`, `handlerparams`, `customevents`, `bindings`），维护成本高，新增属性容易遗漏
- 事件名保留 JSON 原始大小写（如 `"click"`），依赖下游 `ToPascalCase` 补救，导致大小写转换逻辑分散在多个位置
- `EventConfig` 同时保留旧属性（`EventName`, `HandlerType`, `HandlerParams`）和新属性（`EventsDictionary`），职责不清，容易混淆

### 1.2 事件绑定管线

**涉及文件**：
- `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/WinFormsEventBus.cs`
- `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/EventBusStages.cs`

- 5 个 Stage 管线（`EventNameNormalizationStage` → `EventInfoResolutionStage` → `DirectBindStage` → `DynamicMethodBindStage` → `SubscriptionRecordStage`），过于细碎
- `EventBusContext` 用 `IStorageCell` 存储中间结果，读写开销大，且与 `PipelineContextBase` 继承关系复杂
- `catch {}` 吞掉所有绑定失败异常，调试困难
- 诊断日志散落在各个 Stage 中，难以统一控制

### 1.3 绑定编排层

**涉及文件**：
- `CsharpPrj/Share/EventBindingOrchestrator/Invoke/ConfigBindingStage.cs`
- `CsharpPrj/Share/EventBindingOrchestrator/Invoke/DeclarationBindingStage.cs`
- `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventBindingIoC.cs`
- `CsharpPrj/Operation/BackEndDriven/IoC/EventBindingIoC.cs`

- `ConfigBindingStage` 遍历 `EventsDictionary`，`DeclarationBindingStage` 遍历 `Declarations`，两条路径逻辑重复
- 预解析逻辑（`ResolveWorkflow`, `ResolveDelegate`, `PrecomputeParameterSources`, `CreatePreResolvedHandler`）嵌入在遍历循环中，增加了不必要的复杂度
- 错误处理不一致：Config 失败抛异常，Declaration 失败仅记录

### 1.4 诊断日志层

**涉及文件**：
- `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventNames.cs`
- `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs`

- 日志事件名用点号分隔的字符串（如 `"eventbinder.try_bind_event.entry"`），无类型安全，拼写错误无法在编译期发现
- 日志记录散落在 10+ 个文件中，无法统一开关
- 快照输出（`OutputDiagnosticSnapshot`）与业务逻辑耦合

---

## 2. 重构总览

```
┌─────────────────────────────────────────────────────────────┐
│                    重构前（4层，12+文件）                      │
│                                                             │
│  EventConfig (旧属性+新属性) → WinFormsEventBus (5 Stage)    │
│      → ConfigBindingStage + DeclarationBindingStage          │
│      → 散落的诊断日志                                        │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                    重构后（4层，7文件）                        │
│                                                             │
│  EventConfig (仅EventsDictionary+Bindings)                   │
│      → WinFormsEventBus (内联绑定，0 Stage)                  │
│      → ConfigBindingStage (统一编排，自动降级)                │
│      → EventBindingDiagnosticContext (集中控制)              │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. 阶段一：统一事件配置模型

### 3.1 涉及文件

| 文件 | 操作 |
|------|------|
| `CsharpPrj/Plate/TConfig/Models/EventConfig.cs` | 重构 |

### 3.2 修改内容

#### 3.2.1 精简 EventConfig 模型

移除遗留属性，只保留核心：

```csharp
[JsonConverter(typeof(EventConfigConverter))]
public class EventConfig
{
    /// <summary>统一事件字典 — 键为 PascalCase 事件名，值为函数名列表</summary>
    public Dictionary<string, List<string>> EventsDictionary { get; set; } = new();

    /// <summary>参数绑定配置</summary>
    public EventBinding? Bindings { get; set; }
    
    // 移除以下属性（已废弃，无实际使用）：
    // - EventName
    // - HandlerType
    // - HandlerParams
}
```

#### 3.2.2 重写 EventConfigConverter

```csharp
public class EventConfigConverter : JsonConverter<EventConfig>
{
    private const string BindingsKey = "bindings";

    public override EventConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject");

        var config = new EventConfig();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return config;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName");

            var propertyName = reader.GetString() ?? "";
            reader.Read();

            if (string.Equals(propertyName, BindingsKey, StringComparison.OrdinalIgnoreCase))
            {
                config.Bindings = JsonSerializer.Deserialize<EventBinding>(ref reader, options);
            }
            else
            {
                // ★ 统一在反序列化时做 ToPascalCase
                var pascalName = ToPascalCase(propertyName);
                
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var functions = JsonSerializer.Deserialize<List<string>>(ref reader, options);
                    if (functions?.Count > 0)
                        config.EventsDictionary[pascalName] = functions;
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    var funcName = reader.GetString();
                    if (!string.IsNullOrEmpty(funcName))
                        config.EventsDictionary[pascalName] = new List<string> { funcName };
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        throw new JsonException("Expected EndObject");
    }

    public override void Write(Utf8JsonWriter writer, EventConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value.Bindings != null)
        {
            writer.WritePropertyName("bindings");
            JsonSerializer.Serialize(writer, value.Bindings, options);
        }

        foreach (var kvp in value.EventsDictionary)
        {
            if (kvp.Value.Count == 1)
                writer.WriteString(kvp.Key, kvp.Value[0]);
            else
            {
                writer.WritePropertyName(kvp.Key);
                JsonSerializer.Serialize(writer, kvp.Value, options);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>将事件名转为 PascalCase</summary>
    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        
        // 处理下划线分隔：mouse_down → MouseDown
        if (name.Contains('_'))
        {
            var parts = name.Split('_');
            return string.Concat(parts.Select(p => 
                p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant() : ""));
        }
        
        // 处理普通大小写：click → Click, onClick → OnClick
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
```

### 3.3 效果

- JSON 中 `"click": "Add"` 直接解析为 `EventsDictionary["Click"] = ["Add"]`
- 下游不再需要 `_normalizer.ToPascalCase()` 调用
- 移除 `KnownNonEventProperties` 硬编码排除列表
- 移除 3 个遗留属性

---

## 4. 阶段二：精简事件绑定管线

### 4.1 涉及文件

| 文件 | 操作 |
|------|------|
| `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/WinFormsEventBus.cs` | 重构 |
| `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/EventBusStages.cs` | 删除 |

### 4.2 修改内容

#### 4.2.1 简化 WinFormsEventBus.BindControlEvent

将 5 个 Stage 的逻辑内联到 `BindControlEvent` 方法中：

```csharp
public void BindControlEvent(object control, string eventName, Delegate handler)
{
    if (control == null || string.IsNullOrEmpty(eventName) || handler == null)
        return;

    // 1. 事件名已由上游保证为 PascalCase（阶段一改造后）
    var eventInfo = control.GetType().GetEvent(eventName);
    if (eventInfo == null)
    {
        EventBindingDiagnosticContext.Record(EventBindingEvent.BindEventNotFound, new
        {
            controlName = GetControlName(control),
            eventName,
            availableEvents = string.Join(", ", control.GetType().GetEvents().Select(e => e.Name))
        });
        return;
    }

    // 2. 直接绑定或 DynamicMethod 包装
    var handlerType = eventInfo.EventHandlerType;
    if (handlerType == typeof(EventHandler))
    {
        eventInfo.AddEventHandler(control, handler);
    }
    else
    {
        var wrapper = CreateDynamicMethodWrapper(control, eventInfo, handler);
        eventInfo.AddEventHandler(control, wrapper);
    }

    // 3. 记录订阅
    RecordSubscription(control, eventName, handler);
}
```

#### 4.2.2 删除 EventBusStages.cs

删除以下类和文件：
- `EventBusContext` 类
- `EventNameNormalizationStage` 类
- `EventInfoResolutionStage` 类
- `DirectBindStage` 类
- `DynamicMethodBindStage` 类
- `SubscriptionRecordStage` 类
- 整个 `EventBusStages.cs` 文件

#### 4.2.3 保留 DynamicMethod 包装逻辑

将 `DynamicMethodBindStage` 中的 `CreateDynamicMethodWrapper` 逻辑提取为 `WinFormsEventBus` 的私有方法。

### 4.3 效果

- 5 个 Stage + 1 个 Context 类 → 1 个方法
- 减少 80% 的间接层
- 消除 `IStorageCell` 的读写开销
- 消除 `catch {}` 吞异常的问题

---

## 5. 阶段三：统一绑定编排

### 5.1 涉及文件

| 文件 | 操作 |
|------|------|
| `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventBindingIoC.cs` | 精简接口 |
| `CsharpPrj/Operation/BackEndDriven/IoC/EventBindingIoC.cs` | 精简实现 |
| `CsharpPrj/Share/EventBindingOrchestrator/Invoke/ConfigBindingStage.cs` | 重构 |
| `CsharpPrj/Share/EventBindingOrchestrator/Invoke/DeclarationBindingStage.cs` | 重构 |

### 5.2 修改内容

#### 5.2.1 精简 IEventBindingIoC 接口

移除预解析相关方法：

```csharp
public interface IEventBindingIoC
{
    // ========== 核心绑定 ==========
    bool TryBindEvent(IEventBus eventBus, object control, string normalizedEventName, Delegate handler, object errors);
    
    // ========== 事件处理器创建 ==========
    EventHandler CreateEventHandler(
        object control,
        string eventName,
        string functionName,
        IReadOnlyList<ParameterBinding>? parameterBindings = null,
        string? outputTarget = null,
        string? outputKey = null,
        Action<string, string, object?>? onResult = null);
    
    // ========== 执行结果处理 ==========
    Task HandleExecutionResult(object result);
    
    // ========== 辅助方法 ==========
    string? GetControlName(object control);
    (string? outputTarget, string? outputKey) ResolveOutput(object? bindings, string functionName);
    
    // ========== 生命周期 ==========
    bool Validate();
    void Reset();
    
    // 移除以下方法：
    // - ResolveWorkflow(string functionName)
    // - ResolveDelegate(string functionName)
    // - PrecomputeParameterSources(IReadOnlyList<ParameterBinding>? bindings)
    // - CreatePreResolvedHandler(...)
}
```

#### 5.2.2 简化 ConfigBindingStage

```csharp
public object? Process(object input, ErrorCollector errors, PipelineContext context)
{
    var ctx = (EventBindingContext)input;
    var config = ctx.CurrentControlConfig;
    var control = ctx.CurrentControl;
    
    if (config?.Events == null) return ctx;
    
    var eventsConfig = config.Events;
    var eventBus = ctx.EventBus;
    var boundEventCount = 0;
    
    foreach (var kv in eventsConfig.EventsDictionary)
    {
        var eventName = kv.Key;  // 已为 PascalCase（阶段一保证）
        var functionNames = kv.Value;
        if (functionNames == null || functionNames.Count == 0) continue;
        
        foreach (var functionName in functionNames)
        {
            if (string.IsNullOrWhiteSpace(functionName)) continue;
            
            var handler = _ioc.CreateEventHandler(
                control, eventName, functionName,
                parameterBindings: ctx.CurrentParameterBindings,
                outputTarget: resolvedOutputTarget,
                outputKey: resolvedOutputKey,
                onResult: _onEventResult);
            
            if (_ioc.TryBindEvent(eventBus, control, eventName, handler, errors))
            {
                boundEventCount++;
            }
        }
    }
    
    _ioc.RecordBoundControl(ctx, control, boundEventCount, ...);
    return ctx;
}
```

#### 5.2.3 同步简化 DeclarationBindingStage

类似地移除预解析逻辑，保持与 `ConfigBindingStage` 一致的简化结构。

### 5.3 效果

- 移除 4 个预解析方法，减少接口复杂度
- 绑定循环从 40 行减到 20 行
- 消除 `ConfigBindingStage` 和 `DeclarationBindingStage` 的重复预解析逻辑

---

## 6. 阶段四：诊断日志集中化

### 6.1 涉及文件

| 文件 | 操作 |
|------|------|
| `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventNames.cs` | 重构 |
| `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs` | 重构 |
| `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/DiagnosticConfig.cs` | **新增** |

### 6.2 修改内容

#### 6.2.1 新增 DiagnosticConfig.cs

```csharp
namespace Share.EventBindingOrchestrator.Diagnostics;

/// <summary>日志级别</summary>
public enum DiagnosticLevel
{
    None,       // 不输出任何日志
    Error,      // 仅输出错误
    Warning,    // 输出警告和错误
    Info,       // 输出常规信息
    Verbose     // 输出详细信息（含预解析等调试信息）
}

/// <summary>事件绑定诊断配置 — 统一控制所有日志输出</summary>
public static class EventBindingDiagnosticConfig
{
    /// <summary>日志级别（默认：Warning）</summary>
    public static DiagnosticLevel Level { get; set; } = DiagnosticLevel.Warning;
    
    /// <summary>是否输出快照（默认：false）</summary>
    public static bool EnableSnapshot { get; set; } = false;
    
    /// <summary>是否输出事件绑定详情（默认：false）</summary>
    public static bool EnableEventBindingDetail { get; set; } = false;
}
```

#### 6.2.2 重构 EventNames.cs

将字符串常量改为枚举：

```csharp
namespace Share.EventBindingOrchestrator.Diagnostics;

/// <summary>事件绑定诊断事件类型 — 类型安全的事件标识</summary>
public enum EventBindingEvent
{
    // 绑定流程
    BindEventEntry,
    BindEventSuccess,
    BindEventEventBusFailed,
    BindEventReflectionFallback,
    BindEventReflectionFailed,
    BindEventNotFound,
    
    // 绑定结果
    BindEventSkipNullControl,
    BindEventBoundViaEventBus,
    BindEventBoundViaDeclaration,
    
    // 预解析（仅 Verbose 级别输出）
    PreresolveResult,
    
    // 快照
    DiagnosticSnapshot,
}
```

#### 6.2.3 重构 EventBindingDiagnosticContext.cs

```csharp
namespace Share.EventBindingOrchestrator.Diagnostics;

public static class EventBindingDiagnosticContext
{
    /// <summary>统一记录入口</summary>
    public static void Record(EventBindingEvent eventType, object? data = null)
    {
        if (!ShouldRecord(eventType)) return;
        
        var level = GetLevel(eventType);
        var message = FormatMessage(eventType, data);
        
        // 根据级别输出到不同目标
        switch (level)
        {
            case DiagnosticLevel.Error:
                Debug.WriteLine($"[ERROR] {message}");
                break;
            case DiagnosticLevel.Warning:
                Debug.WriteLine($"[WARN]  {message}");
                break;
            case DiagnosticLevel.Info:
                Debug.WriteLine($"[INFO]  {message}");
                break;
            case DiagnosticLevel.Verbose:
                Debug.WriteLine($"[DEBUG] {message}");
                break;
        }
    }
    
    private static bool ShouldRecord(EventBindingEvent eventType)
    {
        return GetLevel(eventType) <= EventBindingDiagnosticConfig.Level;
    }
    
    private static DiagnosticLevel GetLevel(EventBindingEvent eventType)
    {
        return eventType switch
        {
            EventBindingEvent.BindEventNotFound => DiagnosticLevel.Warning,
            EventBindingEvent.BindEventEventBusFailed => DiagnosticLevel.Warning,
            EventBindingEvent.BindEventReflectionFailed => DiagnosticLevel.Warning,
            
            EventBindingEvent.BindEventSuccess => DiagnosticLevel.Info,
            EventBindingEvent.BindEventBoundViaEventBus => DiagnosticLevel.Info,
            EventBindingEvent.BindEventBoundViaDeclaration => DiagnosticLevel.Info,
            
            EventBindingEvent.PreresolveResult => DiagnosticLevel.Verbose,
            EventBindingEvent.DiagnosticSnapshot => DiagnosticLevel.Verbose,
            
            _ => DiagnosticLevel.Info,
        };
    }
    
    private static string FormatMessage(EventBindingEvent eventType, object? data)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        return $"[{timestamp}] [{eventType}] {JsonSerializer.Serialize(data ?? new { })}";
    }
}
```

### 6.3 效果

- 类型安全的事件名，编译期即可发现拼写错误
- 统一的日志级别控制，可按需开关
- 快照输出独立开关，不影响业务逻辑
- 所有日志集中在一个入口，便于后续扩展（如写入文件）

---

## 7. 文件变更清单

| # | 文件路径 | 操作 | 说明 |
|---|---------|------|------|
| 1 | `CsharpPrj/Plate/TConfig/Models/EventConfig.cs` | 重构 | 移除遗留属性，重写 Converter，内联 ToPascalCase |
| 2 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/WinFormsEventBus.cs` | 重构 | 内联绑定逻辑，移除 Stage 管线依赖 |
| 3 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/EventBusStages.cs` | **删除** | 5 个 Stage + EventBusContext 全部移除 |
| 4 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/ConfigBindingStage.cs` | 重构 | 简化绑定循环，移除 ToPascalCase 和预解析逻辑 |
| 5 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/DeclarationBindingStage.cs` | 重构 | 同步简化，移除预解析逻辑 |
| 6 | `CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventBindingIoC.cs` | 精简 | 移除 ResolveWorkflow/ResolveDelegate/PrecomputeParameterSources/CreatePreResolvedHandler |
| 7 | `CsharpPrj/Operation/BackEndDriven/IoC/EventBindingIoC.cs` | 精简 | 同步移除预解析相关实现 |
| 8 | `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventNames.cs` | 重构 | 字符串常量改为 enum |
| 9 | `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs` | 重构 | 统一记录入口，按级别过滤 |
| 10 | `CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/DiagnosticConfig.cs` | **新增** | 统一配置（日志级别、快照开关等） |

### 依赖检查

以下文件引用了将被修改的接口/类，需要同步更新：

| 文件 | 引用内容 | 影响 |
|------|---------|------|
| `CsharpPrj/Share/EventBindingOrchestrator/Transformers/GenericEventBindingPipeline.cs` | `EventBindingDiagnosticContext` | 需更新调用方式 |
| `CsharpPrj/Share/EventBindingOrchestrator/Transformers/EventBindingPipelineFactory.cs` | `EventBindingDiagnosticContext` | 需更新调用方式 |
| `CsharpPrj/Operation/BackEndDriven/Pipeline/RenderPipelineOrchestrator.cs` | `OutputDiagnosticSnapshot` | 需更新调用方式 |
| `CsharpPrj/Operation/WdUI/UI/WdUI.Container.cs` | `OutputDiagnosticSnapshot` | 需更新调用方式 |
| `CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs` | `OutputDiagnosticSnapshot` | 需更新调用方式 |
| `FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs` | `EventBindingDiagnosticContext` | 需更新调用方式 |

---

## 8. 风险与注意事项

### 8.1 向后兼容

- `EventConfig` 移除 `EventName`/`HandlerType`/`HandlerParams` 属性前，需确认无外部代码引用
- `IEventBindingIoC` 移除 4 个方法前，需确认所有实现类已同步更新
- `EventBusStages.cs` 删除前，需确认 `WinFormsEventBus` 已完全内联所有 Stage 逻辑

### 8.2 执行顺序

建议按阶段顺序执行，每个阶段完成后构建验证：

```
阶段一（EventConfig）→ 构建验证
    → 阶段二（WinFormsEventBus）→ 构建验证
        → 阶段三（IoC + Stages）→ 构建验证
            → 阶段四（Diagnostics）→ 构建验证
```

### 8.3 回退策略

每个阶段修改前，先确认当前代码已提交到 git，以便在构建失败时快速回退：

```bash
git add -A
git commit -m "backup before phase N: <description>"
```
