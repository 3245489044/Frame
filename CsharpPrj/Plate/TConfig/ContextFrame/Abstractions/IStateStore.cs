
using System;

namespace TConfig.ContextFrame.Abstractions
{
    public interface IStateStore
    {
        T? Get<T>(string key);

        void Set<T>(string key, T value);

        bool Remove<T>(string key);

        bool Contains<T>(string key);

        T? GetOrCreate<T>(string key, Func<T> factory);

        void Clear();
    }
}
