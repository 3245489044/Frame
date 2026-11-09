namespace CallCenter.LlmGate.Model;

/// <summary>ReasonCode 词表（附录 A）+ 到 HTTP 状态码的单一映射。</summary>
public static class ReasonCodes
{
    public const string Halted = "LG_HALTED";
    public const string DenyHop = "LG_DENY_HOP";
    public const string DenyLoop = "LG_DENY_LOOP";
    public const string DenyRate = "LG_DENY_RATE";
    public const string DenyCost = "LG_DENY_COST";
    public const string DenyTokens = "LG_DENY_TOKENS";
    public const string DenyPendingOverflow = "LG_DENY_PENDING_OVERFLOW";
    public const string DenyTimeout = "LG_DENY_TIMEOUT";
    public const string DenyManual = "LG_DENY_MANUAL";
    public const string HoldCost = "LG_HOLD_COST";
    public const string HoldUnknownModel = "LG_HOLD_UNKNOWN_MODEL";
    public const string HoldManual = "LG_HOLD_MANUAL";
    public const string UpstreamTimeout = "LG_UPSTREAM_TIMEOUT";
    public const string UpstreamError = "LG_UPSTREAM_ERROR";

    /// <summary>拒绝码 → HTTP 状态（Hold 码无状态，返回 0）。</summary>
    public static int HttpStatus(string? code) => code switch
    {
        Halted => 503,
        DenyHop => 403,
        DenyLoop => 429,
        DenyRate => 429,
        DenyCost => 403,
        DenyTokens => 403,
        DenyPendingOverflow => 429,
        DenyTimeout => 403,
        DenyManual => 403,
        UpstreamTimeout => 504,
        UpstreamError => 502,
        _ => 403,
    };
}

/// <summary>规则 ID 词表（§6.1，单源）。</summary>
public static class RuleIds
{
    public const string Halt = "R-HALT";
    public const string Hop = "R-HOP";
    public const string Loop = "R-LOOP";
    public const string Rate = "R-RATE";
    public const string CostSession = "R-COST-SESSION";
    public const string CostTask = "R-COST-TASK";
    public const string CostDay = "R-COST-DAY";
    public const string Tokens = "R-TOKENS";
    public const string Manual = "R-MANUAL";
    public const string Default = "R-DEFAULT";
    public const string Bypass = "R-BYPASS";
}
