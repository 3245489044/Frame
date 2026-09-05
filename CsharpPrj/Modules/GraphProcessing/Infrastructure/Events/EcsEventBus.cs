
#nullable enable

using ECS.Core;
using TConfig.ECS;

namespace Modules.GraphProcessing.Infrastructure.Events
{
    public abstract class EcsEventArgs
    {
        public DateTime Timestamp { get; } = DateTime.UtcNow;

        public Entity? Entity { get; }

        protected EcsEventArgs(Entity? entity = null)
        {
            Entity = entity;
        }
    }

    public class EntityCreatedEventArgs : EcsEventArgs
    {
        public EntityCreatedEventArgs(Entity entity) : base(entity) { }
    }

    public class EntityDestroyedEventArgs : EcsEventArgs
    {
        public EntityDestroyedEventArgs(Entity entity) : base(entity) { }
    }

    public class ComponentChangedEventArgs : EcsEventArgs
    {
        public Type ComponentType { get; }
        public string ChangeType { get; }

        public ComponentChangedEventArgs(Entity entity, Type componentType, string changeType)
            : base(entity)
        {
            ComponentType = componentType;
            ChangeType = changeType;
        }
    }

    public class EcsEventBus : IDisposable
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly object _lock = new();
        private bool _disposed;

        public void Subscribe<TEventArgs>(Action<TEventArgs> handler)
            where TEventArgs : EcsEventArgs
        {
            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(TEventArgs), out var list))
                {
                    list = new List<Delegate>();
                    _handlers[typeof(TEventArgs)] = list;
                }
                list.Add(handler);
            }
        }

        public void Unsubscribe<TEventArgs>(Action<TEventArgs> handler)
            where TEventArgs : EcsEventArgs
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(typeof(TEventArgs), out var list))
                {
                    list.Remove(handler);
                }
            }
        }

        public void Publish<TEventArgs>(TEventArgs args)
            where TEventArgs : EcsEventArgs
        {
            List<Delegate>? snapshot;
            lock (_lock)
            {
                if (!_handlers.TryGetValue(typeof(TEventArgs), out var list))
                    return;
                snapshot = new List<Delegate>(list);
            }

            foreach (var handler in snapshot)
            {
                if (handler is Action<TEventArgs> action)
                {
                    try
                    {
                        action(args);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    {

                    }
                }

            }
        }

        public void BridgeToEntityManager(EntityManager entityManager)
        {
            entityManager.EntityCreated += entity =>
                Publish(new EntityCreatedEventArgs(entity));

            entityManager.EntityDestroyed += entity =>
                Publish(new EntityDestroyedEventArgs(entity));

            entityManager.ComponentAdded += (entity, type) =>
                Publish(new ComponentChangedEventArgs(entity, type, "Added"));

            entityManager.ComponentRemoved += (entity, type) =>
                Publish(new ComponentChangedEventArgs(entity, type, "Removed"));

            entityManager.ComponentUpdated += (entity, type, _, _) =>
                Publish(new ComponentChangedEventArgs(entity, type, "Updated"));
        }

        public void Clear()
        {
            lock (_lock)
            {
                _handlers.Clear();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Clear();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
