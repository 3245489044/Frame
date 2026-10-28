using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
using Operation.DataStoreDriven.Relations;
using Share.Conversation.Aggregates;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Core;

/// <summary>history 域接地服务（U8 域半）：历史根镜像 / 历史↔task / 派生节点集 / 合并 / 端点重键。</summary>
public sealed class HistoryReferenceGrounding
{
    private readonly GraphEdgeWriter _edges;
    private readonly GraphMirrorWriter _mirrors;
    private readonly ReferenceGraphCoordinator _graph;
    private readonly TaskReferenceGrounding _tasks;
    private readonly IRelationPorts _ports;
    private readonly HistoryRelationSurface _relations;

    public HistoryReferenceGrounding(
        GraphEdgeWriter edges, GraphMirrorWriter mirrors, ReferenceGraphCoordinator graph,
        TaskReferenceGrounding tasks, IRelationPorts ports)
    {
        _edges = edges ?? throw new ArgumentNullException(nameof(edges));
        _mirrors = mirrors ?? throw new ArgumentNullException(nameof(mirrors));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _relations = new HistoryRelationSurface(_ports);
    }

    /// <summary>历史 class 容器根端点：幂等建 history.{historyId} 端点镜像。</summary>
    public Task EnsureHistoryMirrorAsync(string historyId, CancellationToken ct = default)
        => _mirrors.EnsureResourceAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HistoryNodeId(historyId) },
            HistoryNodeId(historyId), ct);

    /// <summary>移除历史根端点镜像（级联清边）。</summary>
    public Task RemoveHistoryMirrorAsync(string historyId, CancellationToken ct = default)
        => _graph.DeleteEndpointDurablyAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HistoryNodeId(historyId) }, "history-removed", ct);

    /// <summary>删除历史（返回是否命中删除）。</summary>
    public async Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(historyId)) return false;
        try
        {
            await RemoveHistoryMirrorAsync(historyId, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    /// <summary>历史归拢任务：history → task（history_owns_task，幂等加法；禁自环）。</summary>
    public async Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        if (string.Equals(historyId, taskKey, StringComparison.Ordinal))
            throw new HistorySelfLoopException(historyId, taskKey);
        await EnsureHistoryMirrorAsync(historyId, ct).ConfigureAwait(false);
        await _tasks.EnsureTaskMirrorAsync(taskKey, ct).ConfigureAwait(false);
        var result = await _relations.AttachTaskAsync(historyId, taskKey, ct).ConfigureAwait(false);
        RelationResultGuard.ThrowIfFailed(result);
    }

    /// <summary>解除历史归拢任务边。</summary>
    public async Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
    {
        var result = await _relations.DetachTaskAsync(historyId, taskKey, ct).ConfigureAwait(false);
        // 未命中 = 已不存在（幂等删除）
        RelationResultGuard.ThrowIfFailed(result, allowNotFound: true);
    }

    /// <summary>按 HistoryTasks 派生集返回归拢的任务键集。</summary>
    public async Task<IReadOnlyList<string>> GetHistoryTasksAsync(string historyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(historyId)) return Array.Empty<string>();
        var result = await _relations.DeriveHistoryTasksAsync(historyId, ct).ConfigureAwait(false);
        return result.Keys;
    }

    /// <summary>批量成员集（一次持锁读多个历史出边）。</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetHistoryTasksManyAsync(
        IReadOnlyCollection<string> historyIds, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (historyIds is null) return result;
        var ids = historyIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0) return result;

        var resources = ids
            .Select(id => (HistoryId: id, Resource: new ResourceIdentifier
            {
                ResourceType = NodeResourceTypes.History,
                Id = HistoryNodeId(id),
            }))
            .ToList();
        var refsByKey = await _graph
            .GetReferencesManyAsync(resources.Select(static r => r.Resource).ToList(), includeIncoming: false, ct)
            .ConfigureAwait(false);

        foreach (var (historyId, resource) in resources)
        {
            if (refsByKey.TryGetValue(_graph.Keys.Endpoint(resource), out var refs))
                result[historyId] = GraphEdgeWriter.ProjectOutgoingTargetIds(
                    refs, CrossDbReferenceTypes.HistoryOwnsTask, static r => r.RefType, static r => r.Target?.Id);
            else
                result[historyId] = Array.Empty<string>();
        }
        return result;
    }

    /// <summary>历史的工作流节点键集（派生视图：<c>[Derive] HistoryNodes</c>）。</summary>
    public async Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(historyId)) return Array.Empty<string>();
        var result = await _relations
            .DeriveHistoryNodesAsync(historyId, workflowId, ct)
            .ConfigureAwait(false);
        return result.Keys;
    }

    /// <summary>历史容器合并（先写后撤，幂等可重跑）。</summary>
    public async Task<int> MergeHistoriesAsync(string intoHistory, string fromHistory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(intoHistory) || string.IsNullOrWhiteSpace(fromHistory)) return 0;
        if (string.Equals(intoHistory, fromHistory, StringComparison.Ordinal)) return 0;

        await EnsureHistoryMirrorAsync(intoHistory, ct).ConfigureAwait(false);
        var tasks = await GetHistoryTasksAsync(fromHistory, ct).ConfigureAwait(false);
        var moved = 0;
        foreach (var taskKey in tasks)
        {
            if (string.IsNullOrWhiteSpace(taskKey)) continue;
            await AttachHistoryTaskAsync(intoHistory, taskKey, ct).ConfigureAwait(false);
            await DetachHistoryTaskAsync(fromHistory, taskKey, ct).ConfigureAwait(false);
            moved++;
        }
        return moved;
    }

    /// <summary>K3 端点重键计划（只读）。</summary>
    public async Task<EndpointRekeyPlan> PlanEndpointRekeyAsync(
        IReadOnlyCollection<string> taskKeys, CancellationToken ct = default)
    {
        var resources = await _graph.GetAllResourcesAsync(ct).ConfigureAwait(false);
        var ids = resources.Select(r => r.Identifier).Where(id => id is not null).ToList();
        if (ids.Count == 0) return EndpointRekeyPlan.Empty;
        var byKey = await _graph.GetReferencesManyAsync(ids, includeIncoming: false, ct).ConfigureAwait(false);
        var edges = byKey.Values.SelectMany(list => list).Where(e => !e.IsIncoming).ToList();
        return EndpointKeyRekey.Plan(edges, taskKeys ?? Array.Empty<string>());
    }

    /// <summary>K3 端点重键执行（幂等，归属边经内核 <c>Origin=Rebuild</c>）。</summary>
    public Task<EndpointRekeyResult> ApplyEndpointRekeyAsync(EndpointRekeyPlan plan, CancellationToken ct = default)
        => EndpointKeyRekey.ApplyAsync(_graph, plan, ct, _ports);

    /// <summary>历史 Id 单一权威：history.{historyId}。</summary>
    public static string HistoryNodeId(string historyId) => HierarchyResourceKeys.History(historyId);
}
