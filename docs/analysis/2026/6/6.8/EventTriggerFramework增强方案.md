# EventTriggerFramework 增强方案

## 1. 概述

### 1.1 背景

EventTriggerFramework 在 WdUI 的 ConfigContainer 场景中暴露了不足：

- **事件传播树**：父子容器需要事件隔离和传播，当前 EventBus 是扁平的
- **订阅生命周期**：订阅/取消订阅分散在多处，容易泄漏
- **事件去重**：OnRootContainerLoaded 可能被多次触发
- **事件传播**：子容器事件需要向上传播，当前手动实现

### 1.2 目标

1. ✅ 事件作用域（EventScope）— 父子层次结构的事件隔离
2. ✅ 订阅生命周期追踪（SubscriptionToken）— 统一管理订阅
3. ✅ 事件去重（Deduplication）— 防止重复处理
4. ✅ 事件队列（EventQueue）— 异步队列驱动的事件传播
5. ✅ EventBus 接口扩展 — 补充管理能力

---

## 2. 事件作用域（EventScope）

### 2.1 问题

EventBus 是全局扁平的，无法区分"哪个容器发布了事件"。

### 2.2 方案

引入 `EventScope`，事件携带 ScopeId，订阅者按策略过滤。

### 2.3 核心设计

```csharp
public class EventScope : IDisposable
{
    private readonly IEventBusCore _bus;
    private readonly string _scopeId;
    private readonly EventScope? _parent;
    private readonly List<EventScope> _children = new();
    private readonly SubscriptionManager _subscriptions = new();
    private bool _disposed;

    public string ScopeId => _scopeId;
    public EventScope? Parent => _parent;
    public IReadOnlyList<EventScope> Children => _children.AsReadOnly();

    public EventScope(IEventBusCore bus, string? scopeId = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _scopeId = scopeId ?? Guid.NewGuid().ToString("N");
    }

    public EventScope CreateChildScope(string? scopeId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var child = new EventScope(_bus, scopeId ?? Guid.NewGuid().ToString("N"), this);
        _children.Add(child);
        return child;
    }

    // 在当前作用域发布事件
    public void Publish(string eventName, object? data = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bus.Publish(eventName, new ScopedEvent(_scopeId, eventName, data));
    }

    // 发布事件并通过队列传播到父作用域
    public void PublishAndPropagate(string eventName, object? data = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Publish(eventName, data);
        if (_parent != null)
            EventQueue.Enqueue(new PropagationEvent(_scopeId, eventName, data));
    }

    // 订阅事件，按策略过滤
    public SubscriptionToken Subscribe(string eventName,
        Action<ScopedEvent> handler,
        EventScopePolicy policy = EventScopePolicy.CurrentAndChildren)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Action<object?> wrapped = data =>
        {
            if (data is ScopedEvent scoped && MatchesScope(scoped.ScopeId, policy))
                handler(scoped);
        };
        _bus.Subscribe(eventName, wrapped);
        var token = new SubscriptionToken(() => _bus.Unsubscribe(eventName, wrapped));
        _subscriptions.Add(token);
        return token;
    }

    private bool MatchesScope(string sourceScopeId, EventScopePolicy policy) => policy switch
    {
        EventScopePolicy.Exact => sourceScopeId == _scopeId,
        EventScopePolicy.CurrentAndChildren => sourceScopeId == _scopeId || IsDescendant(sourceScopeId),
        EventScopePolicy.CurrentAndParents => sourceScopeId == _scopeId || IsAncestor(sourceScopeId),
        EventScopePolicy.All => true,
        _ => false
    };

    private bool IsDescendant(string scopeId)
    {
        foreach (var child in _children)
            if (child._scopeId == scopeId || child.IsDescendant(scopeId)) return true;
        return false;
    }

    private bool IsAncestor(string scopeId)
    {
        var current = _parent;
        while (current != null)
        {
            if (current._scopeId == scopeId) return true;
            current = current._parent;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscriptions.Dispose();
        _parent?._children.Remove(this); // ★ 修复：从父作用域移除自己
        foreach (var child in _children.ToList()) child.Dispose();
        _children.Clear();
    }
}

public enum EventScopePolicy
{
    Exact,              // 只接收当前作用域
    CurrentAndChildren, // 接收自己和子作用域
    CurrentAndParents,  // 接收自己和父作用域
    All                 // 接收所有
}

public class ScopedEvent
{
    public string ScopeId { get; }
    public string EventName { get; }
    public object? Data { get; }
    public DateTime Timestamp { get; }
    public bool IsPropagated { get; } // ★ 修复：区分直接事件和传播事件

    public ScopedEvent(string scopeId, string eventName, object? data, bool isPropagated = false)
    {
        ScopeId = scopeId ?? throw new ArgumentNullException(nameof(scopeId));
        EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
        Data = data;
        Timestamp = DateTime.UtcNow;
        IsPropagated = isPropagated;
    }
}

// ★ 修复：传播事件，携带源 ScopeId 和取消能力
public class PropagationEvent : ScopedEvent
{
    public string SourceScopeId { get; }
    public bool IsCancelled { get; private set; }

    public PropagationEvent(string sourceScopeId, string eventName, object? data)
        : base(sourceScopeId, eventName, data, isPropagated: true)
    {
        SourceScopeId = sourceScopeId;
    }

    public void Cancel() => IsCancelled = true;
}
```

