using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Relations;

/// <summary>
/// 关系写面适配模板：把「operation 解析 / 单条与批量归一 / 失败可见化」收到基类。
/// <para>子类只声明键映射；禁 <c>switch(edge)</c> / <c>if(domain==…)</c>（领域差异只能是数据）。</para>
/// </summary>
public abstract class RelationSurfaceAdapter
{
    private readonly string _surfaceName;

    protected RelationSurfaceAdapter(IRelationPorts ports, string surfaceName)
    {
        Ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _surfaceName = string.IsNullOrWhiteSpace(surfaceName) ? GetType().Name : surfaceName;
    }

    /// <summary>唯一端口租约（未装配即抛）。</summary>
    protected IRelationPorts Ports { get; }

    /// <summary>单条/批量统一入口：批量走 <see cref="RelationRequest.TargetKeys"/>（禁调用方循环拼边）。</summary>
    protected Task<RelationResult> EnsureAsync(string operation, RelationRequest request, CancellationToken ct)
    {
        RequireDeclared(operation, remove: false);
        return Ports.Commands.EnsureAsync(operation, request, ct);
    }

    /// <summary>删除入口（intent 必须为 remove）。</summary>
    protected Task<RelationResult> RemoveAsync(string operation, RelationRequest request, CancellationToken ct)
    {
        RequireDeclared(operation, remove: true);
        return Ports.Commands.RemoveAsync(operation, request, ct);
    }

    /// <summary>按派生集读（空集带空因）。</summary>
    protected Task<RelationQueryResult> DeriveAsync(string deriveId, RelationScope scope, CancellationToken ct)
        => Ports.Queries.DeriveAsync(deriveId, scope, ct);

    /// <summary>按边枚举（空集带空因）。</summary>
    protected Task<RelationQueryResult> EnumerateAsync(string edge, RelationScope scope, CancellationToken ct)
        => Ports.Queries.EnumerateAsync(edge, scope, ct);

    /// <summary>edge + intent=ensure ⇒ 唯一 operation；0/多义 ⇒ 抛 <c>edge-not-unique</c>。</summary>
    protected Task<RelationResult> EnsureByEdgeAsync(string edge, RelationRequest request, CancellationToken ct)
        => Ports.Commands.EnsureAsync(ResolveEdge(edge, remove: false).Id, request, ct);

    /// <summary>edge + intent=remove ⇒ 唯一 operation；0/多义 ⇒ 抛 <c>edge-not-unique</c>。</summary>
    protected Task<RelationResult> RemoveByEdgeAsync(string edge, RelationRequest request, CancellationToken ct)
        => Ports.Commands.RemoveAsync(ResolveEdge(edge, remove: true).Id, request, ct);

    /// <summary>失败可见化统一口径：NotFound 可选容忍，其余抛。</summary>
    protected static void ThrowIfFailed(RelationResult result, bool allowNotFound = false)
        => RelationResultGuard.ThrowIfFailed(result, allowNotFound);

    private RelationOperation RequireDeclared(string operation, bool remove)
    {
        var declared = Ports.Operations.Resolve(operation)
            ?? throw new InvalidOperationException($"{_surfaceName}:未声明 operation:{operation}");
        if (declared.IsRemove != remove)
            throw new InvalidOperationException(
                $"{_surfaceName}:operation 意图不符:{operation}（声明 intent={declared.Intent}）");
        return declared;
    }

    private RelationOperation ResolveEdge(string edge, bool remove)
        => Ports.Operations.ResolveByEdge(edge, remove)
           ?? throw new InvalidOperationException(
               $"{_surfaceName}:edge-not-unique:{edge}（intent={(remove ? "remove" : "ensure")}）");
}

/// <summary>
/// operation id 字面量单源（声明面 <c>relation-ops.ushar</c> 的镜像常量；禁调用点各写字符串）。
/// </summary>
public static class RelationOperationIds
{
    public const string MountNodeTask = "mount_node_task";
    public const string UnmountNodeTask = "unmount_node_task";
    public const string MountTaskHistory = "mount_task_history";
    public const string UnmountTaskHistory = "unmount_task_history";
    public const string MountSessionNode = "mount_session_node";
    public const string UnmountSessionNode = "unmount_session_node";
    public const string MountSubTask = "mount_subtask";
    public const string UnmountSubTask = "unmount_subtask";

    public const string DeriveTaskNodes = "TaskNodes";
    public const string DeriveHistoryNodes = "HistoryNodes";
    public const string DeriveHistoryTasks = "HistoryTasks";
}

/// <summary>归属边名（与声明面 <c>[Relation].edge</c> 逐字一致）。</summary>
public static class RelationEdgeNames
{
    public const string CrossDbLink = "cross_db_link";
    public const string HistoryOwnsTask = "history_owns_task";
    public const string SessionOwns = "session_owns";
    public const string TaskOwnsTask = "task_owns_task";
}

/// <summary>task 域写面：task↔node（cross_db_link）/ task↔task（task_owns_task）。</summary>
public sealed class TaskRelationSurface : RelationSurfaceAdapter
{
    public TaskRelationSurface(IRelationPorts ports) : base(ports, "task") { }

    public Task<RelationResult> EnsureNodeTaskManyAsync(
        string taskKey, IReadOnlyList<string> nodeKeys, string? workflowId, CancellationToken ct = default)
        => EnsureAsync(
            RelationOperationIds.MountNodeTask,
            new RelationRequest(taskKey, TargetKeys: nodeKeys, Namespace: workflowId),
            ct);

