
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;
using FrameworkAbstractions.ECS;
using Modules.ComponentData.Reflection;
using TConfig.ECS;

namespace Modules.ComponentData.Lifecycle
{

    public interface IExecutionStrategy
    {
        void Execute(Action action, ExecutionMode mode);
    }

    public class ComponentEventSourceAdapter
    {
        readonly IComponentEventSource _source;
        readonly IGenericComponentEventSource? _genericSource;
        static readonly ConcurrentDictionary<Type, Func<IComponentEventSource, Entity, object>> _getterCache = new();

        public ComponentEventSourceAdapter(IComponentEventSource source) => (_source, _genericSource) = (source ?? throw new ArgumentNullException(nameof(source)), source as IGenericComponentEventSource);

        public event Action<Entity, Type> ComponentAdded { add => _source.ComponentAdded += value; remove => _source.ComponentAdded -= value; }
        public event Action<Entity, Type> ComponentRemoved { add => _source.ComponentRemoved += value; remove => _source.ComponentRemoved -= value; }
        public event Action<Entity, Type, object?, object> ComponentUpdated { add => _source.ComponentUpdated += value; remove => _source.ComponentUpdated -= value; }

        public object? GetComponent(Entity entity, Type componentType)
        {
            var getter = _getterCache.GetOrAdd(componentType, t =>
            {
                var desc = new InvocationDescriptor
                {
                    TargetType = typeof(IComponentEventSource),
                    MemberName = nameof(IComponentEventSource.GetComponent),
                    MemberKind = MemberKind.Method,
                    GenericArguments = new[] { t },
                    ParameterTypes = new[] { typeof(Entity) },
                    IsStatic = false
                };
                return (Func<IComponentEventSource, Entity, object>)DelegateCompiler.GetOrCompile(
                    desc, typeof(Func<IComponentEventSource, Entity, object>));
            });
            return getter(_source, entity);
        }

        public IGenericComponentEventSource? AsGeneric() => _genericSource;
    }

    public class DefaultExecutionStrategy : IExecutionStrategy
    {
        readonly SynchronizationContext _uiContext;
        public DefaultExecutionStrategy(SynchronizationContext uiContext) => _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        public void Execute(Action action, ExecutionMode mode)
        {
            void Safe() { try { action(); } catch { } }
            switch (mode)
            {
                case ExecutionMode.Sync: Safe(); break;
                case ExecutionMode.ThreadPool: ThreadPool.QueueUserWorkItem(_ => Safe()); break;
                case ExecutionMode.UI: _uiContext.Post(_ => Safe(), null); break;
            }
        }
    }

    public class ComponentLifecycleManager : IDisposable
    {

        readonly ComponentEventSourceAdapter _adapter;
        readonly bool _disposeHandlersOnShutdown;
        readonly IExecutionStrategy _executor;
        readonly Dictionary<Type, ImmutableList<HandlerEntry>> _handlers = new();
        readonly ReaderWriterLockSlim _lock = new();
        readonly ConcurrentDictionary<Type, MethodDelegates> _handlerMethodDelegates = new();
        readonly Dictionary<Type, SubscriptionInfo> _genericSubscriptionInfos = new();
        readonly HashSet<Type> _genericSubscribedTypes = new();
        readonly ConcurrentDictionary<Type, IComponentTypeCache> _componentCaches = new();
        int _executingCount;
        bool _subscribed;
        bool _disposed;

        interface IComponentTypeCache { object? Get(Entity entity); void Set(Entity entity, object component); bool Remove(Entity entity); }
        class ComponentTypeCache<T> : IComponentTypeCache, IDisposable where T : struct
        {
            readonly Dictionary<Entity, T> _cache = new();
            readonly ReaderWriterLockSlim _lock = new();
            public object? Get(Entity entity) { _lock.EnterReadLock(); try { return _cache.TryGetValue(entity, out var v) ? v : null; } finally { _lock.ExitReadLock(); } }
            public void Set(Entity entity, T component) { _lock.EnterWriteLock(); try { _cache[entity] = component; } finally { _lock.ExitWriteLock(); } }
            void IComponentTypeCache.Set(Entity entity, object component) => Set(entity, (T)component);
            public bool Remove(Entity entity) { _lock.EnterWriteLock(); try { return _cache.Remove(entity); } finally { _lock.ExitWriteLock(); } }
            public void Dispose() => _lock.Dispose();
        }

