#region USINGS
using Modules.Common;
#endregion

namespace WorkFlowMethod.Infrastructure
{
    public class TypedExecutionContext
    {
        private readonly MemoryCache<string, object?> _data = new();
        private readonly string _name;

        public string Name => _name;

        public TypedExecutionContext(string name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public T? Get<T>(string key) where T : class
        {
            if (_data.TryGet(key, out var value) && value is T typed)
                return typed;
            return null;
        }

        public T GetRequired<T>(string key) where T : class
        {
            var result = Get<T>(key);
            if (result == null)
                throw new KeyNotFoundException($"上下文 '{_name}' 中未找到键 '{key}' 或类型不匹配 {typeof(T).Name}");
            return result;
        }

        public void Set<T>(string key, T? value) where T : class
        {
            if (value != null)
                _data.GetOrAdd(key, value);
            else
                _data.Remove(key);
        }

        public bool TryGet<T>(string key, out T? value) where T : class
        {
            value = Get<T>(key);
            return value != null;
        }

        public void Remove(string key) => _data.Remove(key);

        public void Clear() => _data.Clear();

        public IEnumerable<string> Keys => _data.Keys;
    }

    public static class ContextFactory
    {
        private static readonly MemoryCache<string, TypedExecutionContext> _contexts = new();

        public static TypedExecutionContext GetOrCreate(string name)
        {
            return _contexts.GetOrAdd(name, n => new TypedExecutionContext(n));
        }

        public static bool TryGet(string name, out TypedExecutionContext? context)
        {
            return _contexts.TryGet(name, out context);
        }

        public static void Remove(string name)
        {
            if (_contexts.TryGet(name, out var context))
            {
                _contexts.Remove(name);
                context.Clear();
            }
        }

        public static void Clear() => _contexts.Clear();
    }
}
