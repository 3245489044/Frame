using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 触发频率限制门：仅在"有事件到达"时激活（被动响应 + 节流），非周期轮询。
/// 距上次放行已达窗口 → 立即通过；窗口内到达则 await 到窗口边界（精确定时器，只在被限频时激活）。
/// 串行前提：单消费线程（MaxConcurrency=1），同一时刻至多一个等待者，无需合并窗口内多请求。
/// 窗口为 <see cref="TimeSpan.Zero"/> 时等价于不限频（立即通过，不引入任何延迟）。
/// </summary>
public sealed class TriggerRateGate : IDisposable
{
    private readonly TimeSpan _minInterval;
    private readonly object _lock = new();
    private long _lastReleaseTicks;
    private int _disposed;

    /// <param name="minInterval">相邻两次放行的最小间隔；<see cref="TimeSpan.Zero"/> 表示不限频。</param>
    public TriggerRateGate(TimeSpan minInterval)
    {
        _minInterval = minInterval > TimeSpan.Zero ? minInterval : TimeSpan.Zero;
    }

    /// <summary>申请一次放行；窗口内到达则 await 到窗口边界后放行。</summary>
    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        TimeSpan remaining;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);

            var now = Stopwatch.GetTimestamp();
            if (_lastReleaseTicks == 0 || Elapsed(now) >= _minInterval)
            {
                _lastReleaseTicks = now;
                return; // 立即放行
            }

            remaining = _minInterval - Elapsed(now);
        }

        // 仅在有事件但被限频时激活精确定时器（非周期轮询；无事件则完全静默）。
        await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        lock (_lock) _lastReleaseTicks = Stopwatch.GetTimestamp();
    }

    private TimeSpan Elapsed(long now)
        => TimeSpan.FromTicks((now - _lastReleaseTicks) * TimeSpan.TicksPerSecond / Stopwatch.Frequency);

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}