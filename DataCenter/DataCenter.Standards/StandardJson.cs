using System.Text;
using System.Text.Json;
using DataCenter.Protocol.Standards;

namespace DataCenter.Standards;

/// <summary>
/// 台账 JSON → 中立模型（设计文档 §3.2 <c>StandardJson</c>）：
/// <c>$comment</c> 元数据键忽略；其余未知键 **fail-fast**（防声明漂移静默）。
/// 另提供 <see cref="DuplicateKeys"/>（C1 的机器判据载体：JSON 对象重复键在
/// <see cref="JsonDocument"/> 层被静默合并，故须在原始文本层扫描）。
/// </summary>
public static partial class StandardJson
{
    private static readonly string[] PortTopKeys = ["$comment", "ports", "data", "events", "fakes"];
    private static readonly string[] PortEntryKeys = ["in", "out", "depends", "scope", "version", "emits", "impl"];
    private static readonly string[] PortFieldKeys = ["type", "required"];
    private static readonly string[] DataEntryKeys =
        ["direction", "scope", "fields", "producer", "producers", "consumers", "version", "crossCutting"];
    private static readonly string[] EventTopKeys = ["$comment", "version", "entities", "events"];
    private static readonly string[] EventEntryKeys = ["entity", "carries", "required", "metrics", "enum", "scope", "version"];
    private static readonly string[] FakeTopKeys = ["$comment", "fakes"];
    private static readonly string[] FakeEntryKeys = ["kind", "seed", "version"];

    /// <summary>端口标准（<c>ports</c> 段；键 Ordinal 升序）。</summary>
    public static IReadOnlyList<PortStandard> ReadPorts(string portsJson)
        => Read(portsJson, root =>
        {
            EnsureKnownKeys("ports.json", root, PortTopKeys);
            if (!TrySection(root, "ports", out var ports)) return [];

            return ports.EnumerateObject().Select(p =>
            {
                EnsureKnownKeys($"ports.{p.Name}", p.Value, PortEntryKeys);
                return new PortStandard(
                    p.Name,
                    ReadInputs($"ports.{p.Name}.in", p.Value),
                    PathStrings(p.Value, "out.fields"),
                    Strings(p.Value, "depends"),
                    Text(p.Value, "scope") ?? string.Empty,
                    (int)(Number(p.Value, "version") ?? 1),
                    Strings(p.Value, "emits"),
                    ReadImpl(p.Name, p.Value));
            }).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        });

    /// <summary>数据标准（<c>data</c> 段；键 Ordinal 升序）。</summary>
    public static IReadOnlyList<DataStandardItem> ReadData(string portsJson)
        => Read(portsJson, root =>
        {
            EnsureKnownKeys("ports.json", root, PortTopKeys);
            if (!TrySection(root, "data", out var data)) return [];

            return data.EnumerateObject().Select(p =>
            {
                EnsureKnownKeys($"data.{p.Name}", p.Value, DataEntryKeys);
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                if (p.Value.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
                    foreach (var field in f.EnumerateObject())
                        fields[field.Name] = FieldType(field.Value);

                return new DataStandardItem(
                    p.Name,
                    Text(p.Value, "direction") ?? string.Empty,
                    Text(p.Value, "scope") ?? string.Empty,
                    fields,
                    Producers(p.Value),
                    Strings(p.Value, "consumers"),
                    (int)(Number(p.Value, "version") ?? 1));
            }).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        });

    /// <summary>事件标准（<c>events</c> 段；键 Ordinal 升序）。</summary>
    public static IReadOnlyList<EventStandard> ReadEvents(string eventsJson)
        => Read(eventsJson, root =>
        {
            EnsureKnownKeys("events.json", root, EventTopKeys);
            if (!TrySection(root, "events", out var events)) return [];

            return events.EnumerateObject().Select(p =>
            {
                EnsureKnownKeys($"events.{p.Name}", p.Value, EventEntryKeys);
                return new EventStandard(
                    p.Name,
                    Text(p.Value, "entity"),
                    Strings(p.Value, "carries"),
                    Strings(p.Value, "required"),
                    Strings(p.Value, "metrics"),
                    Text(p.Value, "scope"),
                    (int)(Number(p.Value, "version") ?? 1));
            }).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        });

    /// <summary>替身标准（<c>fakes</c> 段；端口键 Ordinal 升序）。</summary>
    public static IReadOnlyList<FakeStandard> ReadFakes(string fakesJson)
        => Read(fakesJson, root =>
        {
            EnsureKnownKeys("fakes.json", root, FakeTopKeys);
            if (!TrySection(root, "fakes", out var fakes)) return [];

            return fakes.EnumerateObject().Select(p =>
            {
                EnsureKnownKeys($"fakes.{p.Name}", p.Value, FakeEntryKeys);
                return new FakeStandard(
                    p.Name,
                    Text(p.Value, "kind") ?? string.Empty,
                    p.Value.TryGetProperty("seed", out var seed) ? seed.GetRawText() : null,
                    (int)(Number(p.Value, "version") ?? 1));
            }).OrderBy(x => x.PortKey, StringComparer.Ordinal).ToArray();
        });

    /// <summary>扫描 JSON 文本中的重复键（同一对象内同名；Ordinal）。</summary>
    public static IReadOnlyList<string> DuplicateKeys(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var duplicates = new List<string>();
        var stack = new Stack<HashSet<string>>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    stack.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    if (stack.Count > 0) stack.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    var name = reader.GetString() ?? string.Empty;
                    if (stack.Count > 0 && !stack.Peek().Add(name)) duplicates.Add(name);
                    break;
            }
        }

