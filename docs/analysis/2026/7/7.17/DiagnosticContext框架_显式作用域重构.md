# DiagnosticContext 框架 — 显式作用域重构

> **目标**：解决 `AsyncLocal` 在 `async/await` + `ConfigureAwait(false)` 场景下诊断上下文丢失的问题，从框架层面建立可靠的诊断上下文传播机制。
>
> **触发问题**：`RenderPipelineOrchestrator.OutputDiagnosticSnapshot()` 在 `await` 之后调用时，`AsyncLocal<DiagnosticContext?>` 的值丢失，导致快照为空（`"{}"`），日志中反复出现 `DiagnosticSnapshot_Empty` 警告。

---

## 1. 问题分析

### 1.1 当前设计

当前 `DiagnosticContext` 框架使用 `AsyncLocal<DiagnosticContext?>` 作为诊断上下文的传播载体：

```
AsyncLocal<DiagnosticContext?> _current      — 显式 BeginScope 创建的上下文
AsyncLocal<DiagnosticContext?> _lazyCurrent  — 惰性创建的上下文（2026-07-16 新增）
```

`Record()` 通过 `GetOrCreateLazy()` 获取当前上下文：

```csharp
internal static DiagnosticContext GetOrCreateLazy(string scopeName = "Lazy")
{
    if (_current.Value != null) return _current.Value;
    if (_lazyCurrent.Value != null && !_lazyCurrent.Value._disposed)
        return _lazyCurrent.Value;
    var context = DiagnosticContextFactory.CreateLazy(scopeName);
    context.Activate();
    _lazyCurrent.Value = context;
    return context;
}
```

`TryGetSnapshotJson()` 通过 `AsyncLocal` 读取：

```csharp
internal static string? TryGetSnapshotJson()
{
    var context = _current.Value ?? _lazyCurrent.Value;
    if (context == null || context._disposed) return null;
    return context.ToJson();
}
```

### 1.2 问题表现

从日志可以看到典型的调用链：

```
[19:08:13.753] RenderFullAsync 入口
[19:08:13.754] DisposeSession 入口
[19:08:13.828] [WARN] RenderPipelineOrchestrator_DiagnosticSnapshot_Empty
  {"warning":"Diagnostic snapshot is empty. This should not happen after lazy scope refactoring."}
```

以及第二次加载时：

```
[19:08:41.352] RenderFullAsync 入口
[19:08:41.354] DisposeSession 入口
[19:08:41.371] [WARN] RenderPipelineOrchestrator_DiagnosticSnapshot_Empty
  {"warning":"Diagnostic snapshot is empty. This should not happen after lazy scope refactoring."}
```

### 1.3 根因

**`AsyncLocal` 在 `async/await` + `ConfigureAwait(false)` 场景下不可靠。**

调用链分析：

```
RenderFullAsync (async method)
  ├── DisposeSession(target)                          ← 同步，AsyncLocal 正常
  ├── await _pipeline.ExecuteCreateStageAsync(...)    ← async
  ├── await BindEventsInternalAsync(target, config)   ← async
  │     └── await _eventOrchestrator.ExecuteWithWalkAsync(...)  ← async
  │           └── Task.FromResult(ExecuteWithWalk(...))         ← 同步
  │                 ├── _orchestratorPipeline.Execute(context)
  │                 │     └── DiagnosticsScopeStage.Process()
  │                 │           → Record("pipeline.orchestrator.start")
  │                 │             → GetOrCreateLazy("EventBinding")
  │                 │               → 创建惰性 scope，设置 _current.Value + _lazyCurrent.Value
  │                 └── _genericPipeline.ExecuteWithWalk(context, config)
  │                       → Record(...)  → _current.Value != null → 直接返回
  │
  ├── OutputDiagnosticSnapshot()                     ← await 之后
  │     → TryGetSnapshotJson()
  │       → _current.Value ?? _lazyCurrent.Value → null
  │       → return null → "{}"
```

关键点：

1. `BindEventsInternalAsync` 第 161 行使用了 `ConfigureAwait(false)`：
   ```csharp
   await _eventOrchestrator.ExecuteWithWalkAsync(target, config, searchRoot: target).ConfigureAwait(false);
   ```

2. 虽然 `ExecuteWithWalkAsync` 返回 `Task.FromResult(...)`（已完成的 Task），但 `async` 状态机在 `await` 处创建了恢复点

3. `ConfigureAwait(false)` 阻止了 `SynchronizationContext` 的捕获，导致 `ExecutionContext`（包含 `AsyncLocal` 的值）在 `await` 之后的代码中丢失

