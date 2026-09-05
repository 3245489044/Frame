# DiagnosticContext 框架彻底重构方案 — 消除旧体系

> **版本：** v1.1
> **日期：** 2026-07-16
> **状态：** 待执行
> **目标：** 将 `DiagnosticContext` / `DiagnosticManager` / `DiagnosticContextFactory` 的内部实现改为委托给 `DiagnosticScopeStore` + `DiagnosticScope`，对外接口保持不变。从根本上消除双体系并行冲突。
> **旧体系封存：** 旧代码移动到 `CsharpPrj/Modules/DiagnosticContext/_Legacy/` 目录封存，保留历史参考。

---

## 目录

- [DiagnosticContext 框架彻底重构方案 — 消除旧体系](#diagnosticcontext-框架彻底重构方案--消除旧体系)
  - [目录](#目录)
  - [1. 现状分析](#1-现状分析)
    - [1.1 当前架构](#11-当前架构)
    - [1.2 问题：双体系并行](#12-问题双体系并行)
    - [1.3 外部引用方清单](#13-外部引用方清单)
  - [2. 重构方案](#2-重构方案)
    - [2.1 核心原则](#21-核心原则)
    - [2.2 改造后的架构](#22-改造后的架构)
    - [2.3 接口映射表](#23-接口映射表)
  - [3. 详细修改步骤](#3-详细修改步骤)
    - [步骤 1：改造 `DiagnosticContext.cs`](#步骤-1改造-diagnosticcontextcs)
    - [步骤 2：改造 `DiagnosticManager.cs`](#步骤-2改造-diagnosticmanagercs)
    - [步骤 3：改造 `DiagnosticContextFactory.cs`](#步骤-3改造-diagnosticcontextfactorycs)
    - [步骤 4：改造 `DiagnosticSnapshot.Rehydrate()`](#步骤-4改造-diagnosticsnapshotrehydrate)
    - [步骤 5：迁移 6 个 Share 子模块的 `*DiagnosticContext.cs`](#步骤-5迁移-6-个-share-子模块的-diagnosticcontextcs)
    - [步骤 6：迁移 `ControlValueAccessorBase.cs`](#步骤-6迁移-controlvalueaccessorbasecs)
    - [步骤 7：更新 `Architecture.md`](#步骤-7更新-architecturemd)
    - [步骤 8：新增单元测试](#步骤-8新增单元测试)
  - [4. 修改清单](#4-修改清单)
  - [5. 风险与注意事项](#5-风险与注意事项)
    - [5.1 `DiagnosticContext.Dispose()` 不再释放内部 `DiagnosticScope`](#51-diagnosticcontextdispose-不再释放内部-diagnosticscope)
    - [5.2 `DiagnosticContext.Activate()` 变为空操作](#52-diagnosticcontextactivate-变为空操作)
    - [5.3 `DiagnosticManager.Configure()` 变为空操作](#53-diagnosticmanagerconfigure-变为空操作)
    - [5.4 `EventQueue` / `EventPipeline` / `EventDispatcher` 相关代码被删除](#54-eventqueue--eventpipeline--eventdispatcher-相关代码被删除)
  - [6. 验证标准](#6-验证标准)
  - [7. 旧体系代码封存](#7-旧体系代码封存)
    - [7.1 封存策略](#71-封存策略)
    - [7.2 封存目录结构](#72-封存目录结构)
    - [7.3 封存步骤](#73-封存步骤)
    - [7.4 `_Legacy/README.md` 内容](#74-_legacyreadmemd-内容)
    - [7.5 确保 `_Legacy/` 不参与编译](#75-确保-_legacy-不参与编译)

---

## 1. 现状分析

### 1.1 当前架构

```
旧体系（AsyncLocal<DiagnosticContext>）        新体系（ConcurrentDictionary）
┌─────────────────────────────┐          ┌──────────────────────────────┐
│ DiagnosticContext           │          │ DiagnosticScope              │
│  - AsyncLocal<Diagnostic    │          │  - CallChainId (string)      │
│    Context> _current        │          │  - ScopeNode _rootNode       │
│  - ScopeNode _rootScope     │          │  - Stack<ScopeNode>          │
│  - Stack<ScopeNode>         │          └──────────┬───────────────────┘
│  - EventQueue?              │                     │
│  - EventDispatcher?         │          ┌──────────▼───────────────────┐
└──────────┬──────────────────┘          │ DiagnosticScopeStore         │
           │                             │  - AsyncLocal<string?>       │
┌──────────▼──────────────────┐          │  - ConcurrentDictionary      │
│ DiagnosticManager           │          └──────────────────────────────┘
│  - BeginScope() → 旧体系     │
│  - GetSnapshot() → 旧体系    │
│  - _activeContexts           │
└──────────┬──────────────────┘
           │
┌──────────▼──────────────────┐
│ DiagnosticContextFactory    │
│  - Create() → 旧体系         │
└─────────────────────────────┘
```

### 1.2 问题：双体系并行

| 问题 | 说明 | 影响 |
|------|------|------|
| **数据分裂** | `DiagnosticManager.BeginScope()` 创建 `AsyncLocal<DiagnosticContext>` 上下文，`new DiagnosticScope()` 创建 `ConcurrentDictionary` 上下文，两者互不感知 | 同一异步流中混用两者时，`Record()` 写入不同的 ScopeNode |
| **快照不一致** | `DiagnosticManager.GetSnapshot()` 只遍历 `_activeContexts`（旧体系），不包含 `DiagnosticScopeStore` 中的 scope（新体系） | 快照可能遗漏数据 |
| **维护成本** | 两套 AsyncLocal 管理逻辑、两套生命周期管理、两套测试 | 任何修改需要同步更新两套代码 |

### 1.3 外部引用方清单

| 引用方 | 使用方式 | 文件路径 | 行数 |
|--------|----------|----------|------|
| Share.CommandFramework | `DiagnosticContext.Current` | `Share/CommandFramework/Diagnostics/CommandFrameworkDiagnosticContext.cs` | L20 |
| Share.GraphFramework | `DiagnosticContext.Current` | `Share/GraphFramework/Diagnostics/GraphFrameworkDiagnosticContext.cs` | L20 |
| Share.PlaceholderFramework | `DiagnosticContext.Current` | `Share/PlaceholderFramework/Diagnostics/PlaceholderFrameworkDiagnosticContext.cs` | L20 |
| Share.FrameworkAbstractions | `DiagnosticContext.Current` | `Share/FrameworkAbstractions/Diagnostics/FrameworkAbstractionsDiagnosticContext.cs` | L20 |
| Share.StateManagement | `DiagnosticContext.Current` | `Share/StateManagement/Diagnostics/StateManagementDiagnosticContext.cs` | L20 |
| Share.UniversalMiddleware | `DiagnosticContext.Current` | `Share/UniversalMiddleware/Diagnostics/UniversalMiddlewareDiagnosticContext.cs` | L20 |
| Operation.EventTrigger | `DiagnosticManager.GetSnapshot()` | `Operation/BackEndDriven/EventTriggerFramework/ControlValueAccessorBase.cs` | L296 |
| 测试-DiagnosticContextTests | `DiagnosticContextFactory.Create()` | `CentralTest/.../Core/DiagnosticContextTests.cs` | 多处 |
| 测试-DiagnosticManagerTests | `DiagnosticManager.BeginScope()` | `CentralTest/.../Core/DiagnosticManagerTests.cs` | 多处 |
| 测试-DiagnosticSnapshotTests | `DiagnosticContextFactory.Create()` | `CentralTest/.../Core/DiagnosticSnapshotTests.cs` | 多处 |
| 测试-DiagnosticContextFactoryTests | `DiagnosticContextFactory.Create()` | `CentralTest/.../Factory/DiagnosticContextFactoryTests.cs` | 多处 |
| DiagnosticSnapshot.Rehydrate | `DiagnosticContextFactory.Create()` | `Core/DiagnosticSnapshot.cs` | L87 |

---

## 2. 重构方案

### 2.1 核心原则

**旧体系三个文件改内部实现，不改对外接口。** 所有外部调用方无需修改一行代码。

```
改造前：DiagnosticManager.BeginScope() → new DiagnosticContext() → AsyncLocal<DiagnosticContext>
改造后：DiagnosticManager.BeginScope() → new DiagnosticScope() → DiagnosticScopeStore.Register()
```

### 2.2 改造后的架构

```
旧接口（对外不变）              新实现（委托到新体系）
┌─────────────────────┐     ┌──────────────────────────────┐
│ DiagnosticContext    │────▶│ DiagnosticScope (包装)       │
│  - Current (静态)    │     │  - 内部持有 DiagnosticScope  │
│  - Record()          │     │  - 委托给 DiagnosticScope   │
│  - CreateScope()     │     └──────────────────────────────┘
│  - Freeze()          │
│  - Dispose()         │     ┌──────────────────────────────┐
└─────────────────────┘     │ DiagnosticScopeStore          │
                            │  - ConcurrentDictionary       │
┌─────────────────────┐     │  - AsyncLocal<string?>        │
│ DiagnosticManager   │────▶│  - Register/Unregister        │
│  - BeginScope()     │     │  - GetCurrent/GetById         │
│  - GetSnapshot()    │     └──────────────────────────────┘
│  - Current          │
│  - FirstActive      │     ┌──────────────────────────────┐
│  - RecordToAll()    │     │ ScopeNode (复用)              │
└─────────────────────┘     │  - 数据模型不变               │
                            │  - 新旧体系共用                │
┌─────────────────────┐     └──────────────────────────────┘
│ DiagnosticContext   │
│ Factory             │────▶ new DiagnosticScope()
│  - Create()         │
└─────────────────────┘
```

### 2.3 接口映射表

| 旧接口 | 旧实现 | 新实现（委托到） |
|--------|--------|-----------------|
| `DiagnosticContext.Current` | `AsyncLocal<DiagnosticContext> _current` | `DiagnosticScopeStore.GetCurrent()` |
| `DiagnosticContext.Record()` | `CurrentScope.AddEntry()` | `_scope.Record()` |
| `DiagnosticContext.CreateScope()` | `CurrentScope.CreateScope()` + `ScopeGuard` | `_scope.PushScope()` + `ScopeGuard` |
| `DiagnosticContext.Freeze()` | `DiagnosticSnapshot.FromScope(_rootScope)` | `_scope.Freeze()` |
| `DiagnosticContext.Dispose()` | 恢复 AsyncLocal + 清理 | 仅标记 `_disposed`（scope 由外部管理） |
| `DiagnosticContext.Activate()` | 设置 AsyncLocal | 空操作（scope 构造函数已设置） |
| `DiagnosticContext.GetOrCreateLazy()` | 创建惰性 `DiagnosticContext` | 创建 `DiagnosticScope` 并包装 |
| `DiagnosticManager.BeginScope()` | `DiagnosticContextFactory.Create()` + `Activate()` | `new DiagnosticScope()` + 包装 |
| `DiagnosticManager.GetSnapshot()` | 遍历 `_activeContexts` | 遍历 `DiagnosticScopeStore.GetAllActive()` |
| `DiagnosticManager.FirstActive` | 遍历 `_activeContexts` | `DiagnosticScopeStore.GetAllActive().FirstOrDefault()` |
| `DiagnosticManager.RecordToAll()` | 遍历 `_activeContexts` | 遍历 `DiagnosticScopeStore.GetAllActive()` |
| `DiagnosticManager.Configure()` | 设置 `_defaultConfig` | 空操作 |
| `DiagnosticContextFactory.Create()` | 创建 `DiagnosticContext` + 装配队列/管线 | `new DiagnosticScope()` + 包装 |

---

## 3. 详细修改步骤

### 步骤 1：改造 `DiagnosticContext.cs`

**文件：** `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticContext.cs`

**策略：** `DiagnosticContext` 改为包装一个内部 `DiagnosticScope`，所有方法委托给该 scope。

**修改内容：**

```csharp
// ============================================================
// DiagnosticContext.cs — 诊断上下文（兼容性包装）
//
// ★ 2026-07-16 彻底重构：内部委托给 DiagnosticScope，
//   不再维护独立的 AsyncLocal 和 ScopeNode。
//
// 对外接口不变，所有旧调用方无需修改代码。
// ============================================================

using Modules.DiagnosticContext.Abstractions;
using Modules.DiagnosticContext.Factory;

namespace Modules.DiagnosticContext.Core;

/// <summary>
/// 诊断上下文（兼容性包装）
/// <para>★ 2026-07-16 彻底重构：内部委托给 <see cref="DiagnosticScope"/>，</para>
/// <para>   不再维护独立的 AsyncLocal 和 ScopeNode。</para>
/// </summary>
public sealed class DiagnosticContext : IDiagnosticContext
{
    private readonly DiagnosticScope _scope;
    private bool _disposed;

    // ====================================================================
    // 静态属性
    // ====================================================================

    /// <summary>
    /// 获取当前异步流上的诊断上下文
    /// <para>★ 2026-07-16 彻底重构：委托给 <see cref="DiagnosticScopeStore.GetCurrent"/>。</para>
    /// </summary>
    public static DiagnosticContext? Current
    {
        get
        {
            var scope = DiagnosticScopeStore.GetCurrent();
            return scope != null ? new DiagnosticContext(scope) : null;
        }
    }

    // ====================================================================
    // 实例属性（委托给 _scope）
    // ====================================================================

    /// <summary>上下文名称</summary>
    public string Name => _scope.RootNode.Name;

    /// <summary>上下文创建时间</summary>
    public DateTime CreatedAt => _scope.RootNode.CreatedAt;

    /// <summary>当前作用域节点</summary>
    internal ScopeNode CurrentScope => _scope.CurrentNode;

    /// <summary>根作用域节点</summary>
    public ScopeNode RootScope => _scope.RootNode;

    // ====================================================================
    // 构造
    // ====================================================================

    /// <summary>
    /// 从 DiagnosticScope 创建包装
    /// </summary>
    internal DiagnosticContext(DiagnosticScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    // ====================================================================
    // 实例方法（委托给 _scope）
    // ====================================================================

    /// <summary>记录一个插桩事件</summary>
    public void Record(string eventName, object? data = null)
    {
        if (_disposed) return;
        _scope.Record(eventName, data);
    }

    /// <summary>设置运行状态</summary>
    public void SetState(string key, object? value)
    {
        if (_disposed) return;
        _scope.SetState(key, value);
    }

    /// <summary>获取运行状态</summary>
    public T? GetState<T>(string key) => _scope.CurrentNode.GetState<T>(key);

    /// <summary>创建嵌套子作用域</summary>
    public IDiagnosticContext CreateScope(string scopeName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DiagnosticContext));
        var popper = _scope.PushScope(scopeName);
        return new ScopeGuard(this, _scope.CurrentNode, popper);
    }

    /// <summary>冻结当前上下文，创建不可变快照</summary>
    public DiagnosticSnapshot Freeze() => _scope.Freeze();

    /// <summary>导出文本快照</summary>
    public override string ToString() => _scope.ToString();

    /// <summary>导出摘要</summary>
    public string ToSummary() => _scope.ToSummary();

    /// <summary>导出 JSON</summary>
    public string ToJson() => _scope.ToJson();

    /// <summary>
    /// 释放包装器
    /// <para>注意：不释放内部的 DiagnosticScope，其生命周期由外部 using 管理。</para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    // ====================================================================
    // 内部方法
    // ====================================================================

    /// <summary>
    /// 获取或创建惰性上下文
    /// <para>★ 2026-07-16 彻底重构：委托给 DiagnosticScopeStore。</para>
    /// </summary>
    internal static DiagnosticContext GetOrCreateLazy(string scopeName = "Lazy")
    {
        var current = DiagnosticScopeStore.GetCurrent();
        if (current != null)
            return new DiagnosticContext(current);

        var scope = new DiagnosticScope(scopeName);
        return new DiagnosticContext(scope);
    }

    /// <summary>
    /// 尝试获取快照 JSON
    /// </summary>
    internal static string? TryGetSnapshotJson()
    {
        var scope = DiagnosticScopeStore.GetCurrent();
        return scope?.ToJson();
    }

    /// <summary>
    /// 激活（空操作 — DiagnosticScope 构造函数已设置 AsyncLocal）
    /// </summary>
    internal void Activate() { }

    // ====================================================================
    // ScopeGuard — 作用域守卫
    // ====================================================================

    /// <summary>
    /// 作用域守卫
    /// <para>包装子 ScopeNode，在 Dispose 时弹出子作用域。</para>
    /// </summary>
    public sealed class ScopeGuard : IDiagnosticContext
    {
        private readonly DiagnosticContext _owner;
        private readonly ScopeNode _scopeNode;
        private readonly IDisposable _popper;
        private bool _disposed;

        internal ScopeGuard(DiagnosticContext owner, ScopeNode scopeNode, IDisposable popper)
        {
            _owner = owner;
            _scopeNode = scopeNode;
            _popper = popper;
        }

        public string Name => _scopeNode.Name;
        public DateTime CreatedAt => _scopeNode.CreatedAt;

        public void Record(string eventName, object? data = null)
        {
            if (_disposed) return;
            _owner.Record(eventName, data);
        }

        public void SetState(string key, object? value)
        {
            if (_disposed) return;
            _scopeNode.SetState(key, value);
        }

        public T? GetState<T>(string key) => _scopeNode.GetState<T>(key);

        public IDiagnosticContext CreateScope(string scopeName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ScopeGuard));
            return _owner.CreateScope(scopeName);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _popper.Dispose();
        }

        public override string ToString() => _scopeNode.ToString();
        public string ToSummary() => _scopeNode.ToSummary();
        public string ToJson() => _scopeNode.ToJson();
    }
}
```

**删除的旧代码：**
- `AsyncLocal<DiagnosticContext?> _current` — 不再需要，由 `DiagnosticScopeStore` 管理
- `AsyncLocal<DiagnosticContext?> _lazyCurrent` — 不再需要
- `ScopeNode _rootScope` / `Stack<ScopeNode> _scopeStack` — 由 `DiagnosticScope` 管理
- `EventQueue? _eventQueue` / `EventDispatcher? _eventDispatcher` — 旧事件队列机制
- `DiagnosticContext? _previousContext` — 不再需要
- `SetEventQueue()` / `SetEventDispatcher()` / `GetEventQueue()` — 旧装配方法
- 旧 `ScopeGuard` 中的栈操作逻辑（`_owner._scopeStack.Push/Pop`）

---

### 步骤 2：改造 `DiagnosticManager.cs`

**文件：** `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticManager.cs`

**策略：** 所有方法改为委托给 `DiagnosticScopeStore` + `DiagnosticScope`。

**修改内容：**

```csharp
// ============================================================
// DiagnosticManager.cs — 诊断上下文管理器（全局入口）
//
// ★ 2026-07-16 彻底重构：内部委托给 DiagnosticScopeStore + DiagnosticScope，
//   不再维护独立的 _activeContexts 列表。
//
// 对外接口不变，所有旧调用方无需修改代码。
// ============================================================

using Modules.DiagnosticContext.Abstractions;
using Modules.DiagnosticContext.Configuration;
using Modules.DiagnosticContext.Factory;

namespace Modules.DiagnosticContext.Core;

/// <summary>
/// 诊断上下文管理器（全局入口）
/// <para>★ 2026-07-16 彻底重构：内部委托给 <see cref="DiagnosticScopeStore"/>。</para>
/// </summary>
public static class DiagnosticManager
{
    /// <summary>当前上下文（委托给 DiagnosticContext.Current）</summary>
    public static DiagnosticContext? Current => DiagnosticContext.Current;

    /// <summary>
    /// 第一个活跃上下文
    /// <para>★ 2026-07-16 彻底重构：委托给 <see cref="DiagnosticScopeStore.GetAllActive"/>。</para>
    /// </summary>
    public static DiagnosticContext? FirstActive
    {
        get
        {
            var allScopes = DiagnosticScopeStore.GetAllActive();
            var first = allScopes.FirstOrDefault();
            return first != null ? new DiagnosticContext(first) : null;
        }
    }

    /// <summary>
    /// 向所有活跃上下文广播事件
    /// <para>★ 2026-07-16 彻底重构：委托给 <see cref="DiagnosticScopeStore.GetAllActive"/>。</para>
    /// </summary>
    public static void RecordToAll(string eventName, object? data = null)
    {
        foreach (var scope in DiagnosticScopeStore.GetAllActive())
        {
            scope.Record(eventName, data);
        }
    }

    /// <summary>
    /// 配置全局默认配置
    /// <para>★ 2026-07-16 彻底重构：新体系不需要配置，保留为空操作。</para>
    /// </summary>
    public static void Configure(Action<DiagnosticContextConfiguration> configure) { }

    /// <summary>
    /// 确保当前异步流上有活跃的诊断上下文
    /// <para>★ 2026-07-16 彻底重构：委托给 <see cref="DiagnosticContext.GetOrCreateLazy"/>。</para>
    /// </summary>
    public static DiagnosticContext EnsureScope(string scopeName = "Auto")
    {
        return DiagnosticContext.GetOrCreateLazy(scopeName);
    }

    /// <summary>
    /// 尝试获取当前快照 JSON
    /// </summary>
    public static string? TryGetSnapshotJson()
    {
        return DiagnosticContext.TryGetSnapshotJson();
    }

    /// <summary>
    /// 使用默认配置创建诊断上下文
    /// <para>★ 2026-07-16 彻底重构：内部创建 <see cref="DiagnosticScope"/> 并包装。</para>
    /// </summary>
    public static DiagnosticContext BeginScope(string scopeName)
    {
        var scope = new DiagnosticScope(scopeName);
        return new DiagnosticContext(scope);
    }

    /// <summary>
    /// 使用指定配置创建诊断上下文
    /// </summary>
    public static DiagnosticContext BeginScope(string scopeName, Action<DiagnosticContextConfiguration> configure)
    {
        // 配置仅在新体系支持的范围内生效
        return BeginScope(scopeName);
    }

    /// <summary>
    /// 使用指定配置对象创建诊断上下文
    /// </summary>
    public static DiagnosticContext BeginScope(string scopeName, DiagnosticContextConfiguration config)
    {
        return BeginScope(scopeName);
    }

    /// <summary>
    /// 获取所有活跃上下文的快照
    /// <para>★ 2026-07-16 彻底重构：委托给 <see cref="DiagnosticScopeStore.GetAllActive"/>。</para>
    /// </summary>
    public static string GetSnapshot()
    {
        var allScopes = DiagnosticScopeStore.GetAllActive();
        if (allScopes.Count == 0)
            return "(no active contexts)";

        var sb = new System.Text.StringBuilder();
        int index = 0;
        foreach (var scope in allScopes)
        {
            if (index > 0) sb.AppendLine();
            sb.Append(scope.ToString());
            index++;
        }
        return sb.ToString();
    }
}
```

**删除的旧代码：**
- `List<WeakReference<DiagnosticContext>> _activeContexts` — 不再需要
- `object _lock` — 不再需要
- `DiagnosticContextConfiguration _defaultConfig` — 不再需要
- `CleanupDeadReferences()` — 不再需要

---

### 步骤 3：改造 `DiagnosticContextFactory.cs`

**文件：** `CsharpPrj/Modules/DiagnosticContext/Factory/DiagnosticContextFactory.cs`

**策略：** `Create` 方法改为创建 `DiagnosticScope` 并包装为 `DiagnosticContext`。

**修改内容：**

```csharp
// ============================================================
// DiagnosticContextFactory.cs — 诊断上下文工厂
//
// ★ 2026-07-16 彻底重构：内部创建 DiagnosticScope 并包装为 DiagnosticContext，
//   不再装配事件队列/分发器/管线。
//
// 对外接口不变，所有旧调用方无需修改代码。
// ============================================================

using Modules.DiagnosticContext.Configuration;

namespace Modules.DiagnosticContext.Factory;

/// <summary>
/// 诊断上下文工厂
/// <para>★ 2026-07-16 彻底重构：内部创建 <see cref="Core.DiagnosticScope"/> 并包装。</para>
/// </summary>
public static class DiagnosticContextFactory
{
    /// <summary>使用默认配置创建诊断上下文</summary>
    public static Core.DiagnosticContext Create(string scopeName)
    {
        var scope = new Core.DiagnosticScope(scopeName);
        return new Core.DiagnosticContext(scope);
    }

    /// <summary>创建并激活诊断上下文</summary>
    public static Core.DiagnosticContext CreateAndActivate(string scopeName)
    {
        var scope = new Core.DiagnosticScope(scopeName);
        return new Core.DiagnosticContext(scope);
    }

    /// <summary>创建惰性诊断上下文</summary>
    internal static Core.DiagnosticContext CreateLazy(string scopeName)
    {
        var scope = new Core.DiagnosticScope(scopeName);
        return new Core.DiagnosticContext(scope);
    }

    /// <summary>使用配置委托创建诊断上下文</summary>
    public static Core.DiagnosticContext Create(string scopeName, Action<DiagnosticContextConfiguration> configure)
    {
        return Create(scopeName);
    }

    /// <summary>使用配置对象创建诊断上下文</summary>
    public static Core.DiagnosticContext Create(string scopeName, DiagnosticContextConfiguration config)
    {
        return Create(scopeName);
    }
}
```

**删除的旧代码（整个文件删除以下内容）：**
- `using System.Text;`
- `using BasicMethod.Queue;`
- `using Modules.DiagnosticContext.Formatting;`
- `using Modules.DiagnosticContext.Queue;`
- 整个 `CreateEventQueue` 方法
- 整个 `CreatePipeline` 方法
- 整个 `CreateDispatcher` 方法
- 整个 `CreateFormatter` / `FormatEntry` / `CreateOutputAction` 方法
- `EventQueue` / `EventPipeline` / `EventDispatcher` / `MemoryBatchQueue` / `IContextFormatter` 相关引用

---

### 步骤 4：改造 `DiagnosticSnapshot.Rehydrate()`

**文件：** `CsharpPrj/Modules/DiagnosticContext/Core/DiagnosticSnapshot.cs`

**修改内容：**

```csharp
/// <summary>
/// 恢复为新的 DiagnosticContext
/// <para>★ 2026-07-16 彻底重构：创建 DiagnosticScope 而非通过 DiagnosticContextFactory。</para>
/// </summary>
public DiagnosticContext Rehydrate()
{
    var scope = new DiagnosticScope(Name);
    var context = new DiagnosticContext(scope);
    RestoreToScope(scope.RootNode, this);
    return context;
}
```

**删除：** `using Modules.DiagnosticContext.Factory;`（不再需要）

---

### 步骤 5：迁移 6 个 Share 子模块的 `*DiagnosticContext.cs`

**策略：** 将 `DiagnosticContext.Current` 替换为 `DiagnosticScopeStore.GetCurrent()`。

涉及文件（每个文件改一行）：

| # | 文件路径 |
|---|----------|
| 1 | `CsharpPrj/Share/CommandFramework/Diagnostics/CommandFrameworkDiagnosticContext.cs` |
| 2 | `CsharpPrj/Share/GraphFramework/Diagnostics/GraphFrameworkDiagnosticContext.cs` |
| 3 | `CsharpPrj/Share/PlaceholderFramework/Diagnostics/PlaceholderFrameworkDiagnosticContext.cs` |
| 4 | `CsharpPrj/Share/FrameworkAbstractions/Diagnostics/FrameworkAbstractionsDiagnosticContext.cs` |
| 5 | `CsharpPrj/Share/StateManagement/Diagnostics/StateManagementDiagnosticContext.cs` |
| 6 | `CsharpPrj/Share/UniversalMiddleware/Diagnostics/UniversalMiddlewareDiagnosticContext.cs` |

**修改内容（每个文件相同）：**

```csharp
// 修改前：
using Modules.DiagnosticContext.Core;

// ...

var context = DiagnosticContext.Current;
context?.Record(eventName, data);

// 修改后：
using Modules.DiagnosticContext.Core;

// ...

var scope = DiagnosticScopeStore.GetCurrent();
scope?.Record(eventName, data);
```

---

### 步骤 6：迁移 `ControlValueAccessorBase.cs`

**文件：** `CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/ControlValueAccessorBase.cs`

**修改内容：**

```csharp
// 修改前（L294-L310）：
private static void ExportDiagnosticSnapshot(string controlName, string eventType, string? functionName)
{
    var snapshot = DiagnosticManager.GetSnapshot();
    if (!string.IsNullOrEmpty(snapshot) && !snapshot.Contains("(no active contexts)"))
    {
        var truncatedSnapshot = snapshot.Length > 500
            ? "..." + snapshot.Substring(snapshot.Length - 500)
            : snapshot;
        EventTriggerLog.Log("DiagnosticSnapshot",
            $"controlName={controlName}, " +
            $"eventType={eventType}, " +
            $"functionName={functionName}, " +
            $"snapshot={Environment.NewLine}{truncatedSnapshot}");
    }
}

// 修改后：
private static void ExportDiagnosticSnapshot(string controlName, string eventType, string? functionName)
{
    var currentScope = DiagnosticScopeStore.GetCurrent();
    if (currentScope != null)
    {
        var snapshot = currentScope.ToJson();
        var truncatedSnapshot = snapshot.Length > 500
            ? "..." + snapshot.Substring(snapshot.Length - 500)
            : snapshot;
        EventTriggerLog.Log("DiagnosticSnapshot",
            $"controlName={controlName}, " +
            $"eventType={eventType}, " +
            $"functionName={functionName}, " +
            $"snapshot={Environment.NewLine}{truncatedSnapshot}");
    }
}
```

**注意：** 需要在文件顶部添加 `using Modules.DiagnosticContext.Core;`。

---

### 步骤 7：更新 `Architecture.md`

**文件：** `CsharpPrj/Architecture.md`

**修改内容：**

1. **§2.6 Modules.DiagnosticContext 层** — 更新旧体系核心类型说明，标注"★ 2026-07-16 彻底重构：内部委托给 DiagnosticScope"

2. **§5 基础技术理念** — 补充说明旧体系接口（`DiagnosticContext` / `DiagnosticManager` / `DiagnosticContextFactory`）已改造为委托到新体系，对外接口不变

---

### 步骤 8：新增单元测试

**文件：** `CentralTest/CSharpTest/Modules.Tests/DiagnosticContext.Tests/Core/DiagnosticScopeTests.cs`

**测试内容：**

| 测试用例 | 说明 |
|----------|------|
| `DiagnosticScope_Create_RegistersInStore` | 创建 scope 后验证注册到 `DiagnosticScopeStore` |
| `DiagnosticScope_Dispose_UnregistersFromStore` | Dispose 后验证从 store 中注销 |
| `DiagnosticScope_Record_StoresInScopeNode` | Record 事件后验证 ScopeNode 中有数据 |
| `DiagnosticScope_PushScope_CreatesChildNode` | PushScope 创建子 ScopeNode |
| `DiagnosticScope_PushScope_Dispose_PopsScope` | PushScope 的 IDisposable Dispose 后恢复父节点 |
| `DiagnosticScope_Freeze_CreatesSnapshot` | Freeze 创建 DiagnosticSnapshot |
| `DiagnosticScopeStore_GetCurrent_ReturnsCorrectScope` | 同一异步流中 GetCurrent 返回正确的 scope |
| `DiagnosticScopeStore_GetById_ReturnsScope` | 通过 CallChainId 获取 scope |
| `DiagnosticScopeStore_GetAllActive_ReturnsAllScopes` | 获取所有活跃 scope |
| `DiagnosticScope_CrossThread_Access` | 跨线程通过 CallChainId 访问 scope |

**文件：** `CentralTest/CSharpTest/Modules.Tests/DiagnosticContext.Tests/Core/DiagnosticScopeStoreTests.cs`

| 测试用例 | 说明 |
|----------|------|
| `Register_And_GetById` | 注册后能通过 ID 获取 |
| `Unregister_RemovesScope` | 注销后无法获取 |
| `GetCurrent_AsyncLocal_Flow` | AsyncLocal 在 async/await 中传播 |
| `GetCurrent_ConfigureAwaitFalse_DoesNotLose` | ConfigureAwait(false) 不丢失 |
| `ActiveCount_ReturnsCorrectCount` | 活跃数量正确 |
| `Concurrent_Register_Unregister_ThreadSafe` | 并发注册/注销线程安全 |

---

## 4. 修改清单

| # | 文件 | 修改类型 | 说明 |
|---|------|----------|------|
| 1 | `Modules/DiagnosticContext/Core/DiagnosticContext.cs` | **重写** | 改为包装 `DiagnosticScope`，删除 AsyncLocal/EventQueue 等旧代码 |
| 2 | `Modules/DiagnosticContext/Core/DiagnosticManager.cs` | **重写** | 所有方法委托给 `DiagnosticScopeStore` + `DiagnosticScope` |
| 3 | `Modules/DiagnosticContext/Factory/DiagnosticContextFactory.cs` | **重写** | `Create` 改为创建 `DiagnosticScope`，删除队列/管线/分发器代码 |
| 4 | `Modules/DiagnosticContext/Core/DiagnosticSnapshot.cs` | **修改** | `Rehydrate()` 改为创建 `DiagnosticScope`，删除 `using Factory` |
| 5 | `Share/CommandFramework/Diagnostics/CommandFrameworkDiagnosticContext.cs` | **修改** | `DiagnosticContext.Current` → `DiagnosticScopeStore.GetCurrent()` |
| 6 | `Share/GraphFramework/Diagnostics/GraphFrameworkDiagnosticContext.cs` | **修改** | 同上 |
| 7 | `Share/PlaceholderFramework/Diagnostics/PlaceholderFrameworkDiagnosticContext.cs` | **修改** | 同上 |
| 8 | `Share/FrameworkAbstractions/Diagnostics/FrameworkAbstractionsDiagnosticContext.cs` | **修改** | 同上 |
| 9 | `Share/StateManagement/Diagnostics/StateManagementDiagnosticContext.cs` | **修改** | 同上 |
| 10 | `Share/UniversalMiddleware/Diagnostics/UniversalMiddlewareDiagnosticContext.cs` | **修改** | 同上 |
| 11 | `Operation/BackEndDriven/EventTriggerFramework/ControlValueAccessorBase.cs` | **修改** | `DiagnosticManager.GetSnapshot()` → `DiagnosticScopeStore.GetCurrent()` |
| 12 | `CsharpPrj/Architecture.md` | **修改** | 更新描述 |
| 13 | `CentralTest/.../Core/DiagnosticScopeTests.cs` | **新增** | `DiagnosticScope` 单元测试 |
| 14 | `CentralTest/.../Core/DiagnosticScopeStoreTests.cs` | **新增** | `DiagnosticScopeStore` 单元测试 |

---

## 5. 风险与注意事项

### 5.1 `DiagnosticContext.Dispose()` 不再释放内部 `DiagnosticScope`

**原因：** `DiagnosticScope` 的生命周期由 `using` 管理，`DiagnosticContext` 只是包装器。

**典型场景：**
```csharp
// 旧用法（通过 DiagnosticManager）：
using var ctx = DiagnosticManager.BeginScope("X");
// DiagnosticManager.BeginScope 内部创建 DiagnosticScope
// DiagnosticScope 的 Dispose 由它自己的 using 管理
// DiagnosticContext.Dispose 只标记 _disposed，不重复释放

// 新用法（直接使用 DiagnosticScope）：
using var scope = new DiagnosticScope("X");
// scope.Dispose() 会从 DiagnosticScopeStore 注销并清理 AsyncLocal
```

**结论：** 两种用法都正确，`DiagnosticScope` 的 `Dispose` 只执行一次。

### 5.2 `DiagnosticContext.Activate()` 变为空操作

**原因：** `DiagnosticScope` 的构造函数已设置 `DiagnosticScopeStore.CurrentCallChainId`，无需额外激活。

**影响：** 任何调用 `context.Activate()` 的旧代码仍然可以编译通过，但方法体为空。

### 5.3 `DiagnosticManager.Configure()` 变为空操作

**原因：** 新体系不需要 `DiagnosticContextConfiguration` 中的队列/管线/分发器配置。

**影响：** 任何调用 `DiagnosticManager.Configure(cfg => ...)` 的旧代码仍然可以编译通过，但配置被忽略。如果将来需要配置，应在 `DiagnosticScope` 层面扩展。

### 5.4 `EventQueue` / `EventPipeline` / `EventDispatcher` 相关代码被删除

**原因：** 新体系通过 `DiagnosticScope.Record()` 直接写入 `ScopeNode`，不再需要异步队列机制。

**影响：** 如果外部代码直接引用了 `EventQueue` / `EventPipeline` / `EventDispatcher` 类型，需要改为通过 `DiagnosticScope.Record()` 记录事件。这些类型仅在 `DiagnosticContextFactory.cs` 内部使用，外部不应直接引用。

---

## 6. 验证标准

| # | 验证项 | 验证方法 | 预期结果 |
|---|--------|----------|----------|
| 1 | **构建通过** | `dotnet build CsharpPrj/MainProj.sln` | 0 错误 0 警告 |
| 2 | **旧测试通过** | `dotnet test CentralTest/CSharpTest/Modules.Tests/` | 全部通过 |
| 3 | **新测试通过** | 新增的 `DiagnosticScopeTests` + `DiagnosticScopeStoreTests` | 全部通过 |
| 4 | **双体系统一** | `DiagnosticManager.BeginScope("X")` 和 `new DiagnosticScope("X")` 创建的作用域都在 `DiagnosticScopeStore` 中 | `GetCurrent()` 返回同一个 scope |
| 5 | **快照一致** | `DiagnosticManager.GetSnapshot()` 和 `DiagnosticScopeStore.GetAllActive()` 返回相同的数据 | 内容一致 |
| 6 | **AsyncLocal 不丢失** | 在 async/await + ConfigureAwait(false) 场景下验证 | `DiagnosticScopeStore.GetCurrent()` 返回正确的 scope |
| 7 | **跨线程访问** | 在 Task.Run 中通过 CallChainId 访问 scope | 能正确获取 |
| 8 | **旧接口兼容** | 所有旧调用方（Share 层 6 个文件 + ControlValueAccessorBase）编译通过 | 0 错误 |

---

## 7. 旧体系代码封存

### 7.1 封存策略

旧体系代码（改造前的 `DiagnosticContext.cs` / `DiagnosticManager.cs` / `DiagnosticContextFactory.cs`）**不删除**，而是移动到 `_Legacy/` 目录封存，保留完整的历史参考。

### 7.2 封存目录结构

```
CsharpPrj/Modules/DiagnosticContext/
├── Core/
│   ├── DiagnosticContext.cs          ← 改造后（委托到 DiagnosticScope）
│   ├── DiagnosticManager.cs          ← 改造后（委托到 DiagnosticScopeStore）
│   ├── DiagnosticScope.cs            ← 新体系核心
│   ├── DiagnosticScopeStore.cs       ← 新体系核心
│   ├── DiagnosticSnapshot.cs         ← 修改（Rehydrate 改为创建 DiagnosticScope）
│   └── ScopeNode.cs                  ← 共用（数据模型不变）
├── Factory/
│   └── DiagnosticContextFactory.cs   ← 改造后（创建 DiagnosticScope）
├── _Legacy/                          ← ★ 新增：旧体系封存
│   ├── README.md                     ← 封存说明
│   ├── DiagnosticContext.cs          ← 旧版（含 AsyncLocal/EventQueue）
│   ├── DiagnosticManager.cs          ← 旧版（含 _activeContexts）
│   └── DiagnosticContextFactory.cs   ← 旧版（含队列/管线/分发器）
└── ...
```

### 7.3 封存步骤

1. 在 `CsharpPrj/Modules/DiagnosticContext/` 下创建 `_Legacy/` 目录
2. 将改造前的三个文件复制到 `_Legacy/` 目录：
   - `Core/DiagnosticContext.cs` → `_Legacy/DiagnosticContext.cs`
   - `Core/DiagnosticManager.cs` → `_Legacy/DiagnosticManager.cs`
   - `Factory/DiagnosticContextFactory.cs` → `_Legacy/DiagnosticContextFactory.cs`
3. 在 `_Legacy/` 下创建 `README.md`，说明封存原因和日期
4. 确保 `_Legacy/` 目录中的文件**不参与编译**（在 `.csproj` 中排除或使用 `#if LEGACY` 条件编译）

### 7.4 `_Legacy/README.md` 内容

```markdown
# DiagnosticContext 旧体系封存

## 封存信息

- **封存日期：** 2026-07-16
- **封存原因：** 旧体系基于 AsyncLocal<DiagnosticContext> 的隐式传播与新体系 DiagnosticScopeStore 并行，导致双体系数据分裂
- **重构方案：** 详见 `docs/analysis/DiagnosticContext框架_彻底重构消除旧体系.md`
- **改造方式：** 旧接口（DiagnosticContext / DiagnosticManager / DiagnosticContextFactory）内部实现改为委托到新体系，对外接口不变

## 文件说明

| 文件 | 原路径 | 说明 |
|------|--------|------|
| `DiagnosticContext.cs` | `Core/DiagnosticContext.cs` | 旧版诊断上下文，含 AsyncLocal、ScopeNode 栈、EventQueue/EventDispatcher |
| `DiagnosticManager.cs` | `Core/DiagnosticManager.cs` | 旧版管理器，含 _activeContexts 列表和 WeakReference 清理 |
| `DiagnosticContextFactory.cs` | `Factory/DiagnosticContextFactory.cs` | 旧版工厂，含 CreateEventQueue/CreatePipeline/CreateDispatcher |

## 注意事项

- 这些文件仅供历史参考，**不要**重新引入项目编译
- 改造后的文件位于 `Core/` 和 `Factory/` 目录，内部实现已委托到 DiagnosticScope + DiagnosticScopeStore
- 所有外部调用方（Share 层 6 个文件 + ControlValueAccessorBase）无需修改代码
```

### 7.5 确保 `_Legacy/` 不参与编译

在 `CsharpPrj/Modules/DiagnosticContext/DiagnosticContext.csproj` 中添加：

```xml
<ItemGroup>
  <Compile Remove="_Legacy/**/*.cs" />
  <None Include="_Legacy/**/*.cs" />
</ItemGroup>
```

或者在 `Directory.Build.props` 中添加全局排除规则：

```xml
<ItemGroup>
  <Compile Remove="**/_Legacy/**/*.cs" />
  <None Include="**/_Legacy/**/*.cs" />
</ItemGroup>
```
