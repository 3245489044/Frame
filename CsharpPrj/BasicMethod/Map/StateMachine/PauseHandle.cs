using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.StateMachine
{
    #region 暂停句柄

    public class PauseHandle : IPauseHandle
    {
        private readonly object _lock = new();
        private TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isPaused;

        public bool IsPaused
        {
            get { lock (_lock) return _isPaused; }
        }

        public void Pause()
        {
            lock (_lock)
            {
                if (_isPaused) return;
                _isPaused = true;
                _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                if (!_isPaused) return;
                _isPaused = false;
                _tcs.TrySetResult(true);
            }
        }

        public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
        {
            Task waitTask;

            lock (_lock)
            {
                if (!_isPaused) return;
                waitTask = _tcs.Task;
            }

            using var registration = cancellationToken.Register(() =>
            {
                lock (_lock)
                {
                    _tcs.TrySetCanceled(cancellationToken);
                }
            });

            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _isPaused = false;
                _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    #endregion
}
