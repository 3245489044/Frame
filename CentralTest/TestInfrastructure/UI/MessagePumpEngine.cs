
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestInfrastructure
{
    public class MessagePumpEngine
    {
        private static readonly Lazy<MessagePumpEngine> _instance = new(() => new MessagePumpEngine());
        private Thread? _pumpThread;
        private Form? _pumpForm;
        private SynchronizationContext? _uiContext;
        private SynchronizationContext? _originalContext;
        private volatile bool _initialized;
        private readonly object _lock = new();

        public static MessagePumpEngine Instance => _instance.Value;

        public bool IsInitialized => _initialized;

        public void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                var readyEvent = new ManualResetEventSlim(false);

                _pumpThread = new Thread(() =>
                {
                    _pumpForm = new Form { WindowState = FormWindowState.Minimized, ShowInTaskbar = false };
                    _pumpForm.Load += (s, e) =>
                    {
                        _uiContext = SynchronizationContext.Current;
                        readyEvent.Set();
                    };

                    Application.Run(_pumpForm);
                })
                {
                    Name = "MessagePumpThread",
                    IsBackground = true,
                    ApartmentState = ApartmentState.STA
                };

                _pumpThread.Start();
                readyEvent.Wait(TimeSpan.FromSeconds(5));

                if (_uiContext == null)
                    throw new InvalidOperationException("消息泵初始化失败：无法获取 UI SynchronizationContext");

                _originalContext = SynchronizationContext.Current;

                SynchronizationContext.SetSynchronizationContext(new AsyncSyncSynchronizationContext(_uiContext));

                _initialized = true;
            }
        }

        private SynchronizationContext EnsureInitializedAndGetContext()
        {
            if (!_initialized)
            {
                Initialize();
            }

            lock (_lock)
            {
                if (_uiContext == null)
                    throw new InvalidOperationException("消息泵未正确初始化：UI SynchronizationContext 不可用");
                return _uiContext;
            }
        }

        public Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
        {
            var context = EnsureInitializedAndGetContext();

            var tcs = new TaskCompletionSource<T>();
            context.Post(async _ =>
            {
                try
                {
                    var result = await action();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public Task RunOnUiThreadAsync(Func<Task> action)
        {
            var context = EnsureInitializedAndGetContext();

            var tcs = new TaskCompletionSource<bool>();
            context.Post(async _ =>
            {
                try
                {
                    await action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public T RunOnUiThread<T>(Func<T> action)
        {
            var context = EnsureInitializedAndGetContext();

            var result = default(T);
            var exception = default(Exception);
            var done = new ManualResetEventSlim(false);

            context.Post(_ =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    done.Set();
                }
            }, null);

            done.Wait();

            if (exception != null)
                throw new AggregateException(exception);

            return result!;
        }

        public void RunOnUiThread(Action action)
        {
            RunOnUiThread<object?>(() =>
            {
                action();
                return null;
            });
        }

        public bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            EnsureInitializedAndGetContext();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                    return true;

                RunOnUiThread(() => Application.DoEvents());
                Thread.Sleep(10);
            }

            return condition();
        }

        public Form CreateForm(int width = 800, int height = 600)
        {
            return RunOnUiThread(() => new Form
            {
                Width = width,
                Height = height,
                WindowState = FormWindowState.Normal,
                StartPosition = FormStartPosition.CenterScreen
            });
        }

        public void Shutdown()
        {
            lock (_lock)
            {
                if (!_initialized) return;

                var shutdownCompleted = false;
                var shutdownTimeout = TimeSpan.FromSeconds(3);

                _uiContext?.Post(_ =>
                {
                    try
                    {
                        if (_pumpForm != null && !_pumpForm.IsDisposed)
                        {
                            _pumpForm.Close();
                            _pumpForm.Dispose();
                        }
                        _pumpForm = null;
                    }
                    catch
                    {
                    }
                    finally
                    {
                        shutdownCompleted = true;
                    }
                }, null);

                var spinSw = System.Diagnostics.Stopwatch.StartNew();
                while (!shutdownCompleted && spinSw.Elapsed < shutdownTimeout)
                {
                    System.Threading.Thread.Sleep(10);
                }

                _pumpThread?.Join(TimeSpan.FromSeconds(3));
                _pumpThread = null;

                if (_originalContext != null)
                {
                    SynchronizationContext.SetSynchronizationContext(_originalContext);
                    _originalContext = null;
                }

                _uiContext = null;
                _initialized = false;
            }
        }

    }

    internal class AsyncSyncSynchronizationContext : SynchronizationContext
    {
        private readonly SynchronizationContext _innerContext;

        public AsyncSyncSynchronizationContext(SynchronizationContext innerContext)
        {
            _innerContext = innerContext;
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _innerContext.Post(d, state);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            _innerContext.Send(d, state);
        }

        public override SynchronizationContext CreateCopy()
        {
            return new AsyncSyncSynchronizationContext(_innerContext.CreateCopy());
        }
    }
}
