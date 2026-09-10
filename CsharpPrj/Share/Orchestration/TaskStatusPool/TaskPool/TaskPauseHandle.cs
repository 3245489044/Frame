using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务级暂停句柄（TCS 实现，合作式挂起）。自包含实现，替代原 Modules.StateMachine.PauseHandle。
/// 在途处理体调用 <see cref="WaitIfPausedAsync"/> 阻塞至 <see cref="Resume"/>（Running ⇄ Paused 非终止）。
/// </summary>
public sealed class TaskPauseHandle
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
