using System.Diagnostics;
using System.Runtime.InteropServices;
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
        _bus = PortGuard.Required(bus, nameof(bus));
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
/// 幂等：Complete（成功）/ Fail（失败）先到先得；Dispose 只在尚未终结时按失败兜底
/// （避免 using 体内异常路径被记成成功，见 <see cref="Dispose"/>）。
/// 完成事件的投递结果经 <see cref="Delivery"/> 回读（契约未注册/校验失败在 Complete/Fail 时抛错）。
/// </summary>
public sealed class Activity : IDisposable
{
    private readonly IObservabilityBus _bus;
    private readonly EntityId _entity;
    private readonly string _completionKind;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private bool _ended;

    public Activity(IObservabilityBus bus, EntityId entity, string startKind, string? completionKind = null)
    {
        _bus = bus;
        _entity = entity;
        _completionKind = completionKind ?? PortGuard.Required(startKind, nameof(startKind));
    }

    /// <summary>终结后回读：业务任务成败（null = 尚未终结）。</summary>
    public ObservationResult? Result { get; private set; }

    /// <summary>终结时完成事件的实际投递结果（契约校验通过并已送达 Sink；持久化经显式 FlushAsync）。</summary>
    public EmitResult? Delivery { get; private set; }

    /// <summary>正常完成（幂等）：补发完成事件 stage=completed + elapsed_ms，产成 Succeeded 结果。</summary>
    public EmitResult Complete(Action<ObservabilityEventBuilder>? complete = null)
        => End(ObservationStatus.Succeeded, reason: null, build: (b, elapsedMs) =>
        {
            b.Stage("completed").WithMetric("elapsed_ms", elapsedMs);
            complete?.Invoke(b);
        });

    /// <summary>失败结束（幂等）：补发完成事件 stage=failed + failed 载荷，产成 Failed 结果。</summary>
    public EmitResult Fail(string? reason = null)
        => End(ObservationStatus.Failed, reason, build: (b, elapsedMs) => b
            .Stage("failed")
            .WithMetric("elapsed_ms", elapsedMs)
            .Payload(reason is null ? new { failed = true } : new { failed = true, reason }));

    /// <summary>
    /// using 兜底终结：无异常在途 → 按成功补完成事件（轮末 Dispose 即语义，见 <c>AgentTrace.Round</c>）；
    /// 异常在途 → 按失败终结（不把 using 体内的异常记成成功帧）。
    /// </summary>
    public void Dispose()
    {
        if (Unwinding) Fail("活动未显式终结即因异常退出（Dispose 兜底）");
        else Complete();
    }

    /// <summary>是否有异常正在展开（Windows SEH；判定不可用时返回 false = 沿用正常完成语义）。</summary>
    private static bool Unwinding => Marshal.GetExceptionPointers() != IntPtr.Zero;

    /// <summary>
    /// 终结闸（幂等，先到先得）：终态事件投递成功后落定 <see cref="Delivery"/> / <see cref="Result"/>；
    /// 投递抛错则不落定（活动保持未终结，供调用方重试 / 显式终结）。
    /// </summary>
    private EmitResult End(ObservationStatus status, string? reason, Action<ObservabilityEventBuilder, double> build)
    {
        lock (_gate)
        {
            if (_ended) return Delivery!;
            _clock.Stop();

            var elapsedMs = _clock.Elapsed.TotalMilliseconds;
            var emit = _bus.Emit(_entity, _completionKind, b => build(b, elapsedMs));
            Delivery = emit;
            Result = new ObservationResult(status, _completionKind, elapsedMs, reason);
            _ended = true;
            return emit;
        }
    }
}
