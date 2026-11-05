namespace Modules.Threading;

/// <summary>统一"超时运行"的结果分类：区分预算耗尽(TimedOut)、外部取消(Cancelled)、空闲挂起(IdleHung)，
/// 取代笼统"超时"文案导致的归因错误（如把 30s 外部取消误报成 120s 预算耗尽）。</summary>
public enum TimeoutOutcome
{
    Completed = 0,
    TimedOut = 1,
    Cancelled = 2,
    IdleHung = 3,
    Failed = 4,
}

/// <summary>超时运行的结果契约。</summary>
public readonly record struct TimeoutResult<T>(
    TimeoutOutcome Outcome,
    T? Value,
    Exception? Error,
    TimeSpan Elapsed)
{
    /// <summary>成功完成（Outcome == Completed）。</summary>
    public bool IsSuccess => Outcome == TimeoutOutcome.Completed;
}
