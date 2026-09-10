
#nullable enable

namespace Share.TaskStatusPool.Abstractions
{

    public interface IPoolContext
    {
        string ContextId { get; }

        object? Get(string key);

        void Set(string key, object? value);

        T? Get<T>(string key);

        bool Contains(string key);

        IEnumerable<string> Keys { get; }

        IPoolContext Clone();
    }
}
