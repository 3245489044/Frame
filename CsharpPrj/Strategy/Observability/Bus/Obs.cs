using System.Diagnostics;
using Strategy.Observability.Envelope;
using Strategy.Observability.Entity;

namespace Strategy.Observability.Bus;

/// <summary>
/// 埋点静态门面（E4）：轻量 API，using 即闭环。
///   Obs.Emit(...)             — 一次性事件；
///   Obs.Begin(entity, kind)   — 返回 <see cref="Activity"/>，Dispose/Complete 时自动补 elapsed_ms 与完成阶段；
///   Obs.EnterScope(bag)       — 进入关联键作用域（嵌套安全）。
/// </summary>
public static class Obs
{
    private static IObservabilityBus? _bus;

    public static void Configure(IObservabilityBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    private static IObservabilityBus Bus => _bus ?? throw new InvalidOperationException("Obs 尚未 Configure（须装配 IObservabilityBus）");

    public static IObservabilityBus Current => Bus;

    /// <summary>进入关联键作用域（TraceId/SessionId/ScopeKey 透传）。</summary>
    public static IDisposable EnterScope(CorrelationBag bag) => ObsScope.Enter(bag);

    public static void Emit(EntityId entity, string eventKind, Action<ObservabilityEventBuilder>? build = null)
        => Bus.Emit(entity, eventKind, build);

    /// <summary>开始一段活动：立即 Emit 起始事件，返回 Activity；结束自动补完成事件 + elapsed_ms。</summary>
    public static Activity Begin(EntityId entity, string eventKind, Action<ObservabilityEventBuilder>? build = null)
    {
        Bus.Emit(entity, eventKind, build);
        return new Activity(Bus, entity, eventKind);
    }
}

/// <summary>活动句柄：记录开始时间，结束时补发完成事件（含 elapsed_ms）。幂等。</summary>
public sealed class Activity : IDisposable
{
    private readonly IObservabilityBus _bus;
    private readonly EntityId _entity;
    private readonly string _kind;
    private readonly string _completionKind;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _ended;

    public Activity(IObservabilityBus bus, EntityId entity, string kind, string? completionKind = null)
    {
        _bus = bus;
        _entity = entity;
        _kind = kind;
        _completionKind = completionKind ?? kind;
    }

    /// <summary>正常完成（幂等）：补发完成事件，附带 elapsed_ms 与指定度量。</summary>
    public void Complete(Action<ObservabilityEventBuilder>? complete = null)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _clock.Stop();
        _bus.Emit(_entity, _completionKind, b =>
        {
            b.Stage("completed").WithMetric("elapsed_ms", _clock.Elapsed.TotalMilliseconds);
            complete?.Invoke(b);
        });
    }

    /// <summary>失败结束（幂等）：补发完成事件并携带失败载荷。</summary>
    public void Fail(string? reason = null)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _clock.Stop();
        _bus.Emit(_entity, _completionKind, b =>
        {
            b.Stage("failed").WithMetric("elapsed_ms", _clock.Elapsed.TotalMilliseconds)
                .Payload(reason is null ? new { failed = true } : new { failed = true, reason });
        });
    }

    public void Dispose() => Complete();
}