---

## 3. 订阅生命周期追踪（SubscriptionToken）

### 3.1 问题

取消订阅需要持有原始 handler 引用，匿名函数无法取消。

### 3.2 方案

```csharp
public class SubscriptionToken : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    public DateTime CreatedAt { get; }
    public bool IsDisposed => _disposed;

    internal SubscriptionToken(Action unsubscribe)
    {
        _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        CreatedAt = DateTime.UtcNow;
    }

    public void Dispose()
    {
        if (!_disposed) { _disposed = true; _unsubscribe(); }
    }
}

public class SubscriptionManager : IDisposable
{
    private readonly List<SubscriptionToken> _tokens = new();
    private readonly object _lock = new();
    private bool _disposed;

    public int Count { get { lock (_lock) return _tokens.Count; } }

    public SubscriptionToken Add(SubscriptionToken token)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));
        lock (_lock) { ObjectDisposedException.ThrowIf(_disposed, this); _tokens.Add(token); }
        return token;
    }

    public bool Remove(SubscriptionToken token)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));
        lock (_lock) { if (_tokens.Remove(token)) { token.Dispose(); return true; } }
        return false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var token in _tokens) token.Dispose();
            _tokens.Clear();
        }
    }
}
```

---

## 4. 事件去重（Deduplication）

### 4.1 问题

OnRootContainerLoaded 可能被多次触发，订阅者端手动 `if (_initialized) return` 脆弱。

### 4.2 方案

装饰器模式，通过事件指纹去重。

```csharp
public class DeduplicatingEventBusDecorator : IEventBusCore
{
    private readonly IEventBusCore _inner;
    private readonly TimeSpan _dedupWindow;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTime>> _processedEvents = new();

    public DeduplicatingEventBusDecorator(IEventBusCore inner, TimeSpan? dedupWindow = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _dedupWindow = dedupWindow ?? TimeSpan.FromSeconds(5);
    }

    // ★ 修复：使用 RuntimeHelpers.GetHashCode 避免 GetHashCode() 被子类覆盖
    private static string GenerateFingerprint(string eventName, object? data)
    {
        if (data is ScopedEvent scoped)
            return $"{scoped.ScopeId}:{scoped.EventName}";
        var dataRef = data != null
            ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(data).ToString()
            : "null";
        return $"{eventName}:{dataRef}";
    }

    public void Publish(string eventName, object? data = null)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("事件名称不能为空", nameof(eventName));

        var fingerprint = GenerateFingerprint(eventName, data);
        if (_processedEvents.TryGetValue(eventName, out var fingerprints) &&
            fingerprints.TryGetValue(fingerprint, out var processedAt) &&
            DateTime.UtcNow - processedAt < _dedupWindow)
            return; // 重复事件，过滤

        var fprints = _processedEvents.GetOrAdd(eventName, _ => new());
        fprints[fingerprint] = DateTime.UtcNow;
        _inner.Publish(eventName, data);
    }

    // 其他方法直接委托给 _inner ...
    public void Subscribe(string eventName, Action<object?> handler) => _inner.Subscribe(eventName, handler);
    public void Unsubscribe(string eventName, Action<object?> handler) => _inner.Unsubscribe(eventName, handler);
    public bool HasSubscribers(string eventName) => _inner.HasSubscribers(eventName);
    public void Dispose() { _inner.Dispose(); _processedEvents.Clear(); }
    // ... 省略其他委托方法
}
```

---

## 5. 事件队列（EventQueue）

### 5.1 问题

冒泡机制（递归调用父作用域）存在：同步递归异常中断、无法取消、难以调试、双重触发风险。

### 5.2 方案

异步事件队列替代冒泡。

