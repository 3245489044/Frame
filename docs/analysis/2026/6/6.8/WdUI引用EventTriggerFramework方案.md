# WdUI 引用 EventTriggerFramework 方案

## 1. 概述

### 1.1 问题

WdUI 的 `ConfigContainer` 使用原生 C# event 进行事件通信，存在：

| 问题 | 当前实现 | 风险 |
|------|---------|------|
| 事件传播 | 手动 `OnSubContainerConfigLoaded` 转发 | 泄漏、重复触发 |
| 订阅生命周期 | 分散在 RegisterSubContainer/ClearResources/Dispose | 遗漏取消订阅 |
| 事件隔离 | 无差别向上传播 | 父容器收到不应处理的事件 |
| 重复处理 | 订阅者端 `if (_initialized) return` | 脆弱 |

### 1.2 目标

集成 EventTriggerFramework 的 EventScope、SubscriptionToken、EventQueue、去重能力，从架构层面解决事件传播问题。

---

## 2. 架构

```
EventTriggerFramework
  EventScope → SubscriptionToken → EventQueue → EventBus
        ↑                              ↑
        └────────── WdUI ──────────────┘
  ConfigContainer: 持有 EventScope，通过它发布/订阅事件
  MainForm:        通过 EventScope 订阅 rootContainer 事件
```

---

## 3. ConfigContainer 改造

### 3.1 新增 EventScope 集成

```csharp
public class ConfigContainer : Panel
{
    private IEventBusFacade? _eventBus;
    private EventScope? _eventScope;
    private readonly SubscriptionManager _subscriptionManager = new();
    private SubscriptionToken? _subContainerEventSubscription; // ★ 单订阅

    public EventScope? EventScope => _eventScope;

    public void InitializeEventSystem(IEventBusFacade eventBus, EventScope? parentScope = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _eventScope = parentScope != null
            ? parentScope.CreateChildScope(Name ?? Guid.NewGuid().ToString("N"))
            : new EventScope(eventBus, Name ?? Guid.NewGuid().ToString("N"));
    }
}
```

### 3.2 事件发布（FinalizeRender）

```csharp
private void FinalizeRender(UIConfiguration config, bool success, string? error = null)
{
    _isLoaded = true;
    if (_eventScope != null)
    {
        _eventScope.Publish("ConfigLoaded", new ContainerConfigLoadedEventArgs(config, success, error));
        _eventScope.PublishAndPropagate("ContentChanged", EventArgs.Empty); // ★ 通过队列传播
    }
    else
    {
        ConfigLoaded?.Invoke(this, new ContainerConfigLoadedEventArgs(config, success, error));
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

### 3.3 事件订阅（RegisterSubContainer）

```csharp
// ★ 修复：单订阅模式，通过 ScopeId 区分不同子容器
public void RegisterSubContainer(ConfigContainer container)
{
    if (_eventScope != null && container.EventScope != null)
    {
        if (_subContainerEventSubscription == null)
        {
            _subContainerEventSubscription = _subscriptionManager.Add(
                _eventScope.Subscribe("ConfigLoaded", scopedEvent =>
                {
                    if (scopedEvent.ScopeId != _eventScope.ScopeId &&
                        scopedEvent.Data is ContainerConfigLoadedEventArgs args)
                    {
                        var source = FindSubContainerByScopeId(scopedEvent.ScopeId);
                        if (source != null) OnSubContainerConfigLoaded(source, args);
                    }
                }, EventScopePolicy.CurrentAndChildren)
            );
        }
    }
    else
    {
        container.ConfigLoaded += OnSubContainerConfigLoaded; // 兼容
    }
    _subContainerIndex.Register(container.Name, container);
}

private ConfigContainer? FindSubContainerByScopeId(string scopeId)
{
    foreach (var entry in _subContainerIndex.GetAll())
        if (entry.Value is ConfigContainer c && c.EventScope?.ScopeId == scopeId) return c;
    return null;
}
```

### 3.4 清理

```csharp
private void ClearResources()
{
    _subscriptionManager.Dispose();
    _subContainerEventSubscription = null;
    _subContainerIndex.Clear();
    _currentConfig = null;
    WdUIComposer.RenderPipeline.DisposeSession(this);
}

protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        Controls.Clear();
        _subscriptionManager.Dispose();
        _subContainerEventSubscription = null;
        _eventScope?.Dispose();
        ClearResources();
    }
    base.Dispose(disposing);
}
```

---

## 4. MainForm 改造

### 4.1 初始化

```csharp
private EventScope? _rootScope;

private async Task InitializeWdUIAsync()
{
    var eventBus = GetOrInitializeEventBus();
    _rootScope = new EventScope(eventBus, "root");
    _rootContainer.InitializeEventSystem(eventBus, _rootScope);

    // ★ 修复：只用一个 CurrentAndChildren 订阅，handler 中通过 ScopeId 区分
    _rootScope.Subscribe("ConfigLoaded", OnRootContainerConfigLoaded,
        EventScopePolicy.CurrentAndChildren);

    await _rootContainer.LoadConfigAsync(MainLayoutPath);
}
```

### 4.2 事件处理器

```csharp
private void OnRootContainerConfigLoaded(ScopedEvent scopedEvent)
{
    if (scopedEvent.Data is not ContainerConfigLoadedEventArgs args) return;

    if (scopedEvent.ScopeId == _rootScope?.ScopeId)
        HandleRootContainerLoaded(args);
    else
        HandleSubContainerLoaded(scopedEvent.ScopeId, args);
}

