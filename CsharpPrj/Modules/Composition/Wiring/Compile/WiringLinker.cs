using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;
using Modules.Wiring.Runtime;

namespace Modules.Wiring.Compile;

/// <summary>连接解析结果（未解析项以 D1 结构码显式可见，不静默丢弃）。</summary>
public sealed record WiringLinkResult(
    IReadOnlyList<LinkedWire> Wires,
    IReadOnlyList<WiringViolation> Violations)
{
    public static WiringLinkResult Empty { get; } =
        new(Array.Empty<LinkedWire>(), Array.Empty<WiringViolation>());
}

/// <summary>
/// Link：<c>from/to</c> 坐标解析 + Adapter 绑定 + Judge 编译（D1/D2 的解析半边）。
/// <para>坐标解析只读 <see cref="PortSpec.Ref"/>（<c>owner.port</c> 全等匹配）；禁裸键反解（I6）。</para>
/// </summary>
public static class WiringLinker
{
    public static WiringLinkResult Link(WiringCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var links = new List<LinkedWire>();
        var violations = new List<WiringViolation>();

        foreach (var wire in catalog.Wires)
        {
            var fromOk = catalog.TryFindPort(wire.From, out var from);
            var toOk = catalog.TryFindPort(wire.To, out var to);

            if (!fromOk || !toOk)
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireDangling,
                    wire.Id,
                    "端点不存在",
                    fromOk ? "from=" + wire.From : "from(missing)=" + wire.From,
                    toOk ? "to=" + wire.To : "to(missing)=" + wire.To));
                continue;
            }

            if (!from.IsOut || !to.IsIn)
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireDangling,
                    wire.Id,
                    "端点方向非法（须 out→in）",
                    $"from={from.Ref}/{from.Dir}",
                    $"to={to.Ref}/{to.Dir}"));
                continue;
            }

            AdapterSpec? adapter = null;
            if (!string.IsNullOrWhiteSpace(wire.Adapter))
            {
                adapter = catalog.FindAdapter(wire.Adapter!);
                if (adapter is null)
                {
                    violations.Add(WiringViolation.Create(
                        WiringReasonCodes.WireAdapterMissing, wire.Id, "adapter 未声明",
                        "adapt=" + wire.Adapter));
                    continue;
                }
            }

            var judge = CompileJudge(wire, violations);
            links.Add(new LinkedWire(wire, from, to, adapter, judge.Judge, judge.Error));
        }

        return new WiringLinkResult(links, violations);
    }

    private static (Func<ValueEnvelope, bool>? Judge, string? Error) CompileJudge(
        WireSpec wire, List<WiringViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(wire.Judge)) return (null, null);

        var text = wire.Judge!.Trim();
        var separator = text.IndexOf("==", StringComparison.Ordinal);
        if (separator <= 0 || separator >= text.Length - 2)
        {
            violations.Add(WiringViolation.Create(
                WiringReasonCodes.WireJudgeInvalid, wire.Id,
                "judge 仅支持 key==value 形态", "judge=" + text));
            return (null, "judge-unsupported");
        }

        var key = text[..separator].Trim();
        var expected = text[(separator + 2)..].Trim();
        if (key.Length == 0)
        {
            violations.Add(WiringViolation.Create(
                WiringReasonCodes.WireJudgeInvalid, wire.Id, "judge 缺 key", "judge=" + text));
            return (null, "judge-key-missing");
        }

        return (envelope => Matches(envelope, key, expected), null);
    }

    private static bool Matches(ValueEnvelope envelope, string key, string expected)
    {
        if (envelope.Payload is null) return false;

        if (envelope.Payload is IReadOnlyDictionary<string, string> textMap &&
            textMap.TryGetValue(key, out var textValue))
        {
            return string.Equals(textValue, expected, StringComparison.Ordinal);
        }

        if (envelope.Payload is string raw)
        {
            // 纯文本载荷：judge 退化为「载荷全等」，不做键解析（禁从字符串里捞业务键）。
            return string.Equals(raw, expected, StringComparison.Ordinal);
        }

        return false;
    }
}
