namespace Modules.Threading;

/// <summary>
/// 统一结果契约（取代 FunctionCallFacadeResult/ExecutionResult/FunctionCallResult 的碎片）。
/// </summary>
public readonly record struct AsyncOperationResult
{
    public bool IsSuccess { get; init; }

    public string? Output { get; init; }

    public string? Error { get; init; }

    public long ElapsedMs { get; init; }

    public AsyncOpPhase EndPhase { get; init; } // Completed/Failed/Cancelled/TimedOut/IdleHung
}
