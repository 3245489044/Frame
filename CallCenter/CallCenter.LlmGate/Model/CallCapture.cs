namespace CallCenter.LlmGate.Model;

/// <summary>
/// 入站请求的捕获事实（调用前可判的全部结构事实）。
/// <see cref="EstimatedCostUsd"/> 为字符估算，<see cref="PriceKnown"/> 表示模型是否有定价。
/// </summary>
public sealed record CallCapture(
    string CallId,
    string Endpoint,
    string Method,
    string? Model,
    bool Stream,
    int Hop,
    string ClientId,
    string? SessionId,
    string? TaskKey,
    string? RoundId,
    string? Caller,
    int MessageCount,
    string RoleHistogram,
    int SystemChars,
    int UserChars,
    int HistoryChars,
    int ToolCount,
    string? ToolNamesHash,
    int RequestBytes,
    string RequestFingerprint,
    string RequestBody,
    int TotalChars,
    decimal? EstimatedCostUsd,
    bool PriceKnown)
{
    /// <summary>归属缺失（窗口据此标「归属缺失」）。</summary>
    public bool IsAttributionMissing
        => string.IsNullOrWhiteSpace(SessionId) && string.IsNullOrWhiteSpace(TaskKey);

    /// <summary>会话维度键（缺失退化到 ClientId，不隐藏）。</summary>
    public string SessionKey => string.IsNullOrWhiteSpace(SessionId) ? ClientId : SessionId!;

    /// <summary>任务维度键（缺失退化到 ClientId，不隐藏）。</summary>
    public string TaskKeyResolved => string.IsNullOrWhiteSpace(TaskKey) ? ClientId : TaskKey!;
}
