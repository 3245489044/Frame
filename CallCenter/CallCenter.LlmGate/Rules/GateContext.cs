using CallCenter.LlmGate.Model;

namespace CallCenter.LlmGate.Rules;

/// <summary>规则求值输入（全部为结构事实）。</summary>
public sealed record GateContext(
    string CallId,
    string RequestFingerprint,
    int Hop,
    string? SessionKey,
    string? TaskKey,
    string ClientId,
    int FingerprintCountInWindow,
    int SessionCallsLastMinute,
    decimal? SessionCostUsd,
    decimal? TaskCostUsd,
    decimal? DailyCostUsd,
    long? DailyTokens,
    decimal? EstimatedCostUsd,
    bool PriceKnown,
    bool Halted,
    int UnknownCostCount = 0);

/// <summary>规则求值结果。</summary>
public sealed record GateRuleResult(
    GateDecision Decision,
    string? RuleId,
    string? ReasonCode,
    string Message)
{
    public bool IsAllow => Decision == Model.GateDecision.Allow;
    public bool IsHold => Decision == Model.GateDecision.Hold;
    public bool IsDeny => Decision == Model.GateDecision.Deny;

    /// <summary>拒绝/挂起的 HTTP 状态（Allow 为 200）。</summary>
    public int HttpStatus => Decision switch
    {
        Model.GateDecision.Allow => 200,
        Model.GateDecision.Hold => 200,
        _ => Model.ReasonCodes.HttpStatus(ReasonCode),
    };

    public static GateRuleResult Allow(string ruleId = Model.RuleIds.Default, string message = "")
        => new(Model.GateDecision.Allow, ruleId, null, message);

    public static GateRuleResult Deny(string ruleId, string reasonCode, string message)
        => new(Model.GateDecision.Deny, ruleId, reasonCode, message);

    public static GateRuleResult Hold(string ruleId, string reasonCode, string message)
        => new(Model.GateDecision.Hold, ruleId, reasonCode, message);
}
