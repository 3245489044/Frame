using System.Collections.Concurrent;
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
/// </summary>
public sealed class PersistentEventSink : IObservabilitySink
{
    private readonly IEventStore _store;
    private readonly object _gate = new();
    private List<PersistedEvent> _buffer = new();

    public PersistentEventSink(IEventStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>当前尚未排空（FlushAsync）的缓冲条数（诊断可见性）。</summary>
    public int BufferedCount
    {
        get { lock (_gate) return _buffer.Count; }
    }

    public void Push(PersistedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        lock (_gate) _buffer.Add(evt);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<PersistedEvent> batch;
        lock (_gate)
        {
            if (_buffer.Count == 0) return;
            batch = _buffer;
            _buffer = new List<PersistedEvent>();
        }

        try
        {
            await _store.AppendBatchAsync(batch, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 落库失败：回滚缓冲（at-least-once，允许整批重放），把错误抛给调用方决定，而非内部静默吞掉。
            lock (_gate) _buffer.InsertRange(0, batch);
            throw new SinkWriteException($"观测事件显式排空落库失败（{batch.Count} 条），缓冲已保留可重试。", ex);
        }
    }
}

/// <summary>内存环形缓冲热查 Sink（E4）：保留最近 N 条便于进程内即时查询，不承担事实源（无需落库排空）。</summary>
public sealed class HotRingSink : IObservabilitySink
{
    private readonly ConcurrentQueue<PersistedEvent> _queue = new();
    private readonly int _capacity;
    private int _count;

    public HotRingSink(int capacity = 1000)
    {
        _capacity = Math.Max(1, capacity);
    }

    public void Push(PersistedEvent evt)
    {
        _queue.Enqueue(evt);
        if (Interlocked.Increment(ref _count) > _capacity)
        {
            if (_queue.TryDequeue(out _)) Interlocked.Decrement(ref _count);
        }
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

    public IReadOnlyList<PersistedEvent> Snapshot()
        => _queue.OrderBy(e => e.EventSeq).ToList();
}

/// <summary>可选 Console 透出 Sink（E4 调试；无需落库排空）。</summary>
public sealed class ConsoleSink : IObservabilitySink
{
    public void Push(PersistedEvent evt)
        => System.Console.WriteLine($"[obs] {evt.ScopeId} #{evt.EventSeq} {evt.Event.EventKind} phase={evt.Event.Phase}");

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
