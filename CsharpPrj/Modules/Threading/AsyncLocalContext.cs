
namespace Modules.Threading
{
    public static class AsyncLocalContext
    {
        public static AsyncLocalContextSlot<T> CreateSlot<T>(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return new AsyncLocalContextSlot<T>(name);
        }
    }

    public sealed class AsyncLocalContextSlot<T>
    {
        private readonly AsyncLocal<T?> _local = new();
        private readonly string _name;

        internal AsyncLocalContextSlot(string name)
        {
            _name = name;
        }

        public string Name => _name;

        public T? Value => _local.Value;

        public void SetValue(T? value)
        {
            _local.Value = value;
        }

        public IDisposable SetScoped(T? value)
        {
            var previous = _local.Value;
            _local.Value = value;
            return new AsyncLocalScope<T>(_local, previous);
        }

        public void Clear()
        {
            _local.Value = default;
        }

        public override string ToString()
            => $"{_name}: {_local.Value}";
    }

    internal sealed class AsyncLocalScope<T> : IDisposable
    {
        private readonly AsyncLocal<T?> _local;
        private readonly T? _previousValue;
        private bool _disposed;

        public AsyncLocalScope(AsyncLocal<T?> local, T? previousValue)
        {
            _local = local;
            _previousValue = previousValue;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _local.Value = _previousValue;
        }
    }
}
