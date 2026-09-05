namespace ECS.Core
{
    public sealed class Archetype

    {
        private readonly List<Chunk> _chunks = new();
        private readonly Dictionary<Entity, (Chunk Chunk, int Index)> _entityMap = new();

        public ArchetypeSignature Signature { get; }
        public int EntityCount => _entityMap.Count;
        public int ChunkCount => _chunks.Count;
        public IReadOnlyList<Chunk> Chunks => _chunks;

        public Archetype(ArchetypeSignature signature)
        {
            Signature = signature;
        }

        public void AddEntity(Entity entity, IComponent[] components, ComponentTypeId componentTypeId)
        {
            if (_entityMap.ContainsKey(entity))
                throw new InvalidOperationException($"Entity {entity} already exists in this archetype");

            Chunk chunk;
            bool isNewChunk = false;
            if (_chunks.Count > 0 && !_chunks[^1].IsFull)
            {
                chunk = _chunks[^1];
            }
            else
            {
                chunk = new Chunk();
                _chunks.Add(chunk);
                isNewChunk = true;
            }

            try
            {
                int index = chunk.AddEntity(entity, components, componentTypeId);
                _entityMap[entity] = (chunk, index);
            }
            catch
            {
                if (isNewChunk)
                    _chunks.Remove(chunk);
                throw;
            }
        }

        public void RemoveEntity(Entity entity)
        {
            if (!_entityMap.TryGetValue(entity, out var entry))
                throw new InvalidOperationException($"Entity {entity} not found in this archetype");

            var (chunk, index) = entry;
            _entityMap.Remove(entity);

            if (index < chunk.Count - 1)
            {
                Entity movedEntity = chunk.GetEntity(chunk.Count - 1);
                chunk.RemoveEntity(index);
                _entityMap[movedEntity] = (chunk, index);
            }
            else
            {
                chunk.RemoveEntity(index);
            }

            if (chunk.IsEmpty)
            {
                _chunks.Remove(chunk);
            }
        }

        public void MoveEntity(Entity entity, Archetype targetArchetype, ComponentTypeId componentTypeId)
        {
            if (!_entityMap.TryGetValue(entity, out var entry))
                throw new InvalidOperationException($"Entity {entity} not found in this archetype");

            var (chunk, index) = entry;

            var components = chunk.GetComponentsAt(index);

            RemoveEntity(entity);

            targetArchetype.AddEntity(entity, components, componentTypeId);
        }

        public Chunk GetChunk(int index)
        {
            if (index < 0 || index >= _chunks.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _chunks[index];
        }

        public bool ContainsEntity(Entity entity)
        {
            return _entityMap.ContainsKey(entity);
        }

        public (Chunk Chunk, int Index) GetEntityLocation(Entity entity)
        {
            if (!_entityMap.TryGetValue(entity, out var entry))
                throw new InvalidOperationException($"Entity {entity} not found in this archetype");
            return entry;
        }

        public void Clear()
        {
            foreach (var chunk in _chunks)
                chunk.Clear();
            _chunks.Clear();
            _entityMap.Clear();
        }
    }
}
