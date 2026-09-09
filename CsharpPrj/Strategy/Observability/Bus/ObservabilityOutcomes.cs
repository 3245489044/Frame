namespace Strategy.Observability.Bus;

/// <summary>
/// 单条事件投递结果（E4 同步侧）：契约校验通过并被全部 Sink 接收后返回。
/// 持久化是否真正落库不在此判定——由调用方显式 <see cref="IObservabilityBus.FlushAsync"/> 驱动，失败抛 <c>ObservabilityException</c>。
/// </summary>
public sealed record EmitResult(long EventSeq, string EventKind);

/// <summary>观测任务（<see cref="Activity"/> 观测的业务单元）最终成败。</summary>
public enum ObservationStatus
{
    Succeeded,
    Failed,
}

/// <summary>观测任务结果：成败、完成分类、耗时与失败原因（非幂等终结时落定）。</summary>
public sealed record ObservationResult(
    ObservationStatus Status,
    string CompletionKind,
    double ElapsedMs,
    string? Reason);
