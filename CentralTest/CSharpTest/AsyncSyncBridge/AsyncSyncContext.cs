using System.Diagnostics;
using CentralConsole.AsyncSyncBridge.Synchronization;

namespace CentralConsole.AsyncSyncBridge
{
    public sealed class AsyncSyncContext : IAsyncSyncContext
    {
        private readonly IDispatcher _dispatcher;
        private SynchronizationContext? _originalContext;
        private bool _initialized;
        private bool _disposed;
        private readonly object _lock = new();

        public bool IsInitialized => _initialized;

        public IDispatcher Dispatcher => _dispatcher;

        public AsyncSyncContext(IDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                _originalContext = SynchronizationContext.Current;

                var syncContext = new AsyncSyncSynchronizationContext(_dispatcher);
                SynchronizationContext.SetSynchronizationContext(syncContext);

                _initialized = true;

                Debug.WriteLine("[AsyncSyncContext] 初始化完成，已设置 AsyncSyncSynchronizationContext");
            }
        }

        public void Cleanup()
        {
            lock (_lock)
            {
                if (!_initialized) return;

                if (_originalContext != null)
                {
                    SynchronizationContext.SetSynchronizationContext(_originalContext);
                    _originalContext = null;
                }

                _initialized = false;

                Debug.WriteLine("[AsyncSyncContext] 清理完成，已恢复原始 SynchronizationContext");
            }
        }

        public async Task RunAsync(Func<Task> action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!_dispatcher.InvokeRequired)
            {
                await action();
            }
            else
            {
                await _dispatcher.InvokeAsync(action);
            }
        }

        public async Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!_dispatcher.InvokeRequired)
            {
                return await action();
            }

            return await _dispatcher.InvokeAsync(action);
        }

        public void RunSync(Action action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!_dispatcher.InvokeRequired)
            {
                action();
            }
            else
            {
                _dispatcher.Invoke(action);
            }
        }

        public T RunSync<T>(Func<T> action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!_dispatcher.InvokeRequired)
            {
                return action();
            }

            return _dispatcher.Invoke(action);
        }

        public bool PumpUntil(Func<bool> condition, int timeoutMs = 30000)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            var sw = Stopwatch.StartNew();
            while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                if (sw.ElapsedMilliseconds < timeoutMs)
                    Thread.Sleep(10);
            }
            return condition();
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException(
                    "AsyncSyncContext 未初始化。请先调用 Initialize() 方法。");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cleanup();
        }
    }
}
