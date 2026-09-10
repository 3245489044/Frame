using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>任务处理委托：item → TResult。暂停为合作式（处理体内调用 <c>WaitIfPausedAsync</c>），取消经 ct 传导。</summary>
public delegate Task<TResult> TaskPoolHandler<TItem, TResult>(TItem item, string executionId, CancellationToken cancellationToken);

/// <summary>
/// 通用任务池（状态机，canonical）。组合：
/// <see cref="TaskStateMachine"/>（任务生命周期）+ <see cref="ITaskPoolPersistence{TItem}"/>（持久化镜像）
/// + <see cref="TaskPauseHandle"/>（任务级暂停，TCS）+ <see cref="TaskPauseManager"/>（队列级全局暂停）+ 有界并发闸门（SemaphoreSlim）。
/// 激活为独立标志（<see cref="IActivatableTaskPoolItem.Active"/>，与写入解耦）：消费门仅采用「NotStarted 且 Active=true」的任务。
/// 与 Agent 无关：TItem 仅要求实现 <see cref="ITaskPoolItem"/>，TResult 为执行产物，均由消费方注入。
/// </summary>
public interface ITaskPool<TItem> : IAsyncDisposable
    where TItem : ITaskPoolItem
{
    /// <summary>任务状态变更（写即触发观测）。</summary>
    event EventHandler<TaskPoolStateChangedEventArgs>? TaskStateChanged;

    /// <summary>入队（持久化 NotStarted + 唤醒消费者）。实际是否被消费由条目 Active 标志呈递（Active=false 仅持久化休眠）。</summary>
    Task<long> EnqueueAsync(TItem item, CancellationToken ct = default);

    /// <summary>翻转激活标志（激活与写入解耦）：置 false=休眠（仅持久化），置 true=唤醒消费使休眠任务可出队。
    /// 仅当条目实现 <see cref="IActivatableTaskPoolItem"/> 才生效，返回是否支持/命中。</summary>
    Task<bool> SetActiveAsync(string taskKey, bool active, CancellationToken ct = default);

    /// <summary>暂停某任务（非终止）。双源：处理体主动（Loop）或外部强制（External）。</summary>
    Task PauseAsync(string taskKey, string? source = null, string? reason = null, CancellationToken ct = default);

    /// <summary>恢复某任务（非终止；挂起方被唤醒继续）。</summary>
    Task ResumeAsync(string taskKey, CancellationToken ct = default);

    /// <summary>取消某任务（终止域，Cancelled）。</summary>
    Task CancelAsync(string taskKey, CancellationToken ct = default);

    /// <summary>暂停全部活动任务。</summary>
    Task<int> PauseAllAsync(CancellationToken ct = default);

    /// <summary>恢复全部已暂停任务。</summary>
    Task<int> ResumeAllAsync(CancellationToken ct = default);

    /// <summary>查询任务状态。</summary>
    Task<TaskExecutionStatus> GetStatusAsync(string taskKey, CancellationToken ct = default);

    /// <summary>合作式暂停挂起点：若任务已暂停则阻塞至恢复（Agent 轮间挂点 / 处理体循环边界调用）。</summary>
    Task WaitIfPausedAsync(string taskKey, CancellationToken ct = default);

    /// <summary>启动消费循环（订阅"写即激活"信号）。</summary>
    void Start(CancellationToken ct = default);

    /// <summary>停止消费循环（销毁式，不可恢复；暂停 ≠ 停止）。</summary>
    Task StopAsync();

    /// <summary>回填：从持久化重播非终止任务（重启续跑 / 手动驱动）。</summary>
    Task<int> ProcessPendingAsync(CancellationToken ct = default);
}
