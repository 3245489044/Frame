# ParentForm 参数传递重构方案

> **日期**：2026-07-14
> **背景**：当前 ParentForm 通过委托参数显式透传 5 层中间对象，每新增一个跨层值都需要修改所有中间层。
> **目标**：彻底消除所有中间层的 ParentForm 属性透传，恢复委托原始签名。

---

## 1. 当前调用链分析

### 1.1 调用链全景

```
WinFormsRenderPipeline.ExecuteBindEventsStage
  → EventBindingOrchestrator.ExecuteWithWalkAsync
    → EventBindingOrchestrator.ExecuteWithWalk
      → EventBindingPipelineFactory.CreateGenericPipeline
        → EventBindingStageOptions.ParentForm = options?.ParentForm
        → EventBindingStageBase(stageOptions)
          → _parentForm = options?.ParentForm
          → CreateEventHandler → (sender, args) => Task.Run
            → _parameterBuilder(sender, bindings, _parentForm)
              → ParameterBuilder.Build(sender, bindings, parentForm)
                → ParameterBuildContext.ParentForm = parentForm
```

### 1.2 涉及的中间层（共 5 层）

| 层 | 类型 | 修改内容 |
|---|------|---------|
| 1 | `EventBindingOptions` | 新增 `ParentForm` 属性 |
| 2 | `EventBindingPipelineFactory.CreateGenericPipeline` | 新增 `ParentForm = options?.ParentForm` 赋值 |
| 3 | `EventBindingStageOptions` | 新增 `ParentForm` 属性 |
| 4 | `EventBindingStageBase` | 新增 `_parentForm` 字段 + 构造函数赋值 |
| 5 | `_parameterBuilder` 委托签名 | 从 `Func<..., Dictionary<>>` 变为 `Func<..., object?, Dictionary<>>` |

### 1.3 核心问题

**委托签名膨胀**：`_parameterBuilder` 的签名每新增一个跨层值就膨胀一次：

```csharp
// 原始签名（无 ParentForm）
Func<object, IReadOnlyList<ParameterBinding>?, Dictionary<string, object>>

// 当前签名（新增 ParentForm）
Func<object, IReadOnlyList<ParameterBinding>?, object?, Dictionary<string, object>>
//                                                      ↑ 每新增一个值就加一个参数
```

**违反 Open/Closed 原则**：新增跨层值需要修改所有中间层，而非扩展。

---

## 2. 方案

### 2.1 核心思路

**直接 sender.FindForm()**。

关键洞察：`_parentForm` 的引入是为了解决 sideload 场景中控件树未完全挂载导致 `FindForm()` 失败的问题。但注意——**`CreateEventHandler` 返回的 lambda 是在事件触发时才执行**，此时控件树已经完全挂载，`sender.FindForm()` 一定能找到 Form。

所以根本不需要从构造函数注入 `_parentForm`，也不需要 Functor 隐式传播，直接在事件触发时调用 `sender.FindForm()` 即可。

### 2.2 架构图

```
┌──────────────────────────────────────────────────────────────────┐
│ Source 端（事件触发）                                              │
│                                                                  │
│ EventBindingStageBase.CreateEventHandler                         │
│   return (sender, args) =>                                       │
│     Task.Run(async () =>                                         │
│       _parameterBuilder(sender, bindings);  // 原始签名          │
│     );                                                            │
│                                                                  │
│ ┌──────────────────────────────────────────────────────────────┐ │
│ │ 中间层（零修改）                                               │ │
│ │                                                              │ │
│ │ EventBindingOrchestrator    ← 不感知 ParentForm               │ │
│ │ EventBindingPipelineFactory ← 不感知 ParentForm               │ │
│ │ EventBindingStageOptions    ← 不感知 ParentForm               │ │
│ │ EventBindingOptions         ← 不感知 ParentForm               │ │
│ └──────────────────────────────────────────────────────────────┘ │
│                                                                  │
│ Sink 端（主动查找）                                               │
│                                                                  │
│ ParameterBuilder.Build                                           │
│   var parentForm = (sender as Control)?.FindForm();              │
│   ctx.ParentForm = parentForm;                                   │
└──────────────────────────────────────────────────────────────────┘
```

### 2.3 修改清单

#### 修改 1：EventBindingStageBase — 删除所有 ParentForm 相关代码

**文件**：`CsharpPrj/Operation/WorkFlowMethod/EventBinding/EventBindingStageBase.cs`

