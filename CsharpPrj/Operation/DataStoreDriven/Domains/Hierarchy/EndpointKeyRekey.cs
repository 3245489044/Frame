using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Identity;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Graph;
using UniversalWorkflowFramework.Relations;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>端点重键动作：把 <paramref name="Source"/> 上的边迁到 <paramref name="NewSource"/>（target/refType 不变）。</summary>
public sealed record EndpointRekeyAction(
    string RefType,
    ResourceIdentifier Source,
    ResourceIdentifier Target,
    ResourceIdentifier NewSource);

/// <summary>无法机械重键的边（可见化，不猜测、不重连）。</summary>
public sealed record EndpointRekeyOrphan(string RefType, string SourceId, string TargetId, string Reason);

/// <summary>重键计划（只读分类结果）：<see cref="RetiredSources"/> = 迁移后应清理的旧端点（仅 history）。</summary>
public sealed record EndpointRekeyPlan(
    IReadOnlyList<EndpointRekeyAction> Rekeyable,
    IReadOnlyList<EndpointRekeyOrphan> Orphans,
    IReadOnlyList<ResourceIdentifier> RetiredSources,
    int Unchanged)
{
    public static EndpointRekeyPlan Empty { get; } = new(
        Array.Empty<EndpointRekeyAction>(), Array.Empty<EndpointRekeyOrphan>(),
        Array.Empty<ResourceIdentifier>(), 0);
}

/// <summary>重键执行结果（幂等：二次执行 rekeyable=0）。</summary>
public sealed record EndpointRekeyResult(int Rekeyed, int Orphans, int Retired);

/// <summary>
/// 端点键重键（数据迁移单点，K3）：把历史三重前缀与 <c>task:history:*</c> 会话任务边
/// 归位到「族前缀 + 裸身份」口径。
/// <para>
/// 写边铁律：本类只编排，写原语一律经 <see cref="ReferenceGraphCoordinator"/> 的
/// <c>AddReferenceAsync/RemoveReferenceAsync</c>（唯一写边 owner），禁直接操作集合。
/// </para>
/// </summary>
public static class EndpointKeyRekey
{
    /// <summary>
    /// 按边集分类重键计划（纯函数：零 IO，可单测）。
    /// ① history 源：Id 归一后不同 ⇒ rekey（旧端点在迁移后清理）；
    /// ② task 源 <c>history:*</c>：按 <c>history_owns_task</c> 1:1 映射到成员 task；无配对/歧义 ⇒ orphan（保留原边）；
    /// ③ task 源 <c>session</c> 等：<c>ur_tasks</c> 无该行 ⇒ orphan；
    /// ④ 其余（node / session 源）与全部 target：保持（<c>agent-round.</c> 是合法 workflowId）。
    /// </summary>
    /// <param name="edges">图中全部出边（<c>includeIncoming:false</c> 采集）。</param>
    /// <param name="taskKeys">实体真源 <c>ur_tasks</c> 的主键集。</param>
    public static EndpointRekeyPlan Plan(
        IEnumerable<ResourceReference> edges,
        IReadOnlyCollection<string> taskKeys)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var tasks = new HashSet<string>(taskKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
        var list = edges.ToList();

        // 成员配对（history 裸身份 → 成员 task）：仅取 history_owns_task；歧义（同 history 多目标）显式排除，不猜。
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in list)
        {
            if (!string.Equals(e.RefType, CrossDbReferenceTypes.HistoryOwnsTask, StringComparison.Ordinal)) continue;
            if (!IsType(e.Source, NodeResourceTypes.History) || !IsType(e.Target, NodeResourceTypes.Task)) continue;
            var historyId = NodeEndpointIds.NormalizeHistoryId(e.Source!.Id);
            if (historyId.Length == 0 || e.Target!.Id is null) continue;
            if (pairs.TryGetValue(historyId, out var existing))
            {
                if (!string.Equals(existing, e.Target.Id, StringComparison.Ordinal)) ambiguous.Add(historyId);
            }
            else pairs[historyId] = e.Target.Id;
        }

        var actions = new List<EndpointRekeyAction>();
        var orphans = new List<EndpointRekeyOrphan>();
        var retired = new Dictionary<string, ResourceIdentifier>(StringComparer.Ordinal);
        var unchanged = 0;

