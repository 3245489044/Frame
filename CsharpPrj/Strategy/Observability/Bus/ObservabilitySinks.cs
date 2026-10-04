using Strategy.Observability.Diagnostics;
using Strategy.Observability.Persistence;

namespace Strategy.Observability.Bus;

/// <summary>
/// 复合 Sink 成员（E4）：<see cref="Push"/> 立即接收/排队一条已就绪事件；
/// <see cref="FlushAsync"/> 由调用方显式驱动（落库 / 冲刷）。
/// 成败对调用方可见：Push 为同步接收，FlushAsync 失败抛 <see cref="SinkWriteException"/>（不再内嵌后台 DrainLoop）。
/// </summary>
public interface IObservabilitySink
{
    /// <summary>同步接收一条已就绪事件（本实现立即记录/排队；持久化由 <see cref="FlushAsync"/> 显式完成）。</summary>
    void Push(PersistedEvent evt);

    /// <summary>调用方驱动的显式排空：把 Push 收到的批次真正持久化。失败抛 <see cref="SinkWriteException"/>。</summary>
    Task FlushAsync(CancellationToken ct = default);
}

/// <summary>
/// 持久化 Sink（E5 内核实例）：无后台线程、无内部重试。Push 缓冲，FlushAsync 单批落库；
/// 落库失败不静默记日志，直接抛 <see cref="SinkWriteException"/>（缓冲保留，供调用方重放 / at-least-once）。
/// <para>
/// 有界与可重入：缓冲封顶（<see cref="MaxBuffered"/>，超限丢最旧并入 <see cref="DroppedCount"/> 可见账）；
/// 失败不回滚到队首（无 head-of-line 阻塞——时序由 <see cref="PersistedEvent.EventSeq"/> 决定，不依赖文件行序）；
/// 并发 FlushAsync 由闸串行化（同一批不会被重复落库之外的并发写放大）。
/// </para>
/// </summary>
public sealed class PersistentEventSink : IObservabilitySink
{
    /// <summary>缺省缓冲上限（条）：超限丢最旧并记账，防 store 持续故障时缓冲无界膨胀。</summary>
    public const int DefaultMaxBuffered = 65_536;

    private readonly IEventStore _store;
    private readonly int _maxBuffered;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private List<PersistedEvent> _buffer = new();
    private long _dropped;

    public PersistentEventSink(IEventStore store, int maxBuffered = DefaultMaxBuffered)
    {
        _store = PortGuard.Required(store, nameof(store));
        if (maxBuffered <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBuffered), maxBuffered, "缓冲上限须为正数");
        _maxBuffered = maxBuffered;
    }

    /// <summary>缓冲上限（条）。</summary>
    public int MaxBuffered => _maxBuffered;

    /// <summary>当前尚未排空（FlushAsync）的缓冲条数（诊断可见性）。</summary>
    public int BufferedCount
    {
        get { lock (_gate) return _buffer.Count; }
    }

    /// <summary>因缓冲超限被丢弃的条数（有界缓冲的可见账；丢弃不静默）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    public void Push(PersistedEvent evt)
    {
        PortGuard.Required(evt, nameof(evt));
        lock (_gate)
        {
            _buffer.Add(evt);

            var overflow = _buffer.Count - _maxBuffered;
            if (overflow <= 0) return;

            _buffer.RemoveRange(0, overflow);
            Interlocked.Add(ref _dropped, overflow);
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _flushGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<PersistedEvent> batch;
            lock (_gate)
            {
                if (_buffer.Count == 0) return;
                batch = _buffer.ToArray();   // 前缀快照：失败时不重排缓冲（无 head-of-line 累积）
            }

            try
            {
                await _store.AppendBatchAsync(batch, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new SinkWriteException(
                    $"观测事件显式排空落库失败（待落库 {BufferedCount} 条，累计丢弃 {DroppedCount} 条），缓冲保留可重试。",
                    ex);
            }

            lock (_gate)
            {
                // 只移除确实已落库的前缀（按引用比对：缓冲曾因超限丢弃队首时不误删未落库条目，重复落库由 EventSeq 行键幂等承担）。
                var written = 0;
                while (written < batch.Count && written < _buffer.Count
                       && ReferenceEquals(_buffer[written], batch[written]))
                    written++;

                if (written > 0) _buffer.RemoveRange(0, written);
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }
}

/// <summary>内存环形缓冲热查 Sink（E4）：保留最近 N 条便于进程内即时查询，不承担事实源（无需落库排空）。</summary>
public sealed class HotRingSink : IObservabilitySink
{
    private readonly Queue<PersistedEvent> _queue = new();
    private readonly object _gate = new();
    private readonly int _capacity;

    public HotRingSink(int capacity = 1000)
    {
        _capacity = Math.Max(1, capacity);
    }

    public void Push(PersistedEvent evt)
    {
        lock (_gate)
        {
            _queue.Enqueue(evt);
            while (_queue.Count > _capacity) _queue.Dequeue();
        }
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>最近 N 条快照（接收序 = 时间序；<c>EventSeq</c> 为作用域内序号，跨作用域不可排序，故不按它排）。</summary>
    public IReadOnlyList<PersistedEvent> Snapshot()
    {
        lock (_gate) return _queue.ToList();
    }
}

/// <summary>可选 Console 透出 Sink（E4 调试；无需落库排空）。</summary>
public sealed class ConsoleSink : IObservabilitySink
{
    public void Push(PersistedEvent evt)
        => System.Console.WriteLine($"[obs] {evt.ScopeId} #{evt.EventSeq} {evt.Event.EventKind} phase={evt.Event.Phase}");

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