```csharp
// ★ 2026-07-14 重构：删除 _parentForm 字段
//   ParentForm 由 ParameterBuilder 内部通过 sender.FindForm() 获取
// protected readonly object? _parentForm;  ← 删除

// 构造函数中删除 _parentForm 赋值
public EventBindingStageBase(EventBindingStageOptions? options = null)
{
    _eventExecutionService = options?.EventExecutionService;
    _onEventResult = options?.OnEventResult;
    _binderStrategy = options?.BinderStrategy;
    _normalizer = options?.Normalizer ?? new DefaultEventNameNormalizer();
    _parameterBuilder = options?.ParameterBuilder;
    _outputControlFinder = options?.OutputControlFinder;
    _outputWriter = options?.OutputWriter;
    // _parentForm = options?.ParentForm;  ← 删除
}

// ★ 2026-07-14 重构：恢复原始委托签名
// protected readonly Func<object, IReadOnlyList<ParameterBinding>?, object?, Dictionary<string, object>>? _parameterBuilder;
//                                                                  ↑ 删除 parentForm 参数
protected readonly Func<object, IReadOnlyList<ParameterBinding>?, Dictionary<string, object>>? _parameterBuilder;

// CreateEventHandler 中恢复原始调用
public EventHandler CreateEventHandler(...)
{
    return (sender, args) =>
    {
        // ...
        var task = Task.Run(async () =>
        {
            // ★ 重构：恢复原始签名，不再传递 _parentForm
            var parameters = _parameterBuilder(sender, parameterBindings);
            // ...
        });
    };
}
```

#### 修改 2：EventBindingStageOptions — 删除 ParentForm 属性

**文件**：`CsharpPrj/Operation/WorkFlowMethod/EventBinding/EventBindingStageBase.cs`

```csharp
public class EventBindingStageOptions
{
    // ... 其他属性不变 ...

    // ★ 2026-07-14 重构：删除 ParentForm 属性
    // public object? ParentForm { get; set; }  ← 删除
}
```

#### 修改 3：ParameterBuilder.Build — 通过 sender.FindForm() 获取 ParentForm

**文件**：`CsharpPrj/Operation/WorkFlowMethod/Parameters/ParameterBuilder.cs`

```csharp
// ★ 2026-07-14 重构：恢复原始签名，删除 parentForm 参数
//   ParentForm 通过 sender.FindForm() 获取
//   事件触发时控件树已完全挂载，FindForm() 一定成功
public Dictionary<string, object> Build(object sender, List<ParameterBinding>? parameterBindings)
{
    // ★ 2026-07-14 重构：事件触发时控件树已完全挂载
    var parentForm = (sender as System.Windows.Forms.Control)?.FindForm();

    var ctx = new ParameterBuildContext
    {
        Sender = sender,
        ParameterBindings = parameterBindings,
        ParentForm = parentForm,
        TypeConversionService = _typeConversionService
    };

    _buildPipeline.ExecuteAsync(ctx).GetAwaiter().GetResult();
    return ctx.Parameters;
}
```

#### 修改 4：IParameterBuilder — 删除 parentForm 重载

**文件**：`CsharpPrj/Operation/WorkFlowMethod/Parameters/Abstractions/IParameterBuilder.cs`

```csharp
public interface IParameterBuilder
{
    // ★ 2026-07-14 重构：恢复原始签名，删除 parentForm 重载
    Dictionary<string, object> Build(object sender, IReadOnlyList<ParameterBinding>? bindings);
    
    // ★ 删除以下重载：
    // Dictionary<string, object> Build(object sender, IReadOnlyList<ParameterBinding>? bindings, object? parentForm);
}
```

#### 修改 5：EventBindingOptions — 删除 ParentForm 属性

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Models/EventBindingOptions.cs`

```csharp
public class EventBindingOptions
{
    // ... 其他属性不变 ...

    // ★ 2026-07-14 重构：删除 ParentForm 属性
    // public object? ParentForm { get; set; }  ← 删除
}
```

#### 修改 6：EventBindingPipelineFactory — 删除 ParentForm 赋值

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Transformers/EventBindingPipelineFactory.cs`

```csharp
var stageOptions = new EventBindingStageOptions
{
    EventExecutionService = eventExecutionService,
    OnEventResult = onEventResult,
    BinderStrategy = resolvedBinderStrategy,
    ParameterBuilder = options?.ParameterBuilder,
    OutputControlFinder = options?.OutputControlFinder,
    OutputWriter = options?.OutputWriter,
    // ParentForm = options?.ParentForm  ← 删除
};
```