4. `OutputDiagnosticSnapshot()` 执行时，`_current.Value` 和 `_lazyCurrent.Value` 都是 `null`

**本质问题**：`AsyncLocal` 作为**大对象引用**的传播载体，在 `ConfigureAwait(false)` 场景下不可靠。这不是补丁能解决的，需要从框架层面重新设计。

---

## 2. 重构方案：显式作用域（DiagnosticScope）

### 2.1 设计思路

**核心思想**：将"数据"和"传播载体"分离。

- **传播载体**：`AsyncLocal<string>` — 只传播轻量的 `CallChainId`（字符串），`ConfigureAwait(false)` 不会丢失
- **数据存储**：`ConcurrentDictionary<string, DiagnosticScope>` — 全局可访问，不依赖异步流
- **作用域管理**：`DiagnosticScope : IDisposable` — 显式 `using` 管理生命周期

```
┌─────────────────────────────────────────────────────────────┐
│                    DiagnosticScope                            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  CallChainId: "RenderFullAsync#a1b2c3d4"             │   │
│  │  RootNode: ScopeNode("RenderFullAsync")              │   │
│  │  Events: [start, enter, exit, ...]                   │   │
│  │  State: { target: "contentContainer" }               │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
│  传播方式：AsyncLocal<string> CallChainId（轻量字符串）       │
│  数据存储：ConcurrentDictionary<CallChainId, DiagnosticScope> │
│  生命周期：using var scope = new DiagnosticScope(...)         │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 新增类型

#### 2.2.1 `DiagnosticScope` — 显式作用域

```csharp
// Modules/DiagnosticContext/Core/DiagnosticScope.cs

/// <summary>
/// 显式诊断作用域 — 替代 AsyncLocal<DiagnosticContext> 的隐式传播
/// <para>★ 2026-07-16 新增：解决 AsyncLocal 在 async/await + ConfigureAwait(false) 场景下丢失的问题。</para>
/// <para>
/// 设计要点：
/// 1. AsyncLocal 只传播 CallChainId（字符串），不传播大对象
/// 2. 数据存储在 ConcurrentDictionary 中，全局可访问
/// 3. using 管理生命周期，Dispose 时自动清理
/// </para>
/// </summary>
public sealed class DiagnosticScope : IDisposable
{
    private readonly ScopeNode _rootNode;
    private readonly Stack<ScopeNode> _scopeStack = new();
    private bool _disposed;

    /// <summary>调用链 ID（全局唯一）</summary>
    public string CallChainId { get; }

    /// <summary>根作用域节点</summary>
    public ScopeNode RootNode => _rootNode;

    /// <summary>当前作用域节点</summary>
    public ScopeNode CurrentNode => _scopeStack.Count > 0 ? _scopeStack.Peek() : _rootNode;

    /// <summary>创建显式作用域并注册到全局存储</summary>
    public DiagnosticScope(string scopeName)
    {
        CallChainId = $"{scopeName}#{Guid.NewGuid():N}";
        _rootNode = new ScopeNode(scopeName);
        _scopeStack.Push(_rootNode);
        DiagnosticScopeStore.Register(CallChainId, this);
        DiagnosticScopeStore.CurrentCallChainId = CallChainId;
    }

    /// <summary>记录事件</summary>
    public void Record(string eventName, object? data = null)
    {
        if (_disposed) return;
        CurrentNode.Record(eventName, data);
    }

    /// <summary>设置状态</summary>
    public void SetState(string key, object? value)
    {
        if (_disposed) return;
        CurrentNode.SetState(key, value);
    }

    /// <summary>推送子作用域</summary>
    public IDisposable PushScope(string scopeName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DiagnosticScope));
        var child = CurrentNode.CreateScope(scopeName);
        _scopeStack.Push(child);
        return new ScopePopper(this);
    }

    /// <summary>获取快照 JSON</summary>
    public string ToJson() => _rootNode.ToJson();

    /// <summary>获取文本快照</summary>
    public override string ToString() => _rootNode.ToString();

    /// <summary>获取摘要</summary>
    public string ToSummary() => _rootNode.ToSummary();

    /// <summary>释放作用域</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DiagnosticScopeStore.Unregister(CallChainId);
        if (DiagnosticScopeStore.CurrentCallChainId == CallChainId)
            DiagnosticScopeStore.CurrentCallChainId = null;
        _rootNode.Dispose();
        _scopeStack.Clear();
    }

    /// <summary>作用域弹出辅助</summary>
    private sealed class ScopePopper : IDisposable
    {
        private readonly DiagnosticScope _scope;
        private bool _popped;
        public ScopePopper(DiagnosticScope scope) => _scope = scope;
        public void Dispose()
        {
            if (_popped) return;
            _popped = true;
            if (_scope._scopeStack.Count > 1)
                _scope._scopeStack.Pop();
        }
    }
}
```

#### 2.2.2 `DiagnosticScopeStore` — 全局作用域存储

```csharp
// Modules/DiagnosticContext/Core/DiagnosticScopeStore.cs

