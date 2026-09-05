using System.Runtime.ExceptionServices;

namespace CentralConsole.AsyncSyncBridge.Synchronization
{
    public sealed class AsyncSyncSynchronizationContext : SynchronizationContext
    {
        private readonly IDispatcher _dispatcher;

        public AsyncSyncSynchronizationContext(IDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _ = _dispatcher.InvokeAsync(() =>
            {
                d(state);
                return Task.CompletedTask;
            });
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (_dispatcher.InvokeRequired)
            {
                _dispatcher.Invoke(() => d(state));
            }
            else
            {
                d(state);
            }
        }

        public override SynchronizationContext CreateCopy()
        {
            return new AsyncSyncSynchronizationContext(_dispatcher);
        }
    }
}