private void HandleRootContainerLoaded(ContainerConfigLoadedEventArgs args)
{
    if (!args.Success) { LogError($"MainLayout 加载失败: {args.Error}"); return; }
    LogInfo("MainLayout 加载成功");

    var contentContainer = _rootContainer.FindSubContainer("contentContainer");
    if (contentContainer != null)
        contentContainer.InitializeEventSystem(
            EventTriggerFrameworkInitializer.Context!.EventBus!, _rootScope);

    _ = LoadConfigSelectorAsync();
}

private void HandleSubContainerLoaded(string scopeId, ContainerConfigLoadedEventArgs args)
{
    LogInfo($"子容器 {scopeId} 加载完成");
}
```

### 4.3 子容器加载

```csharp
private async Task LoadConfigSelectorAsync()
{
    var contentContainer = _rootContainer.FindSubContainer("contentContainer");
    if (contentContainer == null) { LogError("MainLayout must contain a 'contentContainer'"); return; }

    // ★ 修复：不再重复订阅，_rootScope 已用 CurrentAndChildren 订阅
    await contentContainer.LoadConfigAsync(ConfigSelectorPath);
}
```

---

## 5. Skeleton 改造

### 5.1 CreateLayoutPanelSkeleton

```csharp
private static Control CreateControlWithChildren(ControlConfig config, Control? parent,
    List<ConfigContainer> pendingContainers, EventScope? parentScope = null)
{
    if (IsContainer(config))
    {
        var containerCtrl = new ConfigContainer { Name = config.Name, ... };
        if (parentScope != null)
        {
            var eventBus = EventTriggerFrameworkInitializer.Context?.EventBus;
            if (eventBus != null) containerCtrl.InitializeEventSystem(eventBus, parentScope);
        }
        pendingContainers.Add(containerCtrl);
        // 递归子控件...
        return containerCtrl;
    }
    // 其他控件...
}
```

### 5.2 RegisterSubContainersSkeleton

```csharp
public void RegisterPending(Control root, SkeletonContext ctx)
{
    var pending = ctx.GetData<List<ConfigContainer>>(PendingContainersKey);
    if (pending == null) return;

    foreach (var container in pending)
    {
        var parent = FindNearestConfigContainer(container.Parent);
        if (parent != null)
        {
            if (parent.EventScope != null && container.EventScope == null)
            {
                var eventBus = EventTriggerFrameworkInitializer.Context?.EventBus;
                if (eventBus != null) container.InitializeEventSystem(eventBus, parent.EventScope);
            }
            parent.RegisterSubContainer(container);
        }
        else if (root is ConfigContainer rootContainer)
        {
            rootContainer.RegisterSubContainer(container);
        }
    }
}
```

---

## 6. 事件命名约定

```csharp
public static class ContainerEventNames
{
    public const string ConfigLoaded = "WdUI.Container.ConfigLoaded";
    public const string ContentChanged = "WdUI.Container.ContentChanged";
    public const string EventsBound = "WdUI.Container.EventsBound";
    public const string SubContainerRegistered = "WdUI.Container.SubContainerRegistered";
    public const string RenderStarted = "WdUI.Container.RenderStarted";
    public const string RenderCompleted = "WdUI.Container.RenderCompleted";
}
```

---

## 7. 兼容性策略

渐进式迁移：保留原生事件 → 新代码用 EventScope → 逐步迁移旧代码 → 移除原生事件。

```csharp
public bool UseEventTriggerFramework { get; set; } = false;

private void FinalizeRender(...)
{
    if (UseEventTriggerFramework && _eventScope != null)
        _eventScope.Publish(ContainerEventNames.ConfigLoaded, args);
    else
        ConfigLoaded?.Invoke(this, args);
}
```

---

## 8. 测试

```csharp
[Test]
public void EventScope_Should_FilterByPolicy() { /* Exact 不接收子作用域事件 */ }

[Test]
public void EventScope_Should_ReceiveChildEvents_WithCurrentAndChildren() { /* CurrentAndChildren 接收子作用域事件 */ }

[Test]
public async Task EventQueue_Should_Propagate_Without_DoubleTrigger()
{
    // ★ 验证队列传播不会导致双重触发
    var root = new EventScope(bus, "root");
    var child = root.CreateChildScope("child");
    root.Subscribe("test", e => received.Add(e), EventScopePolicy.CurrentAndChildren);
    child.PublishAndPropagate("test", "data");
    await Task.Delay(100);
    Assert.That(received, Has.Count.EqualTo(1));
}

[Test]
public async Task PropagationEvent_Should_Be_Cancellable()
{
    // ★ 验证传播事件可取消
    var evt = new PropagationEvent("child", "test", "data");
    evt.Cancel();
    EventQueue.Enqueue(evt);
    await Task.Delay(100);
    Assert.That(received, Is.Empty);
}
```

---

## 9. 实施计划

| 步骤 | 内容 | 工作量 | 风险 |
|------|------|--------|------|
| 1 | ConfigContainer 新增 EventScope | 1 天 | 低 |
| 2 | FinalizeRender 改为 EventScope 发布 | 0.5 天 | 中 |
| 3 | RegisterSubContainer 单订阅模式 | 1 天 | 中 |
| 4 | ClearResources/Dispose 统一清理 | 0.5 天 | 低 |
| 5 | MainForm 单订阅 + ScopeId 区分 | 1 天 | 高 |
| 6 | Skeleton 传递 EventScope | 0.5 天 | 中 |
| 7 | 单元测试 | 2 天 | 低 |

**总计：约 6.5 天**

### 风险点

| 风险 | 缓解措施 |
|------|---------|
| EventScope 与原生事件并行导致双重触发 | 兼容模式开关 |
| MainForm 双重订阅 | 只用一个 CurrentAndChildren，ScopeId 区分 |
| 队列异步时序问题 | Task.Delay 或等待队列完成 |
