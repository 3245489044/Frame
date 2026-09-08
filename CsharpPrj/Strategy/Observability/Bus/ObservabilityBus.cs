using System.Collections.Concurrent;
using Strategy.Observability.Diagnostics;
using Strategy.Observability.Entity;
using Strategy.Observability.Envelope;
using Strategy.Observability.Persistence;
using Strategy.Observability.Registry;

namespace Strategy.Observability.Bus;

/// <summary>收集总线端口（E4）：单入口 Emit + 作用域自动捕获 + 复合 Sink。</summary>
public interface IObservabilityBus
{
    void Emit(EntityId entity, string eventKind, Action<ObservabilityEventBuilder>? build = null);
}

public sealed class ObservabilityBusOptions
{
    /// <summary>未注册契约的 Emit 是否抛错拦截（回归可断言的观测标准）。</summary>
    public bool RejectUnregistered { get; set; } = true;

    /// <summary>校验失败是否抛错；false = 记录到 Fallback 后仍透传（宽松模式）。</summary>
    public bool ThrowOnValidationFailure { get; set; } = true;
}

/// <summary>
/// 收集总线实现（E4）：把信封交给复合 Sink；Emit 前经契约注册表校验；
/// 关联键从 ObsScope ambient 自动捕获并入行键；ScopeId 内 EventSeq 单调递增。
/// </summary>
public sealed class ObservabilityBus : IObservabilityBus, IAsyncDisposable
{
    private readonly IObservabilityContractRegistry _registry;
    private readonly ObservabilityBusOptions _options;
    private readonly ObservabilityFallback _fallback;
    private readonly IReadOnlyList<IObservabilitySink> _sinks;
    private readonly ConcurrentDictionary<string, long> _seq = new(StringComparer.Ordinal);
    private readonly List<IAsyncDisposable> _asyncSinks = new();
    private bool _disposed;

    public ObservabilityBus(
        IObservabilityContractRegistry registry,
        IEnumerable<IObservabilitySink> sinks,
        ObservabilityBusOptions? options = null,
        ObservabilityFallback? fallback = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sinks = (sinks ?? throw new ArgumentNullException(nameof(sinks))).ToList();
        _options = options ?? new ObservabilityBusOptions();
        _fallback = fallback ?? new ObservabilityFallback();
        _asyncSinks = _sinks.OfType<IAsyncDisposable>().ToList();
    }

    public ObservabilityBusOptions Options => _options;

    public void Emit(EntityId entity, string eventKind, Action<ObservabilityEventBuilder>? build = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(eventKind)) throw new ArgumentNullException(nameof(eventKind));

        if (_options.RejectUnregistered && !_registry.IsRegistered(eventKind))
            throw new InvalidOperationException($"未注册事件分类 '{eventKind}'：新增观测点请先 Register 契约。");

        var builder = new ObservabilityEventBuilder(entity, eventKind);
        build?.Invoke(builder);
        var evt = builder.Build(NextSeq(entity.ScopeId));

        if (!_registry.Validate(evt, out var issue))
        {
            if (_options.ThrowOnValidationFailure)
                throw new InvalidOperationException($"事件校验失败: {issue}");
            _fallback.Handle("ValidationFailure", new { eventKind, issue });
        }

        var bag = ObsScope.Current;
        var persisted = new PersistedEvent
        {
            ScopeId = entity.ScopeId,
            EventSeq = evt.EventSeq,
            Event = evt,
            TraceId = bag?.TraceId,
            SessionId = bag?.SessionId,
            CorrelationScopeKey = bag?.ScopeKey,
        };

        foreach (var sink in _sinks) sink.Push(persisted);
    }

    private long NextSeq(string scopeId) => _seq.AddOrUpdate(scopeId, 1, (_, c) => c + 1);

    /// <summary>优雅关闭：停止所有异步 sink（BatchedAsyncSink 停止时做最终排空，保证不丢批）。</summary>
    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var s in _asyncSinks) await s.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
    }
}
