using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;

namespace Modules.Wiring.Compile;

public static partial class WiringValidator
{
    /// <summary>D3：required 的 inport 无供给。</summary>
    private static IReadOnlyList<WiringViolation> VerifySupply(WiringCatalog catalog, WiringLinkResult link)
    {
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wire in link.Wires) supplied.Add(wire.To.Ref);

        var violations = new List<WiringViolation>();
        foreach (var box in catalog.Boxes)
        {
            foreach (var port in box.Ports)
            {
                if (!port.IsIn || !port.Required) continue;
                if (supplied.Contains(port.Ref)) continue;

                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireSupplyMissing, port.Ref,
                    "必填输入无供给（无 wire 指向）",
                    "box=" + box.Id,
                    "form=" + port.Form,
                    "required=true"));
            }
        }

        return violations;
    }

    /// <summary>D4：同一 inport ≥2 条 wire 且未声明 merge。</summary>
    private static IReadOnlyList<WiringViolation> VerifyWriters(WiringLinkResult link)
    {
        var violations = new List<WiringViolation>();
        var byTarget = new Dictionary<string, List<LinkedWire>>(StringComparer.Ordinal);

        foreach (var wire in link.Wires)
        {
            if (!byTarget.TryGetValue(wire.To.Ref, out var writers))
            {
                writers = new List<LinkedWire>();
                byTarget[wire.To.Ref] = writers;
            }

            writers.Add(wire);
        }

        foreach (var pair in byTarget.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (pair.Value.Count < 2) continue;

            var undeclared = pair.Value
                .Where(wire => wire.Spec.Merge == MergeMode.None)
                .Select(wire => wire.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            if (undeclared.Length == 0) continue;

            violations.Add(WiringViolation.Create(
                WiringReasonCodes.WireMultiWriter, undeclared[0],
                "同一输入多写者且未声明 merge",
                "to=" + pair.Key,
                "writers=[" + string.Join(",", pair.Value.Select(w => w.Id)) + "]",
                "undeclared=[" + string.Join(",", undeclared) + "]"));
        }

        return violations;
    }
}
