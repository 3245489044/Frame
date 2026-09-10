#nullable enable
using System.Runtime.Versioning;
using System.Windows.Forms;
using FrameworkAbstractions.Threading;

namespace FrameworkAbstractions.UI.WinForms
{
    public static class AsyncUiHelper
    {
        public static void FireAndForget(
            Func<Task> asyncAction,
            Action<Exception>? onError = null)
        {
            AsyncHelper.FireAndForget(asyncAction, onError);
        }

        public static Task RunOnUiThread(Control control, Func<Task> asyncAction)
        {
            if (!control.InvokeRequired)
            {
                return asyncAction();
            }

            var tcs = new TaskCompletionSource<object?>();
            control.BeginInvoke(async () =>
            {
                try
                {
                    await asyncAction();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        public static void ExecuteOnUiThread(Control control, Action action)
        {
            if (!control.InvokeRequired)
            {
                action();
            }
            else
            {
                control.Invoke(action);
            }
        }
    }
}
