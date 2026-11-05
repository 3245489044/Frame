using System.Text.Json;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;

namespace Modules.Wiring.Declaration;

public static partial class JsonWiringDeclarationSource
{
    private static IReadOnlyList<WireSpec> ReadWires(JsonElement root, string sourceId, List<WiringViolation> violations)
    {
        var wires = new List<WireSpec>();
        foreach (var item in Items(root, "wires"))
        {
            var id = Text(item, "id");
            var from = Text(item, "from");
            var to = Text(item, "to");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireDeclInvalid, id ?? sourceId, "wire 缺 id/from/to"));
                continue;
            }

            var onMissing = ParseEnum(Text(item, "onMissing"), OnMissing.Fail);
            var merge = ParseEnum(Text(item, "merge"), MergeMode.None);
            if (onMissing is null || merge is null)
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireDeclInvalid, id,
                    $"wire 枚举非法: onMissing={Text(item, "onMissing")} merge={Text(item, "merge")}"));
                continue;
            }

            wires.Add(new WireSpec(
                id, from, to,
                Adapter: Text(item, "adapt"),
                Judge: Text(item, "judge"),
                OnMissing: onMissing.Value,
                Merge: merge.Value,
                Each: Text(item, "each"),
                Bridge: Flag(item, "bridge"),
                Default: Text(item, "default")));
        }

        return wires;
    }

    private static IReadOnlyList<AdapterSpec> ReadAdapters(JsonElement root, string sourceId, List<WiringViolation> violations)
    {
        var adapters = new List<AdapterSpec>();
        foreach (var item in Items(root, "adapters"))
        {
            var id = Text(item, "id");
            var from = Text(item, "from");
            var to = Text(item, "to");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                violations.Add(WiringViolation.Create(
                    WiringReasonCodes.WireDeclInvalid, id ?? sourceId, "adapter 缺 id/from/to"));
                continue;
            }

            adapters.Add(new AdapterSpec(id, from, to, Text(item, "impl")));
        }

        return adapters;
    }

    private static IReadOnlyList<ProfileSpec> ReadProfiles(JsonElement root, string sourceId, List<WiringViolation> violations)
    {
        var profiles = new List<ProfileSpec>();
        foreach (var item in Items(root, "profiles"))
        {
            var id = Text(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                violations.Add(WiringViolation.Create(WiringReasonCodes.WireDeclInvalid, sourceId, "profile 缺 id"));
                continue;
            }

            profiles.Add(new ProfileSpec(id, Strings(item, "enable").ToArray(), Text(item, "scope") ?? "global"));
        }

        return profiles;
    }
}
