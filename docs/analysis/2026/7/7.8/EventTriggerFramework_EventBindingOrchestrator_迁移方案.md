# EventTriggerFramework → EventBindingOrchestrator 迁移方案

> **分析日期**：2026-07-06
> **分析范围**：`CsharpPrj/Operation/BackEndDriven/EventTriggerFramework` ↔ `CsharpPrj/Share/EventBindingOrchestrator`
> **目标**：消除重复逻辑，统一事件绑定管线，实现 Share 层复用

---

## 目录

1. [现状概览](#1-现状概览)
2. [重复逻辑全景图](#2-重复逻辑全景图)
3. [迁移策略总则](#3-迁移策略总则)
4. [分阶段迁移方案](#4-分阶段迁移方案)
5. [阶段一：清理已迁移的接口（低风险）](#5-阶段一清理已迁移的接口低风险)
6. [阶段二：统一 FrameworkContext 和 FrameworkOptions（中风险）](#6-阶段二统一-frameworkcontext-和-frameworkoptions中风险)
7. [阶段三：统一 Transformer 实现（中高风险）](#7-阶段三统一-transformer-实现中高风险)
8. [阶段四：迁移 EventBinderTransformer（高风险）](#8-阶段四迁移-eventbindertransformer高风险)
9. [阶段五：清理 Operation 层废弃代码（低风险）](#9-阶段五清理-operation-层废弃代码低风险)
10. [影响分析与回退策略](#10-影响分析与回退策略)
11. [附录：文件映射表](#11-附录文件映射表)

---

## 1. 现状概览

### 1.1 项目定位

| 项目 | 层 | 职责 |
|------|----|------|
| `EventTriggerFramework` | **Operation 层** | 事件触发框架，包含事件总线、命令系统、管线系统、WinForms 适配 |
| `EventBindingOrchestrator` | **Share 层** | 事件绑定编排器，提供通用的事件绑定管线，零 WinForms 编译时依赖 |

### 1.2 架构关系

```
Operation/BackEndDriven/EventTriggerFramework
  ├── FrameworkContext / FrameworkOptions     ← 框架上下文（与 Share 层重复）
  ├── EventTriggerFrameworkInitializer        ← 初始化器（Operation 特有）
  ├── EventTypeMapper                         ← 事件映射（委托到 Share 层）
  ├── DefaultEventBindingService              ← 绑定服务（委托到 Share 层管线）
  ├── EventBinding/                           ← 事件绑定 Transformers（与 Share 层重复）
  │   ├── EventBindingModels.cs               ← EventBindingContext（与 Share 层重复）
  │   ├── EventBindingTransformers.cs          ← ScriptModeGuard/BindingMetadata/EventBinder（与 Share 层重复）
  │   ├── ControlFinderTransformer.cs          ← 控件查找（与 Share 层重复）
  │   ├── ControlTreeWalkerTransformer.cs      ← 树遍历（与 Share 层重复）
  │   ├── EventBindingHelper.cs                ← 工具方法（与 Share 层重复）
  │   └── EventBindingPipeline.cs              ← 管线工厂（已委托到 Share 层）
  ├── Abstractions/
  │   ├── IUISubsystemInitializer.cs           ← 与 Share 层完全重复
  │   ├── IWinFormsEventBinder.cs              ← 与 Share 层完全重复
  │   ├── IControlFinder.cs                    ← Operation 特有
  │   └── IControlMetadataStore.cs             ← Operation 特有
  ├── Stores/                                  ← 元数据存储实现（Operation 特有）
  ├── ControlPool/                             ← 控件池（Operation 特有）
  └── Morphisms/                               ← 态射管线（Operation 特有）

Share/EventBindingOrchestrator
  ├── FrameworkContext.cs                      ← 框架上下文（精简版）
  ├── Models/FrameworkOptions.cs               ← 配置选项（精简版）
  ├── Models/EventBindingContext.cs            ← 绑定上下文（增强版）
  ├── Transformers/                            ← 通用 Transformers
  │   ├── ScriptModeGuardTransformer.cs
  │   ├── BindingMetadataTransformer.cs
  │   ├── EventBinderTransformer.cs
  │   ├── ControlFinderTransformer.cs
  │   ├── ControlTreeWalkerTransformer.cs
  │   ├── EventBindingHelper.cs
  │   ├── BindingStatisticsTransformer.cs
  │   ├── EventBindingPipelineFactory.cs
  │   └── EventBindingPipelineRunner.cs
  ├── Abstractions/
  │   ├── IUISubsystemInitializer.cs
  │   ├── IWinFormsEventBinder.cs
  │   └── IWinFormsControlStrategy.cs          ← 策略接口（解耦 WinForms）
  └── Services/
      └── EventTypeMapperService.cs            ← 事件映射服务
```

---

## 2. 重复逻辑全景图

### 2.1 重复程度矩阵

| # | 文件对 | 重复度 | 类型 | 风险 |
|---|--------|--------|------|------|
| 1 | `IUISubsystemInitializer.cs` | **100%** | 完全重复 | 🟢 低 |
| 2 | `IWinFormsEventBinder.cs` | **100%** | 完全重复 | 🟢 低 |
| 3 | `FrameworkContext.cs` | **~90%** | 高度重复 | 🟡 中 |
| 4 | `EventBindingContext` (EventBindingModels.cs) | **~85%** | 高度重复 | 🟡 中 |
| 5 | `ControlFinderTransformer.cs` | **~90%** | 高度重复 | 🟡 中 |
| 6 | `EventBindingHelper.cs` | **~80%** | 高度重复 | 🟢 低 |
| 7 | `FrameworkOptions.cs` | **~60%** | 部分重复 | 🟡 中 |
| 8 | `ControlTreeWalkerTransformer.cs` | **~50%** | 部分重复 | 🟢 低 |
| 9 | `EventBindingInfo.cs` | **~50%** | 部分重复 | 🟢 低 |
| 10 | `ScriptModeGuardTransformer.cs` | 职责相同 | 逻辑重复 | 🟡 中 |
| 11 | `BindingMetadataTransformer.cs` | 职责相同 | 逻辑重复 | 🟡 中 |
| 12 | `EventBinderTransformer.cs` | 职责相同 | 逻辑重复 | 🔴 高 |
| 13 | `EventTypeMapper` / `EventTypeMapperService` | 部分 | 逻辑重复 | 🟢 低 |

### 2.2 已完成的迁移

| 项目 | 状态 | 说明 |
|------|------|------|
| `EventBindingPipeline.cs` → `EventBindingPipelineFactory.cs` | ✅ 已委托 | Operation 层通过 `using` 别名委托到 Share 层 |
| `EventBindingPipelineRunner` → `EventBindingPipelineRunner.cs` | ✅ 已委托 | Operation 层包装 Share 层实例 |
| `EventTypeMapper.GetEventFunctionName` → `EventTypeMapperService` | ✅ 已委托 | Operation 层遍历 Share 层的 `EventPropertyAccessors` |

---

## 3. 迁移策略总则

### 3.1 核心原则

1. **Share 层为主**：所有通用逻辑以 `EventBindingOrchestrator` 为最终归宿
2. **Operation 层为薄壳**：`EventTriggerFramework` 保留 Operation 特有逻辑，通用逻辑委托到 Share 层
3. **向后兼容**：每个迁移步骤保持 Operation 层公开 API 不变（通过委托/适配器）
4. **逐步推进**：按风险从低到高分阶段执行，每阶段可独立验证

### 3.2 依赖关系

```
WdUI (WinForms 实现)
  └── EventTriggerFramework (Operation 层)
        └── EventBindingOrchestrator (Share 层)
              └── BasicMethod, Modules, CommandFramework, ...
```

**关键约束**：Share 层不能引用 Operation 层，Operation 层可以引用 Share 层。

### 3.3 命名约定

| 场景 | 规则 |
|------|------|
| Operation 层委托到 Share 层 | `using _Xxx = Share.EventBindingOrchestrator.Xxx;` |
| Operation 层保留特有逻辑 | 保持原名，添加 `[Obsolete("请使用 Share 层")]` 标记 |
| 适配器 | 命名 `XxxAdapter`，放在 Operation 层的 `Bridge/` 目录 |

---

## 4. 分阶段迁移方案

```
阶段一（低风险） ─── 阶段二（中风险） ─── 阶段三（中高风险） ─── 阶段四（高风险） ─── 阶段五（低风险）
    清理接口           统一上下文          统一 Transformer        迁移 EventBinder      清理废弃代码
```

---

## 5. 阶段一：清理已迁移的接口（低风险）

### 5.1 删除 Operation 层的 `IUISubsystemInitializer.cs`

**现状**：两个完全相同的接口，仅命名空间不同。

**操作**：
1. 删除 `EventTriggerFramework/Abstractions/IUISubsystemInitializer.cs`
2. 将所有 `using EventTriggerFramework.Abstractions.IUISubsystemInitializer` 改为 `using Share.EventBindingOrchestrator.Abstractions.IUISubsystemInitializer`
3. 更新 `EventTriggerFrameworkInitializer.cs` 中的 using 别名

**影响文件**：
- `EventTriggerFrameworkInitializer.cs`（第 30 行 using 别名）

**验证**：编译通过即可。

### 5.2 删除 Operation 层的 `IWinFormsEventBinder.cs`

**现状**：两个完全相同的接口，仅命名空间不同。

**操作**：
1. 删除 `EventTriggerFramework/Abstractions/IWinFormsEventBinder.cs`
2. 将所有引用改为 `Share.EventBindingOrchestrator.Abstractions.IWinFormsEventBinder`

**影响文件**：
- `EventBindingTransformers.cs`（第 26 行 using 别名）
- `EventBindingModels.cs`（第 17 行 using 别名）
- `EventBindingPipeline.cs`（第 16 行 using 别名）
- `DefaultEventBindingService.cs`（第 49 行属性类型）

**验证**：编译通过即可。

### 5.3 标记 Operation 层 `EventBindingInfo.cs` 为废弃

**操作**：
1. 在 `EventTriggerFramework/EventBindingInfo.cs` 添加 `[Obsolete("请使用 Share.EventBindingOrchestrator.Models.EventBindingInfo")]`

---

## 6. 阶段二：统一 FrameworkContext 和 FrameworkOptions（中风险）

### 6.1 统一 `FrameworkOptions`

**现状对比**：

| 属性 | Operation 版 | Share 版 |
|------|-------------|----------|
| `EnableEventBus` | ✅ | ✅ |
| `EnableCommands` / `EnableCommandSystem` | `EnableCommands` | `EnableCommandSystem` |
| `EnablePipeline` | ✅ | ✅ |
| `EnableCore` | ✅（含 StateStore/ValueExtractor） | ❌ |
| `EnableWinForms` | ✅（默认 false） | ✅（默认 true） |
| `EnableDeduplication` / `EnableDeduplicatingEventBus` | `EnableDeduplication` | `EnableDeduplicatingEventBus` |
| `EnableEnhancedEventBus` | ❌（始终启用） | ✅ |
| `EnableEventQueue` | ✅ | ✅ |
| `EnableEventScope` | ✅ | `EnableRootEventScope` |
| `PipelineName` | ✅ | ❌ |
| `CliMode` | ✅ | ❌ |
| `ConfigurePipeline` | ✅ | ❌ |
| `EventBusLogger` | ✅ | ❌ |
| `DedupWindow` | ✅ | ❌ |
| `ParameterExecutionWorkflow` | ✅ | ❌ |

**方案**：**保留两个版本**，因为 Operation 版包含 Share 版不需要的管线/CLI 配置。
- Share 版作为通用配置基类
- Operation 版继承 Share 版并扩展

```csharp
// Share/EventBindingOrchestrator/Models/FrameworkOptions.cs
public class FrameworkOptions
{
    public bool EnableEventBus { get; set; } = true;
    public bool EnableCommandSystem { get; set; } = true;
    public bool EnablePipeline { get; set; } = true;
    public bool EnableWinForms { get; set; } = true;
    public bool EnableDeduplicatingEventBus { get; set; }
    public bool EnableEnhancedEventBus { get; set; }
    public bool EnableEventQueue { get; set; }
    public bool EnableRootEventScope { get; set; }
}

// Operation/EventTriggerFramework/FrameworkOptions.cs
public class FrameworkOptions : Share.EventBindingOrchestrator.Models.FrameworkOptions
{
    public bool EnableCore { get; set; } = true;
    public string PipelineName { get; set; } = "DefaultCommandPipeline";
    public bool CliMode { get; set; } = false;
    public Action<IStagePipeline<CmdCtx>>? ConfigurePipeline { get; set; }
    public Action<string>? EventBusLogger { get; set; }
    public TimeSpan DedupWindow { get; set; } = TimeSpan.FromSeconds(5);
    public IParameterExecutionWorkflow? ParameterExecutionWorkflow { get; set; }
}
```

### 6.2 统一 `FrameworkContext`

**现状对比**：

| 属性 | Operation 版 | Share 版 |
|------|-------------|----------|
| `Options` | ✅ | ✅ |
| `IsDisposed` | ✅ | ✅ |
| `CommandFrameworkHost` | ✅ | ✅ |
| `EventBusCore` | ✅ | ✅ |
| `EventBus` (EventBusFacade) | ✅ | ✅ |
| `EventEngine` | ✅ | ❌ |
| `DeduplicatingEventBus` | ✅ | ✅ |
| `EnhancedEventBus` | ✅ | ✅ |
| `EventQueue` | ✅ | ✅ |
| `RootEventScope` | ✅ | ✅ |
| `CommandParser` | ✅ | ✅ |
| `CommandEngine` | ✅ | ✅ |
| `Pipeline` | ✅ | ✅ |
| `PipelineInstance` | ✅ | ✅ |
| `StateStore` | ✅ | ✅ |
| `ValueExtractor` | ✅ | ✅ |
| `BindingResolver` | ✅ | ❌ |
| `EventTriggerService` | ✅ | ❌ |
| `EventBindingService` | ✅ | ❌ |
| WinForms 适配 (5 个 object 属性) | ✅ | ✅ |

**方案**：**保留两个版本**，Operation 版继承 Share 版。

```csharp
// Operation/EventTriggerFramework/FrameworkContext.cs
public class FrameworkContext : Share.EventBindingOrchestrator.FrameworkContext, IDisposable
{
    // 新增属性
    public EventEngine? EventEngine { get; internal set; }
    public IEventBindingResolver? BindingResolver { get; internal set; }
    public IEventTriggerService? EventTriggerService { get; internal set; }
    public DefaultEventBindingService? EventBindingService { get; internal set; }

    // 重写 Dispose
    public new void Dispose()
    {
        if (IsDisposed) return;
        EventEngine = null;
        // ... 释放 Operation 特有资源
        base.Dispose();
    }
}
```

### 6.3 更新 `EventTriggerFrameworkInitializer.cs`

**操作**：
1. 使用继承后的 `FrameworkContext` 和 `FrameworkOptions`
2. 保持现有初始化逻辑不变

---

## 7. 阶段三：统一 Transformer 实现（中高风险）

### 7.1 统一 `EventBindingContext`

**现状**：两个版本共存，各有差异。

| 属性 | Operation 版 (EventBindingModels.cs) | Share 版 (Models/EventBindingContext.cs) |
|------|--------------------------------------|------------------------------------------|
| 跳过机制 | `ScriptModeSkipped` | `SkipCurrentControl` |
| 统计 | 无 | `Statistics` (BindingStatistics) |
| 绑定元数据 | 无 | `CurrentBindings`, `CurrentParameterBindings`, `CurrentOutput` |
| 执行服务 | 无 | `EventExecutionService` |
| `ResetCurrent()` | 重置 2 个属性 | 重置 3 个属性 |

**方案**：**统一使用 Share 版**，Operation 层通过适配器兼容。

1. 删除 `EventTriggerFramework/EventBinding/EventBindingModels.cs`
2. 所有引用改为 `Share.EventBindingOrchestrator.Models.EventBindingContext`
3. 添加 `using EventBindingContext = Share.EventBindingOrchestrator.Models.EventBindingContext;`

### 7.2 统一 `ControlFinderTransformer`

**差异**：
- Operation 版有**循环引用检测**（`HashSet<object>` + `ReferenceEqualityComparer`）
- Share 版没有循环引用检测

**方案**：**将循环引用检测合并到 Share 版**。

```csharp
// Share 版 ControlFinderTransformer.cs 需要添加：
private static readonly ConcurrentDictionary<Type, (PropertyInfo? nameProp, PropertyInfo? controlsProp)> _reflectionCache = new();

private static object? FindControlInContainer(object container, string name)
{
    if (container == null || string.IsNullOrWhiteSpace(name)) return null;
    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
    return FindControlInContainerCore(container, name, visited);
}

private static object? FindControlInContainerCore(object container, string name, HashSet<object> visited)
{
    if (container == null || string.IsNullOrWhiteSpace(name)) return null;
    if (!visited.Add(container)) return null; // 循环引用检测

    // ... 其余逻辑
}
```

### 7.3 统一 `ControlTreeWalkerTransformer`

**现状**：
- Operation 版：内联遍历 `Controls/Panel1Controls/Panel2Controls`
- Share 版：委托给 `ControlTreeWalker.GetDirectChildren(config)`

**方案**：**Operation 版改为委托到 Share 版**。

```csharp
// Operation 版改为：
public class ControlTreeWalkerTransformer : ITransformer<EventBindingContext, EventBindingContext>
{
    public string Name => "ControlTreeWalker";
    public EventBindingContext Transform(EventBindingContext input)
    {
        var config = input.CurrentControlConfig;
        if (config == null) { input.CurrentControls = null; return input; }
        input.CurrentControls = Share.EventBindingOrchestrator.Utils.ControlTreeWalker.GetDirectChildren(config);
        return input;
    }
}
```

### 7.4 统一 `EventBindingHelper`

**差异**：
- Share 版多一个 `GetExtensionDataValue` 方法
- 引用不同的 `EventTypeMapper`（Operation 版引用 `EventTypeMapper`，Share 版引用 `EventTypeMapperService`）

**方案**：**Operation 版改为委托到 Share 版**。

```csharp
// Operation 版改为：
public static class EventBindingHelper
{
    public static string? ResolveOutput(EventBindingModel? bindings, EventConfig eventsConfig, string eventName)
        => Share.EventBindingOrchestrator.Transformers.EventBindingHelper.ResolveOutput(bindings, eventsConfig, eventName);
    public static string? GetFirstInput(EventBindingModel? bindings)
        => Share.EventBindingOrchestrator.Transformers.EventBindingHelper.GetFirstInput(bindings);
}
```

### 7.5 统一 `ScriptModeGuardTransformer`

**差异**：
- Operation 版：使用 `ScriptModeSkipped` 标记 + 记录 `SkippedControls`
- Share 版：使用 `SkipCurrentControl` 标记 + 通过 `ExtensionData` 检查 script 属性

**方案**：**统一使用 Share 版的跳过机制**。

1. 删除 Operation 版的 `ScriptModeGuardTransformer`（在 `EventBindingTransformers.cs` 中）
2. 引用 Share 版的 `ScriptModeGuardTransformer`
3. 更新 `EventBindingPipeline.cs` 中的管线组合

### 7.6 统一 `BindingMetadataTransformer`

**差异**：
- Operation 版：通过 `IControlMetadataStore` 接口写入 `Control.Tag`
- Share 版：写入 `EventBindingContext.CurrentParameterBindings` / `CurrentOutput`

**方案**：**统一使用 Share 版**。

1. 删除 Operation 版的 `BindingMetadataTransformer`（在 `EventBindingTransformers.cs` 中）
2. 引用 Share 版的 `BindingMetadataTransformer`
3. WinForms 特定的 Tag 写入逻辑由 `WdUI` 层的 `ControlTagWriterTransformer` 负责

---

## 8. 阶段四：迁移 EventBinderTransformer（高风险）

### 8.1 现状

| 对比项 | Operation 版 | Share 版 |
|--------|-------------|----------|
| 行数 | **1051 行** | **434 行** |
| WinForms 依赖 | 直接引用 `System.Windows.Forms` | 通过 `IWinFormsControlStrategy` 解耦 |
| 参数绑定 | 从 `Control.Tag` 读取 | 从 `EventBindingContext` 读取 |
| 输出写回 | 支持 `WorkflowResultWriter` 多控件回写 | 仅单控件回写 |
| 日志 | `EventTriggerLog.Log` | `System.Diagnostics.Debug.WriteLine` |
| 重入保护 | `ReentryGuard` | `ReentryGuard` |
| 超时保护 | 10 秒 | 10 秒 |

### 8.2 迁移策略

**核心思路**：Operation 版保留为**薄适配层**，核心逻辑委托到 Share 版。

```
EventTriggerFramework.EventBinderTransformer (薄壳)
  └── 委托到 Share.EventBindingOrchestrator.Transformers.EventBinderTransformer
  └── 额外处理 WinForms 特定逻辑（WorkflowResultWriter 等）
```

**具体操作**：

1. **删除** Operation 版 `EventBinderTransformer` 中与 Share 版重复的代码：
   - `CreateEventHandler` → 委托到 Share 版
   - `BuildParametersFromBindings` → 委托到 Share 版
   - `FindParentForm` → 通过 `IWinFormsControlStrategy` 注入
   - `GetControlValueStatic` → 通过 `IWinFormsControlStrategy` 注入
   - `WriteOutputToControl` → 保留（含 WorkflowResultWriter 逻辑）

2. **保留** Operation 版特有的逻辑：
   - `TryWriteWithWorkflowResultWriter`（多控件回写）
   - `FindOutputControlAcrossForms`（跨 Form 查找）
   - `EventTriggerLog.Log` 日志（替换 `Debug.WriteLine`）

3. **注入** `IWinFormsControlStrategy`：
   - 通过构造函数注入
   - 在 `DefaultEventBindingService` 中创建时传入

### 8.3 重构后的 EventBinderTransformer（Operation 版）

```csharp
public class EventBinderTransformer : ITransformer<EventBindingContext, EventBindingContext>
{
    private readonly Share.EventBindingOrchestrator.Transformers.EventBinderTransformer _inner;
    private readonly IWinFormsControlStrategy? _winFormsStrategy;

    public EventBinderTransformer(IWinFormsControlStrategy? winFormsStrategy = null)
    {
        _winFormsStrategy = winFormsStrategy;
        _inner = new Share.EventBindingOrchestrator.Transformers.EventBinderTransformer(winFormsStrategy);
    }

    public string Name => "EventBinder";

    public EventBindingContext Transform(EventBindingContext input)
    {
        // 1. 委托到 Share 层执行核心绑定逻辑
        var result = _inner.Transform(input);

        // 2. 额外处理 WinForms 特定逻辑（WorkflowResultWriter 等）
        // ...（保留 Operation 特有逻辑）

        return result;
    }
}
```

---

## 9. 阶段五：清理 Operation 层废弃代码（低风险）

### 9.1 删除文件清单

| 文件 | 原因 | 替代 |
|------|------|------|
| `Abstractions/IUISubsystemInitializer.cs` | 完全重复 | `Share.EventBindingOrchestrator.Abstractions` |
| `Abstractions/IWinFormsEventBinder.cs` | 完全重复 | `Share.EventBindingOrchestrator.Abstractions` |
| `EventBinding/EventBindingModels.cs` | 已迁移 | `Share.EventBindingOrchestrator.Models.EventBindingContext` |
| `EventBinding/EventBindingHelper.cs` | 已迁移 | `Share.EventBindingOrchestrator.Transformers.EventBindingHelper` |

### 9.2 标记废弃文件清单

| 文件 | 标记 |
|------|------|
| `EventBindingInfo.cs` | `[Obsolete("请使用 Share.EventBindingOrchestrator.Models.EventBindingInfo")]` |

### 9.3 简化文件清单

| 文件 | 操作 |
|------|------|
| `EventBinding/ControlTreeWalkerTransformer.cs` | 改为委托到 Share 版 |
| `EventBinding/EventBindingTransformers.cs` | 删除 ScriptModeGuard + BindingMetadata，保留 EventBinder（薄壳） |
| `EventBinding/EventBindingPipeline.cs` | 已委托，保持现状 |

---

## 10. 影响分析与回退策略

### 10.1 影响范围

| 阶段 | 影响文件数 | 影响模块 | 风险 |
|------|-----------|---------|------|
| 一 | ~5 个 | EventTriggerFramework 内部 | 🟢 低 |
| 二 | ~3 个 | FrameworkContext, FrameworkOptions, Initializer | 🟡 中 |
| 三 | ~8 个 | 所有 Transformer 文件 | 🟡 中 |
| 四 | ~3 个 | EventBinderTransformer, DefaultEventBindingService | 🔴 高 |
| 五 | ~6 个 | 删除/标记废弃文件 | 🟢 低 |

### 10.2 外部依赖方

以下模块引用了 `EventTriggerFramework` 的 API，迁移时需同步更新：

| 模块 | 引用内容 |
|------|---------|
| `WdUI` | `IWinFormsEventBinder`, `FrameworkContext`, `EventBindingContext` |
| `BackEndDrivenBuilder` | `FrameworkOptions`, `FrameworkContext` |
| `FileDrivenExecutor` | `DefaultEventBindingService` |

### 10.3 回退策略

1. **每阶段独立提交**，便于回滚
2. **保留旧文件**直到新代码验证通过（添加 `[Obsolete]` 而非直接删除）
3. **编译验证**：每步操作后执行 `dotnet build` 确认无编译错误
4. **运行验证**：执行现有测试用例确认行为不变

---

## 11. 附录：文件映射表

### 11.1 完全重复 → 删除 Operation 版

| Operation 层文件 | Share 层文件 | 操作 |
|-----------------|-------------|------|
| `Abstractions/IUISubsystemInitializer.cs` | `Abstractions/IUISubsystemInitializer.cs` | 删除 |
| `Abstractions/IWinFormsEventBinder.cs` | `Abstractions/IWinFormsEventBinder.cs` | 删除 |

### 11.2 高度重复 → 委托到 Share 版

| Operation 层文件 | Share 层文件 | 操作 |
|-----------------|-------------|------|
| `EventBinding/EventBindingModels.cs` | `Models/EventBindingContext.cs` | 删除，改用 Share 版 |
| `EventBinding/EventBindingHelper.cs` | `Transformers/EventBindingHelper.cs` | 改为委托 |
| `EventBinding/ControlTreeWalkerTransformer.cs` | `Transformers/ControlTreeWalkerTransformer.cs` | 改为委托 |
| `EventBinding/EventBindingTransformers.cs` (ScriptModeGuard + BindingMetadata) | `Transformers/ScriptModeGuardTransformer.cs` + `BindingMetadataTransformer.cs` | 删除，改用 Share 版 |

### 11.3 逻辑重复 → 保留薄壳

| Operation 层文件 | Share 层文件 | 操作 |
|-----------------|-------------|------|
| `EventBinding/EventBindingTransformers.cs` (EventBinder) | `Transformers/EventBinderTransformer.cs` | 保留薄壳，委托核心逻辑 |
| `EventBinding/ControlFinderTransformer.cs` | `Transformers/ControlFinderTransformer.cs` | 保留（含循环引用检测），或合并到 Share 版 |

### 11.4 已委托 → 保持现状

| Operation 层文件 | Share 层文件 | 操作 |
|-----------------|-------------|------|
| `EventBinding/EventBindingPipeline.cs` | `Transformers/EventBindingPipelineFactory.cs` + `EventBindingPipelineRunner.cs` | 保持现状 |
| `EventTypeMapper.cs` | `Services/EventTypeMapperService.cs` | 保持现状（Operation 版含额外映射表） |

### 11.5 Operation 特有 → 保留

| 文件 | 说明 |
|------|------|
| `EventTriggerFrameworkInitializer.cs` | 框架初始化器，Operation 特有 |
| `DefaultEventBindingService.cs` | 实现多个接口，Operation 特有 |
| `ControlPool/` | 控件池，Operation 特有 |
| `Morphisms/` | 态射管线，Operation 特有 |
| `Stores/` | 元数据存储实现，Operation 特有 |
| `Abstractions/IControlFinder.cs` | Operation 特有接口 |
| `Abstractions/IControlMetadataStore.cs` | Operation 特有接口 |
| `EventEngine.cs` | 事件引擎，Operation 特有 |

---

## 总结

本迁移方案共分 **5 个阶段**，按风险从低到高排列：

| 阶段 | 内容 | 风险 | 预计工作量 |
|------|------|------|-----------|
| 一 | 清理已迁移的接口 | 🟢 低 | 0.5 小时 |
| 二 | 统一 FrameworkContext/Options | 🟡 中 | 1 小时 |
| 三 | 统一 Transformer 实现 | 🟡 中 | 2 小时 |
| 四 | 迁移 EventBinderTransformer | 🔴 高 | 3 小时 |
| 五 | 清理废弃代码 | 🟢 低 | 0.5 小时 |

**总计**：约 **7 小时**工作量，可分 3-5 次提交完成。

**核心收益**：
- 消除 ~2000 行重复代码
- 统一事件绑定管线，Share 层可被多个 Operation 模块复用
- 减少维护成本（修复 Bug 只需改一处）
