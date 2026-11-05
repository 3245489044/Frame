using System.Text.Json;
using Modules.Wiring.Diagnostics;
using Modules.Wiring.Model;

namespace Modules.Wiring.Declaration;

public static partial class JsonWiringDeclarationSource
{
    private static IEnumerable<JsonElement> Items(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) yield break;
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) yield break;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object) yield return item;
        }
    }

    private static string? Text(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static bool Flag(JsonElement parent, string name)
    {
        var text = Text(parent, name);
        return !string.IsNullOrWhiteSpace(text) && bool.TryParse(text, out var parsed) && parsed;
    }

    private static int Integer(JsonElement parent, string name, int fallback)
    {
        var text = Text(parent, name);
        return int.TryParse(text, out var parsed) ? parsed : fallback;
    }

    private static IReadOnlyList<string> Strings(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
        if (!parent.TryGetProperty(name, out var value)) return Array.Empty<string>();

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString();
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single! };
        }

        if (value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text)) result.Add(text!);
        }

        return result;
    }

    private static PortDirection ParseDirection(string? text)
        => string.Equals(text, "out", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(text, "output", StringComparison.OrdinalIgnoreCase)
            ? PortDirection.Out
            : PortDirection.In;

    private static TEnum? ParseEnum<TEnum>(string? text, TEnum fallback) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        return Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed) ? parsed : null;
    }

    /// <summary>端口简写 token：<c>name:Form(required)@scope</c>（三段皆可省后两段）。</summary>
    private static bool TryParsePortToken(
        string owner, string? token, PortDirection direction, List<WiringViolation> violations, out PortSpec port)
    {
        port = null!;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var text = token.Trim();
        var separator = text.IndexOf(':');
        if (separator <= 0 || separator >= text.Length - 1)
        {
            violations.Add(WiringViolation.Create(
                WiringReasonCodes.WireDeclInvalid, owner, $"端口 token 非法: {token}"));
            return false;
        }

        var name = text[..separator].Trim();
        var rest = text[(separator + 1)..].Trim();

        const string requiredToken = "(required)";
        var required = rest.Contains(requiredToken, StringComparison.OrdinalIgnoreCase);
        if (required) rest = rest.Replace(requiredToken, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

        var scope = "global";
        var at = rest.LastIndexOf('@');
        if (at > 0 && at < rest.Length - 1)
        {
            scope = rest[(at + 1)..].Trim();
            rest = rest[..at].Trim();
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rest))
        {
            violations.Add(WiringViolation.Create(
                WiringReasonCodes.WireDeclInvalid, owner, $"端口 token 非法: {token}"));
            return false;
        }

        port = new PortSpec(owner, name, direction, rest, required, 1, scope);
        return true;
    }
}