#### 修改 7：WinFormsRenderPipeline fallback — 恢复委托签名 + 删除 ParentForm

**文件**：`CsharpPrj/Operation/WdUI/WdUI.RenderPipeline.cs`

```csharp
// ★ 2026-07-14 重构：恢复原始委托签名（删除 parentForm 参数）
//   删除 ParentForm 赋值
var fallbackOptions = new EventBindingOptions
{
    EventBus = eventBus,
    ParameterBuilder = (sender, bindings) =>
        new WorkFlowMethod.Parameters.ParameterBuilder().Build(
            sender, bindings as List<CSG.Unified.ParameterBinding>),
    OutputControlFinder = (sender, output) =>
        WorkFlowMethod.Output.OutputHelper.FindOutputControl(sender, output),
    OutputWriter = (target, value) =>
        WorkFlowMethod.Output.OutputHelper.WriteOutputToControl(target, value),
    // ParentForm = ...  ← 删除
};
```

### 2.4 修改汇总

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `EventBindingStageBase.cs` | 删除 | 删除 `_parentForm` 字段 |
| `EventBindingStageBase.cs` | 删除 | 删除 `EventBindingStageOptions.ParentForm` |
| `EventBindingStageBase.cs` | 恢复 | `_parameterBuilder` 委托恢复原始签名 |
| `ParameterBuilder.cs` | 修改 | 通过 `sender.FindForm()` 获取 ParentForm |
| `ParameterBuilder.cs` | 删除 | 删除 `parentForm` 参数 |
| `IParameterBuilder.cs` | 删除 | 删除 `parentForm` 重载 |
| `EventBindingOptions.cs` | 删除 | 删除 `ParentForm` 属性 |
| `EventBindingPipelineFactory.cs` | 删除 | 删除 `ParentForm = options?.ParentForm` |
| `WdUI.RenderPipeline.cs` | 恢复 | fallback 委托恢复原始签名 |
| `WdUI.RenderPipeline.cs` | 删除 | 删除 `ParentForm = ...` 赋值 |

---

## 3. 实施步骤

### Step 1：修改 `ParameterBuilder.Build`（Sink 端核心）

- 删除 `parentForm` 参数
- 通过 `(sender as Control)?.FindForm()` 获取 ParentForm

### Step 2：删除 `IParameterBuilder` 接口中的 `parentForm` 重载

### Step 3：修改 `EventBindingStageBase`

- 删除 `_parentForm` 字段
- 删除构造函数中的 `_parentForm` 赋值
- 恢复 `_parameterBuilder` 委托为原始签名
- 恢复 `CreateEventHandler` 中的调用为原始签名

### Step 4：删除 `EventBindingStageOptions.ParentForm`

### Step 5：删除 `EventBindingOptions.ParentForm`

### Step 6：删除 `EventBindingPipelineFactory` 中的 `ParentForm` 赋值

### Step 7：恢复 `WinFormsRenderPipeline` fallback 路径中的委托签名

- 删除 `parentForm` 参数
- 删除 `ParentForm = ...` 赋值

---

## 4. 方案优势

| 维度 | 当前方案 | 重构方案 |
|------|---------|---------|
| 中间层修改 | O(n) 5 层 | **0 层** |
| 委托签名 | 膨胀 | **恢复原始** |
| 新增依赖 | 无 | **无** |
| 复杂度 | 高 | **低** |
| 健壮性 | 依赖调用方正确传递 | **事件触发时 FindForm() 一定成功** |
| 可扩展性 | 每新增值改所有层 | **新增值只需 Source/Sink 两端** |

---

## 5. 风险与注意事项

1. **sender 类型安全**：`sender` 在 WinForms 事件中通常是 `Control` 子类，但理论上可能是任意 `object`。`as Control` 安全转换保证不会抛出异常。

2. **ParentFormLookupStage 兼容性**：检查 `ParentFormLookupStage` 中是否已有 `sender.FindForm()` 的回退逻辑。如果已有，则 `ParameterBuildContext.ParentForm` 为 null 时它会自动处理，无需额外修改。

3. **单元测试**：在单元测试中，`sender` 可能不是 `Control`，此时 `parentForm` 为 null。`ParentFormLookupStage` 应能处理 null 情况。
