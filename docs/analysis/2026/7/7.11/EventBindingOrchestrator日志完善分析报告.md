# EventBindingOrchestrator 日志完善分析报告

> **版本：** v1.1
> **维护者：** WorkFlowPrj Team
> **更新日期：** 2026-07-11
> **分析范围：** `CsharpPrj/Share/EventBindingOrchestrator` + `CsharpPrj/Modules/DiagnosticContext`
> **配套文档：** [`Architecture.md`](../../CsharpPrj/Architecture.md) | [`Implementation.md`](../../CsharpPrj/Implementation.md)

---

## 目录

- [EventBindingOrchestrator 日志完善分析报告](#eventbindingorchestrator-日志完善分析报告)
  - [目录](#目录)
  - [1. 架构总览](#1-架构总览)
    - [1.1 分层日志体系](#11-分层日志体系)
    - [1.2 依赖方向](#12-依赖方向)
    - [1.3 角色职责矩阵](#13-角色职责矩阵)
  - [2. 当前日志使用情况](#2-当前日志使用情况)
    - [2.1 正确用法（Share 层 → DiagnosticContext）](#21-正确用法share-层--diagnosticcontext)
    - [2.2 已废弃用法（Share 层 → EventTriggerLog）](#22-已废弃用法share-层--eventtriggerlog)
    - [2.3 降级路径（Debug.WriteLine）](#23-降级路径debugwriteline)
  - [3. 问题分析：Snapshot 为空](#3-问题分析snapshot-为空)
    - [3.1 问题现象](#31-问题现象)
    - [3.2 调用链分析](#32-调用链分析)
    - [3.3 根因定位](#33-根因定位)
    - [3.4 异常传播路径](#34-异常传播路径)
    - [3.5 影响范围](#35-影响范围)
  - [4. 修复方案](#4-修复方案)
    - [4.1 方案对比](#41-方案对比)
    - [4.2 选定方案：EventCollector 队列模式 + 三重 try-catch 辅助](#42-选定方案eventcollector-队列模式--三重-try-catch-辅助)
    - [4.3 设计细节](#43-设计细节)
      - [4.3.1 PendingEvent 模型](#431-pendingevent-模型)
      - [4.3.2 AsyncLocal 队列](#432-asynclocal-队列)
      - [4.3.3 Record() — 只入队，不序列化](#433-record--只入队不序列化)
      - [4.3.4 Flush() — 批量序列化并写入 ScopeNode](#434-flush--批量序列化并写入-scopenode)
      - [4.3.5 GetSnapshotString() — 先 Flush 再获取快照](#435-getsnapshotstring--先-flush-再获取快照)
      - [4.3.6 BeginScope() — 初始化队列](#436-beginscope--初始化队列)
      - [4.3.7 Dispose() — 先 Flush 再释放](#437-dispose--先-flush-再释放)
    - [4.4 辅助保障：三重 try-catch](#44-辅助保障三重-try-catch)
    - [4.5 修复后效果](#45-修复后效果)
  - [5. 实施步骤](#5-实施步骤)
    - [5.1 修改 EventEntry.cs](#51-修改-evententrycs)
    - [5.2 修改 ScopeNode.cs](#52-修改-scopenodecs)
    - [5.3 修改 DiagnosticContext.cs](#53-修改-diagnosticcontextcs)
    - [5.4 验证方法](#54-验证方法)
  - [6. 附录](#6-附录)
    - [6.1 关键源文件清单](#61-关键源文件清单)
    - [6.2 事件命名规范速查](#62-事件命名规范速查)
    - [6.3 修订历史](#63-修订历史)

---

## 1. 架构总览

### 1.1 分层日志体系

```
┌─────────────────────────────────────────────────────────────┐
│                    OPERATION 层                              │
│  BackEndLog / EventTriggerLog / WdUILog / WorkFlowMethodLog │
│  ① LogPlaceholderHandlerBase.Log  →  FileDriven 日志        │
│  ② PlaceholderLogHelper.Log       →  FileDriven 日志        │
│  ③ Debug.WriteLine                →  VS 输出窗口             │
└───────────────────────┬─────────────────────────────────────┘
                        │ 引用
┌───────────────────────▼─────────────────────────────────────┐
│                     SHARE 层                                 │
│  EventBindingDiagnosticContext                               │
│  BeginScope / Record / CaptureSnapshot / GetSnapshotString   │
│  只负责插桩，不关心日志如何输出                                │
└───────────────────────┬─────────────────────────────────────┘
                        │ 引用
┌───────────────────────▼─────────────────────────────────────┐
│              MODULES.DIAGNOSTICCONTEXT 层                    │
│  DiagnosticContext / DiagnosticManager / ScopeNode / EventEntry│
│  AsyncLocal 自动传播 + 作用域管理 + 事件存储                   │
└───────────────────────┬─────────────────────────────────────┘
                        │ 引用
┌───────────────────────▼─────────────────────────────────────┐
│                  BASICMETHOD 层                               │
│  AsyncContext<T> / JsonSerializer                            │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 依赖方向

```
Operation → Share → Share.Diagnostics → Modules.DiagnosticContext → BasicMethod
```

**关键约束：** Share 层**不能**反向引用 Operation 层。

### 1.3 角色职责矩阵

| 层 | 角色 | 职责 | 不负责 |
|----|------|------|--------|
| **Operation** | 日志消费者 | 控制日志输出时机、级别、持久化 | 不负责插桩事件的定义 |
| **Share** | 插桩生产者 | 记录结构化插桩事件 | 不关心日志如何输出 |
| **Share.Diagnostics** | 基础设施 | 日志管线编排、内存仓库、SQLite 持久化 | 不定义业务日志语义 |
| **Modules.DiagnosticContext** | 插桩框架 | AsyncLocal 传播、作用域管理、事件存储 | 不涉及日志输出策略 |

---

## 2. 当前日志使用情况

### 2.1 正确用法（Share 层 → DiagnosticContext）

| 文件 | 行号 | 调用 | 说明 |
|------|------|------|------|
| `EventBindingPipelineRunner.cs` | 80 | `Record(EventNames.PipelineExecuteWithWalkStart, ...)` | 管线开始 |
| `EventBindingPipelineRunner.cs` | 87 | `Record(EventNames.PipelineExecuteWithWalkComplete, ...)` | 管线完成 |
| `EventBindingPipelineRunner.cs` | 98 | `Record(EventNames.PipelineSnapshot, ...)` | 管线快照 |
| `GenericEventBindingPipeline.cs` | 111 | `Record(EventNames.PipelineWalkAndBindVisiting, ...)` | 正在访问控件 |
| `GenericEventBindingPipeline.cs` | 120 | `Record(EventNames.PipelineWalkAndBindMapResult, ...)` | 映射结果 |
| `GenericEventBindingPipeline.cs` | 129 | `Record(EventNames.PipelineWalkAndBindExecuting, ...)` | 正在执行绑定 |
| `EventBinderTransformer.cs` | 98 | `Record(EventNames.EventBinderTransformEntry, ...)` | 转换器入口 |
| `EventBinderTransformer.cs` | 113 | `Record(EventNames.EventBinderBindFromDeclaration, ...)` | 从声明绑定 |
| `EventBinderTransformer.cs` | 125 | `Record(EventNames.EventBinderSkipNoConfig, ...)` | 跳过无配置项 |
| `EventBinderTransformer.cs` | 152 | `Record(EventNames.EventBinderForEachBoundEvent, ...)` | 遍历绑定事件 |
| `EventBinderTransformer.cs` | 488 | `Record(diagnosticKey, diagnosticData)` | 绑定结果 |

### 2.2 已废弃用法（Share 层 → EventTriggerLog）

以下调用已标记为废弃，应迁移到 `EventBindingDiagnosticContext.Record()`：

| 文件 | 行号 | 调用 | 说明 |
|------|------|------|------|
| `DefaultEventBindingService.cs` | 121 | `EventTriggerLog.Log("EventBindingFunctionResult", ...)` | 函数执行结果 |
| `DefaultEventBindingService.cs` | 180 | `EventTriggerLog.Log("DefaultEventBindingService_NoViews", ...)` | 无 Views |
| `DefaultEventBindingService.cs` | 185 | `EventTriggerLog.Log("DefaultEventBindingService_BindStart", ...)` | 绑定开始 |
| `DefaultEventBindingService.cs` | 191 | `EventTriggerLog.Log("DefaultEventBindingService_SearchRoot", ...)` | 搜索根 |
| `DefaultEventBindingService.cs` | 204 | `EventTriggerLog.Log("DefaultEventBindingService_BindComplete", ...)` | 绑定完成 |
| `DefaultEventBindingService.cs` | 222 | `EventTriggerLog.Log("DiagnosticSnapshot", ...)` | 诊断快照 |

> **注意：** 这些调用属于 Operation 层（`DefaultEventBindingService` 在 `BackEndDriven` 项目中），使用 `EventTriggerLog.Log()` 是符合架构的。但 `DiagnosticSnapshot` 的 snapshot 内容为空，说明底层 `DiagnosticContext` 没有记录到事件。

### 2.3 降级路径（Debug.WriteLine）

当 `EventBindingDiagnosticContext.Record()` 的 `Current` 为 null 时，降级到 `Debug.WriteLine`：

```csharp
// EventBindingDiagnosticContext.cs 第162-166行
var dataJson = data != null
    ? JsonSerializer.Serialize(data, JsonSerializerFactory.Contexts.LogExport)
    : "";
System.Diagnostics.Debug.WriteLine($"[EventBinding] {eventName} {dataJson}");
```

> **注意：** 降级路径中同样有 `JsonSerializer.Serialize()` 调用，如果 `data` 包含无法序列化的对象，同样会抛出异常。

---

## 3. 问题分析：Snapshot 为空

### 3.1 问题现象

日志中 `DiagnosticSnapshot` 的 snapshot 只有标题 `EventBindingOrchestrator`，没有事件内容：

```
"snapshot": "\r\nEventBindingOrchestrator\r\n",
"snapshotOriginalLength": 26
```

`TextFormatter.Format()` 输出 `node.Name` + 换行符 = `"EventBindingOrchestrator\r\n"`，长度为 26。说明 `node.Events.Count == 0`，即 `ScopeNode._events` 列表为空。

### 3.2 调用链分析

```
EventBindingPipelineRunner.Execute()
  ├── using var diagScope = BeginScope()          // 创建上下文
  │     ├── DiagnosticManager.BeginScope()        // 创建 DiagnosticContext
  │     │     ├── new DiagnosticContext(name)      // 创建 ScopeNode
  │     │     └── context.Activate()              // 设置 AsyncLocal
  │     └── AsyncContext<DiagnosticContext>.CreateScope(diagCtx)
  │           └── 设置 AsyncContext._currentScope  // 另一个 AsyncLocal
  │
  ├── Record(PipelineExecuteWithWalkStart)        // 记录开始
  │     └── AsyncContext<DiagnosticContext>.Current → diagCtx
  │           └── diagCtx.Record()
  │                 └── CurrentScope.Record()
  │                       └── new EventEntry(eventName, relativeMs, data)
  │                             └── JsonSerializer.Serialize(data, JsonOptions.Compact)  // ← 可能异常！
  │
  ├── _genericPipeline.ExecuteWithWalk()          // 遍历控件树
  │     └── WalkAndBind()
  │           └── Walk() → callback
  │                 ├── Record(PipelineWalkAndBindVisiting)    // ← 可能异常
  │                 ├── ConfigToDeclarationMapper.Map()        // ← 可能异常
  │                 ├── Record(PipelineWalkAndBindMapResult)   // ← 可能异常
  │                 └── Execute(context)                       // 执行 Transformer 链
  │                       ├── ControlFinderTransformer.Transform()
  │                       ├── BindingMetadataTransformer.Transform()
  │                       └── EventBinderTransformer.Transform()
  │                             ├── Record(EventBinderTransformEntry)    // ← 可能异常
  │                             ├── Record(EventBinderForEachBoundEvent) // ← 可能异常
  │                             └── Record(EventBinderBoundViaEventBus)  // ← 可能异常
  │
  ├── Record(PipelineExecuteWithWalkComplete)     // ← 中断后不执行
  ├── Record(PipelineSnapshot)                    // ← 中断后不执行
  └── return context
```

### 3.3 根因定位

**根因：** `EventEntry` 构造函数中 `JsonSerializer.Serialize(data, JsonOptions.Compact)` 抛出异常。

```csharp
// EventEntry.cs 第45行
DataJson = data != null ? JsonSerializer.Serialize(data, JsonOptions.Compact) : null;
```

当 `data` 包含以下类型时，`JsonSerializer.Serialize()` 会抛出 `JsonException`：

| 类型 | 原因 |
|------|------|
| `System.Windows.Forms.Control` | 循环引用（控件树） |
| `IntPtr` | 无法序列化 |
| `SafeHandle` | 无法序列化 |
| `Delegate` / `EventHandler` | 无法序列化 |
| 包含循环引用的对象图 | 循环引用 |
| 包含 `Type` 属性的对象 | 类型无法序列化 |

**异常传播路径（无 try-catch）：**

```
EventEntry 构造函数抛出 JsonException
  → ScopeNode.Record() 无 try-catch → 异常向上传播
    → DiagnosticContext.Record() 无 try-catch → 异常向上传播
      → EventBindingDiagnosticContext.Record() 无 try-catch → 异常向上传播
        → EventBinderTransformer.Transform() 无 try-catch → 异常向上传播
          → GenericEventBindingPipeline.Execute() 无 try-catch → 异常向上传播
            → GenericEventBindingPipeline.WalkAndBind() 无 try-catch → 异常向上传播
              → EventBindingPipelineRunner.Execute() 无 try-catch → 异常向上传播
                → DefaultEventBindingService.BindEvents() 有 try-catch → 异常被吞掉
```

### 3.4 异常传播路径

```
[EventEntry 构造函数]
  JsonSerializer.Serialize(data, JsonOptions.Compact)
  → 抛出 JsonException
    ↓
[ScopeNode.Record()]
  _events.Add(new EventEntry(...))  // 未执行
  → 异常向上传播
    ↓
[DiagnosticContext.Record()]
  CurrentScope.Record(eventName, data)  // 异常传播
  → 异常向上传播
    ↓
[EventBindingDiagnosticContext.Record()]
  ctx.Record(eventName, data)  // 异常传播
  → 异常向上传播
    ↓
[调用方（如 EventBinderTransformer.Transform()）]
  → 异常向上传播
    ↓
[上层调用方]
  → 异常被吞掉，后续所有 Record() 不执行
```

### 3.5 影响范围

| 影响 | 说明 |
|------|------|
| **当前 Record() 失败** | 事件未记录到 `ScopeNode._events` |
| **后续所有 Record() 不执行** | 异常传播导致整个调用链中断 |
| **GetSnapshotString() 不执行** | 或执行时 snapshot 为空 |
| **诊断信息丢失** | 无法定位中断位置 |
| **调试困难** | 无错误日志，只能通过现象推断 |

---

## 4. 修复方案

### 4.1 方案对比

| 方案 | 修改范围 | 优点 | 缺点 |
|------|----------|------|------|
| **A: Share 层修复** | `EventBindingDiagnosticContext.cs` | 改动小 | 其他模块同样有风险 |
| **B: 框架层三重 try-catch** | `EventEntry.cs` + `ScopeNode.cs` + `DiagnosticContext.cs` | 所有模块自动受益 | 无法定位中断位置 |
| **C: EventCollector 队列模式**（推荐） | `DiagnosticContext.cs` 引入 `ConcurrentQueue` | 最健壮，可定位中断位置，不受中断影响 | 需要修改框架层 |

### 4.2 选定方案：EventCollector 队列模式 + 三重 try-catch 辅助

**核心思路：** 在 `DiagnosticContext` 框架层引入 `ConcurrentQueue`，`Record()` 只入队不序列化，`Flush()` 在 `Dispose()` 或 `GetSnapshotString()` 时统一批量序列化。

```
Record() 调用
  → 入队 ConcurrentQueue（O(1)，不抛异常）
  → 立即返回

GetSnapshotString() / Dispose() 时
  → Flush() 批量出队
  → 逐个 JsonSerializer.Serialize()
  → 失败时降级为 ToString() + 错误标记
  → 写入 ScopeNode._events
```

**优势：**
- **不受中断影响**：即使中间代码异常中断，已入队的事件不会丢失（队列在 AsyncLocal 中）
- **可定位中断位置**：Flush 时记录 `diagnostic.serialize_error` 事件，精确定位哪个事件序列化失败
- **线程安全**：`ConcurrentQueue` 原生支持多线程并发入队
- **性能更好**：序列化从每次 Record 调用延迟到 Flush 时批量执行

### 4.3 设计细节

#### 4.3.1 PendingEvent 模型

```csharp
/// <summary>
/// 待处理事件（入队时暂存原始数据，Flush 时再序列化）
/// </summary>
private sealed class PendingEvent
{
    public string EventName { get; }
    public object? Data { get; }
    public DateTime Timestamp { get; }

    public PendingEvent(string eventName, object? data, DateTime timestamp)
    {
        EventName = eventName;
        Data = data;
        Timestamp = timestamp;
    }
}
```

#### 4.3.2 AsyncLocal 队列

```csharp
/// <summary>
/// 当前异步流上的待处理事件队列
/// </summary>
private static readonly AsyncLocal<ConcurrentQueue<PendingEvent>> _pendingQueue = new();
```

#### 4.3.3 Record() — 只入队，不序列化

```csharp
public static void Record(string eventName, object? data = null)
{
    var queue = _pendingQueue.Value;
    if (queue != null)
    {
        queue.Enqueue(new PendingEvent(eventName, data, DateTime.UtcNow));
        return;
    }

    // 降级：无作用域时 Debug.WriteLine
    var dataJson = data != null
        ? JsonSerializer.Serialize(data, JsonSerializerFactory.Contexts.LogExport)
        : "";
    System.Diagnostics.Debug.WriteLine($"[EventBinding] {eventName} {dataJson}");
}
```

#### 4.3.4 Flush() — 批量序列化并写入 ScopeNode

```csharp
/// <summary>
/// 刷新队列：将收集的事件批量写入 DiagnosticContext
/// </summary>
private static void Flush()
{
    var queue = _pendingQueue.Value;
    if (queue == null || queue.IsEmpty) return;

    var ctx = Current;
    if (ctx == null) return;

    while (queue.TryDequeue(out var pending))
    {
        try
        {
            ctx.Record(pending.EventName, pending.Data);
        }
        catch (Exception ex)
        {
            // 序列化失败时，降级为字符串并记录错误标记
            ctx.Record("diagnostic.serialize_error", new
            {
                eventName = pending.EventName,
                error = ex.Message,
                raw = pending.Data?.ToString()
            });
        }
    }
}
```

#### 4.3.5 GetSnapshotString() — 先 Flush 再获取快照

```csharp
public static string? GetSnapshotString()
{
    Flush();  // 先刷新队列，确保所有事件已写入 ScopeNode
    var ctx = Current;
    if (ctx != null)
    {
        var snapshot = ctx.ToString();
        if (!string.IsNullOrEmpty(snapshot))
            return snapshot;
    }
    return null;
}
```

#### 4.3.6 BeginScope() — 初始化队列

```csharp
public static IDisposable BeginScope()
{
    var diagCtx = DiagnosticManager.BeginScope(ScopeName);
    var asyncScope = AsyncContext<DiagnosticContext>.CreateScope(diagCtx);

    // 初始化待处理事件队列
    _pendingQueue.Value = new ConcurrentQueue<PendingEvent>();

    _currentAsyncScope.Value = asyncScope;

    diagCtx.Record(EventNames.EventBinderScopeBegan, new
    {
        scopeName = ScopeName,
        createdAt = diagCtx.CreatedAt
    });

    return new ScopeDisposable(diagCtx);
}
```

#### 4.3.7 Dispose() — 先 Flush 再释放

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;

    // 1. 先刷新队列（批量写入 DiagnosticContext）
    Flush();

    // 2. 再释放资源
    _currentAsyncScope.Value = null;
    _diagCtx.Dispose();
    _pendingQueue.Value = null;
}
```

### 4.4 辅助保障：三重 try-catch

在队列模式的基础上，保留 `EventEntry.cs`、`ScopeNode.cs`、`DiagnosticContext.cs` 的 try-catch 作为辅助保障：

```
┌─────────────────────────────────────────────────────────────┐
│  主保障：EventCollector 队列                                 │
│  Record 只入队 → 不抛异常 → 不受中断影响                      │
├─────────────────────────────────────────────────────────────┤
│  第1层辅助：EventEntry 构造函数 try-catch                     │
│  序列化失败 → 降级为 ToString() + 错误标记                    │
├─────────────────────────────────────────────────────────────┤
│  第2层辅助：ScopeNode.Record() try-catch                      │
│  极端情况 → 记录错误事件                                     │
├─────────────────────────────────────────────────────────────┤
│  第3层辅助：DiagnosticContext.Record() try-catch              │
│  最终保障 → 静默忽略                                         │
└─────────────────────────────────────────────────────────────┘
```

### 4.5 修复后效果

修复后，即使中间代码异常中断，已记录的事件不会丢失：

```
修复前（异常中断）：
  Record("eventbinder.transform.entry", ...)  // 执行
  Record("eventbinder.transform.for_each_bound_event", ...)  // 抛出异常！
  Record("eventbinder.bound.via_event_bus", ...)  // 不执行（中断）
  Record("pipeline.execute_with_walk.complete", ...)  // 不执行（中断）
  → Snapshot 为空

修复后（异常中断）：
  Record("eventbinder.transform.entry", ...)  // 入队成功
  Record("eventbinder.transform.for_each_bound_event", ...)  // 入队成功（即使后续异常）
  // ← 此处代码异常中断，但队列中已有 2 个事件
  // Dispose() 时 Flush() → 2 个事件写入 ScopeNode
  // 但 GetSnapshotString() 在 Dispose 前调用，所以需要在 GetSnapshotString 中也调用 Flush()
```

修复后的 Snapshot 示例：

```
EventBindingOrchestrator
  eventbinder.scope.began          { scopeName=EventBindingOrchestrator, ... }
  pipeline.execute_with_walk.start { targetType=..., viewCount=... }
  pipeline.walk_and_bind.visiting  { controlName=..., hasEvents=true, ... }
  eventbinder.transform.entry      { _serializeError=..., _raw=... }  ← 序列化失败但未中断
  eventbinder.transform.for_each_bound_event { controlName=..., ... }
  diagnostic.serialize_error       { eventName=eventbinder.bound.via_event_bus, error=... }  ← 定位中断位置
```

---

## 5. 实施步骤

### 5.1 修改 EventEntry.cs

**文件：** `CsharpPrj/Modules/DiagnosticContext/Core/EventEntry.cs`

**修改内容：** 构造函数添加 try-catch，序列化失败降级为 `ToString()` + 错误标记。

**修改后代码：**

```csharp
public EventEntry(string eventName, long relativeMs, object? data = null)
{
    EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
    RelativeMs = relativeMs;
    if (data != null)
    {
        try
        {
            DataJson = JsonSerializer.Serialize(data, JsonOptions.Compact);
        }
        catch (Exception ex)
        {
            // 序列化失败时降级为 ToString()，并记录错误标记
            DataJson = $"{{ \"_serializeError\": \"{ex.Message}\", \"_raw\": \"{data.ToString()?.Replace("\"", "'")}\" }}";
        }
    }
}
```

### 5.2 修改 ScopeNode.cs

**文件：** `CsharpPrj/Modules/DiagnosticContext/Core/ScopeNode.cs`

**修改内容：** `Record()` 方法添加 try-catch，极端情况记录错误事件。

**修改后代码：**

```csharp
public void Record(string eventName, object? data = null)
{
    if (_disposed) return;
    try
    {
        var relativeMs = (long)(DateTime.UtcNow - Root._createdAt).TotalMilliseconds;
        _events.Add(new EventEntry(eventName, relativeMs, data));
    }
    catch (Exception ex)
    {
        // 极端情况降级：记录错误事件
        var fallbackMs = (long)(DateTime.UtcNow - Root._createdAt).TotalMilliseconds;
        _events.Add(new EventEntry(eventName + "._error", fallbackMs, new { error = ex.Message }));
    }
}
```

### 5.3 修改 DiagnosticContext.cs

**文件：** `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticContext.cs`

**修改内容：** `Record()` 方法添加 try-catch，确保不中断调用方。

**修改后代码：**

```csharp
public void Record(string eventName, object? data = null)
{
    if (_disposed) return;
    try
    {
        CurrentScope.Record(eventName, data);
    }
    catch
    {
        // 静默忽略，确保不中断调用方
    }
}
```

### 5.4 验证方法

1. **编译验证：** `dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln"` 确认编译通过

2. **运行验证：** 启动应用程序，触发事件绑定流程，检查日志中 `DiagnosticSnapshot` 是否包含事件内容

3. **异常场景验证：** 构造包含无法序列化对象的 `data` 参数，确认：
   - `Record()` 不抛出异常
   - Snapshot 包含 `_serializeError` 标记
   - 后续 `Record()` 正常执行

---

## 6. 附录

### 6.1 关键源文件清单

| 文件 | 说明 |
|------|------|
| `CsharpPrj/Modules/DiagnosticContext/Core/EventEntry.cs` | 事件条目模型（需修复） |
| `CsharpPrj/Modules/DiagnosticContext/Core/ScopeNode.cs` | 嵌套作用域节点（需修复） |
| `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticContext.cs` | 诊断上下文核心（需修复） |
| `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticManager.cs` | 诊断上下文管理器 |
| `CsharpPrj/Modules/DiagnosticContext/Formatting/TextFormatter.cs` | 文本格式化器 |
| `CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs` | Share 层诊断上下文辅助类 |
| `CsharpPrj/Share/EventBindingOrchestrator/Transformers/EventBindingPipelineRunner.cs` | 管线执行器 |
| `CsharpPrj/Share/EventBindingOrchestrator/Transformers/GenericEventBindingPipeline.cs` | 通用管线 |
| `CsharpPrj/Share/EventBindingOrchestrator/Parameters/EventBinderTransformer.cs` | 事件绑定转换器 |
| `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/DefaultEventBindingService.cs` | Operation 层默认实现 |
| `CsharpPrj/BasicMethod/ContextFrame/Scoping/AsyncContext.cs` | AsyncLocal 作用域管理 |

### 6.2 事件命名规范速查

| 领域 | 事件名 | 含义 |
|------|--------|------|
| pipeline | `pipeline.execute_with_walk.start` | 管线遍历绑定开始 |
| pipeline | `pipeline.execute_with_walk.complete` | 管线遍历绑定完成 |
| pipeline | `pipeline.walk_and_bind.visiting` | 正在访问控件 |
| pipeline | `pipeline.walk_and_bind.executing` | 正在执行绑定 |
| pipeline | `pipeline.walk_and_bind.map_result` | 映射结果 |
| eventbinder | `eventbinder.transform.entry` | 转换器入口 |
| eventbinder | `eventbinder.transform.bind_from_declaration` | 从声明绑定 |
| eventbinder | `eventbinder.transform.skip_no_config` | 跳过无配置项 |
| eventbinder | `eventbinder.transform.for_each_bound_event` | 遍历绑定事件 |
| eventbinder | `eventbinder.function.success` | 函数执行成功 |
| eventbinder | `eventbinder.function.exception` | 函数执行异常 |
| eventbinder | `eventbinder.function.cancelled` | 函数执行取消 |
| eventbinder | `eventbinder.parameter.resolve_failed` | 参数解析失败 |
| eventbinder | `eventbinder.parameter.type_conversion_failed` | 类型转换失败 |
| eventbinder | `eventbinder.parameter.source_not_found` | 参数源未找到 |
| eventbinder | `eventbinder.parameter.empty_value` | 参数值为空 |
| eventbinder | `eventbinder.event.triggered` | 事件触发 |
| eventbinder | `eventbinder.reentry.skip` | 防重入跳过 |
| eventbinder | `eventbinder.scope.began` | 作用域开始 |
| eventbinder | `eventbinder.scope.restored` | 作用域恢复 |
| eventbinder | `eventbinder.workflow.success` | 工作流执行成功 |
| eventbinder | `eventbinder.workflow.failed` | 工作流执行失败 |
| diagnostic | `diagnostic.serialize_error` | 序列化错误（修复后新增） |

### 6.3 修订历史

| 日期 | 版本 | 修订内容 | 修订人 |
|------|------|----------|--------|
| 2026-07-11 | v1.0 | 初始版本，完成问题分析和修复方案设计 | WorkFlowPrj Team |
| 2026-07-11 | v1.1 | 推荐方案改为 EventCollector 队列模式 + 三重 try-catch 辅助 | WorkFlowPrj Team |
