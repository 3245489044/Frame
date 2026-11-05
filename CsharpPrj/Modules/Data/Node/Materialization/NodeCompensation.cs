using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Placement;

namespace Modules.Node.Materialization;

/// <summary>写前快照（Commit→Compensate 的补偿依据）。</summary>
public sealed record NodeCompensationSnapshot(string PlacementId, string PlacementKey, string CarrierId, string? Json, bool Existed);

public sealed record NodeCompensationResult(bool Restored, IReadOnlyList<NodeDiagnostic> Diagnostics);

/// <summary>
/// 补偿原语（G12 补偿侧 / SyncProtocolTriple 的 Commit→Compensate）：
/// 物化前拍快照，失败时按快照回滚（原本存在则还原原文，原本不存在则删除）。
/// 只做补偿动作，不做业务回滚决策。
/// </summary>
public sealed class NodeCompensation
{
    private readonly ICarrierRegistry _carriers;
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeCompensation(ICarrierRegistry carriers, INodeDiagnosticsSink? diagnostics = null)
    {
        _carriers = carriers;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public async Task<IReadOnlyList<NodeCompensationSnapshot>> CaptureAsync(
        IReadOnlyList<NodePlacement> placements,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<NodeCompensationSnapshot>();

        foreach (var placement in placements)
        {
            if (!_carriers.TryGet(placement.Spec.CarrierId, out var carrier))
            {
                snapshots.Add(new NodeCompensationSnapshot(
                    placement.Spec.PlacementId,
                    placement.PlacementKey,
                    placement.Spec.CarrierId,
                    null,
                    false));
                continue;
            }

            var record = await carrier.ReadAsync(placement.PlacementKey, cancellationToken).ConfigureAwait(false);
            snapshots.Add(new NodeCompensationSnapshot(
                placement.Spec.PlacementId,
                placement.PlacementKey,
                placement.Spec.CarrierId,
                record?.Json,
                record is not null));
        }

        return snapshots;
    }

    public async Task<NodeCompensationResult> RollbackAsync(
        IReadOnlyList<NodeCompensationSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<NodeDiagnostic>();
        var restored = true;

        foreach (var snapshot in snapshots)
        {
            var carrier = ResolveCarrier(snapshot, diagnostics);
            if (carrier is null)
            {
                restored = false;
                continue;
            }

            if (snapshot.Existed && carrier.Semantics == CarrierSemantics.AppendOnly)
            {
                // N5 只增不丢：append-only 载体不做回写（补偿只在可覆写载体上生效）
                restored = false;
                ReportAppendOnly(diagnostics, snapshot, "补偿不适用");
                continue;
            }

            if (snapshot.Existed)
            {
                var write = await carrier.UpsertAsync(snapshot.PlacementKey, snapshot.Json ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                if (write.Status == CarrierWriteStatus.Conflict)
                {
                    // 声明为可覆写但写回被判冲突：等价于"不可撤销"（与上面同码，仅因由不同）
                    restored = false;
                    ReportAppendOnly(diagnostics, snapshot, "回写被载体判为冲突");
                    continue;
                }

                if (write.Status is not (CarrierWriteStatus.Written or CarrierWriteStatus.Unchanged))
                {
                    restored = false;
                    diagnostics.Add(new NodeDiagnostic(
                        "compensate.restore-failed",
                        NodeDiagnosticSeverity.Error,
                        $"回滚还原失败（{write.Status}）：{write.Reason}",
                        snapshot.PlacementKey));
                }

                continue;
            }

            await carrier.DeleteAsync(snapshot.PlacementKey, cancellationToken).ConfigureAwait(false);
        }

        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }

        return new NodeCompensationResult(restored, diagnostics);
    }

    /// <summary>append-only 不可撤销诊断单源（两条路径同码，仅因由不同）。</summary>
    private static void ReportAppendOnly(
        List<NodeDiagnostic> diagnostics,
        NodeCompensationSnapshot snapshot,
        string detail) =>
        diagnostics.Add(new NodeDiagnostic(
            "compensate.append-only",
            NodeDiagnosticSeverity.Warning,
            $"载体为只增不丢（append-only）：历史写入不可撤销（{detail}）",
            snapshot.PlacementKey));

    private INodeCarrier? ResolveCarrier(NodeCompensationSnapshot snapshot, List<NodeDiagnostic> diagnostics)
    {
        if (_carriers.TryGet(snapshot.CarrierId, out var carrier))
        {
            return carrier;
        }

        diagnostics.Add(new NodeDiagnostic(
            "compensate.carrier-missing",
            NodeDiagnosticSeverity.Error,
            "回滚时载体未装配（装配期应为 Unbound）",
            snapshot.PlacementKey));
        return null;
    }
}
