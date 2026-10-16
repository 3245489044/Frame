using Strategy.Observability.Persistence;

namespace Strategy.Observability.Bus;

/// <summary>
/// 写穿 Sink（E5 直写）：<see cref="Push"/> 即把事件写入事件区（内存实现同步完成、非阻塞），
/// 使「事实源 = 事件区」在 <c>Emit</c> 返回时即成立（查询/实时出口同源，不依赖调用方 flush）。
/// <para>
/// 与 <see cref="PersistentEventSink"/> 的分工：后者为「批量缓冲 + 调用方驱动 flush」的落库形态（介质 IO 慢）；
/// 本 Sink 为「即时写穿」形态（内存介质快），二者择一装配，禁同装配（同一事件区双写）。
/// </para>
/// </summary>
public sealed class WriteThroughStoreSink : IObservabilitySink
{
    private readonly IEventStore _store;

    public WriteThroughStoreSink(IEventStore store)
    {
        _store = PortGuard.Required(store, nameof(store));
    }

    /// <summary>即时写入（内存事件区为同步完成；异常向上抛，不静默）。</summary>
    public void Push(PersistedEvent evt)
    {
        PortGuard.Required(evt, nameof(evt));
        _store.AppendBatchAsync(new[] { evt }).GetAwaiter().GetResult();
    }

    /// <summary>写穿形态无需排空（空操作）。</summary>
    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
