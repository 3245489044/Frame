using CallCenter.Cost.Costing;
using CallCenter.Cost.Pricing;

namespace CallCenter.LlmGate.Model;

/// <summary>
/// 逐调用账本最小单位（§5.1）。金额/用量全可空 ⇒ Unknown（区别于免费 0）。
/// </summary>
public sealed record LlmCallSpan
{
    // ---- 身份 ----
    public string CallId { get; init; } = "";
    public string? TraceId { get; init; }
    public string? ParentCallId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public long DurationMs { get; init; }

    // ---- 目标 ----
    public string Endpoint { get; init; } = "";
    public string? UpstreamBaseUrl { get; init; }
    public string? Provider { get; init; }
    public string? ModelRequest { get; init; }
    public string? ModelResponse { get; init; }
    public bool Stream { get; init; }
    public int Hop { get; init; }

    // ---- 归属 ----
    public string ClientId { get; init; } = "";
    public string? SessionId { get; init; }
    public string? TaskKey { get; init; }
    public string? RoundId { get; init; }

    // ---- 请求形态 ----
    public int MessageCount { get; init; }
    public string? RoleHistogram { get; init; }
    public int SystemChars { get; init; }
    public int UserChars { get; init; }
    public int HistoryChars { get; init; }
    public int ToolCount { get; init; }
    public string? ToolNamesHash { get; init; }
    public int RequestBytes { get; init; }
    public string RequestFingerprint { get; init; } = "";

    // ---- 响应形态 ----
    public int StatusCode { get; init; }
    public string? FinishReason { get; init; }
    public int ContentChars { get; init; }
    public int ToolCallCount { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorCode { get; init; }

    // ---- 用量 ----
    public long? TokenIn { get; init; }
    public long? TokenOut { get; init; }
    public long? CacheRead { get; init; }
    public long? CacheWrite { get; init; }

    // ---- 成本 ----
    public PriceQuote? PriceQuote { get; init; }
    public decimal? CostAmount { get; init; }
    public string CostCurrency { get; init; } = "USD";
    public IReadOnlyList<CostBreakdownItem> CostBreakdown { get; init; } = Array.Empty<CostBreakdownItem>();
    public CostSource CostSource { get; init; } = CostSource.Unknown;

    // ---- 裁决 ----
    public GateDecision Decision { get; init; } = GateDecision.Allow;
    public string? ReasonCode { get; init; }
    public string? RuleId { get; init; }
    public DecidedBy DecidedBy { get; init; } = DecidedBy.Rule;
    public DateTimeOffset? DecidedAt { get; init; }

    // ---- 隐私（默认关） ----
    public bool RequestBodySampled { get; init; }
    public string? RequestPreview { get; init; }
    public string? ResponsePreview { get; init; }

    /// <summary>归属缺失（会话与任务都未提供）。</summary>
    public bool IsAttributionMissing
        => string.IsNullOrWhiteSpace(SessionId) && string.IsNullOrWhiteSpace(TaskKey);
}
