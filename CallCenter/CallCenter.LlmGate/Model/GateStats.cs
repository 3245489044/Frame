namespace CallCenter.LlmGate.Model;

/// <summary>当日统计（读模型快照）。金额/ token 仅对已知值求和，未知单列计数，禁当 0。</summary>
public sealed record GateStats(
    string Day,
    int Calls,
    int Success,
    int Failure,
    int Denied,
    int Pending,
    long? Tokens,
    decimal? Costs,
    int UnknownCosts,
    long Dropped,
    string? TopFingerprint,
    int TopFingerprintCount);

/// <summary>待审批列表项（只读快照）。</summary>
public sealed record PendingView(
    string CallId,
    DateTimeOffset StartedAt,
    string ClientId,
    string? SessionId,
    string? TaskKey,
    string? Model,
    decimal? EstimatedCostUsd,
    string? RuleId,
    string ReasonCode,
    string Fingerprint);

/// <summary>门事件（Halt/Resume 审计可见）。</summary>
public sealed record GateEvent(
    string EventKind,
    DateTimeOffset At,
    string? Reason,
    string Mode);

/// <summary>门配置公告（状态栏展示用）。</summary>
public sealed record GateStatus(
    string Mode,
    string Listen,
    string Upstream,
    bool Halted,
    string? HaltReason,
    DateTimeOffset StartedAt,
    string? ConfigPath,
    string Version);