        return duplicates;
    }

    /// <summary>解析 → 交给读者 → 释放文档（根元素先 Clone，脱离文档生命周期）。</summary>
    private static IReadOnlyList<T> Read<T>(string json, Func<JsonElement, IReadOnlyList<T>> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        return body(document.RootElement.Clone());
    }

    private static bool TrySection(JsonElement root, string name, out JsonElement section)
    {
        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            section = value;
            return true;
        }

        section = default;
        return false;
    }

    private static void EnsureKnownKeys(string context, JsonElement obj, IReadOnlyList<string> known)
    {
        if (obj.ValueKind != JsonValueKind.Object) return;

        foreach (var property in obj.EnumerateObject())
            if (!known.Contains(property.Name, StringComparer.Ordinal))
                throw new StandardViolationException(
                    $"未知键 '{context}.{property.Name}'（fail-fast：非标准字段不得静默进入标准面，元数据请用 $comment）");
    }

    private static IReadOnlyList<PortFieldStandard> ReadInputs(string context, JsonElement entry)
    {
        if (!entry.TryGetProperty("in", out var input) || input.ValueKind != JsonValueKind.Object) return [];

        return input.EnumerateObject().Select(p =>
        {
            EnsureKnownKeys(context, p.Value, PortFieldKeys);
            var optional = p.Value.ValueKind == JsonValueKind.Object
                && p.Value.TryGetProperty("required", out var required)
                && required.ValueKind == JsonValueKind.False;

            return new PortFieldStandard(p.Name, FieldType(p.Value), !optional);
        }).ToArray();
    }

    private static string FieldType(JsonElement field)
    {
        if (field.ValueKind == JsonValueKind.Object && field.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            return type.GetString() ?? string.Empty;

        return field.ValueKind == JsonValueKind.String ? field.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>点链取值（如 <c>out.fields</c>）→ 字符串数组（缺失 → 空）。</summary>
    private static IReadOnlyList<string> PathStrings(JsonElement entry, string path)
    {
        var current = entry;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next)) return [];
            current = next;
        }

        return current.ValueKind == JsonValueKind.Array
            ? current.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray()
            : [];
    }

    private static IReadOnlyList<string> Strings(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value)) return [];

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray(),
            JsonValueKind.String => [value.GetString()!],
            _ => [],
        };
    }

    private static IReadOnlyList<string> Producers(JsonElement entry)
    {
        var declared = Strings(entry, "producers");
        return declared.Count > 0 ? declared : Strings(entry, "producer");
    }

    private static string? Text(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;
}
