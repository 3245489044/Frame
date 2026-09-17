using Strategy.Observability.Entity;

namespace Strategy.Observability.Persistence;

/// <summary>
/// append-only 事件区端口（E5）。内核机制：批写 + 时序读取，不解释任何事件语义。
/// 生产实现可对 Operation.DataStoreDriven.IZoneStore 适配成 ur_events 事件区；
/// 本层内置 InMemory/File 实现满足 Batch1 验收（零外部依赖）。
/// </summary>
public interface IEventStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>整批追加（由 PersistentEventSink.FlushAsync 调用方显式驱动；at-least-once 允许重放，行键幂等）。</summary>
    Task AppendBatchAsync(IReadOnlyList<PersistedEvent> events, CancellationToken ct = default);

    /// <summary>按作用域返回时序事件（升序 EventSeq；afterSeq 续读）。</summary>
    IAsyncEnumerable<PersistedEvent> TimelineAsync(string scopeId, long? afterSeq = null, int limit = 100, CancellationToken ct = default);

    /// <summary>同一实体（ScopeId+EntityType+Seq 三元全等）的完整事件链（升序 EventSeq）。</summary>
    IAsyncEnumerable<PersistedEvent> EntityChainAsync(EntityId entity, CancellationToken ct = default);
}
