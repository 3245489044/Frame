using System.Collections.Concurrent;
using Strategy.Observability.Diagnostics;
using Strategy.Observability.Persistence;

namespace Strategy.Observability.Bus;

/// <summary>复合 Sink 成员（E4）：接收一条已就绪的持久化行事件。</summary>
public interface IObservabilitySink
{
    void Push(PersistedEvent evt);
}

/// <summary>持久化批写 Sink（E5 内核实例）：复用 Modules.Threading.Streaming.BatchedAsyncSink，单一写者落库。</summary>
public sealed class PersistentEventSink : IObservabilitySink, IAsyncDisposable
{
    private readonly Modules.Threading.Streaming.BatchedAsyncSink<PersistedEvent> _sink;

    /// <param name="log">批写失败诊断插槽（E8）；null = 静默。</param>
    public PersistentEventSink(IEventStore store, int capacity = 1024, int drainBatch = 64, int retryLimit = 3, Action<string, object?>? log = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _sink = new Modules.Threading.Streaming.BatchedAsyncSink<PersistedEvent>(
            capacity: Math.Max(1, capacity),
            drainBatch: Math.Max(1, drainBatch),
            flush: (batch, ct) => store.AppendBatchAsync(batch, ct),
            retryLimit: Math.Max(1, retryLimit),
            log: log);
    }

    public void Push(PersistedEvent evt) => _sink.Emit(evt);

    public ValueTask DisposeAsync() => _sink.DisposeAsync();
}

/// <summary>内存环形缓冲热查 Sink（E4）：保留最近 N 条便于进程内即时查询，不承担事实源。</summary>
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

    public IReadOnlyList<PersistedEvent> Snapshot()
        => _queue.OrderBy(e => e.EventSeq).ToList();
}

/// <summary>可选 Console 透出 Sink（E4 调试）。</summary>
public sealed class ConsoleSink : IObservabilitySink
{
    public void Push(PersistedEvent evt)
        => System.Console.WriteLine($"[obs] {evt.ScopeId} #{evt.EventSeq} {evt.Event.EventKind} phase={evt.Event.Phase}");
}
