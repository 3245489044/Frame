using UniversalWorkflowFramework.Relations;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 存量边盖章回填（R4/C8）—— 只补字段（assertion / created_seq / origin_flow / origin_step），
/// 不改边键与端点；默认 dry-run（只统计），显式 <c>Apply</c> 才落盘。
/// <para>
/// 判据载体 = 回填报告计数（结构事实）；运行期单写门仍为 <c>_gate</c>（与其它写操作同门）。
/// </para>
/// </summary>
public sealed partial class ReferenceGraphCoordinator : IEdgeStampRepairPort
{
    /// <inheritdoc />
    public Task<RelationRepairReport> RepairEdgeStampsAsync(
        RelationRepairRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Apply)
            return Task.FromResult(ReadLocked(ct, () => RepairCore(request, apply: false)));

        return MutateAsync(ct, () => Task.FromResult(RepairCore(request, apply: true)), affectsMembership: false);
    }

    private RelationRepairReport RepairCore(RelationRepairRequest request, bool apply)
    {
        var targetEdge = string.IsNullOrWhiteSpace(request.Edge) || request.Edge == "*"
            ? null
            : request.Edge;

        var total = 0;
        var missing = 0;
        var patched = 0;
        var skipped = 0;

        foreach (var document in _edges.FindAll().OrderBy(x => x["_id"].AsString, StringComparer.Ordinal))
        {
            var key = document["_id"].IsString ? document["_id"].AsString : string.Empty;
            var edge = ReadEdgeDoc(document);
            if (edge is null)
            {
                skipped++;
                continue;
            }

            total++;
            if (targetEdge is not null && !string.Equals(edge.RefType, targetEdge, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(edge.OriginFlow)) continue;

            missing++;
            if (!apply) continue;

            var stamp = _stampSource.Next(edge.RefType, EdgeOrigin.Runtime, request.FallbackFlow);
            var updated = edge with
            {
                Assertion = stamp.Assertion,
                CreatedSeq = stamp.CreatedSeq,
                OriginFlow = request.FallbackFlow,
                OriginStep = string.IsNullOrWhiteSpace(request.FallbackStep) ? edge.RefType : request.FallbackStep,
            };

            document["Doc"] = _codec.SerializeRecord("edge:" + key, "References", key, EdgeRecord(updated));
            _edges.Update(document);
            patched++;
        }

        return new RelationRepairReport(total, missing, patched, skipped, apply);
    }
}
