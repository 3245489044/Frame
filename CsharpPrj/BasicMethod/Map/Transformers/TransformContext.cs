
using System.Collections.Concurrent;

namespace Modules.Transformers
{
    public class TransformContext
    {

        private static readonly AsyncLocal<TransformContext?> _current = new();

        public static TransformContext? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        public static IDisposable BeginScope()
        {
            var parent = Current;
            var scope = new TransformContext { Parent = parent };
            Current = scope;
            return new ScopeDisposable(scope);
        }

        public static IDisposable BeginScope(Action<TransformContext> initialize)
        {
            var parent = Current;
            var scope = new TransformContext { Parent = parent };
            initialize(scope);
            Current = scope;
            return new ScopeDisposable(scope);
        }

        public TransformContext? Parent { get; private set; }


        public Dictionary<string, object> Items { get; } = new();

        public CancellationToken CancellationToken { get; set; }

        public Action<string>? Logger { get; set; }

        public void Log(string message)
        {
            Logger?.Invoke(message);
        }

        public T? Get<T>(string key) where T : class
        {
            if (Items.TryGetValue(key, out var value) && value is T typed)
                return typed;

            return Parent?.Get<T>(key);
        }

        public void Set<T>(string key, T value) where T : class
            => Items[key] = value;

        public bool TryGet<T>(string key, out T? value) where T : class
        {
            if (Items.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            if (Parent != null)
                return Parent.TryGet(key, out value);

            value = default;
            return false;
        }

        public bool Remove(string key) => Items.Remove(key);

        public void Clear() => Items.Clear();

        public TransformContext Snapshot()
        {
            var snapshot = new TransformContext
            {
                CancellationToken = CancellationToken,
                Logger = Logger,
                Parent = Parent
            };
            foreach (var kvp in Items)
                snapshot.Items[kvp.Key] = kvp.Value;
            return snapshot;
        }
    }

    internal class ScopeDisposable : IDisposable
    {
        private readonly TransformContext _scope;

        public ScopeDisposable(TransformContext scope)
        {
            _scope = scope;
        }

        public void Dispose()
        {
            TransformContext.Current = _scope.Parent;
        }
    }
}
