using ECS.Core;
using FrameworkAbstractions.ECS;
using Modules.ComponentData.Lifecycle;
using BasicMethod.ContextFrame.Locking;
using System.Collections.Concurrent;

namespace Modules.ComponentData.Enhanced
{
    public class IndexManager : IDisposable
    {
        private readonly EntityManager _em;
        private readonly ReaderWriterLockSlim _globalLock;
        private readonly ReaderWriterLockSlim _indexLock = new();
        private readonly Dictionary<Type, IIndex> _idx = new();
        private readonly ComponentLifecycleManager _lifecycleManager;
        private readonly ConcurrentQueue<Action> _deferredRemovals = new();

        public IndexManager(EntityManager em, ReaderWriterLockSlim globalLock, ComponentLifecycleManager lifecycleManager)
        {
            _em = em;
            _globalLock = globalLock;
            _lifecycleManager = lifecycleManager;
        }

        public void ProcessDeferredRemovals()
        {
            while (_deferredRemovals.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[IndexManager] Deferred removal error: {ex.Message}");
                }
            }
        }

        public void FlushDeferredRemovals()
        {
            ProcessDeferredRemovals();
        }

        public void RegisterIndex<T>(Func<T, object> keySelector, bool unique = false) where T : struct, IComponent
        {
            var index = new Index<T>(keySelector, unique);
            var handler = new IndexHandler<T>(this, index);

            _lifecycleManager.RegisterHandler(handler, ExecutionMode.Sync);

using (var writeLock = Locks.Write(_indexLock))
            {
                if (_idx.ContainsKey(typeof(T)))
                    throw new InvalidOperationException($"Index already registered for {typeof(T).Name}");
                _idx[typeof(T)] = index;
            }

            try
            {
using (var globalWrite = Locks.Write(_globalLock))
                {
                    index.Rebuild(_em);
                }
            }
            catch
            {
                using (var writeLock = Locks.Write(_indexLock))
                {
                    _idx.Remove(typeof(T));
                }
                try { _lifecycleManager.UnregisterHandler(handler); }
                catch (Exception unregEx)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"IndexManager.RegisterIndex: Failed to unregister handler for {typeof(T).Name}: {unregEx.Message}");
                }
                throw;
            }
        }

        private class IndexHandler<T> : IComponentLifecycleHandler<T> where T : struct, IComponent
        {
            private readonly IndexManager _im;
            private readonly Index<T> _index;
            public IndexHandler(IndexManager im, Index<T> index) => (_im, _index) = (im, index);

            public void OnAdded(Entity entity, T component)
            {
                object? key;
using (var globalRead = Locks.Read(_im._globalLock))
                {
                    key = _index.GetKeyFromEntity(entity, _im._em);
                }

                if (key != null)
                {
                    bool needRemove = false;
using (var indexWrite = Locks.Write(_im._indexLock))
                    {
                        try
                        {
                            if (!_index.TryAddFromEntity(entity, key))
                                needRemove = true;
                        }
                        catch
                        {
                            needRemove = true;
                        }
                    }

                    if (needRemove)
                    {
                        _im._deferredRemovals.Enqueue(() => _im._em.RemoveComponent<T>(entity));
                    }
                }
            }

            public void OnRemoved(Entity entity, T component)
            {
                using (var indexWrite = Locks.Write(_im._indexLock))
                    _index.RemoveEntity(entity);
            }

            public void OnChanged(Entity entity, T newComponent, T oldComponent) { }
        }

        public Entity FindEntity<T>(object key) where T : struct, IComponent
        {
using (var indexRead = Locks.Read(_indexLock))
            {
                var idx = GetIndex<T>();
                using (var globalRead = Locks.Read(_globalLock))
                {
                    return idx.FindEntity(key);
                }
            }
        }

        public IReadOnlyList<Entity> FindEntities<T>(object key) where T : struct, IComponent
        {
            using (var indexRead = Locks.Read(_indexLock))
            {
                var idx = GetIndex<T>();
                using (var globalRead = Locks.Read(_globalLock))
                {
                    return idx.FindEntities(key);
                }
            }
        }

        public bool Contains<T>(object key) where T : struct, IComponent
        {
            using (var indexRead = Locks.Read(_indexLock))
            {
                return _idx.TryGetValue(typeof(T), out var i) && ((Index<T>)i).Contains(key);
            }
        }

        public bool TryCleanKey<T>(object key) where T : struct, IComponent
        {
using (var indexWrite = Locks.Write(_indexLock))
            using (var globalRead = Locks.Read(_globalLock))
            {
                return GetIndex<T>().TryCleanKey(key, _em);
            }
        }

        public bool TryCleanKey<T>(object key, Entity invalidEntity) where T : struct, IComponent
        {
            using (var indexWrite = Locks.Write(_indexLock))
            using (var globalRead = Locks.Read(_globalLock))
            {
                return GetIndex<T>().TryCleanKey(key, invalidEntity, _em);
            }
        }

        private Index<T> GetIndex<T>() where T : struct, IComponent =>
            _idx.TryGetValue(typeof(T), out var idx) ? (Index<T>)idx : throw new KeyNotFoundException($"No index for {typeof(T).Name}");

        private interface IIndex
        {
            bool IsUnique { get; }
            bool AddFromEntity(Entity e, EntityManager em);
            bool TryAddFromEntity(Entity e, object precomputedKey);
            void RemoveEntity(Entity e);
            object? GetKeyFromEntity(Entity e, EntityManager em);
        }

        private class Index<T> : IIndex where T : struct, IComponent
        {
            private readonly Func<T, object> _keySel;
            public bool IsUnique { get; }
            private readonly Dictionary<object, Entity> _unique = new();
            private readonly Dictionary<object, HashSet<Entity>> _nonUnique = new();
            private readonly Dictionary<Entity, object> _entKey = new();

            public Index(Func<T, object> keySel, bool unique) => (_keySel, IsUnique) = (keySel, unique);

            public bool AddFromEntity(Entity e, EntityManager em) => AddInternal(e, _keySel(em.GetComponent<T>(e)));
            public bool TryAddFromEntity(Entity e, object precomputedKey) => AddInternal(e, precomputedKey);

            private bool AddInternal(Entity e, object key)
            {
                if (IsUnique)
                {
                    if (_unique.ContainsKey(key)) return false;
                    _unique[key] = e;
                }
                else
                {
                    if (!_nonUnique.TryGetValue(key, out var set))
                        _nonUnique[key] = set = new();
                    set.Add(e);
                }
                _entKey[e] = key;
                return true;
            }

            public void RemoveEntity(Entity e)
            {
                if (_entKey.TryGetValue(e, out var key))
                {
                    if (IsUnique) _unique.Remove(key);
                    else if (_nonUnique.TryGetValue(key, out var set) && set.Remove(e) && set.Count == 0)
                        _nonUnique.Remove(key);
                    _entKey.Remove(e);
                }
            }

            public Entity FindEntity(object key) =>
                IsUnique ? _unique.GetValueOrDefault(key) : throw new InvalidOperationException("Index is not unique");

            public IReadOnlyList<Entity> FindEntities(object key) =>
                IsUnique
                    ? _unique.TryGetValue(key, out var e) && e.IsValid ? new[] { e } : Array.Empty<Entity>()
                    : _nonUnique.TryGetValue(key, out var set) ? set.ToList() : Array.Empty<Entity>();

            public bool Contains(object key) => IsUnique ? _unique.ContainsKey(key) : _nonUnique.ContainsKey(key);

            public bool TryCleanKey(object key, EntityManager em)
            {
                if (IsUnique)
                {
                    if (_unique.TryGetValue(key, out var e) && !em.IsEntityValid(e))
                    {
                        _unique.Remove(key);
                        _entKey.Remove(e);
                        return true;
                    }
                    return false;
                }
                else
                {
                    if (_nonUnique.TryGetValue(key, out var set))
                    {
                        var invalid = set.Where(e => !em.IsEntityValid(e)).ToList();
                        foreach (var e in invalid)
                        {
                            set.Remove(e);
                            _entKey.Remove(e);
                        }
                        if (set.Count == 0) _nonUnique.Remove(key);
                        return invalid.Count > 0;
                    }
                    return false;
                }
            }

            public bool TryCleanKey(object key, Entity invalidEntity, EntityManager em)
            {
                if (IsUnique) return false;
                if (_nonUnique.TryGetValue(key, out var set) && set.Contains(invalidEntity))
                {
                    set.Remove(invalidEntity);
                    _entKey.Remove(invalidEntity);
                    if (set.Count == 0) _nonUnique.Remove(key);
                    return true;
                }
                return false;
            }

            public Index<T> Rebuild(EntityManager em)
            {
                _unique.Clear();
                _nonUnique.Clear();
                _entKey.Clear();
                foreach (var e in em.GetEntities<T>())
                    if (!AddFromEntity(e, em))
                        throw new InvalidOperationException($"Duplicate key during rebuild for {typeof(T).Name}");
                return this;
            }

            public object? GetKeyFromEntity(Entity e, EntityManager em) => _keySel(em.GetComponent<T>(e));
        }

        public void Dispose() => _indexLock.Dispose();
    }
}
