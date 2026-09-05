namespace Share.AiScheduling.Regulation;

/// <summary>
/// 上下文占用估算（有真实 token 时透传；缺失时由 ContextEstimator 派生）。
/// EstimatedChars 为估算/真实上下文占用字符数；Tokens 为估算/真实 token 数（可空）。
/// </summary>
public sealed record ContextEstimate(long EstimatedChars, long? Tokens)
{
    public static ContextEstimate Empty { get; } = new(0, null);
}
