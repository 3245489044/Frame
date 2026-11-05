
namespace Modules.Threading
{
    public sealed class ExecutionScope : IDisposable
    {
        private static readonly AsyncLocalContextSlot<ExecutionScope?> _currentScope =
            AsyncLocalContext.CreateSlot<ExecutionScope?>("ExecutionScope.Current");

        private readonly IDisposable? _reentryHandle;

        private bool _disposed;

        private ExecutionScope(string name, object? reentryKey)
        {
            Name = name;
            CreatedAt = DateTimeOffset.UtcNow;

            if (reentryKey != null)
            {
                _reentryHandle = ReentryGuard.Enter(reentryKey);
                ReentryKey = reentryKey;
            }
        }

        public string Name { get; }

        public DateTimeOffset CreatedAt { get; }

        public object? ReentryKey { get; }

        public bool IsActive => !_disposed;

        public static ExecutionScope? Current => _currentScope.Value;

        public static ExecutionScope Begin(string name, object? reentryKey = null)
        {
            ArgumentNullException.ThrowIfNull(name);

            var scope = new ExecutionScope(name, reentryKey);
            _currentScope.SetValue(scope);
            return scope;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _currentScope.SetValue(null);

            _reentryHandle?.Dispose();
        }

        public override string ToString()
            => $"[{Name}] Key={ReentryKey} Active={IsActive}";
    }
}
