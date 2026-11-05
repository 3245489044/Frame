using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Threading;

/// <summary>作业键：同一逻辑键可对应多个版本，只有最新版本的结果允许提交。</summary>
public readonly record struct JobKey(long Key, long Version);

/// <summary>一次作业完成结果；<see cref="Stale"/> 由调度器在提交前判定。</summary>
public readonly record struct JobOutcome(
    JobKey Key,
    long Sequence,
    object? Payload,
    Exception? Error,
    bool Stale);

/// <summary>
/// 代际作业调度器：后台执行、主线程定序提交、过期结果丢弃。
/// <para>语义：同一 <c>Key</c> 只有最后提交的 <c>Version</c> 结果可被 <see cref="Pump"/> 交付；
/// 更旧版本的结果即使先完成也会被丢弃（计入 <see cref="DiscardedCount"/>）。</para>
/// <para>提交通过 <see cref="IThreadDispatcher"/> 回投（MainThreadDispatcher/SerialDispatcher 均可）；
/// 未提供派发器时在 Pump 调用线程直接提交，便于无窗口自检。</para>
/// </summary>
public sealed class GenerationJobScheduler : IDisposable
{
    private readonly IThreadDispatcher? _dispatcher;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentQueue<JobOutcome> _completed = new();
    private readonly Dictionary<long, long> _latest = new();
    private readonly HashSet<long> _cancelled = new();
    private readonly object _sync = new();
    private readonly ManualResetEventSlim _idle = new(true);

    private long _sequence;
    private int _pending;
    private int _discarded;
    private int _disposed;

    public GenerationJobScheduler(IThreadDispatcher? dispatcher = null, int maxConcurrency = 0)
    {
        _dispatcher = dispatcher;
        int concurrency = maxConcurrency > 0
            ? maxConcurrency
            : Math.Max(1, Environment.ProcessorCount - 1);
        _slots = new SemaphoreSlim(concurrency, concurrency);
    }

    public int PendingCount => Volatile.Read(ref _pending);
    public int DiscardedCount => Volatile.Read(ref _discarded);
    public int CompletedCount => _completed.Count;

    /// <summary>提交一个作业；返回实际采用的键（含版本）。同一键的新版本会令旧结果过期。</summary>
    public JobKey Submit(JobKey key, Func<object?> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (Volatile.Read(ref _disposed) != 0)
            return key;

        lock (_sync)
        {
            _latest[key.Key] = key.Version;
            _cancelled.Remove(key.Key);
        }

        _idle.Reset();
        Interlocked.Increment(ref _pending);

        _ = Task.Run(async () =>
        {
            object? payload = null;
            Exception? error = null;

            try
            {
                await _slots.WaitAsync().ConfigureAwait(false);
                try
                {
                    payload = work();
                }
                finally
                {
                    _slots.Release();
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }

            long sequence = Interlocked.Increment(ref _sequence);
            _completed.Enqueue(new JobOutcome(key, sequence, payload, error, Stale: false));

            if (Interlocked.Decrement(ref _pending) == 0)
                _idle.Set();
        });

        return key;
    }

    /// <summary>取消某键的全部在途结果；之后提交同键会重新激活。</summary>
    public void Cancel(long key)
    {
        lock (_sync)
        {
            _cancelled.Add(key);
            _latest[key] = long.MinValue;
        }
    }

    /// <summary>
    /// 主线程泵：按完成顺序取出结果并提交。过期（版本被替换/已取消）的结果不交付，只计入 DiscardedCount。
    /// </summary>
    public int Pump(int maxCommits, Action<JobOutcome> commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (maxCommits <= 0) return 0;

        int delivered = 0;
        while (delivered < maxCommits && _completed.TryDequeue(out var outcome))
        {
            bool stale;
            lock (_sync)
            {
                stale = outcome.Stale
                        || _cancelled.Contains(outcome.Key.Key)
                        || _latest.TryGetValue(outcome.Key.Key, out long latest) && latest != outcome.Key.Version;
            }

            if (stale)
            {
                Interlocked.Increment(ref _discarded);
                continue;
            }

            var accepted = outcome with { Stale = false };
            if (_dispatcher != null)
                _dispatcher.Post(() => commit(accepted));
            else
                commit(accepted);

            delivered++;
        }

        return delivered;
    }

    /// <summary>等待所有在途作业完成（仅供自检/退出前使用；游戏循环不要调用）。</summary>
    public bool WaitForIdle(int timeoutMilliseconds = 5000)
    {
        if (PendingCount == 0) return true;
        return _idle.Wait(timeoutMilliseconds);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        lock (_sync)
        {
            foreach (long key in _latest.Keys)
                _cancelled.Add(key);
        }

        _idle.Set();
        _slots.Dispose();
    }
}