/// <summary>
/// 全局诊断作用域存储
/// <para>★ 2026-07-16 新增：集中管理所有活跃的 DiagnosticScope。</para>
/// <para>
/// AsyncLocal 只传播 CallChainId（字符串），数据存储在 ConcurrentDictionary 中。
/// 任何线程/任何时刻都可以通过 CallChainId 访问对应的 DiagnosticScope。
/// </para>
/// </summary>
public static class DiagnosticScopeStore
{
    /// <summary>当前调用链 ID（AsyncLocal 传播，轻量字符串）</summary>
    private static readonly AsyncLocal<string?> _currentCallChainId = new();

    /// <summary>全局作用域存储</summary>
    private static readonly ConcurrentDictionary<string, DiagnosticScope> _scopes = new();

    /// <summary>当前调用链 ID</summary>
    public static string? CurrentCallChainId
    {
        get => _currentCallChainId.Value;
        set => _currentCallChainId.Value = value;
    }

    /// <summary>注册作用域</summary>
    internal static void Register(string id, DiagnosticScope scope)
        => _scopes[id] = scope;

    /// <summary>注销作用域</summary>
    internal static void Unregister(string id)
        => _scopes.TryRemove(id, out _);

    /// <summary>获取当前作用域</summary>
    public static DiagnosticScope? GetCurrent()
    {
        var id = _currentCallChainId.Value;
        if (id == null) return null;
        return _scopes.TryGetValue(id, out var scope) ? scope : null;
    }

    /// <summary>通过 CallChainId 获取指定作用域</summary>
    public static DiagnosticScope? GetById(string callChainId)
        => _scopes.TryGetValue(callChainId, out var scope) ? scope : null;

    /// <summary>获取所有活跃作用域</summary>
    public static IReadOnlyCollection<DiagnosticScope> GetAllActive()
        => _scopes.Values.ToList().AsReadOnly();

    /// <summary>活跃作用域数量</summary>
    public static int ActiveCount => _scopes.Count;
}
```

---

## 3. 彻底重构（不保留 AsyncLocal 回退）

### 3.1 重写 `EventBindingDiagnosticContext`

`EventBindingDiagnosticContext` 是 Share 层对外暴露的诊断 API，重构后内部完全基于 `DiagnosticScopeStore`，不再依赖 AsyncLocal：

```csharp
// Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs

public static class EventBindingDiagnosticContext
{
    /// <summary>开始诊断作用域</summary>
    public static IDisposable BeginScope()
    {
        var scope = new DiagnosticScope("EventBinding");
        return scope;  // DiagnosticScope 本身是 IDisposable
    }

    /// <summary>记录诊断事件</summary>
    public static void Record(string eventName, object? data = null)
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        if (scope != null)
        {
            scope.Record(eventName, data);
        }
        // ★ 没有 scope 时静默忽略（不再自动创建惰性 scope）
    }

    /// <summary>获取快照字符串</summary>
    public static string GetSnapshotString()
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        return scope?.ToJson() ?? "{}";
    }

    /// <summary>捕获当前快照</summary>
    public static object CaptureSnapshot()
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        return scope?.Freeze() ?? new object();
    }

    /// <summary>恢复作用域</summary>
    public static IDisposable RestoreScope(object snapshot) => new Scope();

    /// <summary>获取错误报告</summary>
    public static string GetErrorReport(this object errors) => string.Empty;

    private class Scope : IDisposable
    {
        public void Dispose() { }
    }
}
```

**关键变化**：
- `Record()` 不再调用 `DiagnosticManager.EnsureScope()` — 没有 scope 时静默忽略
- `GetSnapshotString()` 不再调用 `DiagnosticManager.TryGetSnapshotJson()` — 直接走 `DiagnosticScopeStore`
- `BeginScope()` 直接返回 `DiagnosticScope`（它本身就是 `IDisposable`）

### 3.2 入口点引入显式作用域

需要在 async 入口点添加 `using var scope = new DiagnosticScope(...)`：

#### `RenderPipelineOrchestrator.RenderFullAsync`

```csharp
public virtual async Task<(bool eventsBoundSuccess, string? eventsBoundError)> RenderFullAsync(
    object target, UIConfiguration config)
{
    using var scope = new DiagnosticScope("RenderFullAsync");

