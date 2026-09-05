# EventBindingOrchestrator — 事件绑定编排器

> **分析日期**：2026-07-01
> **状态**：设计阶段
> **关联问题**：FileDriven 中 sideContainer 的 UI 按钮事件没有触发

---

## 1. 问题概述

### 1.1 现象

FileDriven 中 sideContainer 的 UI 按钮点击后没有触发任何函数。经过对完整事件绑定链路的追踪，发现 **3 个关键问题**：

| # | 问题 | 严重性 | 所在层 |
|---|------|--------|--------|
| 1 | `buildEvents: true` 在 JSON 管线中未被处理 | 🔴 核心 | JSON 预处理 |
| 2 | `EventBinderTransformer._eventExecutionService` 可能为 null | 🟡 次要 | 事件绑定管线 |
| 3 | sideload 场景下 `ExecuteBindEventsStage` 可能因 `session.Config` 为 null 而跳过 | 🟡 次要 | 渲染管线 |

### 1.2 根因总结

**最可能的原因**：`buildEvents: true` 在 JSON 管线中未被处理，导致按钮的 `events` 配置为空，`EventBinderTransformer` 找不到要绑定的事件，所以按钮点击时没有触发任何函数。

**次要原因**：`EventBinderTransformer` 的 `_eventExecutionService` 可能为 null，导致即使事件被绑定，handler 内部也不执行实际函数。

---

## 2. 当前架构分析

### 2.1 事件绑定链路全景

```
composites.json / layouts.json
  ↓
TemplateExpandConverter          ← JSON 管线：模板展开
  ↓  (buildEvents 未被处理 ❌)
UIConfiguration (含 Events)
  ↓
DefaultEventBindingService       ← 外观：委托给 PipelineRunner
  ↓
EventBindingPipelineRunner       ← 管线执行器
  ↓
EventBinderTransformer           ← 事件绑定执行
  ↓  (_eventExecutionService 可能为 null ❌)
IWinFormsEventBinder.BindControlEvent()
  ↓
WinForms 原生事件绑定
```

### 2.2 散落的逻辑点

事件绑定链路涉及 **3 个层、5 个文件**，逻辑高度分散：

#### 层 1：JSON 预处理（TemplateExpandConverter）

- **文件**：`CsharpPrj/Share/PlaceholderFramework/TemplateExpansion/Converters/TemplateExpandConverter.cs`
- **问题**：`buildEvents: true` 未被处理，模板展开后不生成 `events` 配置
- **现状**：逻辑内联在 Converter 中，与 JSON 管线耦合

**关键代码**（`ExpandTemplate` 方法，第 103-187 行）：

```csharp
private JsonObject? ExpandTemplate(JsonObject source, ErrorCollector errors)
{
    // ... 模板查找、深拷贝、合并 ...
    JsonMerger.Merge(result, source, new HashSet<string> { "template" });
    // ... 多对象展开、占位符替换 ...
    // ★ 没有处理 buildEvents 的逻辑！
    return result;
}
```

`composites.json` 中所有按钮模板（`action-button`、`toolbar-button` 等）都定义了 `"buildEvents": true`，但 `ExpandTemplate` 方法中没有任何处理 `buildEvents` 的逻辑。

#### 层 2：事件绑定管线（Transformers）

- **文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs`
- **文件**：`CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs`
- **问题**：`EventBinderTransformer` 的 `_eventExecutionService` 可能为 null

**关键代码**（`EventBinderTransformer` 构造函数，第 229-234 行）：

```csharp
public EventBinderTransformer(IEventExecutionService? eventExecutionService = null, ...)
```

`_eventExecutionService` 是**可选的**。当它为 null 时，`CreateEventHandler` 创建的 handler **只记录日志，不执行任何函数**（第 391-393 行）：

```csharp
ctx?.Log($"[EventBinder] 警告: 事件执行服务未注入，无法执行函数 '{functionName}'");
```

#### 层 3：初始化与注入

| 文件 | 位置 | 问题 |
|------|------|------|
| `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` | 第 116 行 | `new DefaultEventBindingService()` — **无参数**，`_eventExecutionService` = null |
| `FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs` | 第 132 行 | `InjectEventBinder` — 只注入 `EventBinder`，不注入 `EventExecutionService` |
| `CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs` | 第 96 行 | `SetEventExecutionService()` — 延迟注入方法存在，但**没人调用它** |

---

## 3. 设计方案：EventBindingOrchestrator

### 3.1 定位

创建一个**编排器**，将三个散落的职责集中到一个文件中：

```
EventBindingOrchestrator
├── buildEvents 处理（原 TemplateExpandConverter 的缺失逻辑）
├── EventExecutionService 注入管理（含延迟注入 + 状态跟踪）
└── 绑定触发（统一入口，替代散落的 BindEvents 调用）
```

### 3.2 文件位置

```
CsharpPrj/Operation/EventTriggerFramework/EventBinding/
├── EventBindingOrchestrator.cs    ← 新增：编排器
├── EventBindingPipeline.cs        ← 现有：管线工厂
└── EventBindingTransformers.cs    ← 现有：转换器
```

理由：
- 与 `EventBindingPipeline.cs`、`EventBindingTransformers.cs` 同目录
- 属于 EventTriggerFramework 层，不引入 FileDriven 依赖
- 可被 FrameworkInitializer、TemplateExpandConverter、SideContainerBinder 三方引用

### 3.3 核心接口

```csharp
/// <summary>
/// 事件绑定编排器 — 统一管理从 JSON 配置到事件绑定的完整链路
/// </summary>
public class EventBindingOrchestrator
{
    // ================================================================
    // 1. buildEvents 处理
    // ================================================================

