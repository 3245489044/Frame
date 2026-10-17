namespace CallCenter.Cost.Instrumentation;

/// <summary>
/// 一次工具调用的作用域（using 模式，异常安全）。
/// 未显式 Complete 就 Dispose ⇒ 自动 Abandon（不静默漏计）。
/// </summary>
public sealed class ToolInvocationScope : IDisposable
{
    private readonly IToolInvocationObserver _observer;
    private int _completed;

    internal ToolInvocationScope(
        IToolInvocationObserver observer,
        ToolInvocationRequest request,
        string toolCallId,
        string logicalCallId,
        DateTimeOffset startedAt)
    {
        _observer = observer;
        Request = request;
        ToolCallId = toolCallId;
        LogicalCallId = logicalCallId;
        StartedAt = startedAt;
        Attempt = 1;
        Stopwatch = System.Diagnostics.Stopwatch.StartNew();
    }

    public ToolInvocationRequest Request { get; }
    public string ToolCallId { get; }
    public string LogicalCallId { get; }
    public DateTimeOffset StartedAt { get; }
    public int Attempt { get; internal set; }
    public System.Diagnostics.Stopwatch Stopwatch { get; }
    public long ElapsedMs => Stopwatch.ElapsedMilliseconds;
    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    /// <summary>标记已结算（幂等）。</summary>
    internal bool MarkCompleted() => Interlocked.Exchange(ref _completed, 1) == 0;

    public void Dispose()
    {
        if (!IsCompleted)
        {
            _observer.Abandon(this, "scope_disposed");
        }
    }
}
