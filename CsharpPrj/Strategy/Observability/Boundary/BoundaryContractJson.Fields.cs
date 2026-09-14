using System.Text.Json;

namespace Strategy.Observability.Boundary;

/// <summary>边界契约 JSON 的字段/依赖/枚举读取（E9 加载器内部件）。</summary>
public static partial class BoundaryContractJson
{
    /// <summary>字段段读取：数组（位置式）或对象（命名式或含 <c>fields</c> 子段）。</summary>
    private static IReadOnlyList<FieldSpec> ReadFields(JsonElement? element)
    {
        if (element is not { } value) return [];

        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                {
                    var fields = new List<FieldSpec>();
                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        fields.Add(item.ValueKind == JsonValueKind.String
                            ? new FieldSpec(item.GetString()!, FieldTypes.Any)
                            : ReadField(item, $"arg{index}"));
                        index++;
                    }
                    return fields;
                }
            case JsonValueKind.Object:
                {
                    if (value.TryGetProperty("fields", out var nested)) return ReadFields(nested);

                    var fields = new List<FieldSpec>();
                    foreach (var property in value.EnumerateObject())
                        fields.Add(property.Value.ValueKind == JsonValueKind.String
                            ? new FieldSpec(property.Name, property.Value.GetString()!)
                            : ReadField(property.Value, property.Name));
                    return fields;
                }
            default:
                return [];
        }
    }

    private static FieldSpec ReadField(JsonElement element, string defaultName)
        => new(
            Str(element, "name") ?? defaultName,
            Str(element, "type") ?? FieldTypes.Any,
            Bool(element, "required") ?? true,
            Strings(element, "enum"));

    private static IReadOnlyList<PortDependency> ReadDepends(JsonElement? element)
    {
        if (element is not { } value || value.ValueKind != JsonValueKind.Array) return [];

        var depends = new List<PortDependency>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                depends.Add(new PortDependency(item.GetString()!));
            else if (Str(item, "key") is { } key)
                depends.Add(new PortDependency(key, Bool(item, "required") ?? true));
        }
        return depends;
    }

    private static IEnumerable<(string Name, IReadOnlyList<string> Allowed)> ReadEnums(JsonElement spec)
    {
        if (!spec.TryGetProperty("enum", out var enums) || enums.ValueKind != JsonValueKind.Object) yield break;
        foreach (var entry in enums.EnumerateObject())
            yield return (entry.Name, entry.Value.ValueKind == JsonValueKind.Array
                ? entry.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToArray()
                : []);
    }
}
