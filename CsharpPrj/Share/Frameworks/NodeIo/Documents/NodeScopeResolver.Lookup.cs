using Modules.Node.Identity;
using Modules.Node.Placement;

namespace Share.NodeIo;

public sealed partial class NodeScopeResolver
{
    /// <summary>
    /// 键 → 作用域（**消灭裸行写入口**）：按键模板反解身份值，再由 loc 反解表回找 loc。
    /// 未命中任何声明位置 ⇒ false + 原因（禁回退默认载体）。
    /// </summary>
    public bool TryResolveByKey(string domain, string key, out NodeDocumentScope scope, out string reason)
    {
        scope = new NodeDocumentScope(domain, string.Empty, key);
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(key))
        {
            reason = NodeDocumentReasons.KeyContextMissing;
            return false;
        }

        foreach (var spec in _framework.Placements.ByDomain(domain))
        {
            if (!KeyTemplateEvaluator.TryParsePlacementValues(_framework, spec.PlacementId, key, out var values, out _))
            {
                continue;
            }

            var identityKey = KeyTemplateEvaluator.TryEvaluate(_framework.Identity.Template, values, out var rebuilt, out _)
                ? rebuilt
                : values.TryGetValue("container", out var container) ? container : key;
            var loc = LocOf(spec.PlacementId);
            if (loc is null)
            {
                reason = NodeDocumentReasons.LocUnresolved + ":" + spec.PlacementId;
                return false;
            }

            scope = new NodeDocumentScope(domain, loc, identityKey);
            return true;
        }

        reason = NodeDocumentReasons.PlacementUnresolved + ":" + key;
        return false;
    }

    private string? LocOf(string placementId)
    {
        if (_framework.PlacementResolver is not DeclaredPlacementResolver declared)
        {
            return null;
        }

        foreach (var loc in declared.DeclaredLocs)
        {
            var resolution = declared.Resolve(loc, string.Empty);
            if (resolution.Resolved &&
                resolution.Placements.Any(p => string.Equals(p.PlacementId, placementId, StringComparison.Ordinal)))
            {
                return loc;
            }
        }

        return null;
    }
}