        record HandlerEntry(object Handler, ExecutionMode Mode);
        record SubscriptionInfo(Type ComponentType, Delegate Added, Delegate Removed, Delegate Updated);
        record MethodDelegates(Action<object, Entity, object> Added, Action<object, Entity, object> Removed, Action<object, Entity, object, object> Changed);

        static readonly MethodInfo OnAddedMethodDef = typeof(IComponentLifecycleHandler<>).GetMethod("OnAdded", BindingFlags.Public | BindingFlags.Instance)!;
        static readonly MethodInfo OnRemovedMethodDef = typeof(IComponentLifecycleHandler<>).GetMethod("OnRemoved", BindingFlags.Public | BindingFlags.Instance)!;
        static readonly MethodInfo OnChangedMethodDef = typeof(IComponentLifecycleHandler<>).GetMethod("OnChanged", BindingFlags.Public | BindingFlags.Instance)!;

        public ComponentLifecycleManager(ComponentEventSourceAdapter adapter, bool disposeHandlersOnShutdown = false, IExecutionStrategy? executor = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _disposeHandlersOnShutdown = disposeHandlersOnShutdown;
            _executor = executor ?? new DefaultExecutionStrategy(SynchronizationContext.Current ?? new SynchronizationContext());
        }
        public ComponentLifecycleManager(IComponentEventSource eventSource, SynchronizationContext? uiContext = null, bool disposeHandlersOnShutdown = false)
            : this(new ComponentEventSourceAdapter(eventSource), disposeHandlersOnShutdown, uiContext == null ? null : new DefaultExecutionStrategy(uiContext)) { }
        public ComponentLifecycleManager(IGenericComponentEventSource eventSource, SynchronizationContext? uiContext = null, bool disposeHandlersOnShutdown = false)
            : this(new ComponentEventSourceAdapter(eventSource), disposeHandlersOnShutdown, uiContext == null ? null : new DefaultExecutionStrategy(uiContext)) { }

