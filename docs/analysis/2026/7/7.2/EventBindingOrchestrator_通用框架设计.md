# EventBindingOrchestrator — 通用框架设计

> **版本**：v2.0（通用框架）
> **日期**：2026-07-01
> **位置**：`CsharpPrj/Share/EventBindingOrchestrator/`
> **输出**：`Share.EventBindingOrchestrator.dll`

---

## 1. 设计目标

### 1.1 解决的问题

将事件绑定完整链路从散落的 3 层 5 文件集中到 Share 层的一个通用框架中：

| 原位置 | 原文件 | 新位置 |
|--------|--------|--------|
| Share/PlaceholderFramework | `TemplateExpandConverter.cs` | 通过 `IEventBindingStep` 委托 |
| Operation/EventTriggerFramework | `DefaultEventBindingService.cs` | 通过 `IEventBindingStep` 委托 |
| Operation/EventTriggerFramework | `EventBindingPipeline.cs` | 通过 `IEventBindingStep` 委托 |
| Operation/EventTriggerFramework | `EventBindingTransformers.cs` | 通过 `IEventBindingStep` 委托 |
| 散落各处 | `FrameworkInitializer.cs` 等 | `EnsureEventExecutionService()` 统一管理 |

### 1.2 核心原则

1. **通用框架** — 不依赖 WinForms / Operation 层，位于 Share 层
2. **步骤化** — 每个阶段可独立配置、替换、跳过
3. **可观测** — 每个步骤都有诊断日志和耗时统计
4. **安全** — 异常隔离，单步失败不影响整体

---

## 2. 架构概览

### 2.1 完整链路

```
composites.json / layouts.json
  ↓
┌─ [TemplateExpand] ──────────────────────┐
│  步骤 1：模板展开                        │
│  委托给 PlaceholderFramework 的          │
│  TemplateExpandConverter                 │
└──────────────────────────────────────────┘
  ↓  (buildEvents 未被处理 ❌ → 现在由步骤 2 处理)
┌─ [BuildEvents] ─────────────────────────┐
│  步骤 2：buildEvents 处理（内置）        │
│  BuildEventsFromTemplate()               │
│  从 parameters 生成 events 配置          │
└──────────────────────────────────────────┘
  ↓
UIConfiguration (含 Events)
  ↓
┌─ [EventBinding] ────────────────────────┐
│  步骤 3：事件绑定执行                    │
│  委托给 EventTriggerFramework 的         │
│  DefaultEventBindingService              │
│  → EventBindingPipelineRunner            │
│  → EventBinderTransformer                │
│  → IWinFormsEventBinder.BindControlEvent │
└──────────────────────────────────────────┘
  ↓
┌─ [Diagnostic] ──────────────────────────┐
│  步骤 4：诊断输出（内置）                │
│  输出绑定结果摘要                        │
└──────────────────────────────────────────┘
  ↓
WinForms 原生事件绑定
```

### 2.2 框架组件

```
EventBindingOrchestrator (Share 层)
├── IEventBindingStep          ← 步骤接口（调用方实现）
├── EventBindingOrchestratorContext  ← 编排上下文
├── EventBindingOrchestrator   ← 编排器主类（单例）
│   ├── ExecutePipelineAsync() ← 按步骤执行管线
│   ├── BuildEventsFromTemplate() ← 内置：buildEvents 处理
│   ├── EnsureEventExecutionService() ← 注入管理
│   └── BindEventsWithDiagnosticAsync() ← 绑定触发
├── BuildEventsStep            ← 内置步骤：buildEvents
├── DiagnosticStep             ← 内置步骤：诊断输出
└── DelegateStep               ← 辅助：委托→步骤
```

---

## 3. 核心接口

### 3.1 IEventBindingStep — 步骤接口

```csharp
public interface IEventBindingStep
{
    string Name { get; }
    Task<bool> ExecuteAsync(EventBindingOrchestratorContext context);
}
```

调用方通过实现此接口注入具体逻辑：
- **TemplateExpand** — 由 FileDriven/Operation 实现，委托给 `TemplateExpandConverter`
- **EventBinding** — 由 FileDriven/Operation 实现，委托给 `DefaultEventBindingService`

### 3.2 EventBindingOrchestratorContext — 编排上下文

```csharp
public class EventBindingOrchestratorContext
{
    JsonObject? SourceJson;           // 原始 JSON 配置
    UIConfiguration? Config;          // 展开后的配置
    object? Target;                   // 目标对象（Form）
    object? SearchRoot;               // 查找根容器
    Func<string, object?>? CustomControlFinder; // 自定义查找
    BindingResult? Result;            // 绑定结果
    List<StepRecord> StepRecords;     // 步骤执行记录
    Dictionary<string, object?> Items; // 自定义数据存储
}
```

### 3.3 编排器 API

| 方法 | 说明 | 内置/委托 |
|------|------|-----------|
| `ExecutePipelineAsync(context, steps)` | 按步骤执行管线 | 编排 |
| `BuildEventsFromTemplate(source)` | 处理 buildEvents | 内置 |
| `EnsureEventExecutionService(service)` | 注入管理 | 内置 |
| `BindEventsWithDiagnosticAsync(...)` | 绑定触发 | 委托 |

---

