namespace CentralConsole.AsyncSyncBridge.Utilities
{
    public static class SyncHelper
    {
        public static void WaitSync(Func<Task> asyncAction)
        {
            if (asyncAction == null) throw new ArgumentNullException(nameof(asyncAction));
            Task.Run(async () => await asyncAction()).GetAwaiter().GetResult();
        }

        public static T WaitSync<T>(Func<Task<T>> asyncAction)
        {
            if (asyncAction == null) throw new ArgumentNullException(nameof(asyncAction));
            return Task.Run(async () => await asyncAction()).GetAwaiter().GetResult();
        }

        public static bool WaitSync(Func<Task> asyncAction, TimeSpan timeout)
        {
            if (asyncAction == null) throw new ArgumentNullException(nameof(asyncAction));

            var task = Task.Run(async () => await asyncAction());
            return task.Wait(timeout);
        }

        public static (bool completed, T? result) WaitSync<T>(Func<Task<T>> asyncAction, TimeSpan timeout)
        {
            if (asyncAction == null) throw new ArgumentNullException(nameof(asyncAction));

            var task = Task.Run(async () => await asyncAction());
            if (task.Wait(timeout))
            {
                return (true, task.Result);
            }
            return (false, default);
        }
    }
}
