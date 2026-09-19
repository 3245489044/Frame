using System.Text.Json;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>入口形态判据策略（数据视图 <c>entry</c> 段）：严重度 + 文案（机制在 <see cref="EntryCheckCommand"/>）。</summary>
internal sealed record EntryPolicy(
    IReadOnlyDictionary<string, string> Severity,
    IReadOnlyDictionary<string, string> Messages)
{
    internal string SeverityOf(string rule) => Severity.TryGetValue(rule, out var value) ? value : "error";

    internal string Text(string rule, params object[] args)
    {
        var text = Messages.TryGetValue(rule, out var value) ? value : $"<missing:{rule}>";
        return args.Length == 0 ? text : string.Format(text, args);
    }

    internal static EntryPolicy Load(string dataPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(dataPath));
        if (!document.RootElement.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Object)
            return new EntryPolicy(Empty, Empty);

        return new EntryPolicy(Read(entry, "severity"), Read(entry, "messages"));
    }

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> Read(JsonElement owner, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!owner.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object) return map;

        foreach (var property in node.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String)
                map[property.Name] = property.Value.GetString()!;

        return map;
    }
}

/// <summary>一条入口形态问题（<c>Severity</c> ∈ <c>error</c> / <c>warn</c> / <c>info</c>）。</summary>
internal sealed record EntryIssue(string PortKey, string Rule, string Severity, string Detail);
