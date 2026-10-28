using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
using UniversalWorkflowFramework.Relations;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Core;

/// <summary>task 域接地服务（U8 域半）：task 镜像 / task↔node / task_owns_task 子任务树 / 投影。</summary>
public sealed class TaskReferenceGrounding
{
    private readonly GraphEdgeWriter _edges;
    private readonly GraphMirrorWriter _mirrors;
    private readonly ReferenceGraphCoordinator _graph;
    private readonly IDefaultWorkflowIdProvider? _workflowIds;
    private readonly TaskRelationSurface _relations;

    /// <summary>子任务树遍历预算（不定深度树信任反转上界）。</summary>
    private const int SubTaskTraversalBudget = 4096;

    public TaskReferenceGrounding(
        GraphEdgeWriter edges, GraphMirrorWriter mirrors, ReferenceGraphCoordinator graph,
        IRelationPorts ports, IDefaultWorkflowIdProvider? workflowIds = null)
    {
        _edges = edges ?? throw new ArgumentNullException(nameof(edges));
        _mirrors = mirrors ?? throw new ArgumentNullException(nameof(mirrors));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _workflowIds = workflowIds;
        _relations = new TaskRelationSurface(ports ?? throw new ArgumentNullException(nameof(ports)));
    }

    /// <summary>镜像 task 端点（Id=taskKey，幂等）。</summary>
    public Task EnsureTaskMirrorAsync(string taskKey, CancellationToken ct = default)
        => _mirrors.EnsureResourceAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey }, taskKey, ct);

    /// <summary>移除 task 端点镜像（级联清边）。</summary>
    public Task RemoveTaskMirrorAsync(string taskKey, CancellationToken ct = default)
        => _graph.DeleteEndpointDurablyAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey }, "task-removed", ct);

    /// <summary>task 关联节点（增量维护任务关联；幂等）。<paramref name="workflowId"/> 为空 ⇒ 取 <see cref="IDefaultWorkflowIdProvider"/> 单源。</summary>
    public async Task ReconcileTaskAsync(string taskKey, IReadOnlyCollection<string> relatedNodeKeys,
        string workflowId = "", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);

        var targets = (relatedNodeKeys ?? Array.Empty<string>())
            .Where(key => !string.Equals(key, taskKey, StringComparison.Ordinal))
            .ToList();

        if (targets.Count == 0) return;

        // 单一批量原语（键归一 / 去重 / 节点行存在性 / 端点镜像 / 批量建边）——
        // 禁本类或调用方再实现第二套键规则（DUP-2 / DUP-3）。
        await EnsureTaskNodeLinksAsync(taskKey, workflowId, targets, ct).ConfigureAwait(false);
    }

    /// <summary>工作流 Id 单源（空 ⇒ 配置缺省；禁调用方各传 "agent-round" / ""）。</summary>
    private string ResolveWorkflow(string? workflowId)
        => _workflowIds?.Resolve(workflowId) ?? (workflowId ?? string.Empty);

    /// <summary>运行时 task 归属边（cross_db_link）：task → 节点。幂等加法。</summary>
    public async Task EnsureTaskNodeLinkAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        // 边跟随落库事实（I2）：节点行不存在 ⇒ 不写边（Skip + Diagnostic），不得静默造幻影边。
        if (_edges.NodeStore is not null)
        {
            var record = await _edges.NodeStore.GetAsync(nodeKey, ct).ConfigureAwait(false);
            if (record is null)
            {
                (_edges.EdgeDiagnostics ?? NullReferenceEdgeDiagnostics.Instance)
                    .EdgeUndeclared("cross_db_link:node-missing", $"{taskKey}|{nodeKey}");
                return;
            }
        }
        // 端点镜像（task / node）由关系聚拢点的 provision= 委托承担；未装配时保留兼容镜像（零回归）。
        if (!_relations.UsesGather)
            await EnsureTaskMirrorAsync(taskKey, ct).ConfigureAwait(false);
        var result = await _relations
            .EnsureNodeTaskAsync(taskKey, nodeKey, workflowId, ct)
            .ConfigureAwait(false);
        RelationResultGuard.ThrowIfFailed(result);
    }

    /// <summary>
    /// 批量归属边（单条与批量的唯一生产实现）：键归一 / 去重 → 节点行存在性（边跟随落库事实）→ 一次批量内核调用。
    /// </summary>
    public async Task<int> EnsureTaskNodeLinksAsync(
        string taskKey, string workflowId, IReadOnlyCollection<string> nodeKeys, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        if (nodeKeys is null || nodeKeys.Count == 0) return 0;

        var keys = TaskNodeLinkPlan.Normalize(nodeKeys);
        if (keys.Count == 0) return 0;

        var wf = ResolveWorkflow(workflowId);

        var existing = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            // 边跟随落库事实（I2）：节点行不存在 ⇒ 不写边（诊断 + 跳过），不得静默造幻影边。
            if (_edges.NodeStore is not null
                && await _edges.NodeStore.GetAsync(key, ct).ConfigureAwait(false) is null)
            {
                (_edges.EdgeDiagnostics ?? NullReferenceEdgeDiagnostics.Instance)
                    .EdgeUndeclared("cross_db_link:node-missing", $"{taskKey}|{key}");
                continue;
            }

            existing.Add(key);
        }

        if (existing.Count == 0) return 0;

        // 端点镜像（task / node）由关系聚拢点的 provision= 委托承担；未装配时保留兼容镜像（零回归）。
        if (!_relations.UsesGather)
        {
            await EnsureTaskMirrorAsync(taskKey, ct).ConfigureAwait(false);
            foreach (var key in existing)
                await _mirrors.EnsureNodeMirrorAsync(wf, key, null, ct).ConfigureAwait(false);
        }

        var result = await _relations
            .EnsureNodeTaskManyAsync(taskKey, existing, wf, ct)
            .ConfigureAwait(false);
        RelationResultGuard.ThrowIfFailed(result);
        return existing.Count;
    }

    /// <summary>解除 task 与节点的关联边（M:N 撤销；幂等）。</summary>
    public async Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        var result = await _relations
            .RemoveNodeTaskAsync(taskKey, nodeKey, workflowId, ct)
            .ConfigureAwait(false);
        RelationResultGuard.ThrowIfFailed(result, allowNotFound: true);
    }

    /// <summary>按 task 查 TaskNodes 派生集（唯一读入口：<c>[Derive] TaskNodes</c>）。</summary>
    public async Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return Array.Empty<string>();
        var result = await _relations
            .DeriveTaskNodesAsync(taskKey, workflowId, ct)
            .ConfigureAwait(false);
        return result.Keys;
    }

    /// <summary>批量 task 节点键集（一次图枚举 + 按 task 分组；消 N+1）。</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetTaskNodeKeysManyAsync(
        IReadOnlyCollection<string> taskKeys, string workflowId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var tasks = (taskKeys ?? Array.Empty<string>())
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tasks.Count == 0) return result;

        var resources = tasks
            .Select(key => new ResourceIdentifier
            {
                ResourceType = NodeResourceTypes.Task,
                Id = key,
            })
            .ToList();
        var refsByKey = await _graph
            .GetReferencesManyAsync(resources, includeIncoming: false, ct)
            .ConfigureAwait(false);

        foreach (var taskKey in tasks)
        {
            var resource = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey };
            if (!refsByKey.TryGetValue(_graph.Keys.Endpoint(resource), out var refs))
            {
                result[taskKey] = Array.Empty<string>();
                continue;
            }

            var targets = GraphEdgeWriter.ProjectOutgoingTargetIds(
                refs, CrossDbReferenceTypes.CrossDbLink, static r => r.RefType, static r => r.Target?.Id);
            var keys = new List<string>(targets.Count);
            foreach (var id in targets)
            {
                if (HierarchyResourceKeys.TryParseNodeKey(id, workflowId, out var nodeKey))
                {
                    keys.Add(nodeKey);
                }
            }

            result[taskKey] = keys.Distinct(StringComparer.Ordinal).ToList();
        }

        return result;
    }

    /// <summary>task → sub-task 归属边（task_owns_task，不定深度任务树；幂等加法）。</summary>
    public async Task LinkSubTaskAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentTaskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(subTaskKey);
        if (string.Equals(parentTaskKey, subTaskKey, StringComparison.Ordinal)) return; // 自指无意义

        await EnsureTaskMirrorAsync(parentTaskKey, ct).ConfigureAwait(false);
        await EnsureTaskMirrorAsync(subTaskKey, ct).ConfigureAwait(false);
        var result = await _relations
            .EnsureSubTaskAsync(parentTaskKey, subTaskKey, ct)
            .ConfigureAwait(false);
        RelationResultGuard.ThrowIfFailed(result);
    }

    /// <summary>解除 task → sub-task 归属边（幂等）。</summary>
    public async Task UnlinkSubTaskAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentTaskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(subTaskKey);
        var result = await _relations
            .RemoveSubTaskAsync(parentTaskKey, subTaskKey, ct)
            .ConfigureAwait(false);
        RelationResultGuard.ThrowIfFailed(result, allowNotFound: true);
    }

    /// <summary>按 task 查 task_owns_task 出边，返回其直属子任务键集（一层展开）。</summary>
    public async Task<IReadOnlyList<string>> GetSubTaskKeysAsync(string taskKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return Array.Empty<string>();
        var result = await _relations.EnumerateSubTasksAsync(taskKey, ct).ConfigureAwait(false);
        return result.Keys;
    }

    /// <summary>带环检测的 task_owns_task 写入口：自指/成环 ⇒ 拒绝且不写边。</summary>
    public async Task<bool> LinkSubTaskCheckedAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentTaskKey) || string.IsNullOrWhiteSpace(subTaskKey)) return false;
        if (string.Equals(parentTaskKey, subTaskKey, StringComparison.Ordinal)) return false;
        if (await IsReachableAsync(subTaskKey, parentTaskKey, ct).ConfigureAwait(false)) return false;
        await LinkSubTaskAsync(parentTaskKey, subTaskKey, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>沿 task_owns_task 出边判定可达（环检测，遍历预算收口）。</summary>
    private async Task<bool> IsReachableAsync(string from, string target, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { from };
        var queue = new Queue<string>();
        queue.Enqueue(from);
        var visited = 0;
        while (queue.Count > 0 && visited++ < SubTaskTraversalBudget)
        {
            var current = queue.Dequeue();
            foreach (var next in await GetSubTaskKeysAsync(current, ct).ConfigureAwait(false))
            {
                if (string.Equals(next, target, StringComparison.Ordinal)) return true;
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>投影候选清单：task 作用域挂载 + NodeToNode 组合边 Kahn 拓扑（环内按 nodeKey 稳定序）。</summary>
    public async Task<IReadOnlyList<NodeProjectionItem>> ProjectTaskNodesAsync(string taskKey, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return Array.Empty<NodeProjectionItem>();
        var mounted = await GetTaskNodeKeysAsync(taskKey, workflowId, ct).ConfigureAwait(false);
        if (mounted.Count == 0) return Array.Empty<NodeProjectionItem>();
        var mountedSet = new HashSet<string>(mounted, StringComparer.Ordinal);

        var downstream = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in mounted) { downstream[n] = new List<string>(); indegree[n] = 0; }
        foreach (var n in mounted)
        {
            foreach (var next in await _graph.GetRelatedNodesAsync(workflowId, n, NodeRelationType.NodeToNode, outgoing: true, ct: ct).ConfigureAwait(false))
                if (mountedSet.Contains(next) && !downstream[n].Contains(next))
                {
                    downstream[n].Add(next);
                    indegree[next]++;
                }
        }

        var queue = new List<string>(mounted.Where(n => indegree[n] == 0).OrderBy(n => n, StringComparer.Ordinal));
        var result = new List<string>();
        var processed = new HashSet<string>(StringComparer.Ordinal);
        for (var qi = 0; qi < queue.Count; qi++)
        {
            var n = queue[qi];
            if (!processed.Add(n)) continue;
            result.Add(n);
            foreach (var next in downstream[n])
                if (--indegree[next] == 0)
                    InsertSorted(queue, next);
        }
        foreach (var n in mounted.OrderBy(x => x, StringComparer.Ordinal))
            if (!result.Contains(n))
                result.Add(n);

        return result.Select((nk, idx) => new NodeProjectionItem(nk, workflowId, idx + 1)).ToList();
    }

    private static void InsertSorted(List<string> queue, string value)
    {
        var i = queue.FindIndex(x => string.CompareOrdinal(x, value) > 0);
        if (i < 0) queue.Add(value); else queue.Insert(i, value);
    }
}

