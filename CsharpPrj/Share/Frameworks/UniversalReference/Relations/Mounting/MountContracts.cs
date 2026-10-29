using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 唯一挂载写入口（UMF · L3）。
/// <para>调用方只提供 <c>MountId + Scope + Targets + Trigger/Origin</c>；不允许给 operation / edge /
/// sourceType / targetType / 存储对象（设计单 §5.1 / G3）。</para>
/// </summary>
public interface IMountPort
{
    /// <summary>按 MountId 解析绑定 → 归一 → 仲裁 → 写边；返回逐目标回执。</summary>
    Task<MountReceipt> ApplyAsync(MountRequest request, CancellationToken ct = default);
}

/// <summary>
/// 唯一挂载读入口（UMF · L3）：成员枚举 / 派生集同源，不允许 UI/Context 直接读图（设计单 §5.2 / G5）。
/// </summary>
public interface IMountQuery
{
    /// <summary>按 MountId 枚举成员（等价于按绑定 operation 的 edge 枚举）。</summary>
    Task<MountSet> EnumerateAsync(MountScope scope, string mountId, CancellationToken ct = default);

    /// <summary>按派生声明读取（如 HistoryNodes / TaskNodes / HistoryTasks）。</summary>
    Task<MountSet> DeriveAsync(MountScope scope, string derivationId, CancellationToken ct = default);

    /// <summary>
    /// 批量枚举（UMF · R3）：一次域委托返回多作用域结果，禁门面内 <c>foreach</c> 拼 N+1。
    /// <para>实现方：<see cref="MountQueryService"/>（唯一实现，未命中批量注册表时标 <c>batch-fallback</c>）。
    /// 已删除接口默认 foreach（B4.1：批量回退实现数 = 1）。</para>
    /// </summary>
    Task<IReadOnlyDictionary<string, MountSet>> EnumerateManyAsync(
        IReadOnlyList<MountScope> scopes, string mountId, CancellationToken ct = default);
}

/// <summary>挂载意图（写方向）。</summary>
public enum MountIntent
{
    Ensure = 0,
    Remove = 1,
    Reconcile = 2,
}

/// <summary>挂载来源（映射到内核 <see cref="RelationOrigin"/>）。</summary>
public enum MountOrigin
{
    Live = 0,
    RoundSettle = 1,
    Command = 2,
    Rebuild = 3,
    Admin = 4,
}

/// <summary>目标级结果状态。</summary>
public enum MountTargetStatus
{
    Applied = 0,
    Duplicate = 1,
    Skipped = 2,
    Rejected = 3,
    NotFound = 4,
    Unavailable = 5,
    Suppressed = 6,
}

/// <summary>整体结果（设计单 §7.4）。</summary>
public enum MountOutcome
{
    Committed = 0,
    PartiallyApplied = 1,
    NotApplicable = 2,
    Missing = 3,
    Rejected = 4,
    Failed = 5,
}

/// <summary>挂载作用域（container 实体 + 键 + 命名空间 + 仲裁窗口）。</summary>
/// <param name="ContainerEntity">容器实体类型（history / task / session / …）。</param>
/// <param name="ContainerKey">容器键。</param>
/// <param name="Namespace">作用域轴（workflow_id 等；缺省由命名空间单源解析）。</param>
/// <param name="CorrelationId">仲裁窗口标识（轮次/命令 id；禁永久抑制）。</param>
public sealed record MountScope(
    string ContainerEntity,
    string ContainerKey,
    string? Namespace = null,
    string? CorrelationId = null)
{
    /// <summary>仲裁窗口键（实体 + 键 + 命名空间 + 关联 id）。</summary>
    public string ArbitrationKey
        => string.Join('|', ContainerEntity, ContainerKey, Namespace ?? string.Empty, CorrelationId ?? string.Empty);
}

