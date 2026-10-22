using Share.TaskStatusPool.Loops;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 每轮边界遥测（逻辑边界评估输入）。由 Agent.Core 从一轮工作流产物
/// （ClineResult / WorkflowRoundResult）与统一上下文信号提取；P4 起优先消费真实
/// token/stopReason，缺失时回退 ContextEstimator 估算。
/// </summary>
public sealed record BoundarySignal(
    int Round,
    int MessageCount,
    int ResultTextLength,
    string? OutputState,
    EvalOutcome Eval,
    ErrorClass? ErrorClass,
    int BudgetLevel,
    int TokenValue,
    int TokenMax,
    long? InputTokens = null,
    long? OutputTokens = null,
    string? StopReason = null,
    ProactiveBoundaryIntent? Proactive = null,
    int NoProgressSteps = 0,
    double? SmoothedRate = null)
{
    /// <summary>无信号（首轮评估前的占位）。</summary>
    public static BoundarySignal Empty { get; } = new(0, 0, 0, null, EvalOutcome.None, null, 0, 0, 3);

    /// <summary>是否携带真实 token 用量（P4 daemon 遥测）。</summary>
    public bool HasRealTokens => InputTokens is > 0;
}
