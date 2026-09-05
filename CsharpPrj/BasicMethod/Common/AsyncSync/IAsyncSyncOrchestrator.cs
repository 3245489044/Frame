using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Common.AsyncSync
{
    public class ExecutionContextInfo
    {
        public bool IsUiThread { get; set; }
        public bool IsThreadPoolThread { get; set; }
        public int ManagedThreadId { get; set; }
        public bool HasSynchronizationContext { get; set; }
        public string Description { get; set; } = "";
    }

    public interface IAsyncSyncOrchestrator
    {

        T RunSync<T>(Func<Task<T>> asyncFunc);

        void RunSync(Func<Task> asyncFunc);


        Task FireAndForgetAsync(Func<Task> asyncFunc, string operationName);

        void FireAndForget(Func<Task> asyncFunc, string operationName);


        Task DelayAsync(int millisecondsDelay, CancellationToken cancellationToken = default);


        bool IsUiThread { get; }

        ExecutionContextInfo GetCurrentContext();
    }
}
