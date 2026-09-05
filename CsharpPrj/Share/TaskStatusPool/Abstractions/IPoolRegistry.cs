
#nullable enable

namespace Share.TaskStatusPool.Abstractions
{

    public interface IPoolRegistry<TKey, TContext>
        where TKey : notnull
    {
        int Count { get; }

        IEnumerable<TKey> Keys { get; }

        void Register(TKey key, IMorphism<TContext, TContext> morphism);

        void RegisterOrUpdate(TKey key, IMorphism<TContext, TContext> morphism);

        IMorphism<TContext, TContext> Get(TKey key);

        bool TryGet(TKey key, out IMorphism<TContext, TContext>? morphism);

        bool ContainsKey(TKey key);

        bool Remove(TKey key);

        void Clear();

        TContext Execute(TKey key, TContext context);

        bool TryExecute(TKey key, TContext context, out TContext result);
    }
}
