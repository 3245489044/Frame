namespace ECS.Core
{
    public delegate void ChunkAction(Chunk chunk);

    public delegate void ChunkAction<T>(ChunkAccessor<T> accessor)
        where T : struct, IComponent;

    public delegate void ChunkAction<T1, T2>(ChunkAccessor<T1, T2> accessor)
        where T1 : struct, IComponent
        where T2 : struct, IComponent;

    public delegate void ChunkAction<T1, T2, T3>(ChunkAccessor<T1, T2, T3> accessor)
        where T1 : struct, IComponent
        where T2 : struct, IComponent
        where T3 : struct, IComponent;

    public delegate bool ChunkPredicate(Chunk chunk);

    public delegate bool ChunkPredicate<T>(ChunkAccessor<T> accessor)
        where T : struct, IComponent;

    public static class ChunkJob
    {
        public static void ForEach(
            ArchetypeQueryResult query,
            ChunkAction action)
        {
            while (query.MoveNext())
                action(query.Current);
        }

        public static void ForEach<T>(
            ArchetypeQueryResult query,
            ChunkAction<T> action)
            where T : struct, IComponent
        {
            while (query.MoveNext())
                action(query.GetAccessor<T>());
        }

        public static void ForEach<T1, T2>(
            ArchetypeQueryResult query,
            ChunkAction<T1, T2> action)
            where T1 : struct, IComponent
            where T2 : struct, IComponent
        {
            while (query.MoveNext())
                action(query.GetAccessor<T1, T2>());
        }

        public static void ForEach<T1, T2, T3>(
            ArchetypeQueryResult query,
            ChunkAction<T1, T2, T3> action)
            where T1 : struct, IComponent
            where T2 : struct, IComponent
            where T3 : struct, IComponent
        {
            while (query.MoveNext())
                action(query.GetAccessor<T1, T2, T3>());
        }

        public static bool ForEach(
            ArchetypeQueryResult query,
            ChunkPredicate predicate)
        {
            while (query.MoveNext())
            {
                if (!predicate(query.Current))
                    return false;
            }
            return true;
        }

        public static bool ForEach<T>(
            ArchetypeQueryResult query,
            ChunkPredicate<T> predicate)
            where T : struct, IComponent
        {
            while (query.MoveNext())
            {
                if (!predicate(query.GetAccessor<T>()))
                    return false;
            }
            return true;
        }
    }
}
