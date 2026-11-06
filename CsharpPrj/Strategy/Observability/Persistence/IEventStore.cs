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

    /// <summary>
    /// 按作用域返回时序事件（升序 EventSeq；afterSeq 续读）。
    /// <c>limit</c> 口径三处实现统一：&gt;0 为窗口上限；0 / 负数返回空集（负数不解释为「不限」）。
    /// </summary>
    IAsyncEnumerable<PersistedEvent> TimelineAsync(string scopeId, long? afterSeq = null, int limit = 100, CancellationToken ct = default);

    /// <summary>同一实体（ScopeId+EntityType+Seq 三元全等）的完整事件链（升序 EventSeq）。</summary>
    IAsyncEnumerable<PersistedEvent> EntityChainAsync(EntityId entity, CancellationToken ct = default);
}

/// <summary>
/// 事件区读侧诊断账（下篇 P0）：坏行 / 丢弃不静默 —— 至少可计数、可被读面观察。
/// 实现方可只报告自身保障的指标（无对应风险时返回 0）。
/// </summary>
public interface IEventStoreDiagnostics
{
    /// <summary>读侧累计跳过的损坏行数（只增）。</summary>
    long CorruptLineCount { get; }
}
