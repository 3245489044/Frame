using System.Collections.Concurrent;
using Strategy.Observability.Diagnostics;
using Strategy.Observability.Entity;
using Strategy.Observability.Envelope;
using Strategy.Observability.Persistence;
using Strategy.Observability.Registry;

namespace Strategy.Observability.Bus;

/// <summary>收集总线端口（E4）：单入口 Emit + 作用域自动捕获 + 复合 Sink + 调用方显式 FlushAsync。</summary>
public interface IObservabilityBus
{
    /// <summary>投递一条事件：契约未注册/校验失败一律抛 <see cref="ObservabilityException"/>，成功返回同步投递结果。
    /// 持久化是否真正落库由调用方显式 <see cref="FlushAsync"/> 驱动。</summary>
    EmitResult Emit(EntityId entity, string eventKind, Action<ObservabilityEventBuilder>? build = null);

    /// <summary>调用方驱动的显式排空：把已接收事件冲刷到各 Sink；失败抛 <see cref="SinkWriteException"/>（不再内嵌后台循环）。</summary>
    Task FlushAsync(CancellationToken ct = default);
}

/// <summary>
/// 收集总线实现（E4）：把信封交给复合 Sink；Emit 前经契约注册表校验——契约未注册抛
/// <see cref="UnregisteredEventException"/>、校验失败抛 <see cref="ContractValidationException"/>（错误在边界抛出，不由内核内部决定降级）。
/// 关联键从 ObsScope ambient 自动捕获并入行键；ScopeId 内 EventSeq 单调递增。持久化成败交 <see cref="FlushAsync"/>。
/// </summary>
public sealed class ObservabilityBus : IObservabilityBus, IAsyncDisposable
{
    private readonly IObservabilityContractRegistry _registry;
    private readonly IReadOnlyList<IObservabilitySink> _sinks;
    private readonly ConcurrentDictionary<string, long> _seq = new(StringComparer.Ordinal);
    private readonly object _emitGate = new();
    private volatile bool _disposed;

    public ObservabilityBus(
        IObservabilityContractRegistry registry,
        IEnumerable<IObservabilitySink> sinks)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sinks = (sinks ?? throw new ArgumentNullException(nameof(sinks))).ToList();
    }

    public IReadOnlyList<IObservabilitySink> Sinks => _sinks;

    public EmitResult Emit(EntityId entity, string eventKind, Action<ObservabilityEventBuilder>? build = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(eventKind)) throw new ArgumentNullException(nameof(eventKind));

        // 契约即断言：未注册即抛，不由 Options 决定是否降级。
        if (!_registry.IsRegistered(eventKind))
            throw new UnregisteredEventException(eventKind);

        var builder = new ObservabilityEventBuilder(entity, eventKind);
        build?.Invoke(builder);
        var bag = ObsScope.Current;

        // 序号分配与 Sink 投递同临界区：同进程并发 Emit 时，行序（落库/入环）与 EventSeq 顺序一致（不重排）。
        lock (_emitGate)
        {
            var seq = NextSeq(entity.ScopeId);
            var evt = builder.Build(seq);

            // 校验失败一律抛错（原 ThrowOnValidationFailure=false 的记日志降级路径已移除）。
            if (!_registry.Validate(evt, out var issue))
                throw new ContractValidationException(issue!);

            var persisted = new PersistedEvent
            {
                ScopeId = entity.ScopeId,
                EventSeq = evt.EventSeq,
                Event = evt,
                TraceId = bag?.TraceId,
                SessionId = bag?.SessionId,
                CorrelationScopeKey = bag?.ScopeKey,
                RunId = bag?.RunId,
                RoundId = bag?.RoundId,
            };

            foreach (var sink in _sinks) sink.Push(persisted);

            return new EmitResult(evt.EventSeq, eventKind);
        }
    }

    private long NextSeq(string scopeId) => _seq.AddOrUpdate(scopeId, 1, (_, c) => c + 1);

    /// <summary>调用方驱动的显式排空（冲刷所有 Sink，含持久化落库）。任一 Sink 失败即抛，不静默吞错。</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        foreach (var sink in _sinks)
            await sink.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>优雅关闭：先显式排空（flush 失败抛出，调用方决定处置），再释放可选异步 Sink。</summary>
    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        _disposed = true;
        await FlushAsync(ct).ConfigureAwait(false);
        foreach (var s in _sinks.OfType<IAsyncDisposable>()) await s.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync();
}
