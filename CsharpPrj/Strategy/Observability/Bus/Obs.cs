using System.Diagnostics;
using Strategy.Observability.Envelope;
using Strategy.Observability.Entity;

namespace Strategy.Observability.Bus;

/// <summary>
/// 埋点静态门面（E4）：轻量 API，using 即闭环；成败对调用方可见（见 <see cref="EmitResult"/> / <see cref="ObservationResult"/>）。
///   Obs.Emit(...)             — 一次性事件，返回 <see cref="EmitResult"/>（契约未注册/校验失败即抛 <c>ObservabilityException</c>）；
///   Obs.Begin(...)            — 开始一段活动（自动发起始事件），返回 <see cref="Activity"/>，Complete/Fail 产成观测任务结果；
///   Obs.FlushAsync(...)       — 调用方显式冲刷（持久化落库），失败抛错。
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

    /// <summary>观测总线是否已装配（就绪守卫：未装配时埋点自动降级为直通，不阻断主路径）。</summary>
    public static bool IsReady => _bus is not null;

    public static IObservabilityBus Current => Bus;

    /// <summary>进入关联键作用域（TraceId/SessionId/ScopeKey 透传）。</summary>
    public static IDisposable EnterScope(CorrelationBag bag) => ObsScope.Enter(bag);

    public static EmitResult Emit(EntityId entity, string eventKind, Action<ObservabilityEventBuilder>? build = null)
        => Bus.Emit(entity, eventKind, build);

    /// <summary>调用方驱动的显式排空（把已 Emit 事件持久化落库）；失败抛 <see cref="Diagnostics.SinkWriteException"/>。</summary>
    public static Task FlushAsync(CancellationToken ct = default) => Bus.FlushAsync(ct);

    /// <summary>
    /// 开始一段活动：立即 Emit 起始事件（<paramref name="startKind"/>），返回 <see cref="Activity"/>；
    /// 结束调用 Complete/Fail 自动补完成事件（<paramref name="completionKind"/>，缺省同起始分类）并产成 <see cref="ObservationResult"/>。
    /// </summary>
    public static Activity Begin(EntityId entity, string startKind, string? completionKind = null, Action<ObservabilityEventBuilder>? build = null)
    {
        Bus.Emit(entity, startKind, build);
        return new Activity(Bus, entity, startKind, completionKind);
    }
}

/// <summary>
/// 活动句柄：记录开始时间，结束时补发完成事件（含 elapsed_ms），并产成类型化 <see cref="ObservationResult"/>。
/// 幂等：Complete（成功）/ Fail（失败）先到先得，Dispose 默认按成功终结。
/// 完成事件的投递结果经 <see cref="Delivery"/> 回读（契约未注册/校验失败在 Complete/Fail 时抛错）。
/// </summary>
public sealed class Activity : IDisposable
{
    private readonly IObservabilityBus _bus;
    private readonly EntityId _entity;
    private readonly string _completionKind;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _ended;

    public Activity(IObservabilityBus bus, EntityId entity, string startKind, string? completionKind = null)
    {
        _bus = bus;
        _entity = entity;
        _completionKind = completionKind ?? startKind ?? throw new ArgumentNullException(nameof(startKind));
    }

    /// <summary>终结后回读：业务任务成败（null = 尚未终结）。</summary>
    public ObservationResult? Result { get; private set; }

    /// <summary>终结时完成事件的实际投递结果（契约校验通过并已送达 Sink；持久化经显式 FlushAsync）。</summary>
    public EmitResult? Delivery { get; private set; }

    /// <summary>正常完成（幂等）：补发完成事件 stage=completed + elapsed_ms，产成 Succeeded 结果。</summary>
    public EmitResult Complete(Action<ObservabilityEventBuilder>? complete = null)
    {
        if (!TryEnd(out var elapsed))
            return Delivery ?? new EmitResult(0, _completionKind);

        var emit = _bus.Emit(_entity, _completionKind, b =>
        {
            b.Stage("completed").WithMetric("elapsed_ms", elapsed);
            complete?.Invoke(b);
        });
        Delivery = emit;
        Result = new ObservationResult(ObservationStatus.Succeeded, _completionKind, elapsed, null);
        return emit;
    }

    /// <summary>失败结束（幂等）：补发完成事件 stage=failed + failed 载荷，产成 Failed 结果。</summary>
    public EmitResult Fail(string? reason = null)
    {
        if (!TryEnd(out var elapsed))
            return Delivery ?? new EmitResult(0, _completionKind);

        var emit = _bus.Emit(_entity, _completionKind, b =>
        {
            b.Stage("failed").WithMetric("elapsed_ms", elapsed)
                .Payload(reason is null ? new { failed = true } : new { failed = true, reason });
        });
        Delivery = emit;
        Result = new ObservationResult(ObservationStatus.Failed, _completionKind, elapsed, reason);
        return emit;
    }

    public void Dispose() => Complete();

    /// <summary>幂等终结闸：首次调用停表并返回 true + 耗时；重复调用返回 false。</summary>
    private bool TryEnd(out double elapsed)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            elapsed = 0;
            return false;
        }
        _clock.Stop();
        elapsed = _clock.Elapsed.TotalMilliseconds;
        return true;
    }
}
