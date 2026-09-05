﻿namespace ECS.Core
{
    public ref struct ArchetypeQueryResult
    {
        private readonly List<Chunk> _chunks;
        private int _currentChunk;
        private readonly ComponentTypeId _componentTypeId;

        internal ArchetypeQueryResult(List<Chunk> chunks, ComponentTypeId componentTypeId)
        {
            _chunks = chunks;
            _currentChunk = -1;
            _componentTypeId = componentTypeId ?? throw new ArgumentNullException(nameof(componentTypeId));
        }

        public bool MoveNext()
        {
            _currentChunk++;
            return _currentChunk < _chunks.Count;
        }

        public Chunk Current => _chunks[_currentChunk];

        public int CurrentCount => _chunks[_currentChunk].Count;

        public Span<T> GetSpan<T>() where T : struct, IComponent
        {
            return _chunks[_currentChunk].GetComponentSpan<T>(_componentTypeId);
        }

        public Span<Entity> GetEntitySpan()
        {
            return _chunks[_currentChunk].EntitySpan;
        }

        public ChunkAccessor<T> GetAccessor<T>() where T : struct, IComponent
        {
            var chunk = _chunks[_currentChunk];
            return new ChunkAccessor<T>(
                chunk.GetComponentSpan<T>(_componentTypeId),
                chunk.EntitySpan
            );
        }

        public ChunkAccessor<T1, T2> GetAccessor<T1, T2>()
            where T1 : struct, IComponent
            where T2 : struct, IComponent
        {
            var chunk = _chunks[_currentChunk];
            return new ChunkAccessor<T1, T2>(
                chunk.GetComponentSpan<T1>(_componentTypeId),
                chunk.GetComponentSpan<T2>(_componentTypeId),
                chunk.EntitySpan
            );
        }

        public ChunkAccessor<T1, T2, T3> GetAccessor<T1, T2, T3>()
            where T1 : struct, IComponent
            where T2 : struct, IComponent
            where T3 : struct, IComponent
        {
            var chunk = _chunks[_currentChunk];
            return new ChunkAccessor<T1, T2, T3>(
                chunk.GetComponentSpan<T1>(_componentTypeId),
                chunk.GetComponentSpan<T2>(_componentTypeId),
                chunk.GetComponentSpan<T3>(_componentTypeId),
                chunk.EntitySpan
            );
        }

        public int ChunkCount => _chunks.Count;

        public int TotalEntityCount
        {
            get
            {
                int count = 0;
                foreach (var chunk in _chunks)
                    count += chunk.Count;
                return count;
            }
        }
    }

    public static class ArchetypeChunkQueryExtensions
    {
        public static ArchetypeQueryResult QueryChunksResult(
            this ArchetypeManager manager,
            ComponentTypeId componentTypeId,
            params Type[] componentTypes)
        {
            var chunks = manager.QueryChunks(componentTypes).ToList();
            return new ArchetypeQueryResult(chunks, componentTypeId);
        }

        public static ArchetypeQueryResult QueryChunksResult<T>(
            this ArchetypeManager manager,
            ComponentTypeId componentTypeId)
            where T : struct, IComponent
        {
            var chunks = manager.QueryChunks(typeof(T)).ToList();
            return new ArchetypeQueryResult(chunks, componentTypeId);
        }

        public static ArchetypeQueryResult QueryChunksResult<T1, T2>(
            this ArchetypeManager manager,
            ComponentTypeId componentTypeId)
            where T1 : struct, IComponent
            where T2 : struct, IComponent
        {
            var chunks = manager.QueryChunks(typeof(T1), typeof(T2)).ToList();
            return new ArchetypeQueryResult(chunks, componentTypeId);
        }
    }
}
