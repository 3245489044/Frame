using System.Collections.Generic;

namespace FrontEndDriven.ProductionConsole;

/// <summary>任务状态机：排队 → 运行中 → 成功 / 失败 / 取消。</summary>
public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// 任务快照（不可变视图）：既用于页面渲染，也作为历史落盘载体（可 JSON 序列化）。
/// 所有字段都是纯数据，插件与框架之间只传它。
/// </summary>
public sealed class JobSnapshot
{
    public string JobId { get; init; } = "";

    public string CapabilityId { get; init; } = "";

    public string Group { get; init; } = "";

    public JobStatus Status { get; init; } = JobStatus.Queued;

    /// <summary>0..1。</summary>
    public double Progress { get; init; }

    /// <summary>当前阶段（节点 Id 或语义名，如 探活/提交/轮询/下载/校验）。</summary>
    public string Phase { get; init; } = "";

    public long ElapsedMs { get; init; }

    public string? Error { get; init; }

    /// <summary>降级/不可用原因（区别于 Error：不视为任务失败）。</summary>
    public string? Reason { get; init; }

    public Dictionary<string, object?> Params { get; init; } = new(StringComparer.Ordinal);

    public List<ArtifactSpec> Artifacts { get; init; } = new();

    /// <summary>一稿一痕键（插件可空）。</summary>
    public string? InputHash { get; init; }

    public bool CacheHit { get; init; }

    public bool Planned { get; init; }

    /// <summary>排队位置（1 = 下一个；0 = 正在运行/已结束）。</summary>
    public int QueuePosition { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public bool IsTerminal => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled;
}

/// <summary>任务执行结果（Runner 返回给 JobRegistry 的唯一形态；错误即数据，不抛）。</summary>
public sealed class JobExecutionResult
{
    public List<ArtifactSpec> Artifacts { get; init; } = new();

    public string? Error { get; init; }

    public string? Reason { get; init; }

    public string? InputHash { get; init; }

    public bool CacheHit { get; init; }

    public bool Planned { get; init; }

    public bool IsSuccess => Error is null;

    public static JobExecutionResult Success(
        IEnumerable<ArtifactSpec>? artifacts = null,
        string? inputHash = null,
        bool cacheHit = false,
        bool planned = false) =>
        new()
        {
            Artifacts = artifacts is null ? new List<ArtifactSpec>() : new List<ArtifactSpec>(artifacts),
            InputHash = inputHash,
            CacheHit = cacheHit,
            Planned = planned,
        };

    public static JobExecutionResult Failed(string error, string? reason = null, IEnumerable<ArtifactSpec>? artifacts = null) =>
        new()
        {
            Error = error,
            Reason = reason,
            Artifacts = artifacts is null ? new List<ArtifactSpec>() : new List<ArtifactSpec>(artifacts),
        };
}
