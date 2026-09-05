namespace ECS.Core
{
    public struct EntityQueryBuilder
    {
        private readonly List<Type> _allTypes;
        private readonly HashSet<int> _anyIds;
        private readonly HashSet<int> _noneIds;
        private readonly ComponentTypeId _componentTypeId;

        private EntityQueryBuilder(Builder builder)
        {
            _allTypes = builder._allTypes;
            _anyIds = builder._anyIds;
            _noneIds = builder._noneIds;
            _componentTypeId = builder._componentTypeId;
        }

        public ArchetypeQueryResult Execute(ArchetypeManager manager)
        {
            var chunks = manager.QueryChunks(_allTypes.ToArray()).ToList();

            var anyIds = _anyIds;
            var noneIds = _noneIds;

            if (anyIds.Count > 0)
            {
                chunks = chunks.Where(chunk =>
                {
                    foreach (var id in anyIds)
                        if (chunk.HasComponent(id))
                            return true;
                    return false;
                }).ToList();
            }

            if (noneIds.Count > 0)
            {
                chunks = chunks.Where(chunk =>
                {
                    foreach (var id in noneIds)
                        if (chunk.HasComponent(id))
                            return false;
                    return true;
                }).ToList();
            }

            return new ArchetypeQueryResult(chunks, _componentTypeId);
        }

        public IEnumerable<Entity> GetEntities(ArchetypeManager manager)
        {
            var result = Execute(manager);
            var entities = new List<Entity>(result.TotalEntityCount);
            while (result.MoveNext())
            {
                var chunk = result.Current;
                for (int i = 0; i < chunk.Count; i++)
                    entities.Add(chunk.GetEntity(i));
            }
            return entities;
        }


        public int Count(ArchetypeManager manager)
        {
            int count = 0;
            var query = Execute(manager);
            while (query.MoveNext())
                count += query.CurrentCount;
            return count;
        }

        public struct Builder
        {
            internal List<Type> _allTypes;
            internal HashSet<int> _anyIds;
            internal HashSet<int> _noneIds;
            internal readonly ComponentTypeId _componentTypeId;

            public Builder(ComponentTypeId componentTypeId)
            {
                _componentTypeId = componentTypeId ?? throw new ArgumentNullException(nameof(componentTypeId));
                _allTypes = new List<Type>();
                _anyIds = new HashSet<int>();
                _noneIds = new HashSet<int>();
            }

            public Builder WithAll<T>() where T : struct, IComponent
            {
                _allTypes.Add(typeof(T));
                return this;
            }

            public Builder WithAll(params Type[] types)
            {
                _allTypes.AddRange(types);
                return this;
            }

            public Builder WithAny<T>() where T : struct, IComponent
            {
                _anyIds.Add(_componentTypeId.GetId(typeof(T)));
                return this;
            }

            public Builder WithAny(params Type[] types)
            {
                foreach (var t in types)
                    _anyIds.Add(_componentTypeId.GetId(t));
                return this;
            }

            public Builder WithNone<T>() where T : struct, IComponent
            {
                _noneIds.Add(_componentTypeId.GetId(typeof(T)));
                return this;
            }

            public Builder WithNone(params Type[] types)
            {
                foreach (var t in types)
                    _noneIds.Add(_componentTypeId.GetId(t));
                return this;
            }

            public EntityQueryBuilder Build()
            {
                if (_allTypes.Count == 0)
                    throw new InvalidOperationException("EntityQueryBuilder must have at least one WithAll condition");
                return new EntityQueryBuilder(this);
            }
        }
    }

    public static class EntityQueryBuilderExtensions
    {
        public static void ForEach(
            this EntityQueryBuilder query,
            ArchetypeManager manager,
            ChunkAction action)
        {
            ChunkJob.ForEach(query.Execute(manager), action);
        }

        public static void ForEach<T>(
            this EntityQueryBuilder query,
            ArchetypeManager manager,
            ChunkAction<T> action)
            where T : struct, IComponent
        {
            ChunkJob.ForEach(query.Execute(manager), action);
        }

        public static void ForEach<T1, T2>(
            this EntityQueryBuilder query,
            ArchetypeManager manager,
            ChunkAction<T1, T2> action)
            where T1 : struct, IComponent
            where T2 : struct, IComponent
        {
            ChunkJob.ForEach(query.Execute(manager), action);
        }

        public static void ForEach<T1, T2, T3>(
            this EntityQueryBuilder query,
            ArchetypeManager manager,
            ChunkAction<T1, T2, T3> action)
            where T1 : struct, IComponent
            where T2 : struct, IComponent
            where T3 : struct, IComponent
        {
            ChunkJob.ForEach(query.Execute(manager), action);
        }
    }
}