    /// <summary>
    /// 从模板的 parameters 中提取事件元数据，生成 events 配置
    /// <para>解决 buildEvents: true 未被处理的问题</para>
    /// </summary>
    /// <param name="source">模板展开后的 JSON 对象（含 buildEvents 和 parameters）</param>
    /// <returns>是否生成了 events 配置</returns>
    public static bool BuildEventsFromTemplate(JsonObject source);

    // ================================================================
    // 2. EventExecutionService 注入管理
    // ================================================================

    /// <summary>
    /// 统一管理 IEventExecutionService 的注入状态
    /// <para>跟踪是否已注入，避免重复创建 PipelineRunner</para>
    /// </summary>
    public void EnsureEventExecutionService(IEventExecutionService service);

    // ================================================================
    // 3. 绑定触发（带诊断）
    // ================================================================

    /// <summary>
    /// 包装 DefaultEventBindingService.BindEvents，增加诊断日志
    /// </summary>
    public BindingResult BindEventsWithDiagnostic(
        object target,
        UIConfiguration config,
        object? searchRoot = null,
        Func<string, object?>? customControlFinder = null);
}
```

### 3.4 BuildEventsFromTemplate 处理逻辑

**输入**（模板展开后的 JSON）：
```json
{
  "template": "action-button",
  "buildEvents": true,
  "parameters": {
    "functionName": "someFunction",
    "eventName": "click",
    "inputs": [],
    "output": ""
  }
}
```

**处理步骤**：
1. 检测 `source` 中是否有 `buildEvents: true`
2. 从 `parameters` 中读取：
   - `functionName`（必需）
   - `eventName`（默认 `"click"`）
   - `inputs`（默认 `[]`）
   - `output`（默认 `""`）
3. 构建 `events` 对象
4. 移除 `buildEvents` 字段（消费后清理）

**输出**：
```json
{
  "template": "action-button",
  "events": {
    "click": {
      "functionName": "someFunction",
      "inputs": [],
      "output": ""
    }
  }
}
```

### 3.5 调用链路变化

**修改前**（散落）：
```
FrameworkInitializer
  → new DefaultEventBindingService()          // _eventExecutionService = null
  → FileDrivenWdUIBridge.InjectEventBinder()  // 只设 EventBinder

TemplateExpandConverter.ExpandTemplate()
  → 忽略 buildEvents                          // events 为空

SideContainerBinder / RenderPipeline
  → BindEvents()                              // _eventExecutionService 可能为 null
```

**修改后**（集中）：
```
FrameworkInitializer
  → EventBindingOrchestrator.EnsureEventExecutionService(service)
  → FileDrivenWdUIBridge.InjectEventBinder()

TemplateExpandConverter.ExpandTemplate()
  → EventBindingOrchestrator.BuildEventsFromTemplate()  // 生成 events

SideContainerBinder / RenderPipeline
  → EventBindingOrchestrator.BindEventsWithDiagnostic() // 带诊断
```

---

## 4. 实施步骤

### Step 1：创建 EventBindingOrchestrator.cs

在 `CsharpPrj/Operation/EventTriggerFramework/EventBinding/` 下创建编排器，实现：
- `BuildEventsFromTemplate()` — 静态方法，处理 `buildEvents`
- `EnsureEventExecutionService()` — 注入管理
- `BindEventsWithDiagnostic()` — 带诊断的绑定触发

### Step 2：集成到 TemplateExpandConverter

在 `ExpandTemplate()` 方法末尾（`return result` 之前），调用 `EventBindingOrchestrator.BuildEventsFromTemplate(result)`。

### Step 3：修复注入链路

在 `FrameworkInitializer` 中，创建 `EventExecutionService` 后调用 `EnsureEventExecutionService()`。

### Step 4：验证

- 确认 `composites.json` 中所有按钮模板展开后都生成 `events` 字段
- 确认 `EventBinderTransformer` 能正确消费生成的 `events`
- 确认 `_eventExecutionService` 不为 null

---

## 5. 附录：composites.json 中涉及的模板

| 模板名 | 类型 | functionName | eventName | inputs | output |
|--------|------|-------------|-----------|--------|--------|
| `action-button` | button | required | click(默认) | [] | "" |
| `toolbar-button` | button | required | click(默认) | [] | "" |
| `time-button` | button | required | click(默认) | [] | outputTextBox |
| `danger-button` | button | required | click(默认) | [] | "" |
| `secondary-button` | button | required | click(默认) | [] | "" |
| `labeled-textbox-with-button` | button(子项) | required | — | — | — |

所有按钮模板的 `buildEvents` 均为 `true`，`eventName` 默认值为 `"click"`。
