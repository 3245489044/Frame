
#nullable enable

using System.Collections.Concurrent;
using System.Reflection;
using ECS.Core;
using TConfig.ECS;

namespace GraphFramework.Ecs
{
    internal class IndexDefinition
    {
        public Type ComponentType { get; }
        public Func<object, object?> KeySelector { get; }
        public bool IsUnique { get; }
        public string Name { get; }

        public IndexDefinition(Type componentType, Func<object, object?> keySelector, bool isUnique, string name)
        {
            ComponentType = componentType;
            KeySelector = keySelector;
            IsUnique = isUnique;
            Name = name;
        }
    }

    public class EcsIndexManager : IDisposable
    {
        private readonly EntityManager _entityManager;
        private readonly ConcurrentDictionary<string, IndexDefinition> _indexDefinitions = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<object, HashSet<Entity>>> _indexData = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<object, Entity>> _uniqueIndexData = new();
        private readonly object _lock = new();
        private bool _disposed;

        private static readonly ConcurrentDictionary<Type, Func<EntityManager, Entity, object?>> _tryGetComponentCache = new();

        public EcsIndexManager(EntityManager entityManager)
        {
            _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        }

        public void RegisterIndex<TComponent>(
            Func<TComponent, object?> keySelector,
            bool isUnique = false,
            string? name = null)
            where TComponent : struct, IComponent
        {
            var indexName = name ?? typeof(TComponent).Name;

            if (_indexDefinitions.ContainsKey(indexName))
                throw new InvalidOperationException($"Index '{indexName}' is already registered.");

            var definition = new IndexDefinition(
                typeof(TComponent),
                obj => keySelector((TComponent)obj),
                isUnique,
                indexName);

            _indexDefinitions[indexName] = definition;

            if (isUnique)
                _uniqueIndexData[indexName] = new ConcurrentDictionary<object, Entity>();
            else
                _indexData[indexName] = new ConcurrentDictionary<object, HashSet<Entity>>();

            RebuildIndex(indexName);
        }

        public void UnregisterIndex(string name)
        {
            _indexDefinitions.TryRemove(name, out _);
            _indexData.TryRemove(name, out _);
            _uniqueIndexData.TryRemove(name, out _);
        }

        public bool TryFindEntity<TComponent>(object key, out Entity entity)
            where TComponent : struct, IComponent
        {
            return TryFindEntity(typeof(TComponent).Name, key, out entity);
        }