## 4. 内置步骤

### 4.1 BuildEventsStep

处理 `buildEvents: true`，从模板 `parameters` 生成 `events` 配置。

**输入**：
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

### 4.2 DiagnosticStep

输出绑定结果摘要，包含：
- 绑定成功的控件数量
- 绑定跳过的控件数量及原因
- 总耗时

---

## 5. 使用示例

### 5.1 完整使用（FileDriven 集成）

```csharp
// 1. 获取编排器实例
var orchestrator = EventBindingOrchestrator.Instance;

// 2. 注入事件执行服务（在 FrameworkInitializer 阶段）
orchestrator.EnsureEventExecutionService(eventExecutionService);

// 3. 创建上下文
var ctx = new EventBindingOrchestratorContext
{
    SourceJson = jsonObject,
    Target = form,
    Config = uiConfig,
    SearchRoot = configContainer
};

// 4. 注册步骤并执行管线
var steps = new IEventBindingStep[]
{
    // 步骤 1：模板展开（委托给 PlaceholderFramework）
    new DelegateStep("TemplateExpand", async c =>
    {
        var converter = new TemplateExpandConverter(storage);
        var errors = new ErrorCollector();
        var result = converter.Convert(c.SourceJson!, errors, pipelineContext);
        return result != null;
    }),

    // 步骤 2：buildEvents 处理（内置）
    new BuildEventsStep(),

    // 步骤 3：事件绑定执行（委托给 EventTriggerFramework）
    new DelegateStep("EventBinding", async c =>
    {
        var result = await orchestrator.BindEventsWithDiagnosticAsync(
            bindingService: defaultEventBindingService,
            target: c.Target!,
            config: c.Config!,
            searchRoot: c.SearchRoot,
            customControlFinder: c.CustomControlFinder);
        c.Result = result;
        return result.BoundCount > 0 || result.SkippedCount == 0;
    }),

    // 步骤 4：诊断输出（内置）
    new DiagnosticStep()
};

// 5. 执行
bool allSuccess = await orchestrator.ExecutePipelineAsync(ctx, steps);
```

### 5.2 简化使用（仅 buildEvents + 绑定）

```csharp
var orchestrator = EventBindingOrchestrator.Instance;

// 处理 buildEvents
EventBindingOrchestrator.BuildEventsFromTemplate(jsonObject);

// 执行绑定
var result = await orchestrator.BindEventsWithDiagnosticAsync(
    bindingService, form, uiConfig);
```

### 5.3 仅注入管理

```csharp
var orchestrator = EventBindingOrchestrator.Instance;

// 在 FrameworkInitializer 中
orchestrator.EnsureEventExecutionService(eventExecutionService);

// 在其他地方检查
if (orchestrator.IsEventExecutionServiceInjected)
{
    var service = orchestrator.GetEventExecutionService();
    // 使用 service...
}
```

---

## 6. 依赖关系

### 6.1 项目依赖

```
EventBindingOrchestrator.csproj
├── BasicMethod（基础方法）
├── Modules.Pipeline（管线抽象）
├── Modules.Parsing（解析）
├── PlaceholderFramework（占位符/日志）
├── CommandFramework（命令框架）
├── UniversalMiddleware（中间件/配置模型）
└── Share.ConfigurationModels（配置模型）
```

### 6.2 层依赖

```
Operation 层（EventTriggerFramework）
  ↓ 调用
Share 层（EventBindingOrchestrator）
  ↓ 不依赖
Operation 层（通过弱类型 object 解耦）
```

---

## 7. 与现有代码的集成点

### 7.1 TemplateExpandConverter

在 `ExpandTemplate()` 方法末尾调用 `BuildEventsFromTemplate()`：

```csharp
// 在 return result 之前
EventBindingOrchestrator.BuildEventsFromTemplate(result);
```

### 7.2 FrameworkInitializer

创建 `EventExecutionService` 后调用 `EnsureEventExecutionService()`：

```csharp
var eventExecutionService = new EventExecutionServiceAdapter(backendService);
EventBindingOrchestrator.Instance.EnsureEventExecutionService(eventExecutionService);
```

### 7.3 DefaultEventBindingService

`BindEvents()` 方法内部可委托给 Orchestrator 的诊断包装：

```csharp
public void BindEvents(object target, UIConfiguration config, ...)
{
    var result = await EventBindingOrchestrator.Instance
        .BindEventsWithDiagnosticAsync(this, target, config, ...);
    // 使用 result 更新统计
}
```

---

## 8. 文件清单

| 文件 | 说明 |
|------|------|
| `EventBindingOrchestrator.csproj` | 项目文件 |
| `EventBindingOrchestrator.cs` | 编排器主类 + 步骤接口 + 内置步骤 |
| `Models/BindingResult.cs` | 绑定结果模型 |
| `Models/ControlBindingInfo.cs` | 控件绑定信息模型 |

---

## 9. 后续优化方向

1. **配置化步骤** — 支持从 JSON 配置步骤列表和顺序
2. **事件溯源** — 记录每个事件的绑定/触发历史
3. **性能统计** — 统计每个步骤的 P50/P95/P99 耗时
4. **步骤重试** — 支持失败步骤自动重试
5. **步骤依赖** — 支持步骤间的依赖关系声明