    public Task<RelationResult> EnsureNodeTaskAsync(
        string taskKey, string nodeKey, string? workflowId, CancellationToken ct = default)
        => EnsureAsync(
            RelationOperationIds.MountNodeTask,
            new RelationRequest(taskKey, TargetKey: nodeKey, Namespace: workflowId),
            ct);

    public Task<RelationResult> RemoveNodeTaskAsync(
        string taskKey, string nodeKey, string? workflowId, CancellationToken ct = default)
        => RemoveAsync(
            RelationOperationIds.UnmountNodeTask,
            new RelationRequest(taskKey, TargetKey: nodeKey, Namespace: workflowId),
            ct);

    public Task<RelationResult> EnsureSubTaskAsync(
        string parentTaskKey, string subTaskKey, CancellationToken ct = default)
        => EnsureAsync(
            RelationOperationIds.MountSubTask,
            new RelationRequest(parentTaskKey, TargetKey: subTaskKey),
            ct);

    public Task<RelationResult> RemoveSubTaskAsync(
        string parentTaskKey, string subTaskKey, CancellationToken ct = default)
        => RemoveAsync(
            RelationOperationIds.UnmountSubTask,
            new RelationRequest(parentTaskKey, TargetKey: subTaskKey),
            ct);

    public Task<RelationQueryResult> DeriveTaskNodesAsync(
        string taskKey, string? workflowId, CancellationToken ct = default)
        => DeriveAsync(
            RelationOperationIds.DeriveTaskNodes,
            new RelationScope(taskKey, string.IsNullOrWhiteSpace(workflowId) ? null : workflowId),
            ct);

    public Task<RelationQueryResult> EnumerateSubTasksAsync(
        string taskKey, CancellationToken ct = default)
        => EnumerateAsync(RelationEdgeNames.TaskOwnsTask, new RelationScope(taskKey), ct);
}

/// <summary>history 域写面：history↔task（history_owns_task）。</summary>
public sealed class HistoryRelationSurface : RelationSurfaceAdapter
{
    public HistoryRelationSurface(IRelationPorts ports) : base(ports, "history") { }

    public Task<RelationResult> AttachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => EnsureAsync(
            RelationOperationIds.MountTaskHistory,
            new RelationRequest(historyId, TargetKey: taskKey),
            ct);

    public Task<RelationResult> DetachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => RemoveAsync(
            RelationOperationIds.UnmountTaskHistory,
            new RelationRequest(historyId, TargetKey: taskKey),
            ct);

    public Task<RelationQueryResult> DeriveHistoryTasksAsync(string historyId, CancellationToken ct = default)
        => DeriveAsync(RelationOperationIds.DeriveHistoryTasks, new RelationScope(historyId), ct);

    public Task<RelationQueryResult> DeriveHistoryNodesAsync(
        string historyId, string? workflowId = null, CancellationToken ct = default)
        => DeriveAsync(
            RelationOperationIds.DeriveHistoryNodes,
            new RelationScope(historyId, string.IsNullOrWhiteSpace(workflowId) ? null : workflowId),
            ct);

    public Task<RelationQueryResult> EnumerateTasksAsync(string historyId, CancellationToken ct = default)
        => EnumerateAsync(RelationEdgeNames.HistoryOwnsTask, new RelationScope(historyId), ct);
}

/// <summary>session 域写面：session↔node（session_owns）。</summary>
public sealed class SessionRelationSurface : RelationSurfaceAdapter
{
    public SessionRelationSurface(IRelationPorts ports) : base(ports, "session") { }

    public Task<RelationResult> AttachNodeAsync(
        string sessionName, string nodeKey, string? workflowId, CancellationToken ct = default)
        => EnsureAsync(
            RelationOperationIds.MountSessionNode,
            new RelationRequest(sessionName, TargetKey: nodeKey, Namespace: workflowId),
            ct);

    public Task<RelationResult> RemoveNodeAsync(
        string sessionName, string nodeKey, string? workflowId, CancellationToken ct = default)
        => RemoveAsync(
            RelationOperationIds.UnmountSessionNode,
            new RelationRequest(sessionName, TargetKey: nodeKey, Namespace: workflowId),
            ct);

    public Task<RelationQueryResult> EnumerateNodesAsync(
        string sessionName, string? workflowId, CancellationToken ct = default)
        => EnumerateAsync(
            RelationEdgeNames.SessionOwns,
            new RelationScope(sessionName, string.IsNullOrWhiteSpace(workflowId) ? null : workflowId),
            ct);
}

/// <summary>关系结果可见化（唯一实现；适配器与域服务共用，禁各自复制判定）。</summary>
public static class RelationResultGuard
{
    /// <summary>失败可见化统一口径：NotFound 可选容忍，其余抛。</summary>
    public static void ThrowIfFailed(RelationResult result, bool allowNotFound = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Ok) return;
        if (allowNotFound && result.Outcome == RelationOutcome.NotFound) return;

        var detail = result.Diagnostics.Count > 0 ? string.Join("; ", result.Diagnostics) : result.Outcome.ToString();
        throw new InvalidOperationException(
            $"关系操作失败：operation={result.Operation} edge={result.Edge} outcome={result.Outcome} ｜ {detail}");
    }
}

