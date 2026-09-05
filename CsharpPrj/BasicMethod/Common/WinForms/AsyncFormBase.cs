
using System.ComponentModel;

namespace Modules.Common.WinForms
{
    public class AsyncFormBase : Form
    {
        private readonly CancellationTokenSource _formCts = new();
        private bool _isLoaded;
        private bool _isShown;
        private int _loadingCount;

        protected CancellationToken FormCancellationToken => _formCts.Token;

        protected bool IsLoading => _loadingCount > 0;

        protected bool IsLoaded => _isLoaded;

        protected bool IsShown => _isShown;

        public AsyncFormBase()
        {
            DoubleBuffered = true;
        }

        protected virtual Task OnAsyncLoad()
        {
            return Task.CompletedTask;
        }

        protected virtual Task OnAsyncShown()
        {
            return Task.CompletedTask;
        }

        protected virtual Task OnAsyncClosing(CancelEventArgs e)
        {
            return Task.CompletedTask;
        }

        protected virtual void OnAsyncError(Exception exception)
        {
#if DEBUG

#endif
        }

        #region 生命周期

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                await OnAsyncLoad();
            }
            catch (Exception ex) when (!System.Diagnostics.Debugger.IsAttached)
            {

                OnAsyncError(ex);
            }
            finally
            {
                _isLoaded = true;
            }
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            try
            {
                await OnAsyncShown();
            }
            catch (Exception ex) when (!System.Diagnostics.Debugger.IsAttached)
            {

                OnAsyncError(ex);
            }
            finally
            {
                _isShown = true;
            }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                await OnAsyncClosing(e);
            }
            catch (Exception ex) when (!System.Diagnostics.Debugger.IsAttached)
            {

                OnAsyncError(ex);
            }
            finally
            {
                _formCts.Cancel();
                base.OnFormClosing(e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _formCts.Cancel();
                _formCts.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion

        #region 线程安全 UI 更新

        protected void InvokeIfRequired(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (InvokeRequired)
                Invoke(action);
            else
                action();
        }

        protected void BeginInvokeIfRequired(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (InvokeRequired)
                BeginInvoke(action);
            else
                action();
        }

        protected void SafeSet<T>(T control, Action<T> action) where T : Control
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (control.InvokeRequired)
                control.Invoke(() => action(control));
            else
                action(control);
        }

        protected TResult SafeGet<T, TResult>(T control, Func<T, TResult> func) where T : Control
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (control.InvokeRequired)
                return (TResult)control.Invoke(func, control);
            else
                return func(control);
        }

        #endregion

        #region 加载状态管理

        protected virtual void ShowLoading()
        {
            Interlocked.Increment(ref _loadingCount);
            Cursor = Cursors.WaitCursor;
        }

        protected virtual void HideLoading()
        {
            if (Interlocked.Decrement(ref _loadingCount) <= 0)
            {
                _loadingCount = 0;
                Cursor = Cursors.Default;
            }
        }

        protected async Task WithLoading(Func<Task> asyncAction)
        {
            ShowLoading();
            try
            {
                await asyncAction();
            }
            finally
            {
                HideLoading();
            }
        }

        protected async Task<TResult> WithLoading<TResult>(Func<Task<TResult>> asyncFunc)
        {
            ShowLoading();
            try
            {
                return await asyncFunc();
            }
            finally
            {
                HideLoading();
            }
        }

        #endregion
    }
}
