using System.Text.Json;
using System.Text.RegularExpressions;

namespace DataCenter.Standards;

/// <summary>
/// 台账结构校验（设计文档 §3.2 <c>StandardSchemaValidator</c>）：按 <c>schemas\*.schema.json</c>
/// 做 **结构校验（先结构、后语义）**——结构不过即阻断，不进 C1–C6 语义判据。
/// <para>
/// 实现范围（U2 实测：库内仅 <c>System.Text.Json</c>，**不引第三方 JSON Schema 库**）：
/// 仅支持本仓库 schema 实际用到的子集 —— <c>type</c>（含数组）/ <c>required</c> / <c>properties</c> /
/// <c>additionalProperties</c>（bool 或子 schema）/ <c>propertyNames.pattern</c> / <c>items</c> /
/// <c>minItems</c> / <c>minProperties</c> / <c>enum</c> / <c>pattern</c>；
/// 忽略 <c>$schema</c>/<c>$comment</c>/<c>title</c>。
/// </para>
/// </summary>
public static class StandardSchemaValidator
{
    private static readonly string[] IgnoredKeywords = ["$schema", "$comment", "title"];

    /// <summary>结构校验；返回违规明细（空 = 通过）。</summary>
    public static IReadOnlyList<string> Validate(string json, string schemaJson, string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);

        var violations = new List<string>();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        using var schema = JsonDocument.Parse(schemaJson, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        Walk(document.RootElement, schema.RootElement, context ?? "$", violations);
        return violations;
    }

    /// <summary>结构校验；不通过即抛（阻断装载）。</summary>
    public static void EnsureValid(string json, string schemaJson, string? context = null)
    {
        var violations = Validate(json, schemaJson, context);
        if (violations.Count == 0) return;

        throw new StandardViolationException(
            $"结构校验失败（{violations.Count} 条；先结构后语义）：" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", violations),
            violations);
    }

    private static void Walk(JsonElement value, JsonElement schema, string path, List<string> violations)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;

        if (schema.TryGetProperty("type", out var type) && !MatchesType(value, type))
        {
            violations.Add($"类型不符：{path} = {value.ValueKind}，应为 {Describe(type)}");
            return;
        }

        if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array
            && !allowed.EnumerateArray().Any(a => a.GetRawText() == value.GetRawText()))
            violations.Add($"取值不在枚举内：{path} = {Literal(value)}");

        if (schema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String
            && value.ValueKind == JsonValueKind.String
            && !Regex.IsMatch(value.GetString() ?? string.Empty, pattern.GetString()!))
            violations.Add($"不匹配模式：{path} = {Literal(value)}（pattern = {pattern.GetString()}）");

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WalkObject(value, schema, path, violations);
                break;
            case JsonValueKind.Array:
                WalkArray(value, schema, path, violations);
                break;
        }
    }

    private static void WalkObject(JsonElement value, JsonElement schema, string path, List<string> violations)
    {
        if (schema.TryGetProperty("minProperties", out var min) && min.ValueKind == JsonValueKind.Number
            && value.EnumerateObject().Count() < min.GetInt32())
            violations.Add($"对象键数不足：{path} 需 ≥ {min.GetInt32()}");

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var name in required.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()!))
                if (!value.TryGetProperty(name, out _))
                    violations.Add($"缺少必填键：{path}.{name}");

        if (schema.TryGetProperty("propertyNames", out var propertyNames) && propertyNames.ValueKind == JsonValueKind.Object
            && propertyNames.TryGetProperty("pattern", out var namePattern) && namePattern.ValueKind == JsonValueKind.String)
            foreach (var property in value.EnumerateObject())
                if (!Regex.IsMatch(property.Name, namePattern.GetString()!))
                    violations.Add($"键名不匹配模式：{path}.{property.Name}（pattern = {namePattern.GetString()}）");

        var properties = schema.TryGetProperty("properties", out var declared) && declared.ValueKind == JsonValueKind.Object
            ? declared
            : default;
        var additional = schema.TryGetProperty("additionalProperties", out var extra) ? extra : default;

        foreach (var property in value.EnumerateObject())
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var subSchema))
            {
                Walk(property.Value, subSchema, $"{path}.{property.Name}", violations);
                continue;
            }

            if (IgnoredKeywords.Contains(property.Name, StringComparer.Ordinal)) continue;

            switch (additional.ValueKind)
            {
                case JsonValueKind.False:
                    violations.Add($"未声明的键：{path}.{property.Name}");
                    break;
                case JsonValueKind.Object:
                    Walk(property.Value, additional, $"{path}.{property.Name}", violations);
                    break;
            }
        }
    }

    private static void WalkArray(JsonElement value, JsonElement schema, string path, List<string> violations)
    {
        if (schema.TryGetProperty("minItems", out var min) && min.ValueKind == JsonValueKind.Number
            && value.GetArrayLength() < min.GetInt32())
            violations.Add($"数组长度不足：{path} 需 ≥ {min.GetInt32()}");

        if (!schema.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object) return;

        var index = 0;
        foreach (var element in value.EnumerateArray())
            Walk(element, items, $"{path}[{index++}]", violations);
    }

    private static bool MatchesType(JsonElement value, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.Array)
            return type.EnumerateArray().Any(t => MatchesType(value, t));

        if (type.ValueKind != JsonValueKind.String) return true;

        return type.GetString() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        };
    }

    private static string Describe(JsonElement type)
        => type.ValueKind == JsonValueKind.Array
            ? string.Join("|", type.EnumerateArray().Select(t => t.ToString()))
            : type.ToString();

    private static string Literal(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}
