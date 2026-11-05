using System.Text.Json;

namespace Usharp.Bundle;

/// <summary>
/// 键空间事实规范化（A2）：把「声明编译产物」与「已生成 JSON」收敛成同一字形，
/// 供门禁做纯文本 Ordinal 对拍，PowerShell 不再解析任何键空间声明字段。
/// <para>
/// 只投影既有事实字段（id/template/businessKeyTemplate/derivedPrefixes/name/direction），
/// 不派生、不校验、不复制枚举；family 与 port 按 Ordinal 排序，derivedPrefixes 保留声明序。
/// </para>
/// </summary>
public static class KeySpaceFactsNormalizer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var families = new List<FamilyFacts>();
        if (root.TryGetProperty("families", out var familyNodes) && familyNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var family in familyNodes.EnumerateArray())
            {
                var id = ReadString(family, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var derived = new List<string>();
                if (family.TryGetProperty("derivedPrefixes", out var prefixes) && prefixes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var prefix in prefixes.EnumerateArray())
                    {
                        if (prefix.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prefix.GetString()))
                        {
                            derived.Add(prefix.GetString()!);
                        }
                    }
                }

                families.Add(new FamilyFacts(
                    id,
                    ReadString(family, "template"),
                    ReadString(family, "businessKeyTemplate"),
                    derived));
            }
        }

        var ports = new List<PortFacts>();
        if (root.TryGetProperty("ports", out var portNodes) && portNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var port in portNodes.EnumerateArray())
            {
                var name = ReadString(port, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                ports.Add(new PortFacts(name, ReadString(port, "direction")));
            }
        }

        families.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        ports.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

        return JsonSerializer.Serialize(new KeySpaceFacts(families, ports), Options);
    }

    private static string ReadString(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private sealed record FamilyFacts(string Id, string Template, string BusinessKeyTemplate, IReadOnlyList<string> DerivedPrefixes);

    private sealed record PortFacts(string Name, string Direction);

    private sealed record KeySpaceFacts(IReadOnlyList<FamilyFacts> Families, IReadOnlyList<PortFacts> Ports);
}
