
#nullable enable
using Share.TaskStatusPool.Models;

namespace Share.TaskStatusPool.Abstractions
{

    public interface IPool<T>
    {
        int Count { get; }

        bool IsEmpty { get; }

        void Clear();

        event EventHandler<PoolChangedEventArgs<T>>? Changed;
    }


    public interface IReadablePool<TKey, TValue> : IPool<TValue>
        where TKey : notnull
    {
        TValue? Read(TKey key);

        bool TryRead(TKey key, out TValue? value);

        bool Contains(TKey key);

        IEnumerable<TKey> Keys { get; }
    }


    public interface IWritablePool<TKey, TValue> : IPool<TValue>
        where TKey : notnull
    {
        void Write(TKey key, TValue? value);

        void WriteRange(IEnumerable<KeyValuePair<TKey, TValue?>> items);

        bool Remove(TKey key);
    }


    public interface IReadWritePool<TKey, TValue> : IReadablePool<TKey, TValue>, IWritablePool<TKey, TValue>
        where TKey : notnull
    {
    }
}