/// <summary>单个挂载目标。</summary>
public sealed record MountTarget(
    string Entity,
    string Key,
    int? Order = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>批量挂载选项。</summary>
public sealed record MountOptions(
    bool Coalesce = true,
    bool FailFast = false,
    string? ExpectedFamily = null);

/// <summary>挂载请求（唯一入参面）。</summary>
public sealed record MountRequest(
    string MountId,
    MountScope Scope,
    IReadOnlyList<MountTarget> Targets,
    MountIntent Intent = MountIntent.Ensure,
    MountOrigin Origin = MountOrigin.Live,
    string Trigger = MountTriggers.CommandExplicit,
    string Actor = "system",
    string? CorrelationId = null,
    MountOptions? Options = null);

/// <summary>逐目标回执。</summary>
public sealed record MountTargetResult(
    string Entity,
    string Key,
    MountTargetStatus Status,
    string? Edge = null,
    string? Reason = null);

/// <summary>挂载回执（返回值，非第二真源；只做可观测性）。</summary>
public sealed record MountReceipt(
    string MountId,
    string Operation,
    string? Edge,
    MountOrigin Origin,
    string Trigger,
    string? CorrelationId,
    IReadOnlyList<MountTargetResult> Results,
    int Written,
    int Revived,
    int Skipped,
    bool Ok,
    string? Reason,
    MountOutcome Outcome)
{
    /// <summary>批内聚合（同 scope 多次挂载合并为一份回执）。</summary>
    public MountReceipt Merge(MountReceipt next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return new MountReceipt(
            MountId,
            Operation,
            Edge,
            Origin,
            Trigger,
            CorrelationId,
            Results.Concat(next.Results).ToList(),
            Written + next.Written,
            Revived + next.Revived,
            Skipped + next.Skipped,
            Ok && next.Ok,
            CombineReason(Reason, next.Reason),
            Worse(Outcome, next.Outcome));
    }

    /// <summary>合法不参与（Reduced / 空目标 / 触发抑制）。</summary>
    public static MountReceipt NotApplicable(
        MountRequest request, string operation, string? edge, string reason,
        IReadOnlyList<MountTargetResult>? results = null)
        => new(request.MountId, operation, edge, request.Origin, request.Trigger, request.CorrelationId,
            results ?? Array.Empty<MountTargetResult>(), 0, 0, results?.Count ?? 0, true, reason,
            MountOutcome.NotApplicable);

    /// <summary>入参非法 / 未声明 MountId / 端点类型不符。</summary>
    public static MountReceipt Rejected(MountRequest request, string operation, string? edge, string reason)
        => Failure(request, operation, edge, reason, MountOutcome.Rejected, MountTargetStatus.Rejected);

    /// <summary>声明/端口/执行器缺失（Require 姿态）。</summary>
    public static MountReceipt Missing(MountRequest request, string operation, string? edge, string reason)
        => Failure(request, operation, edge, reason, MountOutcome.Missing, MountTargetStatus.Unavailable);

    /// <summary>内核拒绝 / 不变量 / 容器不符 / 写失败。</summary>
    public static MountReceipt Failed(MountRequest request, string operation, string? edge, string reason)
        => Failure(request, operation, edge, reason, MountOutcome.Failed, MountTargetStatus.Rejected);

    private static MountReceipt Failure(
        MountRequest request, string operation, string? edge, string reason,
        MountOutcome outcome, MountTargetStatus status)
        => new(request.MountId, operation, edge, request.Origin, request.Trigger, request.CorrelationId,
            request.Targets.Select(t => new MountTargetResult(t.Entity, t.Key, status, edge, reason)).ToList(),
            0, 0, request.Targets.Count, false, reason, outcome);

    private static string? CombineReason(string? left, string? right)
        => string.IsNullOrWhiteSpace(left) ? right
            : string.IsNullOrWhiteSpace(right) ? left
            : $"{left}; {right}";

    private static MountOutcome Worse(MountOutcome left, MountOutcome right)
        => (int)left >= (int)right ? left : right;
}

/// <summary>读集合（键集 + 空因 + 挂载/派生 id + 指纹）。</summary>
public sealed record MountSet(
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> EmptyReasons,
    string MountId,
    string? Fingerprint = null)
{
    public static MountSet Empty(string mountId, string reason)
        => new(Array.Empty<string>(), new[] { reason }, mountId);

    public static MountSet Unavailable(string mountId, string reason)
        => new(Array.Empty<string>(), new[] { "unavailable:" + reason }, mountId);
}

/// <summary>挂载语义 id 词表（调用方唯一需要知道的语义名）。</summary>
public static class MountIds
{
    public const string NodeToTask = "node_to_task";
    public const string TaskToHistory = "task_to_history";
    public const string MessageToSession = "message_to_session";
    public const string TaskTree = "task_tree";
}

/// <summary>触发事件 id 词表（复用 <c>[Obs] kind=event</c> 的既有事件面）。</summary>
public static class MountTriggers
{
    public const string NodePersisted = "node.persisted";
    public const string RoundSettled = "round.settled";
    public const string TaskCreated = "task.created";
    public const string CommandExplicit = "command.explicit";
    public const string RebuildRequested = "rebuild.requested";
}

/// <summary>触发阶段类词表（排序/批量边界）。</summary>
public static class MountStages
{
    public const string Live = "live";
    public const string Provision = "provision";
    public const string Settle = "settle";
    public const string Explicit = "explicit";
    public const string Rebuild = "rebuild";
}

/// <summary>读模型隔离族。</summary>
public static class MountFamilies
{
    public const string Structural = "structural";
    public const string Presentation = "presentation";
}

/// <summary>失败姿态词表。</summary>
public static class MountPostures
{
    public const string Require = "require";
    public const string BestEffort = "best-effort";
    public const string Reduced = "reduced";
}
