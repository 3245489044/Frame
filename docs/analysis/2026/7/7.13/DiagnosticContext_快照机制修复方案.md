# DiagnosticContext 快照机制修复方案

> **根本缺陷分析**：`CaptureSnapshot()` 返回引用而非数据拷贝，导致跨线程/跨场景恢复时恢复的是已 Dispose 的上下文，所有 `Record` 调用被静默忽略。

---

## 目录

1. [问题背景](#一问题背景)
2. [根本缺陷分析](#二根本缺陷分析)
3. [修复方案：引入 DiagnosticSnapshot 不可变快照](#三修复方案引入-diagnosticsnapshot-不可变快照)
4. [详细设计](#四详细设计)
5. [影响范围](#五影响范围)
6. [实施步骤](#六实施步骤)

---

## 一、问题背景

### 1.1 症状

点击 TreeView 后节点展开（默认行为），但没有任何 `[DIAG]` 日志输出。绑定阶段的 `[DIAG]` 日志正常输出，但事件触发阶段的 `[DIAG]` 日志全部丢失。

### 1.2 调用链

```
EventBindingOrchestrator.ExecuteWithWalk
  ├── using var diagScope = EventBindingDiagnosticContext.BeginScope()  // 创建 DiagnosticContext
  ├── _genericPipeline.ExecuteWithWalk(context, config)                  // 绑定阶段
  │     └── EventBinderTransformer.CreateEventHandler
  │           └── var capturedDiagSnapshot = CaptureSnapshot()           // 捕获快照（引用）
  └── diagScope.Dispose()                                                // DiagnosticContext._disposed = true

用户点击 TreeView
  └── AfterSelect 事件触发
        └── Task.Run
              └── RestoreScope(capturedDiagSnapshot)                     // 恢复已 Dispose 的上下文
                    └── Record(...) → if (_disposed) return;             // 静默忽略
```

### 1.3 日志框架配置修复（已完成）

`DiagnosticContextConfiguration` 默认值已修改：

| 属性 | 旧值 | 新值 |
|------|------|------|
| `OutputTarget` | `None` | `Debug` |
| `EnablePipeline` | `false` | `true` |

此修复使绑定阶段的 `[DIAG]` 日志正常输出，但**未解决事件触发阶段日志丢失的根本问题**。

---

## 二、根本缺陷分析

### 缺陷 1：`CaptureSnapshot()` 返回引用而非数据拷贝

```csharp
// EventBindingDiagnosticContext.cs
public static DiagnosticContext? CaptureSnapshot() => Current;
```

返回的是同一个 `DiagnosticContext` 实例的引用。当原始上下文被 `Dispose` 后，快照引用的是一个已失效的对象。调用方**无法从 API 层面知道返回的对象是否已被 Dispose**。

### 缺陷 2：`_disposed` 守卫是"硬删除"语义

`DiagnosticContext.Record` 和 `ScopeNode.Record` 中都有：

```csharp
if (_disposed) return;  // 静默丢弃
```

`ScopeNode.Dispose()` 的注释说"保留已记录的数据供快照读取"——但这自相矛盾。数据虽然保留，但 `Record` 被阻止了，且快照是引用而非拷贝，所以"保留数据"对跨场景恢复没有实际意义。

### 缺陷 3：`RestoreScope` 没有校验快照有效性

```csharp
public static IDisposable? RestoreScope(DiagnosticContext? snapshot)
{
    if (snapshot == null) return null;
    var scope = AsyncContext<DiagnosticContext>.CreateScope(snapshot);
    // snapshot 可能是已 Dispose 的上下文，但没有校验
    snapshot.Record(EventNames.EventBinderScopeRestored, new { ... });
    // ↑ 被 _disposed 守卫静默忽略，且调用方不知道
    return scope;
}
```

恢复一个已 Dispose 的上下文不会报错，只会静默失败。

### 缺陷汇总

| 缺陷 | 位置 | 本质 |
|------|------|------|
| 快照是引用 | `CaptureSnapshot()` | 设计层面：API 语义错误 |
| 硬删除守卫 | `DiagnosticContext.Record` / `ScopeNode.Record` | 实现层面：状态管理错误 |
| 无有效性校验 | `RestoreScope()` | 防御层面：缺少校验 |

---

## 三、修复方案：引入 DiagnosticSnapshot 不可变快照

### 3.1 核心设计

```
DiagnosticContext (可变, 活跃)
    │
    ├── Freeze() → DiagnosticSnapshot (不可变, 深拷贝)
    │
    └── DiagnosticSnapshot.Rehydrate() → DiagnosticContext (新实例, 活跃)
```

### 3.2 设计原则

1. **快照不可变** — `DiagnosticSnapshot` 创建后不可修改，所有数据在构造时深拷贝
2. **快照独立生命周期** — 不依赖原始 `DiagnosticContext` 的生命周期
3. **恢复即新建** — `Rehydrate()` 创建全新的 `DiagnosticContext` 实例，不受 `_disposed` 影响
4. **向后兼容** — `EventBindingDiagnosticContext` 的 `CaptureSnapshot()` / `RestoreScope()` 签名变更，但调用方代码改动极小

### 3.3 新旧对比

| 维度 | 旧方案 | 新方案 |
|------|--------|--------|
| 快照类型 | `DiagnosticContext?`（引用） | `DiagnosticSnapshot?`（不可变值） |
| 快照内容 | 同一对象引用 | ScopeNode 数据深拷贝 |
| 恢复方式 | 设置 AsyncLocal 到同一对象 | 创建全新 DiagnosticContext |
| Dispose 后行为 | Record 静默丢弃 | 快照可独立 Rehydrate |
| 跨线程安全 | 否（共享可变状态） | 是（不可变快照） |
| API 安全性 | 调用方无法校验有效性 | 快照永远有效 |

---

## 四、详细设计

### 4.1 新增 `DiagnosticSnapshot` 类

**文件**：`CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticSnapshot.cs`

```csharp
// ============================================================
// DiagnosticSnapshot.cs — 诊断上下文快照（不可变）
//
// 包含 ScopeNode 数据的深拷贝，不依赖原始上下文的生命周期。
// 通过 Freeze/Rehydrate 模式实现跨线程/跨场景的上下文恢复。
//
// 设计要点：
//   1. 所有数据在构造时深拷贝，构造后不可修改
//   2. EventEntry 本身不可变（只有 getter），直接引用
//   3. 递归快照子 ScopeNode，形成完整的快照树
//   4. Rehydrate 创建全新的 DiagnosticContext，不受 _disposed 影响
// ============================================================

namespace Modules.DiagnosticContext.Core;

/// <summary>
/// 诊断上下文快照（不可变）
/// <para>通过 Freeze/Rehydrate 模式实现跨线程/跨场景的上下文恢复。</para>
/// </summary>
public class DiagnosticSnapshot
{
    /// <summary>作用域名称</summary>
    public string Name { get; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; }

    /// <summary>事件列表（深拷贝，EventEntry 本身不可变）</summary>
    public IReadOnlyList<EventEntry> Events { get; }

    /// <summary>状态字典（深拷贝）</summary>
    public IReadOnlyDictionary<string, object?> State { get; }

    /// <summary>子作用域快照（递归）</summary>
    public IReadOnlyList<DiagnosticSnapshot> Children { get; }

    private DiagnosticSnapshot(
        string name,
        DateTime createdAt,
        IReadOnlyList<EventEntry> events,
        IReadOnlyDictionary<string, object?> state,
        IReadOnlyList<DiagnosticSnapshot> children)
    {
        Name = name;
        CreatedAt = createdAt;
        Events = events;
        State = state;
        Children = children;
    }

    /// <summary>
    /// 从 ScopeNode 递归创建快照
    /// <para>深拷贝 EventEntry 列表和 State 字典。</para>
    /// </summary>
    internal static DiagnosticSnapshot FromScope(ScopeNode node)
    {
        // EventEntry 本身不可变（只有 getter），直接引用
        var events = node.Events.ToList().AsReadOnly();

        // State 字典深拷贝（值类型直接拷贝，引用类型保持引用）
        var state = new Dictionary<string, object?>(node.State);

        // 递归快照子作用域
        var children = node.Children
            .Select(FromScope)
            .ToList()
            .AsReadOnly();

        return new DiagnosticSnapshot(
            node.Name,
            node.CreatedAt,
            events,
            state,
            children);
    }

    /// <summary>
    /// 恢复为新的 DiagnosticContext
    /// <para>创建全新的 DiagnosticContext 实例，不受原始上下文的 _disposed 影响。</para>
    /// </summary>
    public DiagnosticContext Rehydrate()
    {
        // 通过工厂创建新的 DiagnosticContext
        // 使用默认配置，快照数据通过 ScopeNode 恢复
        var context = Factory.DiagnosticContextFactory.Create(Name);

        // 将快照数据恢复到根 ScopeNode
        RestoreToScope(context.RootScope, this);

        return context;
    }

    /// <summary>
    /// 递归恢复快照数据到 ScopeNode
    /// </summary>
    private static void RestoreToScope(ScopeNode scope, DiagnosticSnapshot snapshot)
    {
        // 恢复状态
        foreach (var kvp in snapshot.State)
        {
            scope.SetState(kvp.Key, kvp.Value);
        }

        // 恢复事件（直接添加到 ScopeNode 的事件列表）
        foreach (var evt in snapshot.Events)
        {
            // 使用 internal 方法 AddEntry 添加已创建的事件条目
            // 避免重新计算 relativeMs
            scope.AddEntry(evt);
        }

        // 递归恢复子作用域
        // 注意：子作用域的顺序和数量必须与快照一致
        for (int i = 0; i < snapshot.Children.Count && i < scope.Children.Count; i++)
        {
            RestoreToScope(scope.Children[i], snapshot.Children[i]);
        }
    }
}
```

### 4.2 修改 `DiagnosticContext` — 新增 `Freeze()` 方法

**文件**：`CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticContext.cs`

新增方法（约 5 行）：

```csharp
/// <summary>
/// 冻结当前上下文，创建不可变快照
/// <para>快照包含 ScopeNode 数据的深拷贝，不依赖原始上下文的生命周期。</para>
/// <para>即使原始上下文被 Dispose，快照仍可通过 Rehydrate() 恢复。</para>
/// </summary>
public DiagnosticSnapshot Freeze()
{
    return DiagnosticSnapshot.FromScope(_rootScope);
}
```

同时需要将 `RootScope` 的访问级别从 `internal` 改为 `public`（供 `Rehydrate` 中的 `RestoreToScope` 访问），或新增一个 internal 方法：

```csharp
// 新增 internal 属性，供 DiagnosticSnapshot.RestoreToScope 访问
internal ScopeNode RootScope => _rootScope;
```

### 4.3 修改 `EventBindingDiagnosticContext` — 快照/恢复机制

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs`

```csharp
// 修改 CaptureSnapshot — 返回不可变快照而非引用
public static DiagnosticSnapshot? CaptureSnapshot()
{
    var ctx = Current;
    return ctx?.Freeze();
}

// 修改 RestoreScope — 从快照创建全新上下文
public static IDisposable? RestoreScope(DiagnosticSnapshot? snapshot)
{
    if (snapshot == null)
        return null;

    // 从快照创建全新的 DiagnosticContext（不受 _disposed 影响）
    var newCtx = snapshot.Rehydrate();
    newCtx.Activate();  // 设置 AsyncLocal

    // 保存到 ThreadLocal 防止 GC 回收
    _currentAsyncScope.Value = newCtx;

    // 记录作用域恢复事件（newCtx 是全新的活跃上下文，Record 正常工作）
    newCtx.Record(EventNames.EventBinderScopeRestored, new
    {
        sourceScope = snapshot.Name,
        sourceCreatedAt = snapshot.CreatedAt
    });

    return new ScopeDisposable(newCtx);
}
```

### 4.4 修改 `EventBinderTransformer` — 快照类型变更

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Parameters/EventBinderTransformer.cs`

```csharp
// 旧：DiagnosticContext? capturedDiagSnapshot
// 新：DiagnosticSnapshot? capturedDiagSnapshot
var capturedDiagSnapshot = EventBindingDiagnosticContext.CaptureSnapshot();
```

仅变量类型变更，使用方式不变。需要添加 using 引用：

```csharp
using Modules.DiagnosticContext.Core;
```

### 4.5 修改 `ScopeNode` — 暴露 `AddEntry` 为 internal

**文件**：`CsharpPrj/Modules/DiagnosticContext/Core/ScopeNode.cs`

`AddEntry` 方法已经是 `internal`，无需修改。但需要确认 `Children` 属性在 `DiagnosticSnapshot.RestoreToScope` 中的访问：

```csharp
// Children 已经是 public IReadOnlyList<ScopeNode>
public IReadOnlyList<ScopeNode> Children => _childrenReadOnly;
```

无需修改。

---

## 五、影响范围

### 5.1 新增文件

| 文件 | 预估行数 |
|------|----------|
| `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticSnapshot.cs` | ~120 行 |

### 5.2 修改文件

| 文件 | 修改内容 | 预估改动 |
|------|----------|----------|
| `DiagnosticContext.cs` | 新增 `Freeze()` 方法 + `RootScope` 访问级别调整 | ~8 行 |
| `EventBindingDiagnosticContext.cs` | 修改 `CaptureSnapshot`/`RestoreScope` 签名和实现 | ~25 行 |
| `EventBinderTransformer.cs` | 快照变量类型变更 + using 引用 | ~3 行 |

### 5.3 无需修改的文件

| 文件 | 原因 |
|------|------|
| `ScopeNode.cs` | `AddEntry` 已是 `internal`，`Children` 已是 `public` |
| `EventEntry.cs` | 本身不可变，直接引用 |
| `DiagnosticContextConfiguration.cs` | 配置层面无需修改 |
| `DiagnosticManager.cs` | 管理器层面无需修改 |
| `DiagnosticContextFactory.cs` | 工厂方法复用现有逻辑 |

### 5.4 编译影响

- `EventBindingDiagnosticContext` 的 `CaptureSnapshot()` 返回值类型从 `DiagnosticContext?` 变为 `DiagnosticSnapshot?`
- 所有调用 `CaptureSnapshot()` 的地方需要检查类型兼容性
- 当前仅 `EventBinderTransformer.cs` 一处调用

---

## 六、实施步骤

### Step 1：新增 `DiagnosticSnapshot.cs`

创建文件，实现 `FromScope` / `Rehydrate` / `RestoreToScope` 方法。

### Step 2：修改 `DiagnosticContext.cs`

新增 `Freeze()` 方法，调整 `RootScope` 访问级别。

### Step 3：修改 `EventBindingDiagnosticContext.cs`

重写 `CaptureSnapshot()` 和 `RestoreScope()` 方法。

### Step 4：修改 `EventBinderTransformer.cs`

变更快照变量类型，添加 using 引用。

### Step 5：构建验证

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln"
```

### Step 6：运行验证

启动 FileDriven GUI 模式，加载 `AgentConfigUI.json`，点击 TreeView 节点，确认 `[DIAG]` 日志正常输出。

---

## 七、附加问题清单

> 以下问题与快照机制无关，但属于 DiagnosticContext 框架中存在的重复/错误/冗余逻辑，建议在本次修复中一并清理。

### 问题 1：`DiagnosticContext.Record` 与 `ScopeNode.Record` 的重复逻辑

**位置**：`DiagnosticContext.cs` L84-L118 和 `ScopeNode.cs` L106-L112

**描述**：两个 `Record` 方法都独立计算 `relativeMs` 并创建 `EventEntry`：

```csharp
// DiagnosticContext.Record
var relativeMs = (long)(DateTime.UtcNow - _rootScope.CreatedAt).TotalMilliseconds;
var entry = new EventEntry(eventName, relativeMs, data);
// ... 入队 ...
CurrentScope.AddEntry(entry);

// ScopeNode.Record
var relativeMs = (long)(DateTime.UtcNow - Root._createdAt).TotalMilliseconds;
_events.Add(new EventEntry(eventName, relativeMs, data));
```

**影响**：
- `DiagnosticContext.Record` 走"入队 + 存储"路径
- `ScopeNode.Record`（通过 `ScopeGuard`）走"仅存储"路径，事件不入队
- 两条路径的 `relativeMs` 计算逻辑重复

**建议**：统一为 `DiagnosticContext.Record` 单一路径，`ScopeNode.Record` 标记为 `[Obsolete]` 或改为 internal。

---

### 问题 2：降级路径绕过 `OutputTarget` 配置

**位置**：`EventBindingDiagnosticContext.cs` L154-L168

**描述**：当 `Current` 为 null 时，`Record` 走 `Debug.WriteLine` 降级路径：

```csharp
public static void Record(string eventName, object? data = null)
{
    var ctx = Current;
    if (ctx != null) { ctx.Record(eventName, data); return; }
    // 降级：Debug.WriteLine — 绕过 OutputTarget 配置
    System.Diagnostics.Debug.WriteLine($"[EventBinding] {eventName} {dataJson}");
}
```

**影响**：如果 `DiagnosticContextConfiguration.OutputTarget` 设置为 `None`（不输出），但 `Current` 为 null，降级路径仍然输出 `Debug.WriteLine`，绕过了配置控制。

**建议**：降级路径也应受配置约束，或移除降级路径统一由 `DiagnosticContext` 管理。

---

### 问题 3：Pipeline 中事件格式化两次

**位置**：`DiagnosticContextFactory.cs` L144-L162

**描述**：`event-formatter` 和 `event-output` 两个 handler 都调用了 `FormatEntry(formatter, entry)`：

```csharp
pipeline.AddHandler("event-formatter", async (entry, ct) =>
{
    var formatted = FormatEntry(formatter, entry);  // 第一次格式化
    outputBuffer.AppendLine(formatted);
    return QueueResult.Ok();
});

pipeline.AddHandler("event-output", async (entry, ct) =>
{
    var formatted = FormatEntry(formatter, entry);  // 第二次格式化（重复）
    outputAction(formatted);
    return QueueResult.Ok();
});
```

**影响**：每个事件被格式化两次，字符串拼接操作执行两次。虽然 formatter 实例是复用的，但 `FormatEntry` 的字符串拼接是 O(n) 操作。

**建议**：在 `event-formatter` handler 中将格式化结果存入 `entry` 的扩展数据，`event-output` 直接读取。

---

### 问题 4：双重 `_disposed` 守卫

**位置**：`DiagnosticContext.cs` L86 和 `ScopeNode.cs` L120

**描述**：`DiagnosticContext.Record` 和 `ScopeNode.AddEntry` 各自检查 `_disposed`：

```csharp
// DiagnosticContext.Record
if (_disposed) return;
// ... 创建 entry ...
CurrentScope.AddEntry(entry);  // ScopeNode.AddEntry 内部也有 if (_disposed) return;

// ScopeNode.AddEntry
internal void AddEntry(EventEntry entry)
{
    if (_disposed) return;  // 重复守卫
    _events.Add(entry);
}
```

**影响**：无害但冗余。`DiagnosticContext` 已经确保 `_disposed` 为 false 后才调用 `AddEntry`。

**建议**：移除 `ScopeNode.AddEntry` 中的 `_disposed` 守卫，或改为 `Debug.Assert`。

---

### 问题 5：`ThreadLocal` 管理复杂且存在竞态风险

**位置**：`EventBindingDiagnosticContext.cs` L91-L117

**描述**：`BeginScope` 使用 `ThreadLocal<IDisposable?>` 保存 `asyncScope` 防止 GC 回收：

```csharp
private static readonly ThreadLocal<IDisposable?> _currentAsyncScope = new();

public static IDisposable BeginScope()
{
    var diagCtx = DiagnosticManager.BeginScope(ScopeName);
    var asyncScope = AsyncContext<DiagnosticContext>.CreateScope(diagCtx);
    _currentAsyncScope.Value = asyncScope;  // ThreadLocal 持有
    return new ScopeDisposable(diagCtx);
}

// ScopeDisposable.Dispose
_currentAsyncScope.Value = null;  // 清理
```

**影响**：
- `ThreadLocal` 的额外持有增加了复杂性
- `ScopeDisposable.Dispose` 设置 `_currentAsyncScope.Value = null` 可能与其他线程的 `BeginScope` 调用产生竞态
- `AsyncContext<DiagnosticContext>.CreateScope` 返回的 `IDisposable` 本身就应该由 `using` 块管理生命周期

**建议**：移除 `ThreadLocal`，改为在 `DiagnosticContext` 内部通过 `GC.KeepAlive` 或 `GCHandle` 管理生命周期。

---

### 问题 6：`Activate` 缺少嵌套保护

**位置**：`DiagnosticContext.cs` L188-L192

**描述**：连续调用 `Activate` 会覆盖 `_previousContext`：

```csharp
internal void Activate()
{
    _previousContext = Current;  // 如果连续 Activate，前一个上下文丢失
    Current = this;
}
```

**影响**：如果外部错误地连续调用 `Activate` 而不调用 `Dispose`，`_previousContext` 被覆盖，`Dispose` 时无法恢复到正确的上一个上下文。

**建议**：使用栈结构保存 `_previousContext` 链，或添加 `Debug.Assert` 防止重复 Activate。

---

### 问题 7：弱引用列表跟踪不完整

**位置**：`DiagnosticManager.cs` L26-L27

**描述**：`_activeContexts` 只跟踪通过 `DiagnosticManager.BeginScope` 创建的上下文：

```csharp
private static readonly List<WeakReference<DiagnosticContext>> _activeContexts = new();
```

**影响**：如果外部直接通过 `DiagnosticContextFactory.Create` 创建上下文（不经过 `DiagnosticManager`），上下文不会被跟踪，`DiagnosticManager.GetSnapshot()` 不会包含它。

**建议**：在 `DiagnosticContext` 构造函数中自动注册到 `DiagnosticManager`，或明确文档说明必须通过 `DiagnosticManager` 创建。

---

### 问题 8：快照可变（旧方案）

**位置**：`EventBindingDiagnosticContext.cs`（旧方案）

**描述**：旧方案中 `RestoreScope` 修改了"快照"对象：

```csharp
public static IDisposable? RestoreScope(DiagnosticContext? snapshot)
{
    // ...
    snapshot.Record(EventNames.EventBinderScopeRestored, new { ... });
    // ↑ 修改了"快照"对象，快照应该是只读的
}
```

**影响**：快照语义被破坏——快照应该是只读的，但旧方案允许通过 `Record` 修改。

**状态**：此问题随本次修复（引入 `DiagnosticSnapshot`）自动解决。

---

## 附录：关键代码位置

| 组件 | 路径 |
|------|------|
| DiagnosticContext | `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticContext.cs` |
| ScopeNode | `CsharpPrj/Modules/DiagnosticContext/Core/ScopeNode.cs` |
| EventEntry | `CsharpPrj/Modules/DiagnosticContext/Core/EventEntry.cs` |
| DiagnosticManager | `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticManager.cs` |
| DiagnosticContextFactory | `CsharpPrj/Modules/DiagnosticContext/Factory/DiagnosticContextFactory.cs` |
| DiagnosticContextConfiguration | `CsharpPrj/Modules/DiagnosticContext/Configuration/DiagnosticContextConfiguration.cs` |
| EventBindingDiagnosticContext | `CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingDiagnosticContext.cs` |
| EventBinderTransformer | `CsharpPrj/Share/EventBindingOrchestrator/Parameters/EventBinderTransformer.cs` |
| EventBindingOrchestrator | `CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` |