        public void RegisterHandler<T>(IComponentLifecycleHandler<T> handler, ExecutionMode mode = ExecutionMode.Sync) where T : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            CheckDisposed();
            var type = typeof(T);
            _lock.EnterWriteLock();
            try
            {
                _handlers[type] = (_handlers.TryGetValue(type, out var list) ? list : ImmutableList<HandlerEntry>.Empty).Add(new(handler, mode));
                if (_handlers[type].Count == 1 && _adapter.AsGeneric() is { } generic)
                {
                    var (added, removed, updated) = (new Action<Entity, T>(OnAddedGeneric), new Action<Entity, T>(OnRemovedGeneric), new Action<Entity, T, T>(OnUpdatedGeneric));
                    generic.SubscribeAdded(added); generic.SubscribeRemoved(removed); generic.SubscribeUpdated(updated);
                    _genericSubscriptionInfos[type] = new(type, added, removed, updated);
                    _genericSubscribedTypes.Add(type);
                }
                if (!_subscribed)
                {
                    _adapter.ComponentAdded += OnComponentAddedNonGeneric;
                    _adapter.ComponentRemoved += OnComponentRemovedNonGeneric;
                    _adapter.ComponentUpdated += OnComponentUpdatedNonGeneric;
                    _subscribed = true;
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void UnregisterHandler<T>(IComponentLifecycleHandler<T> handler) where T : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            CheckDisposed();
            if (!_subscribed) return;
            var type = typeof(T);
            _lock.EnterWriteLock();
            try
            {
                if (!_handlers.TryGetValue(type, out var list)) return;
                var newList = list.RemoveAll(e => ReferenceEquals(e.Handler, handler));
                if (newList.IsEmpty)
                {
                    _handlers.Remove(type);
                    _genericSubscribedTypes.Remove(type);
                    if (_genericSubscriptionInfos.TryGetValue(type, out var info) && _adapter.AsGeneric() is { } generic)
                    {
                        if (info.Added is Action<Entity, T> a) generic.UnsubscribeAdded(a);
                        if (info.Removed is Action<Entity, T> r) generic.UnsubscribeRemoved(r);
                        if (info.Updated is Action<Entity, T, T> u) generic.UnsubscribeUpdated(u);
                        _genericSubscriptionInfos.Remove(type);
                    }
                }
                else _handlers[type] = newList;
            }
            finally { _lock.ExitWriteLock(); }
        }

        void OnAddedGeneric<T>(Entity entity, T component) where T : struct { if (_disposed) return; GetCacheForType<T>().Set(entity, component); ForEachHandler(entity, component, (h, e, c) => h.OnAdded(e, c)); }
        void OnRemovedGeneric<T>(Entity entity, T component) where T : struct { if (_disposed) return; GetCacheForType<T>().Remove(entity); ForEachHandler(entity, component, (h, e, c) => h.OnRemoved(e, c)); }
        void OnUpdatedGeneric<T>(Entity entity, T newComponent, T oldComponent) where T : struct { if (_disposed) return; GetCacheForType<T>().Set(entity, newComponent); ForEachHandler(entity, newComponent, (h, e, _) => h.OnChanged(e, newComponent, oldComponent)); }

        void ForEachHandler<T>(Entity entity, T component, Action<IComponentLifecycleHandler<T>, Entity, T> invoke) where T : struct
        {
            if (TryGetHandlers(typeof(T)) is not { } list) return;
            foreach (var e in list)
                if (e.Handler is IComponentLifecycleHandler<T> h)
                {
                    Interlocked.Increment(ref _executingCount);
                    _executor.Execute(() => { try { invoke(h, entity, component); } finally { Interlocked.Decrement(ref _executingCount); } }, e.Mode);
                }
        }

        void OnComponentAddedNonGeneric(Entity entity, Type type)
        {
            if (_disposed) return;
            var (handlers, isGeneric) = GetHandlersAndGenericFlag(type);
            if (isGeneric || handlers == null || handlers.IsEmpty) return;
            if (_adapter.GetComponent(entity, type) is not { } comp) return;
            UpdateCache(entity, type, comp);
            InvokeHandlers(entity, type, comp, null, true);
        }

        void OnComponentRemovedNonGeneric(Entity entity, Type type)
        {
            if (_disposed) return;
            var (handlers, isGeneric) = GetHandlersAndGenericFlag(type);
            if (isGeneric || handlers == null || handlers.IsEmpty) return;
            object? old = TryGetFromCache(entity, type);
            if (old == null) return;
            RemoveFromCache(entity, type);
            InvokeHandlers(entity, type, old, null, false);
        }

        void OnComponentUpdatedNonGeneric(Entity entity, Type type, object? oldComponent, object? newComponent)
        {
            if (_disposed || newComponent == null) return;
            var (handlers, isGeneric) = GetHandlersAndGenericFlag(type);
            if (isGeneric || handlers == null || handlers.IsEmpty) return;
            var cachedOld = oldComponent ?? TryGetFromCache(entity, type);
            UpdateCache(entity, type, newComponent);
            InvokeHandlers(entity, type, newComponent, cachedOld, true);
        }

        (ImmutableList<HandlerEntry>? handlers, bool isGeneric) GetHandlersAndGenericFlag(Type type)
        {
            _lock.EnterReadLock();
            try
            {
                return (_handlers.TryGetValue(type, out var l) ? l : null, _genericSubscribedTypes.Contains(type));
            }
            finally { _lock.ExitReadLock(); }
        }

        void InvokeHandlers(Entity entity, Type type, object component, object? oldComponent, bool isAddOrUpdate)
        {
            var list = TryGetHandlers(type);
            if (list == null) return;
            var del = GetCachedDelegates(type);
            foreach (var e in list)
            {
                Interlocked.Increment(ref _executingCount);
                _executor.Execute(() =>
                {
                    try
                    {
                        if (isAddOrUpdate)
                            if (oldComponent == null) del.Added(e.Handler, entity, component);
                            else del.Changed(e.Handler, entity, component, oldComponent);
                        else del.Removed(e.Handler, entity, component);
                    }
                    finally { Interlocked.Decrement(ref _executingCount); }
                }, e.Mode);
            }
        }

        ComponentTypeCache<T> GetCacheForType<T>() where T : struct => (ComponentTypeCache<T>)_componentCaches.GetOrAdd(typeof(T), _ => new ComponentTypeCache<T>());
        IComponentTypeCache CreateCache(Type type) => (IComponentTypeCache)Activator.CreateInstance(typeof(ComponentTypeCache<>).MakeGenericType(type))!;
        void UpdateCache(Entity entity, Type type, object component) => _componentCaches.GetOrAdd(type, CreateCache).Set(entity, component);
        object? TryGetFromCache(Entity entity, Type type) => _componentCaches.TryGetValue(type, out var c) ? c.Get(entity) : null;
        void RemoveFromCache(Entity entity, Type type) { if (_componentCaches.TryGetValue(type, out var c)) c.Remove(entity); }

        ImmutableList<HandlerEntry>? TryGetHandlers(Type type)
        {
            if (_disposed) return null;
            _lock.EnterReadLock();
            try
            {
                return _handlers.TryGetValue(type, out var l) ? l : null;
            }
            finally { _lock.ExitReadLock(); }
        }

        MethodDelegates GetCachedDelegates(Type type) => _handlerMethodDelegates.GetOrAdd(type, t =>
        {
            var handlerType = typeof(IComponentLifecycleHandler<>).MakeGenericType(t);

            var addedDesc = new InvocationDescriptor
            {
                TargetType = handlerType,
                MemberName = "OnAdded",
                MemberKind = MemberKind.Method,
                ParameterTypes = new[] { typeof(Entity), t },
                IsStatic = false
            };
            var added = (Action<object, Entity, object>)DelegateCompiler.GetOrCompile(
                addedDesc, typeof(Action<object, Entity, object>));

            var removedDesc = new InvocationDescriptor
            {
                TargetType = handlerType,
                MemberName = "OnRemoved",
                MemberKind = MemberKind.Method,
                ParameterTypes = new[] { typeof(Entity), t },
                IsStatic = false
            };
            var removed = (Action<object, Entity, object>)DelegateCompiler.GetOrCompile(
                removedDesc, typeof(Action<object, Entity, object>));

            var changedDesc = new InvocationDescriptor
            {
                TargetType = handlerType,
                MemberName = "OnChanged",
                MemberKind = MemberKind.Method,
                ParameterTypes = new[] { typeof(Entity), t, t },
                IsStatic = false
            };
            var changed = (Action<object, Entity, object, object>)DelegateCompiler.GetOrCompile(
                changedDesc, typeof(Action<object, Entity, object, object>));

            return new MethodDelegates(added, removed, changed);
        });

        void CheckDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(ComponentLifecycleManager)); }

