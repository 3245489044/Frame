namespace Modules.Governance;

/// <summary>
/// 资源键（配置可扩展；内置键为常量，新增资源只改 limits.json）。
/// 字符串即坐标：内核不解释资源含义，只按 <c>scope + resource</c> 计量。
/// </summary>
public static class ResourceKeys
{
    public const string LlmCalls = "llm.calls";
    public const string LlmInputTokens = "llm.tokens.input";
    public const string LlmOutputTokens = "llm.tokens.output";
    public const string ToolCalls = "tool.calls";
    public const string WallMs = "time.wall_ms";
    public const string CpuMs = "cpu.ms";
    public const string DiskBytes = "disk.bytes";
    public const string Concurrency = "concurrency";
}

/// <summary>作用域层级（进程 → 会话 → 任务 → run → 轮 → 调用）；子 scope 裁决逐级上查父链。</summary>
public enum LimitScopeKind
{
    Process = 0,
    Session = 1,
    Task = 2,
    Run = 3,
    Round = 4,
    Call = 5,
}

/// <summary>计量窗口：生命周期累计 / 滑动窗口。</summary>
public enum LimitWindowKind
{
    Lifetime,
    Window,
}

/// <summary>五种裁决（只由内核产出；其余模块只消费）。</summary>
public enum LimitVerdict
{
    Allow,
    Throttle,
    Degrade,
    Deny,
    Terminate,
}

/// <summary>未知用量策略（估算只用于预留，终止只认真实/保守累计）。</summary>
public enum LimitUnknownPolicy
{
    /// <summary>用上一次真实用量 / 估算端口给出的值；缺失时按 0 预留但记 Unknown。</summary>
    Conservative,

    /// <summary>用配置中的固定常量预留（不写 0）。</summary>
    Fixed,

    /// <summary>只计数，不预留数量（仍记 Unknown）。</summary>
    CountOnly,
}

/// <summary>单条规则；同一 scope+resource 可有多条（按 Priority 从高到低匹配，第一条触发者生效）。</summary>
public sealed record LimitRule(
    string Resource,
    LimitScopeKind Scope,
    LimitWindowKind Window,
    long WindowSeconds,
    long? HardLimit,
    long? SoftLimit,
    LimitVerdict Action,
    int Priority = 0,
    bool IsGauge = false,
    long ThrottleDelayMs = 1000)
{
    /// <summary>是否声明了任一阈值（无阈值规则不参与裁决，仅用于资源声明）。</summary>
    public bool HasThreshold => HardLimit is not null || SoftLimit is not null;
}

/// <summary>一次裁决（纯值）；Reason 必须可读且能落到事件。</summary>
public sealed record LimitDecision(
    LimitVerdict Verdict,
    string Resource,
    string ScopeId,
    long? Limit,
    long Used,
    long Reserved,
    string Reason)
{
    /// <summary>Degrade 仍然执行（走降级路径）；Allow/Degrade 都算放行。</summary>
    public bool IsAllowed => Verdict is LimitVerdict.Allow or LimitVerdict.Degrade;

    /// <summary>Throttle 建议等待毫秒（其他裁决为 0）。</summary>
    public long DelayMs { get; init; }

    /// <summary>裁决发生的作用域层级（事件载荷用）。</summary>
    public LimitScopeKind ScopeKind { get; init; }

    public static LimitDecision Allow(string resource, string scopeId, long used, long reserved)
        => new(LimitVerdict.Allow, resource, scopeId, null, used, reserved, "allow");

    public static LimitDecision Blocked(LimitVerdict verdict, string resource, string scopeId,
        long? limit, long used, long reserved, string reason, long delayMs = 0, LimitScopeKind kind = LimitScopeKind.Process)
        => new(verdict, resource, scopeId, limit, used, reserved, reason)
        {
            DelayMs = delayMs,
            ScopeKind = kind,
        };
}

/// <summary>单个 scope 内单个资源的只读用量。</summary>
public sealed record LimitResourceSnapshot(
    string Resource,
    long Used,
    long Reserved,
    long? Limit,
    long UnknownCount);

/// <summary>单个 scope 的只读快照。</summary>
public sealed record LimitSnapshot(
    string ScopeId,
    LimitScopeKind Kind,
    string? ParentScopeId,
    DateTimeOffset StartedAt,
    IReadOnlyList<LimitResourceSnapshot> Resources);

/// <summary>两相预留租约；Dispose 未 Commit 即按 Abort 释放。</summary>
public interface ILimitLease : IDisposable
{
    LimitDecision Decision { get; }

    /// <summary>提交真实用量；<c>null</c> = Unknown（不写 0、不进分母，仅记 Unknown 计数）。</summary>
    void Commit(long? actualUsage);

    /// <summary>释放预留但不变更已用（失败/取消路径）。</summary>
    void Abort();
}

/// <summary>计量与裁决端口（唯一入口）。</summary>
public interface ILimitKernel
{
    /// <summary>只裁决不占用（预检）：放行/节流/降级/拒绝/终止，不改变计数器。</summary>
    LimitDecision TryBegin(string resource, long? estimated, string? scopeId = null);

    /// <summary>两相预留：放行后占用预留额度，返回租约（必须 Commit 或 Abort）。</summary>
    ILimitLease Begin(string resource, long? estimated, string? scopeId = null);

    /// <summary>事后记账（真实用量；用于 token 等无法预知用量的资源）：不追罚本次，超限即发 LimitTripped。</summary>
    void Record(string resource, long? actualUsage, string? scopeId = null);

    LimitSnapshot Snapshot(string scopeId);

    IReadOnlyList<LimitSnapshot> Snapshots();

    /// <summary>最高权限人工中止（kill switch）：立即拒绝后续全部调用。</summary>
    void Halt(string? reason = null);

    /// <summary>解除中止并清空进程级计数。</summary>
    void Reset();
}

/// <summary>限制事件种类（对应语义契约 7 个 EventKind）。</summary>
public enum LimitEventKind
{
    Reserved,
    Committed,
    Aborted,
    Denied,
    Throttled,
    Tripped,
    Unscoped,
}

/// <summary>限制事件（内核唯一产出；宿主适配器负责投影到既有观测出口，不新建总线）。</summary>
public sealed record LimitEvent(
    LimitEventKind Kind,
    string Resource,
    string ScopeId,
    LimitScopeKind ScopeKind,
    long? Estimated,
    long? Actual,
    long Used,
    long Reserved,
    long? Limit,
    string? Reason,
    long DelayMs,
    DateTimeOffset CreatedAt);

/// <summary>观测端口：宿主实现（投影到既有语义观测/事件区）；异常必须就地吞，观测不阻断主路径。</summary>
public interface ILimitObserver
{
    void Observe(LimitEvent evt);
}

/// <summary>终止端口：宿主实现（写既有 ControlFlag.Halt / budget_exhausted 事件）；内核不持轮次状态。</summary>
public interface ILimitTerminationSink
{
    void Terminate(string resource, string scopeId, string reason);
}

/// <summary>估算端口（可选）：未知用量策略为 Conservative 时的估算来源。</summary>
public interface IUsageEstimator
{
    long? Estimate(string resource, string scopeId);
}

/// <summary>限制拒绝异常（宿主接缝消费；不替代既有终止通道）。</summary>
public sealed class LimitDeniedException : Exception
{
    public LimitDeniedException(LimitDecision decision)
        : base($"limit_{decision.Verdict.ToString().ToLowerInvariant()}: {decision.Reason}")
        => Decision = decision ?? throw new ArgumentNullException(nameof(decision));

    public LimitDecision Decision { get; }
}
