using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Graph;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 通用图写边器（U8 通用半）：准入 + 写边/删边/读边过滤的唯一实现，**零领域词汇**。
/// <para>单一写边 owner 不变：一律经协调器 <c>AddReferenceAsync / RemoveReferenceAsync</c>（TH-12）。</para>
/// </summary>
public sealed class GraphEdgeWriter
{
    private readonly ReferenceGraphCoordinator _graph;

    public GraphEdgeWriter(ReferenceGraphCoordinator graph)
        => _graph = graph ?? throw new ArgumentNullException(nameof(graph));

    /// <summary>写透目标图协调器（graph-log.db 唯一 owner）。</summary>
    public ReferenceGraphCoordinator Coordinator => _graph;

    /// <summary>可选准入诊断出口（缺省静默）。</summary>
    public IReferenceEdgeDiagnostics? EdgeDiagnostics { get; set; }

    /// <summary>可选归属边白名单（未装配 ⇒ 报告模式放行）。</summary>
    public IReferenceEdgeCatalog EdgeCatalog { get; set; } = PermissiveReferenceEdgeCatalog.Instance;

    /// <summary>可选节点库探针（cross_db_link 写边前断言节点行存在）。</summary>
    public global::Operation.DataStoreDriven.Abstractions.INodeStore? NodeStore { get; set; }

    /// <summary>归属边准入：未在声明面登记 ⇒ 拒写 + 可见化（未装配白名单 ⇒ 报告模式放行）。</summary>
    public bool AdmitEdge(string edgeType, string subject)
    {
        if (EdgeCatalog.IsDeclared(edgeType)) return true;
        (EdgeDiagnostics ?? NullReferenceEdgeDiagnostics.Instance).EdgeUndeclared(edgeType, subject);
        return false;
    }

    /// <summary>写归属边（幂等加法）：先准入，后经协调器单一写边原语落图。</summary>
    public async Task AttachEdgeAsync(string edgeType, ResourceIdentifier source, ResourceIdentifier target, string subject, CancellationToken ct)
    {
        if (!AdmitEdge(edgeType, subject)) return;
        await _graph.AddReferenceAsync(source, target, edgeType, 1.0, ct).ConfigureAwait(false);
    }

    /// <summary>删归属边（幂等：不存在则无副作用）。</summary>
    public Task DetachEdgeAsync(string edgeType, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
        => _graph.RemoveReferenceAsync(source, target, edgeType, ct);

    /// <summary>读某边类型的出边目标 Id 集（去重保序）。</summary>
    public async Task<IReadOnlyList<string>> GetOutgoingTargetIdsAsync(string edgeType, ResourceIdentifier source, CancellationToken ct)
    {
        var refs = await _graph.GetReferencesAsync(source, includeIncoming: false, ct).ConfigureAwait(false);
        return ProjectOutgoingTargetIds(refs, edgeType, static r => r.RefType, static r => r.Target?.Id);
    }

    /// <summary>出边过滤 + 目标 Id 去重保序唯一实现（单条/批量共用）。</summary>
    public static IReadOnlyList<string> ProjectOutgoingTargetIds<T>(
        IEnumerable<T>? refs, string edgeType, Func<T, string?> refType, Func<T, string?> targetId)
    {
        var result = new List<string>();
        if (refs is null) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in refs)
        {
            if (!string.Equals(refType(reference), edgeType, StringComparison.Ordinal)) continue;
            var id = targetId(reference);
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (seen.Add(id!)) result.Add(id!);
        }
        return result;
    }
}
