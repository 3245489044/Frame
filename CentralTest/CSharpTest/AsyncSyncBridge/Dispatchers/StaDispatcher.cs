using System.Runtime.ExceptionServices;

namespace CentralConsole.AsyncSyncBridge.Dispatchers
{
    public sealed class StaDispatcher : IDispatcher, IDisposable
    {
        private Thread? _staThread;
        private WinFormsDispatcher? _winFormsDispatcher;
        private bool _initialized;
        private bool _disposed;
        private readonly object _lock = new();

        public bool InvokeRequired
        {
            get
            {
                if (!_initialized) return true;
                return Thread.CurrentThread != _staThread;
            }
        }

        public bool IsInitialized => _initialized;

        public void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                var readyEvent = new ManualResetEventSlim(false);
                var initException = (Exception?)null;

                _staThread = new Thread(() =>
                {
                    try
                    {
                        _winFormsDispatcher = new WinFormsDispatcher();
                        _winFormsDispatcher.EnsureInitialized();
                        initException = null;
                    }
                    catch (Exception ex)
                    {
                        initException = ex;
                    }
                    finally
                    {
                        readyEvent.Set();
                    }

                    if (initException == null)
                    {
                        Application.Run();
                    }
                })
                {
                    Name = "STA Dispatcher Thread",
                    IsBackground = true
                };
                _staThread.SetApartmentState(ApartmentState.STA);
                _staThread.Start();

                if (!readyEvent.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("STA 线程初始化超时（10秒）");
                }

                if (initException != null)
                {
                    throw new InvalidOperationException(
                        "STA 线程初始化失败", initException);
                }

                _initialized = true;
            }
        }

        public void Invoke(Action action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!InvokeRequired)
            {
                action();
                return;
            }

            var exception = (Exception?)null;
            _winFormsDispatcher!.Invoke(() =>
            {
                try { action(); }
                catch (Exception ex) { exception = ex; }
            });

            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        public T Invoke<T>(Func<T> action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!InvokeRequired)
            {
                return action();
            }

            var result = default(T);
            var exception = (Exception?)null;
            _winFormsDispatcher!.Invoke(() =>
            {
                try { result = action(); }
                catch (Exception ex) { exception = ex; }
            });

            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
            return result!;
        }

        public Task InvokeAsync(Func<Task> action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!InvokeRequired)
            {
                return action();
            }

            return _winFormsDispatcher!.InvokeAsync(action);
        }

        public Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!InvokeRequired)
            {
                return action();
            }

            return _winFormsDispatcher!.InvokeAsync(action);
        }

        public void Cleanup()
        {
            lock (_lock)
            {
                if (_winFormsDispatcher != null)
                {
                    _winFormsDispatcher.Invoke(() => Application.ExitThread());
                    _winFormsDispatcher.Dispose();
                    _winFormsDispatcher = null;
                }

                _staThread = null;
                _initialized = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cleanup();
        }
    }
}