        foreach (var e in list)
        {
            if (e.Source?.Id is null || e.Target?.Id is null) continue;

            if (IsType(e.Source, NodeResourceTypes.History))
            {
                var newId = NodeEndpointIds.History(e.Source.Id);
                if (!string.Equals(newId, e.Source.Id, StringComparison.Ordinal))
                {
                    actions.Add(new EndpointRekeyAction(e.RefType, e.Source, e.Target, WithId(e.Source, newId)));
                    retired[e.Source.GetUniqueKey()] = e.Source;
                    continue;
                }
                unchanged++;
                continue;
            }

            if (IsType(e.Source, NodeResourceTypes.Task))
            {
                if (TryGetHistoryTaskKey(e.Source.Id, out var historyId))
                {
                    if (ambiguous.Contains(historyId))
                    {
                        orphans.Add(new EndpointRekeyOrphan(e.RefType, e.Source.Id, e.Target.Id,
                            $"history {historyId} 的成员边歧义（>1 目标），不迁移"));
                        continue;
                    }
                    if (!pairs.TryGetValue(historyId, out var member))
                    {
                        orphans.Add(new EndpointRekeyOrphan(e.RefType, e.Source.Id, e.Target.Id,
                            $"history {historyId} 无 history_owns_task 成员边，不迁移（保留）"));
                        continue;
                    }
                    actions.Add(new EndpointRekeyAction(e.RefType, e.Source, e.Target, WithId(e.Source, member)));
                    continue;
                }

                if (!tasks.Contains(e.Source.Id))
                {
                    orphans.Add(new EndpointRekeyOrphan(e.RefType, e.Source.Id, e.Target.Id,
                        "ur_tasks 无该行（幻影 task 端点），不猜测重连"));
                    continue;
                }
                unchanged++;
                continue;
            }

            unchanged++;
        }

        return new EndpointRekeyPlan(actions, orphans, retired.Values.ToList(), unchanged);
    }

    /// <summary>执行计划（幂等；顺序 = 先加新边后删旧边，失败时不清理旧端点）。</summary>
    /// <param name="ports">关系端口（可选；装配后归属边经内核 <c>Origin=Rebuild</c>，未声明边仍走图原语）。</param>
    public static async Task<EndpointRekeyResult> ApplyAsync(
        ReferenceGraphCoordinator graph,
        EndpointRekeyPlan plan,
        CancellationToken ct = default,
        IRelationPorts? ports = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(plan);

        var rekeyed = 0;
        foreach (var action in plan.Rekeyable)
        {
            ct.ThrowIfCancellationRequested();

            if (ports is not null && ports.Operations.ResolveByEdge(action.RefType, remove: false) is { } ensureOp)
            {
                var request = new RelationRequest(
                    action.NewSource.Id ?? string.Empty,
                    TargetKey: action.Target.Id ?? string.Empty,
                    Namespace: action.NewSource.Location,
                    Origin: RelationOrigin.Rebuild);
                var applied = await ports.Commands.EnsureAsync(ensureOp.Id, request, ct).ConfigureAwait(false);
                if (!applied.Ok) continue;

                if (ports.Operations.ResolveByEdge(action.RefType, remove: true) is { } removeOp)
                {
                    var removeRequest = request with { SourceKey = action.Source.Id ?? string.Empty };
                    await ports.Commands.RemoveAsync(removeOp.Id, removeRequest, ct).ConfigureAwait(false);
                }
            }
            else
            {
                await graph.AddReferenceAsync(action.NewSource, action.Target, action.RefType, 1.0, ct).ConfigureAwait(false);
                await graph.RemoveReferenceAsync(action.Source, action.Target, action.RefType, ct).ConfigureAwait(false);
            }

            rekeyed++;
        }

        var retired = 0;
        foreach (var old in plan.RetiredSources)
        {
            ct.ThrowIfCancellationRequested();
            await graph.RemoveResourceAsync(old, ct).ConfigureAwait(false);
            retired++;
        }

        return new EndpointRekeyResult(rekeyed, plan.Orphans.Count, retired);
    }

    /// <summary><c>history:{historyId}</c> 形态的 task 端点判定（前缀单源 = <see cref="NodeEndpointIds"/>）。</summary>
    private static bool TryGetHistoryTaskKey(string taskSourceId, out string historyId)
        => NodeEndpointIds.TryParseHistoryTypeKey(taskSourceId, out historyId);

    private static bool IsType(ResourceIdentifier? id, string type)
        => id is not null && string.Equals(id.ResourceType, type, StringComparison.Ordinal);

    private static ResourceIdentifier WithId(ResourceIdentifier source, string id)
        => new() { ResourceType = source.ResourceType, Id = id, Location = source.Location };
}

