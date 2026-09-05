namespace CentralConsole.AsyncSyncBridge.Utilities
{
    public static class AsyncHelper
    {
        public static Task RunSyncAsAsync(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return Task.Run(action);
        }

        public static Task<T> RunSyncAsAsync<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return Task.Run(action);
        }

        public static async Task<Task[]> WhenAllOrException(params Task[] tasks)
        {
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));

            var allTasks = Task.WhenAll(tasks);
            try
            {
                await allTasks;
            }
            catch
            {
            }

            return tasks;
        }

        public static async Task<bool> WithTimeout(Task task, TimeSpan timeout)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
            return completedTask == task;
        }

        public static async Task<T?> WithTimeout<T>(Task<T> task, TimeSpan timeout)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
            if (completedTask == task)
            {
                return await task;
            }
            return default;
        }

        public static async Task RetryAsync(Func<Task> action, int maxRetries = 3, int delayMs = 100)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await action();
                    return;
                }
                catch when (i < maxRetries - 1)
                {
                    await Task.Delay(delayMs);
                }
            }
        }

        public static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 3, int delayMs = 100)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await action();
                }
                catch when (i < maxRetries - 1)
                {
                    await Task.Delay(delayMs);
                }
            }

            return await action();
        }
    }
}
