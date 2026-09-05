using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Common.Async;

namespace Modules.Common.AsyncSync
{
    public class AsyncSyncOrchestrator : IAsyncSyncOrchestrator
    {
        private readonly Action<string>? _logAction;

        public AsyncSyncOrchestrator(Action<string>? logAction = null)
        {
            _logAction = logAction;
        }

        public T RunSync<T>(Func<Task<T>> asyncFunc)
        {
            if (asyncFunc == null)
                throw new ArgumentNullException(nameof(asyncFunc));

            try
            {
                return Task.Run(asyncFunc).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError($"AsyncSyncOrchestrator.RunSync<T> failed: {ex.Message}");
                throw;
            }
        }

        public void RunSync(Func<Task> asyncFunc)
        {
            if (asyncFunc == null)
                throw new ArgumentNullException(nameof(asyncFunc));

            try
            {
                Task.Run(asyncFunc).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError($"AsyncSyncOrchestrator.RunSync failed: {ex.Message}");
                throw;
            }
        }

        public async Task FireAndForgetAsync(Func<Task> asyncFunc, string operationName)
        {
            if (asyncFunc == null)
                throw new ArgumentNullException(nameof(asyncFunc));

            try
            {
                await asyncFunc();
            }
            catch (Exception ex)
            {
                LogError($"FireAndForget operation '{operationName}' failed: {ex.Message}");
            }
        }

        public async void FireAndForget(Func<Task> asyncFunc, string operationName)
        {
            AsyncHelper.FireAndForget(asyncFunc, operationName, ex => LogError($"FireAndForget '{operationName}' failed: {ex.Message}"));
        }

        public Task DelayAsync(int millisecondsDelay, CancellationToken cancellationToken = default)
        {
            return Task.Delay(millisecondsDelay, cancellationToken);
        }

        public bool IsUiThread
        {
            get
            {
                var context = SynchronizationContext.Current;
                return context != null && context.GetType().Name.Contains("WindowsForms");
            }
        }

        public ExecutionContextInfo GetCurrentContext()
        {
            var context = SynchronizationContext.Current;
            return new ExecutionContextInfo
            {
                IsUiThread = IsUiThread,
                IsThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread,
                ManagedThreadId = Environment.CurrentManagedThreadId,
                HasSynchronizationContext = context != null,
                Description = context != null
                    ? $"SynchronizationContext: {context.GetType().Name}"
                    : "No SynchronizationContext"
            };
        }

        private void LogError(string message)
        {
            try
            {
                _logAction?.Invoke($"[ERROR] {message}");
                Console.Error.WriteLine(message);
            }
            catch
            {
            }
        }
    }
}
