
namespace Modules.Threading
{
    public static class ThreadContext
    {
        private static int _mainThreadId;

        private static bool _initialized;

        private static Action<Action>? _customDispatcher;

        private static readonly object _lock = new();

        public static bool IsMainThread
        {
            get
            {
                EnsureInitialized();
                return Environment.CurrentManagedThreadId == _mainThreadId;
            }
        }

        public static void SetMainThread()
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    _mainThreadId = Environment.CurrentManagedThreadId;
                    _initialized = true;
                }
            }
        }

        public static void SetDispatcher(Action<Action> dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            _customDispatcher = dispatcher;
        }

        public static void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (IsMainThread)
            {
                action();
                return;
            }

            if (_customDispatcher != null)
            {
                _customDispatcher(action);
                return;
            }

            var syncCtx = SynchronizationContext.Current;
            if (syncCtx != null)
            {
                syncCtx.Post(_ => action(), null);
            }
            else
            {
                System.Threading.ThreadPool.QueueUserWorkItem(_ => action());
            }
        }

        public static Task InvokeAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (IsMainThread)
            {
                try
                {
                    action();
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Post(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        public static Task<T> InvokeAsync<T>(Func<T> func)
        {
            ArgumentNullException.ThrowIfNull(func);

            if (IsMainThread)
            {
                try
                {
                    return Task.FromResult(func());
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Post(() =>
            {
                try
                {
                    var result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                lock (_lock)
                {
                    if (!_initialized)
                    {
                        _mainThreadId = Environment.CurrentManagedThreadId;
                        _initialized = true;
                    }
                }
            }
        }
    }
}
