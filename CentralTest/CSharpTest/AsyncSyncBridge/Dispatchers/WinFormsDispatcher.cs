using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace CentralConsole.AsyncSyncBridge.Dispatchers
{
    public sealed class WinFormsDispatcher : IDispatcher, IDisposable
    {
        private Form? _pumpForm;
        private bool _disposed;
        private readonly object _lock = new();

        public bool InvokeRequired
        {
            get
            {
                var form = _pumpForm;
                return form == null || form.IsDisposed || form.InvokeRequired;
            }
        }

        public bool IsInitialized => _pumpForm != null && !_pumpForm.IsDisposed;

        public void EnsureInitialized()
        {
            if (IsInitialized) return;

            lock (_lock)
            {
                if (IsInitialized) return;

                _pumpForm = new Form
                {
                    WindowState = FormWindowState.Minimized,
                    ShowInTaskbar = false,
                    ControlBox = false,
                    FormBorderStyle = FormBorderStyle.None
                };
                _pumpForm.Show();
                Application.DoEvents();
            }
        }

        public void Invoke(Action action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!_pumpForm!.InvokeRequired)
            {
                action();
                return;
            }

            var exception = (Exception?)null;
            _pumpForm.Invoke(() =>
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

            if (!_pumpForm!.InvokeRequired)
            {
                return action();
            }

            var result = default(T);
            var exception = (Exception?)null;
            _pumpForm.Invoke(() =>
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

            if (!_pumpForm!.InvokeRequired)
            {
                return action();
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pumpForm.BeginInvoke(async () =>
            {
                try
                {
                    await action();
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        public Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            EnsureInitialized();
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!_pumpForm!.InvokeRequired)
            {
                return action();
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pumpForm.BeginInvoke(async () =>
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
            });

            return tcs.Task;
        }

        public Form CreateForm(int width = 800, int height = 600)
        {
            EnsureInitialized();
            var form = new Form { Size = new System.Drawing.Size(width, height) };
            form.Show();
            Application.DoEvents();
            return form;
        }

        public void CloseForm(Form form)
        {
            if (form == null || form.IsDisposed) return;
            form.Close();
            Application.DoEvents();
        }

        public void Cleanup()
        {
            lock (_lock)
            {
                if (_pumpForm != null && !_pumpForm.IsDisposed)
                {
                    _pumpForm.Close();
                    _pumpForm.Dispose();
                    _pumpForm = null;
                }
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
