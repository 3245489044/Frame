
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace BasicMethod.Functor
{
    public class FunctorScope : IFunctorScope
    {

        private static readonly AsyncLocal<FunctorScope?> _current = new();

        public static FunctorScope? Current
        {
            get => _current.Value;
            private set => _current.Value = value;
        }


        private readonly ConcurrentDictionary<string, object?> _items = new();


        public string Name { get; }

        public IFunctorScope? Parent => _parent;

        private readonly FunctorScope? _parent;


        public FunctorScope(string name, FunctorScope? parent = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _parent = parent;
        }


        private static string TypeKey<T>() => typeof(T).FullName!;

        private static void EnsureKeyNotNull(string key, [CallerArgumentExpression(nameof(key))] string? paramName = null)
        {
            if (key == null) throw new ArgumentNullException(paramName);
        }


        public static IFunctorScope Begin(string name)
        {
            var scope = new FunctorScope(name, Current);
            Current = scope;
            return scope;
        }

        public static IFunctorScope Begin(string name, Action<IFunctor> initialize)
        {
            var scope = (FunctorScope)Begin(name);
            initialize(scope);
            return scope;
        }


        public T? Get<T>() where T : class
        {
            var key = TypeKey<T>();
            if (_items.TryGetValue(key, out var value) && value is T typed)
                return typed;
            return _parent?.Get<T>();
        }

        public void Set<T>(T value) where T : class
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            _items[TypeKey<T>()] = value;
        }

        public bool Has<T>() where T : class
        {
            var key = TypeKey<T>();
            return _items.ContainsKey(key) || (_parent?.Has<T>() ?? false);
        }

        public bool Remove<T>() where T : class
        {
            var key = TypeKey<T>();
            return _items.TryRemove(key, out _);
        }


        public object? Get(string key)
        {
            EnsureKeyNotNull(key);
            return _items.TryGetValue(key, out var value) ? value : _parent?.Get(key);
        }

        public void Set(string key, object? value)
        {
            EnsureKeyNotNull(key);
            _items[key] = value;
        }

        public bool Has(string key)
        {
            EnsureKeyNotNull(key);
            return _items.ContainsKey(key) || (_parent?.Has(key) ?? false);
        }

        public bool Remove(string key)
        {
            EnsureKeyNotNull(key);
            return _items.TryRemove(key, out _);
        }


        public IFunctorScope CreateScope()
        {
            var scope = new FunctorScope($"{Name}.Child", this);
            Current = scope;
            return scope;
        }

        public IReadOnlyDictionary<string, object?> Snapshot()
        {
            return new Dictionary<string, object?>(_items);
        }


        public void Dispose()
        {
            Current = _parent;
        }

        public override string ToString() => $"FunctorScope({Name}, Items={_items.Count})";
    }
}
