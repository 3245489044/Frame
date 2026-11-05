using Modules.Wiring.Abstractions;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;

namespace Modules.Wiring.Compile;

public static partial class WiringValidator
{
    /// <summary>D5：wire 图成环（本层无顺序声明，成环一律红）。</summary>
    private static IReadOnlyList<WiringViolation> VerifyAcyclic(WiringLinkResult link)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var edges = new List<(string From, string To, string WireId)>();

        foreach (var wire in link.Wires)
        {
            nodes.Add(wire.From.Owner);
            nodes.Add(wire.To.Owner);
            if (!string.Equals(wire.From.Owner, wire.To.Owner, StringComparison.Ordinal))
            {
                edges.Add((wire.From.Owner, wire.To.Owner, wire.Id));
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=未访问 1=在栈 2=完成
        var violations = new List<WiringViolation>();

        foreach (var node in nodes) state[node] = 0;

        void Visit(string node)
        {
            state[node] = 1;
            foreach (var edge in edges)
            {
                if (!string.Equals(edge.From, node, StringComparison.Ordinal)) continue;

                if (!state.TryGetValue(edge.To, out var mark)) mark = 0;
                if (mark == 1)
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WireCycle, edge.WireId,
                        "wire 图成环", "from=" + edge.From, "to=" + edge.To));
                    continue;
                }

                if (mark == 0) Visit(edge.To);
            }

            state[node] = 2;
        }

        foreach (var node in nodes.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (state.TryGetValue(node, out var mark) && mark == 0) Visit(node);
        }

        return violations;
    }

    /// <summary>D6：非 terminal 盒的 out 端口出度为 0（card&lt;0 的动态端口豁免并保持可见）。</summary>
    private static IReadOnlyList<WiringViolation> VerifyOrphanOut(WiringCatalog catalog, WiringLinkResult link)
    {
        var sourced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wire in link.Wires) sourced.Add(wire.From.Ref);

        var violations = new List<WiringViolation>();
        foreach (var box in catalog.Boxes)
        {
            if (box.Terminal) continue;

            foreach (var port in box.Ports)
            {
                if (!port.IsOut) continue;
                if (sourced.Contains(port.Ref)) continue;
                if (port.Card < 0) continue;

                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireOrphanOut, port.Ref,
                    "输出端口无下游（非 terminal 盒）",
                    "box=" + box.Id,
                    "form=" + port.Form));
            }
        }

        return violations;
    }

    /// <summary>D7：声明端口名 ⇔ 实现产出名（双向）。</summary>
    private static IReadOnlyList<WiringViolation> VerifyPortDrift(
        WiringCatalog catalog, IWiringImplementationCatalog? implementations)
    {
        if (implementations is null) return Array.Empty<WiringViolation>();

        var violations = new List<WiringViolation>();
        foreach (var box in catalog.Boxes)
        {
            if (!implementations.TryGetProduced(box.Impl, out var produced)) continue;

            var producedSet = new HashSet<string>(produced, StringComparer.Ordinal);
            var declared = new HashSet<string>(
                box.Ports.Where(port => port.IsOut).Select(port => port.Name), StringComparer.Ordinal);

            var missing = declared.Where(name => !producedSet.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WirePortDrift, box.Id,
                    "声明输出端口不在实现产出集",
                    "impl=" + box.Impl,
                    "declared-only=[" + string.Join(",", missing) + "]",
                    "produced=[" + string.Join(",", producedSet.OrderBy(n => n, StringComparer.Ordinal)) + "]"));
            }

            var undeclared = producedSet.Where(name => !declared.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (undeclared.Length > 0)
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WirePortDrift, box.Id,
                    "实现产出端口未在声明面登记",
                    "impl=" + box.Impl,
                    "produced-only=[" + string.Join(",", undeclared) + "]",
                    "declared=[" + string.Join(",", declared.OrderBy(n => n, StringComparer.Ordinal)) + "]"));
            }
        }

        return violations;
    }
}
