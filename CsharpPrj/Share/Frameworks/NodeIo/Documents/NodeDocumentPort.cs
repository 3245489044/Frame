using System.Collections.Concurrent;
using Modules.Node;
using Modules.Node.Placement;
using Modules.Node.Projection;
using Modules.Node.Read;

namespace Share.NodeIo;

/// <summary>
/// 节点文档端口实现（读/写同一内核）：构造即绑定一份域框架（身份/位置/载体/选择/形态）。
/// 无静态、无服务定位；组合根按域装配一次。
/// </summary>
public sealed partial class NodeDocumentPort : INodeDocumentPort
{
    private readonly NodeFramework _framework;
    private readonly NodeScopeResolver _resolver;
    private readonly NodePayloadSchemaRegistry _schemas;
    private readonly NodeDocumentProjectionRegistry _projections;
    private readonly ConcurrentDictionary<string, NodeStreamRun> _runs = new(StringComparer.Ordinal);

    public NodeDocumentPort(
        NodeFramework framework,
        NodePayloadSchemaRegistry? schemas = null,
        NodeDocumentProjectionRegistry? projections = null)
    {
        _framework = framework ?? throw new ArgumentNullException(nameof(framework));
        _resolver = new NodeScopeResolver(_framework);
        _schemas = schemas ?? new NodePayloadSchemaRegistry();
        _projections = projections ?? new NodeDocumentProjectionRegistry();
    }

    public NodeFramework Framework => _framework;

    public NodePayloadSchemaRegistry Schemas => _schemas;

    /// <inheritdoc />
    public async Task<NodeReadResult> QueryAsync(
        NodeDocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = query.Scope;
        var resolved = _resolver.Resolve(scope, forWrite: false);
        if (!resolved.Ok)
        {
            return Unavailable(scope.Domain, resolved.Reasons);
        }

        if (query.PlacementIds is { Count: > 0 } placementIds)
        {
            var narrowed = resolved.Placements
                .Where(placement => placementIds.Contains(placement.PlacementId))
                .ToArray();
            if (narrowed.Length == 0)
            {
                return Unavailable(
                    scope.Domain,
                    new[] { NodeDocumentReasons.NoReadablePlacement + ":" + scope.Domain });
            }

            resolved = new ResolvedScope(resolved.Scope, narrowed, true, resolved.Reasons, narrowed[0].Form);
        }

        var items = new List<NodeProjectionItem>();
        var reasons = new List<string>(resolved.Reasons);
        foreach (var placement in resolved.Placements)
        {
            if (!_framework.Carriers.TryGet(placement.CarrierId, out var carrier))
            {
                // 载体未绑定不得静默跳过（E10）：显式归因，避免「读不到」与「没有」同形。
                reasons.Add(NodeDocumentReasons.CarrierUnbound + ":" + placement.CarrierId);
                continue;
            }

            IReadOnlyList<string>? explicitKeys;
            if (query.Full)
            {
                var enumerated = await carrier
                    .EnumerateKeysAsync(query.KeyPrefix, cancellationToken)
                    .ConfigureAwait(false);
                if (enumerated.Count == 0)
                {
                    continue;
                }

                explicitKeys = enumerated;
            }
            else
            {
                explicitKeys = query.ExplicitKeys ??
                               (query.KeyPrefix is null ? new[] { placement.PositionKey } : null);
            }

            var readScope = new ReadScope(
                Domain: scope.Domain,
                PlacementIds: new[] { placement.PlacementId },
                SelectionId: scope.SelectionId,
                KeyPrefix: query.Full ? null : query.KeyPrefix,
                Limit: query.Limit,
                Loc: null,
                Container: scope.KeyContext,
                Round: query.Round,
                Keys: explicitKeys);

            var raw = await _framework.Projection
                .ProjectAsync(readScope, null, cancellationToken)
                .ConfigureAwait(false);
            if (raw.Count == 0)
            {
                continue;
            }

            if (!_projections.TryResolve(placement.ProjectionId, out var projection))
            {
                reasons.Add(NodeDocumentReasons.ProjectionUnknown + ":" + placement.ProjectionId);
                projection = _projections.Resolve(null);
            }

            // 投影失败原因上送（E9）：投影实现把失败标记在行上，结果层并入 Reasons（禁静默退回文档行）。
            var projected = projection.Project(
                scope, placement, raw, _schemas.Resolve(placement.PayloadSchemaId));
            foreach (var row in projected)
            {
                if (!string.IsNullOrWhiteSpace(row.ProjectionStatus))
                {
                    reasons.Add(row.ProjectionStatus!);
                }
            }

            items.AddRange(projected);
        }

        if (query.Overlays is { Count: > 0 } overlays)
        {
            var byKey = new Dictionary<string, NodeProjectionItem>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                byKey[item.Key] = item;
            }

            foreach (var overlay in overlays)
            {
                byKey[overlay.Key] = overlay;
            }

            items = byKey.Values.ToList();
        }

        if (items.Count == 0)
        {
            reasons.Add(NodeKeySetReasons.NoProjectionRows);
        }

        var keys = new NodeKeySetResult(
            items.Select(item => item.Key).Distinct(StringComparer.Ordinal).ToArray(),
            NodeKeySetOrigin.Container,
            reasons);
        return new NodeReadResult(
            scope.Domain,
            keys,
            items,
            reasons,
            items.Count > 0 ? NodeReadStatus.Ok : NodeReadStatus.OkButEmpty,
            items.Count > 0
                ? Array.Empty<NodeReadReason>()
                : new[] { NodeReadReason.NoProjectionRows });
    }

    /// <inheritdoc />
    public INodeStreamSession BeginStream(NodeDocumentScope scope, int round)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var resolved = _resolver.Resolve(scope, forWrite: true);
        if (!resolved.Ok)
        {
            throw new NodeDocumentStreamException(string.Join("; ", resolved.Reasons));
        }

        var placement = resolved.Placements.FirstOrDefault(p => p.Form == PlacementForm.Stream);
        if (placement is null)
        {
            throw new NodeDocumentStreamException(NodeDocumentReasons.StreamFormRequired + ":" + scope.Domain);
        }

        var key = StreamKey(scope);
        var run = _runs.GetOrAdd(key, _ => new NodeStreamRun(scope.KeyContext, placement.PositionKey));
        run.BeginRound(round);
        return new NodeStreamSession(this, run, scope, key);
    }

    /// <inheritdoc />
    public bool TryResolveByKey(string domain, string key, out NodeDocumentScope scope, out string reason)
        => _resolver.TryResolveByKey(domain, key, out scope, out reason);

    internal void ReleaseStream(string key)
    {
        if (_runs.TryRemove(key, out var run))
        {
            run.Release();
        }
    }

    private static string StreamKey(NodeDocumentScope scope) =>
        scope.Domain + "|" + scope.Loc + "|" + scope.KeyContext;

    private static NodeReadResult Unavailable(string domain, IReadOnlyList<string> reasons) =>
        // 结果构造单源（D4）：与 Registry 共用同一工厂。
        NodeDocumentResultFactory.Unavailable(domain, reasons);
}
