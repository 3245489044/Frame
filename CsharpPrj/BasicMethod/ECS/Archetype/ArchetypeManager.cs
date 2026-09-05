using TConfig.ContextFrame.Abstractions;

namespace ECS.Core
{
    public sealed class ArchetypeManager
    {
        private readonly Dictionary<ArchetypeSignature, Archetype> _archetypes = new();
        private readonly Dictionary<Entity, Archetype> _entityArchetype = new();

        private readonly IECSStorage _ecsStorage;

        private readonly ComponentTypeId _componentTypeId;

        private int _nextEntityId;

        public int EntityCount => _entityArchetype.Count;

        private static ArchetypeSignature EmptySignature => new();

        public ArchetypeManager(IECSStorage ecsStorage, ComponentTypeId componentTypeId)
        {
            _ecsStorage = ecsStorage ?? throw new ArgumentNullException(nameof(ecsStorage),
                "ArchetypeManager 需要 IECSStorage 实例。请通过构造函数注入。");
            _componentTypeId = componentTypeId ?? throw new ArgumentNullException(nameof(componentTypeId),
                "ArchetypeManager 需要 ComponentTypeId 实例。请通过构造函数注入。");

            int maxId = 0;
            foreach (var kvp in _ecsStorage.GetAllEntityArchetypeIds())
            {
                if (kvp.Key > maxId)
                    maxId = kvp.Key;
            }
            _nextEntityId = maxId + 1;
        }
        private int GetNextEntityId()
        {
            return _nextEntityId++;
        }

        public Entity CreateEntity()
        {
            var taken = _ecsStorage.TakeFreeEntityId();
            int id = taken ?? GetNextEntityId();

            var entity = new Entity(id, _ecsStorage.GetEntityVersion(id));

            var signature = EmptySignature;
            if (!_archetypes.TryGetValue(signature, out var archetype))
            {
                archetype = new Archetype(signature);
                _archetypes[signature] = archetype;
            }
            archetype.AddEntity(entity, Array.Empty<IComponent>(), _componentTypeId);
            _entityArchetype[entity] = archetype;

            return entity;
        }

