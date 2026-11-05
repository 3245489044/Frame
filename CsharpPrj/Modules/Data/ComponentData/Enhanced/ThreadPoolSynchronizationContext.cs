
using System.Threading;

namespace Modules.ComponentData.Enhanced
{
    public sealed class ThreadPoolSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            ThreadPool.QueueUserWorkItem(static s =>
            {
                var (callback, stateObj) = ((SendOrPostCallback, object?))s!;
                callback(stateObj);
            }, (d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }
}