```csharp
// ★ 简化设计：EventQueue 只负责入队和异步处理，不持有 EventBus 引用
// ★ 由 EventScope 在创建时注册到 EventQueue
public class EventQueue : IDisposable
{
    private readonly ConcurrentQueue<PropagationEvent> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private int _isProcessing;
    private readonly IEventBusCore _bus;

    public const int MaxLength = 1000;
    public int Count => _queue.Count;

    public EventQueue(IEventBusCore bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public void Enqueue(PropagationEvent propagationEvent)
    {
        if (propagationEvent == null) throw new ArgumentNullException(nameof(propagationEvent));
        if (propagationEvent.IsCancelled) return;
        if (_queue.Count >= MaxLength) return;

        _queue.Enqueue(propagationEvent);
        TryProcessAsync();
    }

    private void TryProcessAsync()
    {
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0)
            Task.Run(() => ProcessAsync());
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (_queue.TryDequeue(out var evt))
            {
                if (_cts.IsCancellationRequested) break;
                if (evt.IsCancelled) continue;

                // ★ 将传播事件发布到 EventBus，父作用域的 Subscribe 通过 CurrentAndChildren 接收
                _bus.Publish(evt.EventName, evt);
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EventQueue] 异常: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessing, 0);
            if (!_queue.IsEmpty && !_cts.IsCancellationRequested)
                TryProcessAsync();
        }
    }

    public void Clear() { while (_queue.TryDequeue(out _)) { } }
    public void Dispose() { _cts.Cancel(); _cts.Dispose(); Clear(); }
}
```

### 5.3 传播流程

```
子作用域.PublishAndPropagate("Evt", data)
  ├── Publish("Evt", data) → 当前作用域订阅者收到 ✅
  └── EventQueue.Enqueue(PropagationEvent)
       └── 异步: _bus.Publish("Evt", propagationEvent)
            └── 父作用域 Subscribe(CurrentAndChildren) 收到 ✅
                └── scopedEvent.IsPropagated == true 可区分
```

### 5.4 队列 vs 冒泡

| 特性 | 冒泡 | 队列 |
|------|------|------|
| 调用方式 | 同步递归 | 异步队列 |
| 异常处理 | 中断整个链 | 不影响后续 |
| 传播控制 | 无法取消 | CancelPropagation() |
| 调试 | 递归链难追踪 | 队列可枚举 |
| 双重触发 | 易发生 | 通过 IsPropagated 区分 |

---

## 6. EventBus 接口扩展

```csharp
public interface IEventBusCore : IDisposable
{
    void Publish(string eventName, object? data = null);
    void Publish<TEvent>(TEvent @event) where TEvent : class;
    void Subscribe(string eventName, Action<object?> handler);
    void Subscribe<TEvent>(object subscriber, Action<TEvent> handler) where TEvent : class;
    void Unsubscribe(string eventName, Action<object?> handler);
    void Unsubscribe<TEvent>(object subscriber) where TEvent : class;
    bool HasSubscribers(string eventName);
    void BindEvent(object controlHandle, string eventName, Delegate handler);
    void UnbindEvent(object controlHandle, string eventName);
    void BindEventFromConfig(object controlHandle, object eventConfig, Delegate handler);

    // 新增管理方法
    void UnsubscribeAll(string eventName);
    int SubscriberCount(string eventName);
    IEnumerable<string> GetEventNames();
    void ClearAllSubscriptions();
}
```

---

## 7. 文件结构

```
Operation/EventTriggerFramework/EventBus/
├── EventBus.cs                          # IEventBusCore, EventBusFacade, DefaultEventBusCore
├── EnhancedEventBusCore.cs              # EnhancedEventBusDecorator
├── EventScope.cs                        # 新增：事件作用域
├── SubscriptionToken.cs                 # 新增：订阅令牌 + 订阅管理器
├── DeduplicatingEventBusDecorator.cs    # 新增：事件去重装饰器
└── EventQueue.cs                        # 新增：事件队列
```

---

## 8. 实施计划

| 阶段 | 内容 | 工作量 |
|------|------|--------|
| 1 | SubscriptionToken + SubscriptionManager | 1 天 |
| 2 | EventScope（含作用域策略） | 2 天 |
| 3 | EventQueue | 1 天 |
| 4 | DeduplicatingEventBusDecorator | 1 天 |
| 5 | IEventBusCore 接口扩展 | 0.5 天 |
| 6 | 单元测试 | 2 天 |
| 7 | WdUI 集成改造 | 3 天 |

**总计：约 10.5 天**
