using ECS.Core;
using FrameworkAbstractions.ECS;
using Modules.ComponentData.Lifecycle;
using BasicMethod.ContextFrame.Locking;
using System.Collections.Concurrent;
using System.Reflection;

namespace Modules.ComponentData.Enhanced
{
    public class NotificationCenter : IDisposable
    {
        private readonly EntityManager _em;
        private readonly SynchronizationContext _uiCtx;
        private readonly Dictionary<Type, List<Subscription>> _addedSubs = new();
        private readonly Dictionary<Type, List<Subscription>> _removedSubs = new();
        private readonly ReaderWriterLockSlim _subsLock = new();
        private readonly ConcurrentDictionary<Type, object> _handlerMap = new();
        private readonly ComponentLifecycleManager _lifecycleManager;

        private static readonly MethodInfo _registerHandlerMethod = typeof(ComponentLifecycleManager)
            .GetMethod(nameof(ComponentLifecycleManager.RegisterHandler))!;
        private static readonly MethodInfo _unregisterHandlerMethod = typeof(ComponentLifecycleManager)
            .GetMethod(nameof(ComponentLifecycleManager.UnregisterHandler))!;

        public NotificationCenter(EntityManager em, ComponentLifecycleManager lifecycleManager, SynchronizationContext? uiCtx = null)
        {
            _em = em;
            _lifecycleManager = lifecycleManager;
            _uiCtx = uiCtx ?? SynchronizationContext.Current ?? new ThreadPoolSynchronizationContext();
        }

        public void SubscribeAdded<T>(Action<Entity, T> handler, ExecutionMode ctx = ExecutionMode.Sync) where T : struct, IComponent =>
            AddSub(typeof(T), EventType.Added, handler, args => { var (e, c) = ((Entity, object))args; handler(e, (T)c); }, ctx);

        public void SubscribeRemoved<T>(Action<Entity> handler, ExecutionMode ctx = ExecutionMode.Sync) where T : struct, IComponent =>
            AddSub(typeof(T), EventType.Removed, handler, args => handler((Entity)args), ctx);

        public void UnsubscribeAdded<T>(Action<Entity, T> handler) where T : struct, IComponent =>
            RemoveSub(typeof(T), EventType.Added, handler);

        public void UnsubscribeRemoved<T>(Action<Entity> handler) where T : struct, IComponent =>
            RemoveSub(typeof(T), EventType.Removed, handler);

        private void AddSub(Type t, EventType ev, Delegate handler, Action<object> invoke, ExecutionMode ctx)
        {
            var dict = ev == EventType.Added ? _addedSubs : _removedSubs;
            var sub = new Subscription(ev, handler, ctx, invoke);

using (Locks.Write(_subsLock))
            {
                if (!dict.TryGetValue(t, out var list)) dict[t] = list = new();
                list.Add(sub);

                if (!_handlerMap.ContainsKey(t))
                {
                    var handlerType = typeof(NotificationHandler<>).MakeGenericType(t);
                    var handlerInstance = Activator.CreateInstance(handlerType, this);
                    _handlerMap[t] = handlerInstance;
                    _registerHandlerMethod
                        .MakeGenericMethod(t)
                        .Invoke(_lifecycleManager, new[] { handlerInstance, ExecutionMode.Sync });
                }
            }
        }

        private void RemoveSub(Type t, EventType ev, Delegate handler)
        {
            var dict = ev == EventType.Added ? _addedSubs : _removedSubs;
            using (Locks.Write(_subsLock))
            {
                if (dict.TryGetValue(t, out var list))
                {
                    list.RemoveAll(s => s.Handler == handler);
                    if (list.Count == 0) dict.Remove(t);
                }

                bool addedEmpty = !_addedSubs.TryGetValue(t, out var addList) || addList.Count == 0;
                bool removedEmpty = !_removedSubs.TryGetValue(t, out var remList) || remList.Count == 0;
                if (addedEmpty && removedEmpty && _handlerMap.TryRemove(t, out var handlerInstance))
                {
                    _unregisterHandlerMethod
                        .MakeGenericMethod(t)
                        .Invoke(_lifecycleManager, new[] { handlerInstance });
                }
            }
        }

        private Subscription[] GetAddedSubscriptions(Type type)
        {
using var _ = Locks.Read(_subsLock);
            return _addedSubs.TryGetValue(type, out var list) ? list.ToArray() : Array.Empty<Subscription>();
        }

        private Subscription[] GetRemovedSubscriptions(Type type)
        {
            using var _ = Locks.Read(_subsLock);
            return _removedSubs.TryGetValue(type, out var list) ? list.ToArray() : Array.Empty<Subscription>();
        }

        private class NotificationHandler<T> : IComponentLifecycleHandler<T> where T : struct, IComponent
        {
            private readonly NotificationCenter _nc;
            public NotificationHandler(NotificationCenter nc) => _nc = nc;
            public void OnAdded(Entity entity, T component)
            {
                var subs = _nc.GetAddedSubscriptions(typeof(T));
                _nc.Invoke(subs, (entity, (object)component));
            }
            public void OnRemoved(Entity entity, T component)
            {
                var subs = _nc.GetRemovedSubscriptions(typeof(T));
                _nc.Invoke(subs, entity);
            }
            public void OnChanged(Entity entity, T newComponent, T oldComponent) { }
        }

        private void Invoke(IEnumerable<Subscription> subs, object args)
        {
            foreach (var s in subs)
                try
                {
                    switch (s.Context)
                    {
                        case ExecutionMode.Sync: s.Invoke(args); break;
                        case ExecutionMode.ThreadPool: ThreadPool.QueueUserWorkItem(_ => s.Invoke(args)); break;
                        case ExecutionMode.UI: _uiCtx.Post(_ => s.Invoke(args), args); break;
                    }
                }
                catch { }
        }

        private enum EventType { Added, Removed }

        private record Subscription(EventType Type, Delegate Handler, ExecutionMode Context, Action<object> Invoke);

        public void Dispose() => _subsLock.Dispose();
    }
}
