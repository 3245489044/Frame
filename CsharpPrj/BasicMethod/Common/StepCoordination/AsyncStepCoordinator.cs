using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Common.AsyncSync;

namespace Modules.Common.StepCoordination
{
    public class AsyncStepCoordinator : IStepCoordinator
    {
        private TaskCompletionSource<bool>? _currentSignal;
        private readonly object _lock = new();
        private readonly IAsyncSyncOrchestrator? _orchestrator;

        public AsyncStepCoordinator(IAsyncSyncOrchestrator? orchestrator = null)
        {
            _orchestrator = orchestrator;
        }

        public bool HasPendingSignal
        {
            get
            {
                lock (_lock)
                {
                    return _currentSignal != null;
                }
            }
        }

        public TaskCompletionSource<bool> GetOrCreateSignal()
        {
            lock (_lock)
            {
                if (_currentSignal == null)
                {
                    _currentSignal = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
                return _currentSignal;
            }
        }

        public void CompleteStep(bool success = true)
        {
            TaskCompletionSource<bool>? signal;
            lock (_lock)
            {
                signal = _currentSignal;
                _currentSignal = null;
            }
            signal?.TrySetResult(success);
        }

        public void CompleteStepWithException(Exception ex)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));

            TaskCompletionSource<bool>? signal;
            lock (_lock)
            {
                signal = _currentSignal;
                _currentSignal = null;
            }
            signal?.TrySetException(ex);
        }

        public async Task<bool> WaitForStepAsync(int timeoutMs = 30000)
        {
            var signal = GetOrCreateSignal();

            try
            {
                if (_orchestrator != null)
                {
                    var delayTask = _orchestrator.DelayAsync(timeoutMs);
                    var completedTask = await Task.WhenAny(signal.Task, delayTask);

                    if (completedTask == delayTask)
                    {
                        return false;
                    }
                }
                else
                {
                    using var cts = new CancellationTokenSource(timeoutMs);
                    await signal.Task.WaitAsync(cts.Token);
                }

                return await signal.Task;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (AggregateException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                var oldSignal = _currentSignal;
                _currentSignal = null;
                oldSignal?.TrySetCanceled();
            }
        }
    }
}
