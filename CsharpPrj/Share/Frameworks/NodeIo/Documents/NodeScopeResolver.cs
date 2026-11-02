using Modules.Node;
using Modules.Node.Identity;
using Modules.Node.Placement;

namespace Share.NodeIo;

/// <summary>
/// **唯一位置解析器**（loc 控制反转）：loc → 声明反解 → 位置集 → 身份模板求值 → 位置键。
/// 读写两侧共用同一实例；方向只影响「可写/可读」过滤，不影响解析语义。
/// </summary>
public sealed partial class NodeScopeResolver
{
    private readonly NodeFramework _framework;

    public NodeScopeResolver(NodeFramework framework) =>
        _framework = framework ?? throw new ArgumentNullException(nameof(framework));

    public NodeFramework Framework => _framework;

    public ResolvedScope Resolve(NodeDocumentScope scope, bool forWrite)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(scope.Domain))
        {
            return ResolvedScope.Failed(scope, NodeDocumentReasons.DomainMissing);
        }

        if (string.IsNullOrWhiteSpace(scope.Loc))
        {
            return ResolvedScope.Failed(scope, NodeDocumentReasons.LocMissing);
        }

        if (string.IsNullOrWhiteSpace(scope.KeyContext))
        {
            return ResolvedScope.Failed(scope, NodeDocumentReasons.KeyContextMissing);
        }

        var resolution = _framework.PlacementResolver.Resolve(scope.Loc, scope.KeyContext);
        if (!resolution.Resolved || resolution.Placements.Count == 0)
        {
            return ResolvedScope.Failed(scope, NodeDocumentReasons.PlacementUnresolved + ":" + scope.Loc);
        }

        if (!TryValues(scope, out var values, out var valueReason))
        {
            return ResolvedScope.Failed(scope, NodeDocumentReasons.IdentityUnparsable + ":" + valueReason);
        }

        var candidates = resolution.Placements
            .Where(spec => string.Equals(spec.Domain, scope.Domain, StringComparison.Ordinal))
            .Where(spec => forWrite ? spec.IsWritable : spec.IsReadable)
            .ToArray();
        if (candidates.Length == 0)
        {
            return ResolvedScope.Failed(
                scope,
                (forWrite ? NodeDocumentReasons.NoWritablePlacement : NodeDocumentReasons.NoReadablePlacement)
                + ":" + scope.Domain);
        }

        var resolved = new List<ResolvedPlacement>(candidates.Length);
        var reasons = new List<string>();
        foreach (var spec in candidates)
        {
            if (scope.FormOverride is { Length: > 0 } forcedText)
            {
                if (!Enum.TryParse<PlacementForm>(forcedText, ignoreCase: true, out var forced) || forced != spec.Form)
                {
                    reasons.Add(NodeDocumentReasons.FormMismatch + ":" + spec.PlacementId);
                    continue;
                }
            }

            if (!KeyTemplateEvaluator.TryEvaluatePlacement(
                    _framework, spec.PlacementId, values, out var key, out var keyReason))
            {
                reasons.Add(NodeDocumentReasons.PlacementKeyUnresolved + ":" + spec.PlacementId + ":" + keyReason);
                continue;
            }

            resolved.Add(new ResolvedPlacement(
                spec.PlacementId, spec.CarrierId, key, spec.Form, spec.WriterId,
                spec.PayloadSchemaId, spec.RecordType, spec.ThreadCoordinate));
        }

        return new ResolvedScope(
            scope,
            resolved,
            resolved.Count > 0,
            reasons,
            resolved.Count > 0 ? resolved[0].Form : null);
    }

    private bool TryValues(
        NodeDocumentScope scope,
        out IReadOnlyDictionary<string, string> values,
        out string reason)
    {
        if (KeyTemplateEvaluator.TryParseValues(_framework.Identity.Template, scope.KeyContext, out values))
        {
            reason = string.Empty;
            return true;
        }

        var contextKeys = _framework.Identity.ContextKeys;
        if (contextKeys.Count == 1)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [contextKeys[0]] = scope.KeyContext,
            };
            reason = string.Empty;
            return true;
        }

        values = new Dictionary<string, string>(StringComparer.Ordinal);
        reason = "identity-template:" + _framework.Identity.Template;
        return false;
    }
}
