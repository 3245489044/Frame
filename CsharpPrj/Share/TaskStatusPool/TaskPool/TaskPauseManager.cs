using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 队列级全局暂停（TCS 反应式实现，零轮询；暂停语义非终止）。自包含实现，替代原 Modules.StateMachine.PauseManager。
/// 用于消费循环在派发新任务前的全局暂停闸（在途自然跑完）。
/// 恢复由 <see cref="ResumeGlobalPause"/> 确定性状态变更精确触发（镜像 <see cref="TaskPauseHandle"/> 的 TCS 范式），无任何周期轮询。
/// </summary>
public sealed class TaskPauseManager
{
    private readonly object _lock = new();
    private TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isPaused;

    public bool IsGloballyPaused
    {
        get { lock (_lock) return _isPaused; }
    }

    public void RequestGlobalPause()
    {
        lock (_lock)
        {
            if (_isPaused) return;
            _isPaused = true;
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void ResumeGlobalPause()
    {
        lock (_lock)
        {
            if (!_isPaused) return;
            _isPaused = false;
            _tcs.TrySetResult(true);
        }
    }

    public async Task WaitForResumeAsync(CancellationToken cancellationToken)
    {
        Task waitTask;

        lock (_lock)
        {
            if (!_isPaused) return;
            waitTask = _tcs.Task;
        }

        // WaitAsync 仅观察本次取消，不污染共享 _tcs（取消不使后续等待失效）。
        await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