        public void DestroyEntity(Entity entity)
        {
            if (!_entityArchetype.TryGetValue(entity, out var archetype))
                return;

            List<Type> componentTypes = new();
            try
            {
                var (chunk, index) = archetype.GetEntityLocation(entity);
                foreach (var typeId in chunk.GetComponentTypeIds())
                {
                    var type = _componentTypeId.GetType(typeId);
                    if (type != null)
                        componentTypes.Add(type);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ArchetypeManager.DestroyEntity: Error collecting components for {entity}: {ex.Message}");
            }

            archetype.RemoveEntity(entity);
            _entityArchetype.Remove(entity);

            _ecsStorage.SetEntityVersion(entity.Id, entity.Version + 1);
            _ecsStorage.AddFreeEntityId(entity.Id);

        }

        public void AddComponent<T>(Entity entity, T component) where T : struct, IComponent
        {
            if (!_entityArchetype.TryGetValue(entity, out var currentArchetype))
                throw new ArgumentException($"Entity {entity} not found", nameof(entity));

            var currentSig = currentArchetype.Signature;
            int typeId = _componentTypeId.GetId(typeof(T));
            bool alreadyHas = typeId < currentSig.Components.Length && currentSig.Components[typeId];
            if (alreadyHas)
                throw new InvalidOperationException($"Entity {entity} already has component {typeof(T).Name}");

            var newSignature = currentSig.WithComponent(typeId, true);

            if (!_archetypes.TryGetValue(newSignature, out var targetArchetype))
            {
                targetArchetype = new Archetype(newSignature);
                _archetypes[newSignature] = targetArchetype;
            }

            var (backupChunk, backupIndex) = currentArchetype.GetEntityLocation(entity);
            var backupComponents = backupChunk.GetComponentsAt(backupIndex);

            try
            {
                targetArchetype.AddEntity(entity, backupComponents, _componentTypeId);
                _entityArchetype[entity] = targetArchetype;

                currentArchetype.RemoveEntity(entity);

                var (chunk, index) = targetArchetype.GetEntityLocation(entity);
                if (!chunk.HasComponent(typeId))
                {
                    var column = Array.CreateInstance(typeof(T), Chunk.Capacity);
                    var columnsField = typeof(Chunk).GetField("_componentColumns",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (columnsField != null)
                    {
                        var columns = (System.Collections.Generic.Dictionary<int, Array>)columnsField.GetValue(chunk)!;
                        columns[typeId] = column;
                    }
                }
                chunk.SetComponent(index, component, _componentTypeId);
            }
            catch
            {
                try
                {
                    if (_entityArchetype.TryGetValue(entity, out var current) && current == targetArchetype)
                    {
                        targetArchetype.RemoveEntity(entity);
                    }

                    if (!currentArchetype.ContainsEntity(entity))
                    {
                        currentArchetype.AddEntity(entity, backupComponents, _componentTypeId);
                        _entityArchetype[entity] = currentArchetype;
                    }
                }
                catch (Exception rollbackEx)
                {
                    throw new InvalidOperationException(
                        $"Critical: AddComponent rollback failed for {entity} on {typeof(T).Name}. " +
                        $"Entity may be in an inconsistent state. Original error: {rollbackEx.Message}", rollbackEx);
                }
                throw;
            }
        }

        public void RemoveComponent<T>(Entity entity) where T : struct, IComponent
        {
            if (!_entityArchetype.TryGetValue(entity, out var currentArchetype))
                throw new ArgumentException($"Entity {entity} not found", nameof(entity));

            var currentSig = currentArchetype.Signature;
            int typeId = _componentTypeId.GetId(typeof(T));
            if (typeId >= currentSig.Components.Length || !currentSig.Components[typeId])
                throw new InvalidOperationException($"Entity {entity} does not have component {typeof(T).Name}");

            var newSignature = currentSig.WithComponent(typeId, false);

            if (!_archetypes.TryGetValue(newSignature, out var targetArchetype))
            {
                targetArchetype = new Archetype(newSignature);
                _archetypes[newSignature] = targetArchetype;
            }

            currentArchetype.MoveEntity(entity, targetArchetype, _componentTypeId);
            _entityArchetype[entity] = targetArchetype;
        }

        public bool HasComponent<T>(Entity entity) where T : struct, IComponent
        {
            if (!_entityArchetype.TryGetValue(entity, out var archetype))
                return false;

            int typeId = _componentTypeId.GetId(typeof(T));
            return HasComponentByTypeId(archetype, typeId);
        }

        public bool HasComponent(Entity entity, Type componentType)
        {
            if (!_entityArchetype.TryGetValue(entity, out var archetype))
                return false;

            int typeId = _componentTypeId.GetId(componentType);
            return HasComponentByTypeId(archetype, typeId);
        }

        private static bool HasComponentByTypeId(Archetype archetype, int typeId)
        {
            var sig = archetype.Signature;
            return typeId < sig.Components.Length && sig.Components[typeId];
        }

        public T GetComponent<T>(Entity entity) where T : struct, IComponent
        {
            if (!_entityArchetype.TryGetValue(entity, out var archetype))
                throw new ArgumentException($"Entity {entity} not found", nameof(entity));

            var (chunk, index) = archetype.GetEntityLocation(entity);
            return chunk.GetComponent<T>(index, _componentTypeId);
        }

        public void SetComponent<T>(Entity entity, T component) where T : struct, IComponent
        {
            if (!_entityArchetype.TryGetValue(entity, out var archetype))
                throw new ArgumentException($"Entity {entity} not found", nameof(entity));

            var (chunk, index) = archetype.GetEntityLocation(entity);
            chunk.SetComponent(index, component, _componentTypeId);
        }

        public bool TryGetComponent<T>(Entity entity, out T component) where T : struct, IComponent
        {
            if (!_entityArchetype.TryGetValue(entity, out var archetype))
            {
                component = default;
                return false;
            }

            int typeId = _componentTypeId.GetId(typeof(T));
            var sig = archetype.Signature;
            if (typeId >= sig.Components.Length || !sig.Components[typeId])
            {
                component = default;
                return false;
            }

            var (chunk, index) = archetype.GetEntityLocation(entity);
            component = chunk.GetComponent<T>(index, _componentTypeId);
            return true;
        }

        public bool IsEntityValid(Entity entity)
        {
            return _entityArchetype.ContainsKey(entity) &&
                   entity.Id >= 0 &&
                   entity.Version == _ecsStorage.GetEntityVersion(entity.Id);
        }

        public IEnumerable<Chunk> QueryChunks(params Type[] componentTypes)
        {
            if (componentTypes.Length == 0)
                yield break;

            var querySig = new ArchetypeSignature(componentTypes, _componentTypeId);

            foreach (var archetype in _archetypes.Values)
            {
                if (querySig.IsSubsetOf(archetype.Signature))
                {
                    foreach (var chunk in archetype.Chunks)
                        yield return chunk;
                }
            }
        }

        public IEnumerable<Entity> QueryEntities(params Type[] componentTypes)
        {
            foreach (var chunk in QueryChunks(componentTypes))
            {
                for (int i = 0; i < chunk.Count; i++)
                    yield return chunk.GetEntity(i);
            }
        }

        public Archetype GetArchetype(Entity entity)
        {
            if (!_entityArchetype.TryGetValue(entity, out var archetype))
                throw new ArgumentException($"Entity {entity} not found", nameof(entity));
            return archetype;
        }

        public int GetEntityCount() => _entityArchetype.Count;

        public IEnumerable<Entity> GetAllEntities()
        {
            return _entityArchetype.Keys.ToList();
        }

        public void Clear()
        {
            foreach (var archetype in _archetypes.Values)
                archetype.Clear();
            _archetypes.Clear();
            _entityArchetype.Clear();
        }
    }
}
