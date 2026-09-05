using System;
using System.Threading.Tasks;

namespace Modules.Common.StepCoordination
{
    public interface IStepCoordinator
    {
        TaskCompletionSource<bool> GetOrCreateSignal();

        void CompleteStep(bool success = true);

        void CompleteStepWithException(Exception ex);

        Task<bool> WaitForStepAsync(int timeoutMs = 30000);

        void Reset();

        bool HasPendingSignal { get; }
    }
}