        void UnsubscribeGenericEvents(IGenericComponentEventSource generic)
        {
            foreach (var kv in _genericSubscriptionInfos)
                try
                {
                    typeof(IGenericComponentEventSource).GetMethod("UnsubscribeAdded")!.MakeGenericMethod(kv.Key).Invoke(generic, new[] { kv.Value.Added });
                    typeof(IGenericComponentEventSource).GetMethod("UnsubscribeRemoved")!.MakeGenericMethod(kv.Key).Invoke(generic, new[] { kv.Value.Removed });
                    typeof(IGenericComponentEventSource).GetMethod("UnsubscribeUpdated")!.MakeGenericMethod(kv.Key).Invoke(generic, new[] { kv.Value.Updated });
                }
                catch { }
            _genericSubscriptionInfos.Clear(); _genericSubscribedTypes.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lock.EnterWriteLock();
            try
            {
                if (_subscribed)
                {
                    _adapter.ComponentAdded -= OnComponentAddedNonGeneric;
                    _adapter.ComponentRemoved -= OnComponentRemovedNonGeneric;
                    _adapter.ComponentUpdated -= OnComponentUpdatedNonGeneric;
                    _subscribed = false;
                }
                if (_adapter.AsGeneric() is { } generic) UnsubscribeGenericEvents(generic);
            }
            finally { _lock.ExitWriteLock(); }
            var spinWait = new SpinWait();
            for (int retries = 0; retries < 5000; retries++)
            {
                if (Interlocked.CompareExchange(ref _executingCount, 0, 0) == 0)
                    break;
                spinWait.SpinOnce();
            }
            var disposables = new List<IDisposable>();
            _lock.EnterWriteLock();
            try
            {
                if (_disposeHandlersOnShutdown)
                    foreach (var list in _handlers.Values)
                        foreach (var e in list)
                            if (e.Handler is IDisposable d) disposables.Add(d);
                foreach (var c in _componentCaches.Values) if (c is IDisposable d) disposables.Add(d);
                _handlers.Clear(); _componentCaches.Clear(); _handlerMethodDelegates.Clear();
            }
            finally { _lock.ExitWriteLock(); }
            foreach (var d in disposables) try { d.Dispose(); } catch { }
            _lock.Dispose();
        }
    }
}
