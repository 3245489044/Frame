using System;
using System.Threading.Tasks;

namespace Modules.Common.StepCoordination
{
    public static class StepCoordinationExtensions
    {
        public static IStepCoordinator CreateDefault()
        {
            return new AsyncStepCoordinator();
        }

        public static IStepCoordinator CreateWithTimeout(Modules.Common.AsyncSync.IAsyncSyncOrchestrator orchestrator)
        {
            if (orchestrator == null)
                throw new ArgumentNullException(nameof(orchestrator));

            return new AsyncStepCoordinator(orchestrator);
        }

        public static async Task<bool> ExecuteWithStepWait(
            this IStepCoordinator coordinator,
            Func<string, Task<bool>> executeLine,
            string command,
            int timeoutMs = 30000)
        {
            if (coordinator == null)
                throw new ArgumentNullException(nameof(coordinator));

            var success = await executeLine(command);

            if (coordinator.HasPendingSignal)
            {
                var stepSuccess = await coordinator.WaitForStepAsync(timeoutMs);
                if (!stepSuccess)
                {

                }
            }

            return success;
        }
    }
}
