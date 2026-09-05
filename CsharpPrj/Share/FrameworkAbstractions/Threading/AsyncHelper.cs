#nullable enable

namespace FrameworkAbstractions.Threading;

public static class AsyncHelper
{
    public static async void FireAndForget(Func<Task> asyncAction, Action<Exception>? onError = null)
    {
        try { await asyncAction(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { onError?.Invoke(ex); }
    }

    public static async void FireAndForget(Action action, Action<Exception>? onError = null)
    {
        try { await Task.Run(action); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { onError?.Invoke(ex); }
    }

    public static Task RunOnSynchronizationContext(Func<Task> asyncAction)
    {
        var sc = SynchronizationContext.Current;
        if (sc != null)
        {
            var tcs = new TaskCompletionSource<object?>();
            sc.Post(async _ =>
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
            }, null);
            return tcs.Task;
        }

        return asyncAction();
    }
}