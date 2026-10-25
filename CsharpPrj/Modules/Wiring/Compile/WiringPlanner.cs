using Modules.Wiring.Abstractions;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;
using Modules.Wiring.Runtime;

namespace Modules.Wiring.Compile;

/// <summary>
/// Plan：判别 → 拓扑序 → 委托 + 索引（运行期零字典扫描、零反射，R5）。
/// </summary>
public static class WiringPlanner
{
    public static WiringPlan Plan(
        WiringCatalog catalog,
        IWiringImplementationCatalog? implementations = null,
        IFormResolver? formResolver = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var report = WiringValidator.Verify(catalog, implementations, formResolver);
        var link = WiringLinker.Link(catalog);

        var compiled = link.Wires
            .Select(linked => new CompiledWire(linked, BuildRoute(linked)))
            .ToArray();

        var ordered = OrderByTopology(compiled);
        return new WiringPlan(catalog.Fingerprint, ordered, report.Violations);
    }

    /// <summary>拓扑序（同输入同序；成环时剩余项按 Id 序补齐，可复算）。</summary>
    public static IReadOnlyList<CompiledWire> OrderByTopology(IReadOnlyList<CompiledWire> wires)
    {
        ArgumentNullException.ThrowIfNull(wires);

        var nodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wire in wires)
        {
            nodes.Add(wire.Link.From.Owner);
            nodes.Add(wire.Link.To.Owner);
        }

        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes) indegree[node] = 0;

        foreach (var wire in wires)
        {
            if (string.Equals(wire.Link.From.Owner, wire.Link.To.Owner, StringComparison.Ordinal)) continue;
            indegree[wire.Link.To.Owner] = indegree[wire.Link.To.Owner] + 1;
        }

        var ready = new List<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        ready.Sort(StringComparer.Ordinal);

        var boxOrder = new List<string>();
        while (ready.Count > 0)
        {
            var node = ready[0];
            ready.RemoveAt(0);
            boxOrder.Add(node);

            foreach (var wire in wires)
            {
                if (!string.Equals(wire.Link.From.Owner, node, StringComparison.Ordinal)) continue;
                if (string.Equals(wire.Link.From.Owner, wire.Link.To.Owner, StringComparison.Ordinal)) continue;

                var target = wire.Link.To.Owner;
                indegree[target] = indegree[target] - 1;
                if (indegree[target] == 0)
                {
                    ready.Add(target);
                    ready.Sort(StringComparer.Ordinal);
                }
            }
        }

        foreach (var node in nodes.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!boxOrder.Contains(node, StringComparer.Ordinal)) boxOrder.Add(node);
        }

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < boxOrder.Count; index++) rank[boxOrder[index]] = index;

        return wires
            .OrderBy(wire => rank.TryGetValue(wire.Link.From.Owner, out var value) ? value : int.MaxValue)
            .ThenBy(wire => wire.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>单条 wire 的委托：判据 → 缺值 → 就绪（适配与投递由 Junction 统一承担）。</summary>
    private static Func<ValueEnvelope, ValueTask<WireResult>> BuildRoute(LinkedWire link)
        => envelope =>
        {
            if (link.JudgeError is not null)
            {
                return ValueTask.FromResult(
                    new WireResult(WireStatus.Skipped, link.Id, envelope.Length, link.JudgeError));
            }

            if (link.Judge is not null && !link.Judge(envelope))
            {
                return ValueTask.FromResult(
                    new WireResult(WireStatus.Skipped, link.Id, envelope.Length, "judge-false"));
            }

            if (!envelope.HasPayload && link.To.Required)
            {
                return ValueTask.FromResult(WireResult.Missing(link.Id, "missing-required"));
            }

            return ValueTask.FromResult(
                new WireResult(WireStatus.Delivered, link.Id, envelope.Length, null));
        };
}
