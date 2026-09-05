# DiagnosticContext 框架 — 惰性 Scope 重构

> **目标**：消除调用方手动管理 `BeginScope()` / `Dispose()` 的负担，让 `Record()` 和 `GetSnapshotString()` 在没有活跃 scope 时自动创建惰性 scope。

---

## 1. 问题分析

### 1.1 当前设计

`DiagnosticContext` 框架要求调用方**显式管理 scope 生命周期**：

```csharp
// 调用方必须手动 BeginScope/Dispose
using var scope = DiagnosticManager.BeginScope("EventBinding");
scope.Record("event.start", data);
var snapshot = DiagnosticContext.Current?.ToJson();
```

### 1.2 问题表现

从日志可以看到，`RenderPipelineOrchestrator.DisposeSession()` 在没有 `BeginScope()` 的上下文中调用了 `GetSnapshotString()`：

```
[WARN] [BackEndDriven] [backend:RenderPipelineOrchestrator_DiagnosticSnapshot_Empty]
  {"warning":"Diagnostic snapshot is empty: DiagnosticContext.Current is null..."}
```

类似地，`WdUI.RenderPipeline` 中的 `DiagnosticSnapshot_Empty` 也是同样原因。

### 1.3 根因

- `DisposeSession()` 的职责是清理，不是诊断，它不应该需要手动创建 scope
- 但 `DisposeSession()` 内部调用了 `GetSnapshotString()` 来"顺便"获取诊断快照
- 此时没有活跃的 `DiagnosticContext`，导致快照为空

---

## 2. 重构方案：惰性 Scope（Lazy Scope）

### 2.1 设计思路

在 `DiagnosticContext` 框架中引入**惰性 scope**机制：

- `Record()` 首次调用时，如果没有活跃的 `DiagnosticContext`，自动创建一个
- `GetSnapshotString()` / `ToJson()` 调用时，如果没有活跃的 `DiagnosticContext`，返回 `"{}"`（不创建 scope）
- 惰性创建的 scope 在**当前操作完成后自动释放**（通过 `AsyncLocal` 的 `ValueChangedHandler` 或引用计数）

### 2.2 修改位置

| 文件 | 修改内容 |
|------|---------|
| `Modules/DiagnosticContext/Core/DiagnosticContext.cs` | 新增 `LazyCurrent` 属性 + 惰性创建逻辑 |
| `Modules/DiagnosticContext/Core/DiagnosticManager.cs` | 新增 `EnsureScope()` / `TryGetSnapshot()` 方法 |
| `Modules/DiagnosticContext/Factory/DiagnosticContextFactory.cs` | 新增 `CreateLazy()` 方法 |

### 2.3 详细设计

#### 2.3.1 `DiagnosticContext.cs` — 新增 LazyCurrent

```csharp
public sealed class DiagnosticContext : IDiagnosticContext
{
    // 现有代码...

    /// <summary>
    /// 惰性当前上下文 — 没有活跃 context 时自动创建
    /// <para>★ 2026-07-16 新增：消除调用方手动管理 BeginScope/Dispose 的负担。</para>
    /// <para>首次 Record() 调用时自动创建，当前异步流结束时自动释放。</para>
    /// </summary>
    private static readonly AsyncLocal<DiagnosticContext?> _lazyCurrent = new();

    /// <summary>
    /// 获取或创建惰性上下文
    /// <para>如果已有活跃上下文（通过 BeginScope 创建），直接返回。</para>
    /// <para>如果没有活跃上下文，创建一个惰性上下文并注册到 AsyncLocal。</para>
    /// </summary>
    internal static DiagnosticContext? GetOrCreateLazy(string scopeName = "Lazy")
    {
        // 优先使用显式创建的上下文
        if (_current.Value != null)
            return _current.Value;

        // 检查是否已有惰性上下文
        if (_lazyCurrent.Value != null && !_lazyCurrent.Value._disposed)
            return _lazyCurrent.Value;

        // 创建新的惰性上下文
        var context = DiagnosticContextFactory.Create(scopeName);
        context.Activate();
        _lazyCurrent.Value = context;
        return context;
    }

    /// <summary>
    /// 尝试获取快照字符串，没有活跃上下文时返回 "{}"
    /// </summary>
    internal static string? TryGetSnapshotJson()
    {
        var context = _current.Value ?? _lazyCurrent.Value;
        if (context == null || context._disposed) return null;
        return context.ToJson();
    }

    // Dispose 时清理 _lazyCurrent
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 如果是惰性上下文，清理 _lazyCurrent
        if (_lazyCurrent.Value == this)
        {
            _lazyCurrent.Value = null;
        }

        Current = _previousContext;
        _rootScope.Dispose();
        _scopeStack.Clear();
    }
}
```

#### 2.3.2 `DiagnosticManager.cs` — 新增 EnsureScope / TryGetSnapshot

