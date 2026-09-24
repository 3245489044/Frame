using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Identity;
using Modules.Node.Placement;

namespace Modules.Node.Materialization;

public enum MaterializeStatus
{
    Written,
    Unchanged,
    Conflict,
    Unbound,
    Rejected,
}

public sealed record NodeMaterializeRequest(
    string Key,
    string Json,
    string? Domain = null,
    IReadOnlyList<string>? PlacementIds = null,
    string? WriterId = null);

public sealed record NodePlacementResult(
    string PlacementId,
    string PlacementKey,
    CarrierWriteStatus? Status,
    string? Reason);

public sealed record NodeMaterializationResult(
    MaterializeStatus Status,
    string Key,
    IReadOnlyList<NodePlacementResult> Placements,
    IReadOnlyList<NodeDiagnostic> Diagnostics)
{
    public bool AnyWritten => Placements.Any(p => p.Status == CarrierWriteStatus.Written);
}

/// <summary>
/// <b>唯一写入口</b>（框架内所有落库经此；旁路调用点 = 0，治 P16）：
/// 身份键 → 位置集合 → 形态编码 → 单写门 → 载体 upsert（幂等 / 冲突可见化）。
/// 不做：决定"写哪些位置"之外的语义（位置由声明/反解给定）；不直连库（只经 <see cref="INodeCarrier"/>）。
/// </summary>
public sealed class NodeMaterializer
{
    private readonly IdentitySpec _identity;
    private readonly PlacementRegistry _placements;
    private readonly ICarrierRegistry _carriers;
    private readonly NodeWriteGate _writeGate;
    private readonly NodeFormCodec _codec;
    private readonly SpecDefaults _defaults;
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeMaterializer(
        IdentitySpec identity,
        PlacementRegistry placements,
        ICarrierRegistry carriers,
        NodeWriteGate writeGate,
        NodeFormCodec? codec = null,
        SpecDefaults? defaults = null,
        INodeDiagnosticsSink? diagnostics = null)
    {
        _identity = identity;
        _placements = placements;
        _carriers = carriers;
        _writeGate = writeGate;
        _codec = codec ?? new NodeFormCodec();
        _defaults = defaults ?? new SpecDefaults();
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public NodeFormCodec Codec => _codec;

    public async Task<NodeMaterializationResult> MaterializeAsync(
        NodeMaterializeRequest request,
        CancellationToken cancellationToken = default)
    {
        var collected = new List<NodeDiagnostic>();
        var results = new List<NodePlacementResult>();

        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return Reject(request.Key, "materialize.empty-key", "身份键为空", collected, results);
        }

        if (!KeyTemplateEvaluator.TryParseValues(_identity.Template, request.Key, out var values))
        {
            return Reject(
                request.Key,
                "materialize.key-unparsable",
                $"身份键不符合身份规范 '{_identity.Template}'",
                collected,
                results);
        }

        var candidates = ResolvePlacements(request, collected);
        if (candidates.Count == 0)
        {
            return Reject(
                request.Key,
                "placement.none",
                "无可用位置声明（域内无 Active 位置 / 未指定位置）",
                collected,
                results);
        }

        var payloadSource = _defaults.Merge(request.Json);

        foreach (var spec in candidates)
        {
            if (!_carriers.TryGet(spec.CarrierId, out var carrier))
            {
                collected.Add(new NodeDiagnostic(
                    "carrier.unbound",
                    NodeDiagnosticSeverity.Warning,
                    $"载体 '{spec.CarrierId}' 未注册：该位置记为 Unbound（不阻断其它位置）",
                    spec.PlacementId));
                results.Add(new NodePlacementResult(spec.PlacementId, string.Empty, null, "carrier-unbound"));
                continue;
            }

            if (spec.State == PlacementState.Retired)
            {
                collected.Add(new NodeDiagnostic(
                    "placement.retired",
                    NodeDiagnosticSeverity.Info,
                    "位置已退役：不再命中（跳过）",
                    spec.PlacementId));
                continue;
            }

            if (!spec.IsWritable)
            {
                collected.Add(new NodeDiagnostic(
                    "placement.legacy-read-only",
                    NodeDiagnosticSeverity.Error,
                    "位置为 Legacy：拒写可读",
                    spec.PlacementId));
                results.Add(new NodePlacementResult(spec.PlacementId, string.Empty, CarrierWriteStatus.Rejected, "legacy-read-only"));
                continue;
            }

            if (request.WriterId is not null &&
                !string.Equals(request.WriterId, spec.WriterId, StringComparison.Ordinal))
            {
                collected.Add(new NodeDiagnostic(
                    "write.writer-mismatch",
                    NodeDiagnosticSeverity.Error,
                    $"写入者与位置声明不一致（声明 writer='{spec.WriterId}'）",
                    spec.PlacementId));
                results.Add(new NodePlacementResult(spec.PlacementId, string.Empty, CarrierWriteStatus.Rejected, "writer-mismatch"));
                continue;
            }

            var placementKey = KeyTemplateEvaluator.Evaluate(spec.KeyTemplate, values);
            if (!_codec.IsLossless(spec.Form))
            {
                collected.Add(new NodeDiagnostic(
                    "form.lossy",
                    NodeDiagnosticSeverity.Info,
                    $"形态 '{spec.Form}' 有损（摘要不可还原原文，读侧须以原文位置为准）",
                    spec.PlacementId));
            }

            var payload = _codec.Encode(spec.Form, payloadSource);
            var target = $"{spec.CarrierId}|{placementKey}";

            CarrierWriteResult write;
            try
            {
                write = await _writeGate
                    .RunAsync(target, () => carrier.UpsertAsync(placementKey, payload, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (NodeWriteConflictException ex)
            {
                collected.Add(new NodeDiagnostic(
                    "write.gate-conflict",
                    NodeDiagnosticSeverity.Error,
                    ex.Message,
                    spec.PlacementId));
                results.Add(new NodePlacementResult(spec.PlacementId, placementKey, CarrierWriteStatus.Rejected, "gate-conflict"));
                continue;
            }

            if (write.Status == CarrierWriteStatus.Conflict)
            {
                collected.Add(new NodeDiagnostic(
                    "write.conflict",
                    NodeDiagnosticSeverity.Error,
                    $"同位置异载荷：{write.Reason}（版本化/可见化冲突，不静默覆盖）",
                    placementKey));
            }

            results.Add(new NodePlacementResult(spec.PlacementId, placementKey, write.Status, write.Reason));
        }

        Report(collected);

        return new NodeMaterializationResult(Aggregate(results), request.Key, results, collected);
    }

    private IReadOnlyList<PlacementSpec> ResolvePlacements(NodeMaterializeRequest request, List<NodeDiagnostic> collected)
    {
        if (request.PlacementIds is { Count: > 0 })
        {
            var selected = new List<PlacementSpec>();
            foreach (var id in request.PlacementIds)
            {
                if (_placements.TryGet(id, out var spec))
                {
                    selected.Add(spec);
                }
                else
                {
                    collected.Add(new NodeDiagnostic(
                        "placement.unknown",
                        NodeDiagnosticSeverity.Error,
                        $"位置未声明: {id}",
                        request.Key));
                }
            }

            return selected;
        }

        var domain = request.Domain ?? string.Empty;
        return domain.Length == 0
            ? Array.Empty<PlacementSpec>()
            : _placements.ByDomain(domain).ToArray();
    }

    private static MaterializeStatus Aggregate(IReadOnlyList<NodePlacementResult> results)
    {
        if (results.Any(r => r.Status == CarrierWriteStatus.Conflict))
        {
            return MaterializeStatus.Conflict;
        }

        if (results.Any(r => r.Status == CarrierWriteStatus.Rejected))
        {
            return MaterializeStatus.Rejected;
        }

        if (results.Any(r => r.Status is null))
        {
            return MaterializeStatus.Unbound;
        }

        return results.Any(r => r.Status == CarrierWriteStatus.Written)
            ? MaterializeStatus.Written
            : MaterializeStatus.Unchanged;
    }

    private NodeMaterializationResult Reject(
        string key,
        string code,
        string message,
        List<NodeDiagnostic> collected,
        List<NodePlacementResult> results)
    {
        collected.Add(new NodeDiagnostic(code, NodeDiagnosticSeverity.Error, message, key));
        Report(collected);
        return new NodeMaterializationResult(MaterializeStatus.Rejected, key, results, collected);
    }

    private void Report(List<NodeDiagnostic> collected)
    {
        foreach (var diagnostic in collected)
        {
            _diagnostics.Report(diagnostic);
        }
    }
}
