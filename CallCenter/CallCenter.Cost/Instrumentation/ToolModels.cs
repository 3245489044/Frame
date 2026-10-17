using CallCenter.Cost.Costing;
using CallCenter.Cost.Metering;

namespace CallCenter.Cost.Instrumentation;

/// <summary>工具执行介质（开放受控词；PS 脚本 = PowerShell，不是独立类别）。</summary>
public static class ToolMediums
{
    public const string Csharp = "Csharp";
    public const string TypeScript = "TypeScript";
    public const string Mcp = "Mcp";
    public const string PowerShell = "PowerShell";
    public const string Shell = "Shell";
    public const string Http = "Http";
}

/// <summary>检测点来源（开放受控词）。</summary>
public static class ToolOrigins
{
    public const string CallCenterRouter = "CallCenterRouter";
    public const string AgentToolProvider = "AgentToolProvider";
    public const string FrameInvoke = "FrameInvoke";
    public const string McpBridge = "McpBridge";
    public const string DirectShell = "DirectShell";
}

/// <summary>一次调用结果（与 ErrorClass 可映射）。</summary>
public enum ToolOutcome
{
    Success,
    Failure,
    Timeout,
    Cancelled,
}

/// <summary>工具调用请求（Begin 的输入）。</summary>
public sealed record ToolInvocationRequest(
    string ToolName,
    string Medium,
    string Origin,
    InstanceRef Instance,
    string? ArgsHash = null,
    string? CallSite = null,
    string? TaskKey = null,
    string? RoundId = null,
    string? SessionKey = null,
    string? TraceId = null,
    string? LogicalCallId = null,
    string? ParentSpanId = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>
/// 工具调用计数（唯一口径）：
/// PhysicalCount = distinct ToolCallId；LogicalCount = distinct LogicalCallId；
/// RetryCount = Physical - Logical；取消/超时计调用但不计成功。
/// </summary>
public sealed record ToolCallUsage(
    int PhysicalCount,
    int LogicalCount,
    int RetryCount,
    int SuccessCount,
    int FailureCount,
    int TimeoutCount,
    int CancelledCount,
    long WallMsTotal,
    ComputeUsage? Compute = null)
{
    public static ToolCallUsage Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    public double? SuccessRate => LogicalCount == 0 ? null : (double)SuccessCount / LogicalCount;

    public double? RetryRate => LogicalCount == 0 ? null : (double)RetryCount / LogicalCount;

    public double? AvgWallMs => PhysicalCount == 0 ? null : (double)WallMsTotal / PhysicalCount;

    public ToolCallUsage WithCompute(ComputeUsage? compute) => this with { Compute = compute ?? Compute };
}

/// <summary>一次工具调用事实（落库/聚合的最小单元）。</summary>
public sealed record ToolInvocation(
    string ToolCallId,
    string? TraceId,
    string? ParentSpanId,
    string ToolName,
    string Medium,
    string Origin,
    InstanceRef Instance,
    string? ArgsHash,
    string? CallSite,
    string? TaskKey,
    string? RoundId,
    string? SessionKey,
    DateTimeOffset StartedAt,
    long DurationMs,
    ToolOutcome Outcome,
    int Attempt,
    string LogicalCallId,
    ToolCallUsage Usage,
    CostResult? Cost = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>工具调用观察者（唯一检测原语）。</summary>
public interface IToolInvocationObserver
{
    ToolInvocationScope Begin(ToolInvocationRequest request);
    void Complete(ToolInvocationScope scope, ToolOutcome outcome, ToolCallUsage usage);
    void Retry(ToolInvocationScope scope, int attempt);
    void Abandon(ToolInvocationScope scope, string reason);
}