```csharp
public static class DiagnosticManager
{
    /// <summary>
    /// 确保当前异步流上有活跃的诊断上下文
    /// <para>★ 2026-07-16 新增：没有活跃上下文时自动创建惰性上下文。</para>
    /// <para>适用于"有则记录，无则创建"的场景，消除调用方手动管理 scope 的负担。</para>
    /// </summary>
    /// <param name="scopeName">作用域名称（仅在创建新上下文时使用）</param>
    /// <returns>当前诊断上下文（不会返回 null）</returns>
    public static DiagnosticContext EnsureScope(string scopeName = "Auto")
    {
        return DiagnosticContext.GetOrCreateLazy(scopeName)
            ?? throw new InvalidOperationException("Failed to create diagnostic context");
    }

    /// <summary>
    /// 尝试获取当前快照 JSON
    /// <para>★ 2026-07-16 新增：没有活跃上下文时返回 null，不创建 scope。</para>
    /// <para>适用于"有快照则用，没有就算了"的场景。</para>
    /// </summary>
    public static string? TryGetSnapshotJson()
    {
        return DiagnosticContext.TryGetSnapshotJson();
    }
}
```

#### 2.3.3 `DiagnosticContextFactory.cs` — 新增 CreateLazy

```csharp
public static class DiagnosticContextFactory
{
    /// <summary>
    /// 创建惰性诊断上下文（不自动激活，由 GetOrCreateLazy 管理）
    /// <para>★ 2026-07-16 新增：使用默认配置创建，不装配事件队列。</para>
    /// </summary>
    internal static Core.DiagnosticContext CreateLazy(string scopeName)
    {
        // 惰性上下文使用最小配置，不装配事件队列/分发器/管线
        var config = new DiagnosticContextConfiguration
        {
            EnableEventQueue = false,
            EnableDispatcher = false,
            EnablePipeline = false
        };
        return Create(scopeName, config);
    }
}
```

---

## 3. 调用方修改

### 3.1 `EventBindingDiagnosticContext.cs` — 使用惰性 scope

```csharp
public static class EventBindingDiagnosticContext
{
    public static IDisposable BeginScope()
    {
        return DiagnosticContextFactory.CreateAndActivate("EventBinding");
    }

    public static void Record(string eventName, object? data = null)
    {
        // ★ 2026-07-16 重构：使用 EnsureScope，没有活跃 context 时自动创建
        var context = DiagnosticManager.EnsureScope("EventBinding");
        context.Record(eventName, data);
    }

    public static string GetSnapshotString()
    {
        // ★ 2026-07-16 重构：使用 TryGetSnapshotJson，没有快照时返回 "{}"
        return DiagnosticManager.TryGetSnapshotJson() ?? "{}";
    }

    // CaptureSnapshot / RestoreScope / GetErrorReport 保持不变...
}
```

### 3.2 `EventBindingOrchestrator.cs` — 保留 ExecuteCore 中的 Dispose

`ExecuteCore` 中的 `DiagnosticsScope` Dispose 逻辑**保留不变**，因为 `BeginScope()` 创建的显式 scope 仍然需要手动 Dispose 来恢复 AsyncLocal 上下文。

---

## 4. 影响范围

| 影响 | 说明 |
|------|------|
| `RenderPipelineOrchestrator.DisposeSession()` | 不再出现 `DiagnosticSnapshot_Empty` 警告 |
| `WdUI.RenderPipeline` | 不再出现 `DiagnosticSnapshot_Empty` 警告 |
| `EventBindingDiagnosticContext.Record()` | 没有 BeginScope 时自动创建惰性 scope |
| `EventBindingDiagnosticContext.GetSnapshotString()` | 没有活跃 context 时返回 `"{}"` |
| 现有 `BeginScope()` 调用方 | **无影响**，显式 scope 优先级高于惰性 scope |

---

## 5. 实施步骤

1. **`DiagnosticContext.cs`** — 新增 `_lazyCurrent` AsyncLocal + `GetOrCreateLazy()` + `TryGetSnapshotJson()` + 修改 `Dispose()`
2. **`DiagnosticContextFactory.cs`** — 新增 `CreateLazy()` 方法
3. **`DiagnosticManager.cs`** — 新增 `EnsureScope()` + `TryGetSnapshotJson()`
4. **`EventBindingDiagnosticContext.cs`** — `Record()` 改为 `EnsureScope`，`GetSnapshotString()` 改为 `TryGetSnapshotJson`
5. **构建验证** — `dotnet build MainProj.sln` 确认 0 错误
6. **功能验证** — GUI 启动确认无 `DiagnosticSnapshot_Empty` 警告

---

## 6. 设计决策记录

| 决策 | 选项 | 选择 | 理由 |
|------|------|------|------|
| 惰性 scope 的清理时机 | AsyncLocal ValueChangedHandler / 引用计数 / 每次操作后 | **每次操作后** | 简单可靠，避免 AsyncLocal 泄漏 |
| 惰性 scope 是否装配事件队列 | 是 / 否 | **否** | 惰性 scope 用于简单记录，不需要队列/分发器/管线 |
| `GetSnapshotString()` 是否创建惰性 scope | 是 / 否 | **否** | 只读操作不应产生副作用 |
| 显式 scope 和惰性 scope 的优先级 | 显式优先 / 惰性优先 | **显式优先** | 保证 BeginScope/Dispose 语义不变 |
