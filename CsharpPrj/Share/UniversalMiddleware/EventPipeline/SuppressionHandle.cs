
namespace UniversalMiddleware.EventPipeline
{
    internal class SuppressionHandle : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public SuppressionHandle(Action onDispose)
        {
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _onDispose();
            }
        }
    }
}
