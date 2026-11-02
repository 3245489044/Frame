using System.Collections.Concurrent;
using Modules.Node;
using Modules.Node.Diagnostics;
using Modules.Node.Materialization;
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
    private readonly NodeWriteGate _rmwGate;
    private readonly ConcurrentDictionary<string, NodeStreamRun> _runs = new(StringComparer.Ordinal);

    public NodeDocumentPort(
        NodeFramework framework,
        NodePayloadSchemaRegistry? schemas = null,
        NodeWriteGate? readModifyWriteGate = null)
    {
        _framework = framework ?? throw new ArgumentNullException(nameof(framework));
        _resolver = new NodeScopeResolver(_framework);
        _schemas = schemas ?? new NodePayloadSchemaRegistry();
        _rmwGate = readModifyWriteGate ?? new NodeWriteGate("node-io:rmw", TimeSpan.Zero, _framework.Diagnostics);
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

        var items = new List<NodeProjectionItem>();
        foreach (var placement in resolved.Placements)
        {
            if (!_framework.Carriers.TryGet(placement.CarrierId, out _))
            {
                continue;
            }

            var explicitKeys = query.ExplicitKeys ??
                               (query.KeyPrefix is null ? new[] { placement.PositionKey } : null);
            var readScope = new ReadScope(
                Domain: scope.Domain,
                PlacementIds: new[] { placement.PlacementId },
                SelectionId: scope.SelectionId,
                KeyPrefix: query.KeyPrefix,
                Limit: query.Limit,
                Loc: null,
                Container: scope.KeyContext,
                Round: query.Round,
                Keys: explicitKeys);

            items.AddRange(await _framework.Projection
                .ProjectAsync(readScope, null, cancellationToken)
                .ConfigureAwait(false));
        }

        var reasons = new List<string>(resolved.Reasons);
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
        new(
            domain,
            NodeKeySetResult.Empty(NodeKeySetOrigin.Container, reasons.ToArray()),
            Array.Empty<NodeProjectionItem>(),
            reasons,
            NodeReadStatus.Unavailable,
            new[] { NodeReadReason.PlacementUnresolved });
}