        public bool TryFindEntity(string indexName, object key, out Entity entity)
        {
            entity = default;

            if (_uniqueIndexData.TryGetValue(indexName, out var uniqueIndex))
            {
                if (uniqueIndex.TryGetValue(key, out var e))
                {
                    entity = e;
                    return true;
                }
                return false;
            }

            if (_indexData.TryGetValue(indexName, out var nonUniqueIndex))
            {
                if (nonUniqueIndex.TryGetValue(key, out var entities) && entities.Count > 0)
                {
                    entity = entities.First();
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<Entity> FindEntities<TComponent>(object key)
            where TComponent : struct, IComponent
        {
            return FindEntities(typeof(TComponent).Name, key);
        }

        public IReadOnlyList<Entity> FindEntities(string indexName, object key)
        {
            if (_uniqueIndexData.TryGetValue(indexName, out var uniqueIndex))
            {
                if (uniqueIndex.TryGetValue(key, out var e))
                    return new[] { e };
                return Array.Empty<Entity>();
            }

            if (_indexData.TryGetValue(indexName, out var nonUniqueIndex))
            {
                if (nonUniqueIndex.TryGetValue(key, out var entities))
                    return entities.ToList();
            }

            return Array.Empty<Entity>();
        }

        public IEnumerable<object> GetKeys(string indexName)
        {
            if (_uniqueIndexData.TryGetValue(indexName, out var uniqueIndex))
                return uniqueIndex.Keys;

            if (_indexData.TryGetValue(indexName, out var nonUniqueIndex))
                return nonUniqueIndex.Keys;

            return Enumerable.Empty<object>();
        }

        public bool HasIndex(string name)
            => _indexDefinitions.ContainsKey(name);

        public IEnumerable<string> GetIndexNames()
            => _indexDefinitions.Keys;

        public void RebuildIndex(string name)
        {
            if (!_indexDefinitions.TryGetValue(name, out var definition))
                return;

            if (definition.IsUnique)
            {
                if (_uniqueIndexData.TryGetValue(name, out var uniqueIndex))
                    uniqueIndex.Clear();
            }
            else
            {
                if (_indexData.TryGetValue(name, out var nonUniqueIndex))
                    nonUniqueIndex.Clear();
            }

            foreach (var entity in _entityManager.GetAllEntities())
            {
                IndexEntity(entity, definition);
            }
        }

        public void RebuildAll()
        {
            foreach (var name in _indexDefinitions.Keys)
            {
                RebuildIndex(name);
            }
        }

        internal void OnComponentAdded(Entity entity, Type componentType)
        {
            foreach (var kvp in _indexDefinitions)
            {
                if (kvp.Value.ComponentType == componentType)
                {
                    IndexEntity(entity, kvp.Value);
                }
            }
        }

        internal void OnComponentRemoved(Entity entity, Type componentType)
        {
            foreach (var kvp in _indexDefinitions)
            {
                if (kvp.Value.ComponentType == componentType)
                {
                    DeindexEntity(entity, kvp.Value);
                }
            }
        }

        internal void OnEntityDestroyed(Entity entity)
        {
            foreach (var kvp in _indexDefinitions)
            {
                DeindexEntity(entity, kvp.Value);
            }
        }

        private void IndexEntity(Entity entity, IndexDefinition definition)
        {
            if (!_entityManager.HasComponent(entity, definition.ComponentType))
                return;

            var component = TryGetComponentCached(_entityManager, entity, definition.ComponentType);
            if (component == null)
                return;

            var key = definition.KeySelector(component);
            if (key == null) return;

            if (definition.IsUnique)
            {
                if (_uniqueIndexData.TryGetValue(definition.Name, out var uniqueIndex))
                {
                    uniqueIndex[key] = entity;
                }
            }
            else
            {
                if (_indexData.TryGetValue(definition.Name, out var nonUniqueIndex))
                {
                    var entities = nonUniqueIndex.GetOrAdd(key, _ => new HashSet<Entity>());
                    lock (entities)
                    {
                        entities.Add(entity);
                    }
                }
            }
        }

        private void DeindexEntity(Entity entity, IndexDefinition definition)
        {
            if (definition.IsUnique)
            {
                if (_uniqueIndexData.TryGetValue(definition.Name, out var uniqueIndex))
                {
                    object? foundKey = null;
                    foreach (var kvp in uniqueIndex)
                    {
                        if (kvp.Value.Equals(entity))
                        {
                            foundKey = kvp.Key;
                            break;
                        }
                    }
                    if (foundKey != null)
                    {
                        uniqueIndex.TryRemove(foundKey, out _);
                    }
                }
            }
            else
            {
                if (_indexData.TryGetValue(definition.Name, out var nonUniqueIndex))
                {
                    foreach (var kvp in nonUniqueIndex)
                    {
                        lock (kvp.Value)
                        {
                            kvp.Value.Remove(entity);
                        }
                    }
                }
            }
        }

        private static object? TryGetComponentCached(EntityManager entityManager, Entity entity, Type componentType)
        {
            var func = _tryGetComponentCache.GetOrAdd(componentType, type =>
            {
                var method = typeof(EntityManager).GetMethod("TryGetComponent", new[] { typeof(Entity), componentType.MakeByRefType() });
                if (method == null)
                {
                    method = typeof(EntityManager)
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m =>
                            m.Name == "TryGetComponent" &&
                            m.IsGenericMethodDefinition &&
                            m.GetParameters().Length == 2 &&
                            m.GetParameters()[0].ParameterType == typeof(Entity) &&
                            m.GetParameters()[1].ParameterType.IsByRef)
                        ?.MakeGenericMethod(componentType);
                }

                if (method == null)
                    return (_, _) => null;

                var emParam = System.Linq.Expressions.Expression.Parameter(typeof(EntityManager), "em");
                var entityParam = System.Linq.Expressions.Expression.Parameter(typeof(Entity), "e");
                var resultVar = System.Linq.Expressions.Expression.Variable(componentType, "result");
                var call = System.Linq.Expressions.Expression.Call(emParam, method, entityParam, resultVar);

                var condition = System.Linq.Expressions.Expression.Condition(
                    call,
                    System.Linq.Expressions.Expression.Convert(resultVar, typeof(object)),
                    System.Linq.Expressions.Expression.Constant(null, typeof(object))
                );

                var block = System.Linq.Expressions.Expression.Block(
                    new[] { resultVar },
                    condition
                );

                return System.Linq.Expressions.Expression.Lambda<Func<EntityManager, Entity, object?>>(block, emParam, entityParam).Compile();
            });

            return func(entityManager, entity);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _indexDefinitions.Clear();
                _indexData.Clear();
                _uniqueIndexData.Clear();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}


