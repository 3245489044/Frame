
namespace Modules.StateManagement
{
    public interface IHandlerRegistry<TKey, THandler>
        where TKey : notnull
        where THandler : class
    {
        void Register(TKey key, THandler handler);

        void RegisterRange(IEnumerable<KeyValuePair<TKey, THandler>> handlers);

        THandler? Get(TKey key);

        bool TryGet(TKey key, out THandler? handler);

        bool Remove(TKey key);

        void Clear();

        IEnumerable<TKey> GetKeys();

        IEnumerable<KeyValuePair<TKey, THandler>> GetAll();

        Task ForEachAsync(Func<THandler, Task> action);

        int Count { get; }
    }
}
