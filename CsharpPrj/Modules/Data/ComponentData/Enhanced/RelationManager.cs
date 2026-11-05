using ECS.Core;
using FrameworkAbstractions.ECS;
using Modules.ComponentData.Lifecycle;
using BasicMethod.ContextFrame.Locking;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Modules.ComponentData.Enhanced
{
    public class RelationManager : IDisposable
    {
        private readonly struct RelationFullKey : IEquatable<RelationFullKey>
        {
            public readonly Entity Src;
            public readonly Entity Tgt;
            public readonly RelationKey Key;
            public RelationFullKey(Entity src, Entity tgt, RelationKey key) { Src = src; Tgt = tgt; Key = key; }
            public bool Equals(RelationFullKey other) => Src.Equals(other.Src) && Tgt.Equals(other.Tgt) && Key.Equals(other.Key);
            public override bool Equals(object? obj) => obj is RelationFullKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Src, Tgt, Key);
        }

        private readonly EntityManager _em;
        private readonly ReaderWriterLockSlim _globalLock;
        private readonly Dictionary<RelationFullKey, Entity> _relMap = new();
        private readonly Dictionary<Entity, RelationFullKey> _relToKey = new();
        private static readonly ConditionalWeakTable<EntityManager, RelationIndex<RelationKey>> _cores = new();
        private static readonly ConditionalWeakTable<EntityManager, RelationManager> _instances = new();
        private readonly RelationIndex<RelationKey> _core;
        private readonly ComponentLifecycleManager _lifecycleManager;
        private readonly HashSet<Type> _registeredHandlerTypes = new();
        private readonly ConcurrentDictionary<Entity, byte> _pendingDestroy = new();

        public RelationManager(EntityManager em, ReaderWriterLockSlim globalLock, ComponentLifecycleManager lifecycleManager)
        {
            if (!_instances.TryAdd(em, this))
                throw new InvalidOperationException("Only one RelationManager per EntityManager is allowed.");
            _em = em;
            _globalLock = globalLock;
            _lifecycleManager = lifecycleManager;
            _core = _cores.GetValue(em, _ => new());
            _em.EntityDestroyed += OnEntityDestroyed;
        }

        private void EnsureHandlerRegistered<TRelation>() where TRelation : Enum
        {
            var type = typeof(TRelation);
            lock (_registeredHandlerTypes)
                if (_registeredHandlerTypes.Add(type))
                    _lifecycleManager.RegisterHandler(new RelationComponentHandler<TRelation>(this), ExecutionMode.Sync);
        }

        private class RelationComponentHandler<TRelation> : IComponentLifecycleHandler<Relation<TRelation>> where TRelation : Enum
        {
            private readonly RelationManager _rm;
            public RelationComponentHandler(RelationManager rm) => _rm = rm;
            public void OnAdded(Entity entity, Relation<TRelation> component) { }
            public void OnRemoved(Entity entity, Relation<TRelation> component) => _rm.HandleRelationComponentRemoved(entity, component);
            public void OnChanged(Entity entity, Relation<TRelation> newComponent, Relation<TRelation> oldComponent) { }
        }

        private void HandleRelationComponentRemoved<TRelation>(Entity entity, Relation<TRelation> component) where TRelation : Enum
        {
            if (_pendingDestroy.ContainsKey(entity))
                return;

            var fullKey = new RelationFullKey(component.Source, component.Target, new(component.Type));
            using (var writeLock = Locks.Write(_globalLock))
            {
                if (_relMap.TryGetValue(fullKey, out var rel) && rel == entity)
                {
                    _relMap.Remove(fullKey);
                    _relToKey.Remove(rel);
                }
                _core.Remove(component.Source, component.Target, new(component.Type));
            }
        }

        public void AddRelation<TRelation>(Entity src, Entity tgt, TRelation type) where TRelation : Enum
        {
            if (!_em.IsEntityValid(src) || !_em.IsEntityValid(tgt))
                throw new ArgumentException("Invalid entity");

            EnsureHandlerRegistered<TRelation>();

            var key = new RelationKey(type);
            var fullKey = new RelationFullKey(src, tgt, key);

            using (var globalWrite = Locks.Write(_globalLock))
            {
                if (_relMap.ContainsKey(fullKey))
                    return;

                var rel = _em.CreateEntity();

                try
                {
                    _core.Add(src, tgt, key);
                    _relMap[fullKey] = rel;
                    _relToKey[rel] = fullKey;
                    _em.AddComponent(rel, new Relation<TRelation> { Source = src, Target = tgt, Type = type });
                }
                catch
                {
                    _core.Remove(src, tgt, key);
                    _relMap.Remove(fullKey);
                    _relToKey.Remove(rel);
                    if (rel.IsValid)
                        _em.DestroyEntity(rel);
                    throw;
                }
            }
        }

        public bool RemoveRelation<TRelation>(Entity src, Entity tgt, TRelation type) where TRelation : Enum
        {
            var fullKey = new RelationFullKey(src, tgt, new(type));
            Entity rel = default;

            using (var writeLock = Locks.Write(_globalLock))
            {
                if (_relMap.TryGetValue(fullKey, out rel) && rel.IsValid)
                {
                    _core.Remove(src, tgt, new(type));
                    _relMap.Remove(fullKey);
                    _relToKey.Remove(rel);
                }
                else
                    return false;
            }

            if (rel.IsValid)
                _em.DestroyEntity(rel);
            return true;
        }

        public void RemoveAllRelations(Entity e)
        {
            if (_globalLock.IsWriteLockHeld)
            {
                RemoveAllRelationsCore(e);
            }
            else
            {
                using (var upgradeable = Locks.UpgradeableRead(_globalLock))
                using (var globalWrite = Locks.Write(_globalLock))
                {
                    RemoveAllRelationsCore(e);
                }
            }
        }

        private void RemoveAllRelationsCore(Entity e)
        {
            List<Entity> toDestroy = new();
            HashSet<Entity> visited = new();

            var queue = new Queue<Entity>();
            queue.Enqueue(e);
            visited.Add(e);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var relations = _core.GetAllRelations(current);
                foreach (var (src, tgt, key) in relations)
                {
                    var fullKey = new RelationFullKey(src, tgt, key);
                    if (_relMap.TryGetValue(fullKey, out var rel) && visited.Add(rel))
                        queue.Enqueue(rel);
                }
                if (current != e)
                    toDestroy.Add(current);
            }

            foreach (var ent in visited)
            {
                if (_relToKey.TryGetValue(ent, out var fullKey))
                {
                    _core.Remove(fullKey.Src, fullKey.Tgt, fullKey.Key);
                    _relMap.Remove(fullKey);
                    _relToKey.Remove(ent);
                }
                _core.RemoveAll(ent);
            }

            foreach (var ent in visited)
                _pendingDestroy.TryAdd(ent, 0);

            foreach (var ent in toDestroy)
            {
                if (ent.IsValid)
                    _em.DestroyEntity(ent);
            }
        }

        public IReadOnlyList<Entity> GetRelatedTargets<TRelation>(Entity src, TRelation type) where TRelation : Enum
        {
            using (var globalRead = Locks.Read(_globalLock))
                return _core.GetTargets(src, new(type));
        }

        public IReadOnlyList<Entity> GetRelatedSources<TRelation>(Entity tgt, TRelation type) where TRelation : Enum
        {
            using (var globalRead = Locks.Read(_globalLock))
                return _core.GetSources(tgt, new(type));
        }

        public bool HasRelation<TRelation>(Entity src, Entity tgt, TRelation type) where TRelation : Enum
        {
            using (var globalRead = Locks.Read(_globalLock))
                return _core.Has(src, tgt, new(type));
        }

        private struct Relation<TRelation> : IComponent where TRelation : Enum
        {
            public Entity Source, Target;
            public TRelation Type;
        }

        public IReadOnlyList<(RelationKey Key, Entity Target)> GetForwardRelations(Entity entity)
        {
            using (var globalRead = Locks.Read(_globalLock))
            {
                var all = _core.GetAllRelations(entity);
                return all.Where(r => r.src.Equals(entity))
                    .Select(r => (r.type, r.tgt))
                    .ToList();
            }
        }

        public IReadOnlyList<(RelationKey Key, Entity Source)> GetReverseRelations(Entity entity)
        {
            using (var globalRead = Locks.Read(_globalLock))
            {
                var all = _core.GetAllRelations(entity);
                return all.Where(r => r.tgt.Equals(entity))
                    .Select(r => (r.type, r.src))
                    .ToList();
            }
        }

        private void OnEntityDestroyed(Entity entity)
        {
            if (_pendingDestroy.TryRemove(entity, out _))
                return;
            RemoveAllRelations(entity);
        }

        public void Dispose()
        {
            _em.EntityDestroyed -= OnEntityDestroyed;
        }
    }
}
