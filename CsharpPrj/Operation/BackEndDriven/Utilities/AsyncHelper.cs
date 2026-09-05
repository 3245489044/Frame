
using System;
using System.Threading.Tasks;

namespace BackEndDriven.Utilities
{
    public static class AsyncHelper
    {
        public static void RunSync(Func<Task> asyncAction, int timeoutMilliseconds = 30000)
        {
            RunSyncCore<object?>(async () => { await asyncAction(); return null; }, timeoutMilliseconds);
        }

        public static TResult RunSync<TResult>(Func<Task<TResult>> asyncFunc, int timeoutMilliseconds = 30000)
        {
            return RunSyncCore(asyncFunc, timeoutMilliseconds);
        }

        private static TResult RunSyncCore<TResult>(Func<Task<TResult>> asyncFunc, int timeoutMilliseconds)
        {
            if (asyncFunc == null)
                throw new ArgumentNullException(nameof(asyncFunc));

            var task = Task.Run(asyncFunc);
            if (timeoutMilliseconds > 0)
            {
                if (!task.Wait(timeoutMilliseconds))
                {
                    throw new TimeoutException(
                        $"异步任务未在 {timeoutMilliseconds}ms 内完成，可能已死锁或挂起。");
                }
            }

            return task.GetAwaiter().GetResult();
        }
    }
}