    BackEndLog.Log("RenderPipelineOrchestrator", "RenderFullAsync 入口",
        new { targetName = GetTargetName(target), orchestratorName = GetOrchestratorName() });

    // 1. 释放旧会话
    DisposeSession(target);

    // 2. 创建新会话
    // ...

    // 3. 执行事件绑定阶段
    var (eventsBoundSuccess, eventsBoundError) = await BindEventsInternalAsync(target, config);

    // 4. 输出诊断快照
    if (_eventOrchestrator != null)
    {
        OutputDiagnosticSnapshot(scope);
    }

    return (eventsBoundSuccess, eventsBoundError);
}

protected void OutputDiagnosticSnapshot(DiagnosticScope scope)
{
    var snapshot = scope.ToJson();
    if (string.IsNullOrWhiteSpace(snapshot) || snapshot == "{}")
    {
        BackEndLog.Log("RenderPipelineOrchestrator_DiagnosticSnapshot_Empty",
            new { warning = "Diagnostic snapshot is empty..." });
        return;
    }
    BackEndLog.Log("RenderPipelineOrchestrator_DiagnosticSnapshot", new { snapshot });
}
```

#### `EventBindingOrchestrator.ExecuteCore`

```csharp
private ExecutionResult ExecuteCore(EventBindingContext context, UIConfiguration config)
{
    using var scope = new DiagnosticScope("EventBindingOrchestrator");
    // ... 现有逻辑不变 ...
}
```

#### `WdUI.RenderPipeline` 相关方法

```csharp
// 在调用 GetSnapshotString() 的 async 方法入口处
using var scope = new DiagnosticScope("WdUIRenderPipeline");
// ... 现有逻辑不变 ...
```

### 3.3 删除废弃代码

重构后可以安全删除以下 AsyncLocal 相关代码：

| 文件 | 删除内容 |
|------|---------|
| `DiagnosticContext.cs` | `_lazyCurrent` 字段 + `GetOrCreateLazy()` 方法 + `TryGetSnapshotJson()` 中的 AsyncLocal 分支 |
| `DiagnosticManager.cs` | `EnsureScope()` 方法 + `TryGetSnapshotJson()` 方法 |
| `DiagnosticContextFactory.cs` | `CreateLazy()` 方法 |

---

## 4. 架构对比

| 方面 | 当前设计（AsyncLocal 隐式） | 新设计（显式作用域） |
|------|---------------------------|---------------------|
| **传播载体** | `AsyncLocal<DiagnosticContext?>`（大对象引用） | `AsyncLocal<string>`（轻量字符串） |
| **数据存储** | AsyncLocal 内部（隐式，不可直接访问） | `ConcurrentDictionary<string, DiagnosticScope>`（全局可访问） |
| **async/await 可靠性** | ❌ `ConfigureAwait(false)` 可能丢失 | ✅ 字符串传播更可靠 |
| **跨线程访问** | ❌ 不支持（AsyncLocal 线程隔离） | ✅ 通过 CallChainId 可跨线程访问 |
| **作用域生命周期** | 隐式（无 Dispose，或依赖 using） | 显式（`using var scope = new DiagnosticScope(...)`） |
| **快照读取时机** | 必须在同一异步流中 | 任意时刻、任意线程均可读取 |
| **嵌套作用域** | 通过 BeginScope/Dispose 栈管理 | 通过 PushScope/PopScope 显式管理 |
| **调试友好性** | ❌ 难以追踪（AsyncLocal 值不可见） | ✅ 可通过 `GetAllActive()` 列出所有活跃作用域 |
| **没有 scope 时的行为** | 自动创建惰性 scope | 静默忽略（要求入口点显式创建） |

---

## 5. 对 Share 层的影响

**结论：Share 层的使用方式完全不变，零影响。**

| Share 层 API | 调用方 | 重构后行为 | 调用方是否需要修改 |
|---|---|---|---|
| `Record()` | 38 处调用（15 个文件） | 优先走 `DiagnosticScopeStore` | ❌ 不需要 |
| `GetSnapshotString()` | `RenderPipelineOrchestrator` + `WdUI.RenderPipeline` | 走 `DiagnosticScopeStore` | ❌ 不需要 |
| `BeginScope()` | `EventBindingIoCInitializer` | 返回 `DiagnosticScope`（仍是 `IDisposable`） | ❌ 不需要 |
| `CaptureSnapshot()` | `EventBindingIoCInitializer` | 走 `DiagnosticScopeStore` | ❌ 不需要 |
| `RestoreScope()` | `EventBindingIoCInitializer` | 不变 | ❌ 不需要 |
| `GetErrorReport()` | 各调用方 | 不变 | ❌ 不需要 |

**38 处 `EventBindingDiagnosticContext.Record()` 调用一行代码都不需要修改。**

---

## 6. 修改清单

| 文件 | 操作 | 行数估计 |
|------|------|---------|
| **新增** `Modules/DiagnosticContext/Core/DiagnosticScope.cs` | 新增 | ~120 行 |
| **新增** `Modules/DiagnosticContext/Core/DiagnosticScopeStore.cs` | 新增 | ~60 行 |
| **修改** `Share/.../EventBindingDiagnosticContext.cs` | 重写 Record/GetSnapshotString/BeginScope | ~20 行 |
| **修改** `Operation/.../RenderPipelineOrchestrator.cs` | 入口加 `using var scope` + OutputDiagnosticSnapshot 改参数 | ~10 行 |
| **修改** `Share/.../EventBindingOrchestrator.cs` | 入口加 `using var scope` | ~5 行 |
| **修改** `Operation/.../WdUI.RenderPipeline.cs` | 入口加 `using var scope` | ~5 行 |
| **删除** `DiagnosticContext.cs` | 删除 `_lazyCurrent` / `GetOrCreateLazy` / `TryGetSnapshotJson` 中的 AsyncLocal 分支 | ~30 行 |
| **删除** `DiagnosticManager.cs` | 删除 `EnsureScope` / `TryGetSnapshotJson` | ~15 行 |
| **删除** `DiagnosticContextFactory.cs` | 删除 `CreateLazy` | ~10 行 |

**总计**：新增 ~180 行，修改 ~40 行，删除 ~55 行。

---

## 7. 实施步骤

### 阶段一：新增类型

1. 新增 `DiagnosticScope.cs` — 显式作用域类（含 ScopePopper）
2. 新增 `DiagnosticScopeStore.cs` — 全局作用域存储

### 阶段二：重写 Share 层

3. 重写 `EventBindingDiagnosticContext.cs` — 完全基于 `DiagnosticScopeStore`

### 阶段三：入口点引入显式作用域

4. 修改 `RenderPipelineOrchestrator.cs` — `RenderFullAsync()` 引入 `using var scope`
5. 修改 `EventBindingOrchestrator.cs` — `ExecuteCore()` 引入 `using var scope`
6. 修改 `WdUI.RenderPipeline.cs` — 相关 async 方法引入 `using var scope`

### 阶段四：清理废弃代码

7. 删除 `DiagnosticContext.cs` 中的 `_lazyCurrent` / `GetOrCreateLazy` / `TryGetSnapshotJson` AsyncLocal 分支
8. 删除 `DiagnosticManager.cs` 中的 `EnsureScope` / `TryGetSnapshotJson`
9. 删除 `DiagnosticContextFactory.cs` 中的 `CreateLazy`

### 阶段五：验证

10. 构建验证 — `dotnet build MainProj.sln` 确认 0 错误
11. 功能验证 — GUI 启动确认无 `DiagnosticSnapshot_Empty` 警告

---

## 8. 设计决策记录

| 决策 | 选项 | 选择 | 理由 |
|------|------|------|------|
| AsyncLocal 传播内容 | `DiagnosticContext?` / `string` | **`string`（CallChainId）** | 字符串是值类型，`ConfigureAwait(false)` 不会丢失 |
| 数据存储结构 | `ConcurrentDictionary` / `WeakReference` 列表 | **`ConcurrentDictionary`** | 线程安全，O(1) 查找，支持跨线程访问 |
| 作用域 ID 生成 | GUID / 自增ID / 组合ID | **`{scopeName}#{GUID}`** | 全局唯一，可读性好，便于调试 |
| 是否保留 AsyncLocal 回退 | 是 / 否 | **否** | 彻底重构，消除历史包袱 |
| 没有 scope 时 Record() 的行为 | 自动创建 / 静默忽略 | **静默忽略** | 强制入口点显式创建 scope，避免隐式行为 |
| DiagnosticScope 是否实现 IDiagnosticContext | 是 / 否 | **否** | 保持职责单一 |
| 嵌套作用域实现 | BeginScope/Dispose / PushScope/PopScope | **PushScope/PopScope** | 语义更清晰，避免与 DiagnosticContext.BeginScope 混淆 |
