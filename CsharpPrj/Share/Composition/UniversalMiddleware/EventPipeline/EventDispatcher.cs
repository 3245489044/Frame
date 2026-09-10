
namespace UniversalMiddleware.EventPipeline
{
    public class EventDispatcher : IEventDispatcher
    {
        public async Task DispatchAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var syncContext = SynchronizationContext.Current;
            if (syncContext != null && syncContext.GetType().Name != "WindowsFormsSynchronizationContext")
            {
                var tcs = new TaskCompletionSource();
                syncContext.Post(async _ =>
                {
                    try
                    {
                        await action();
                        tcs.SetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }, null);
                await tcs.Task;
            }
            else if (syncContext is WindowsFormsSynchronizationContext)
            {
                var tcs = new TaskCompletionSource();
                syncContext.Post(async _ =>
                {
                    try
                    {
                        await action();
                        tcs.SetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }, null);
                await tcs.Task;
            }
            else
            {
                await action();
            }
        }

        public void Dispatch(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var syncContext = SynchronizationContext.Current;
            if (syncContext != null)
            {
                syncContext.Send(_ => action(), null);
            }
            else
            {
                action();
            }
        }
    }
}


