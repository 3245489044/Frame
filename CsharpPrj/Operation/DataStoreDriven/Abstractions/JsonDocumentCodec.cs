using System.Text.Json;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 默认文档编解码（JSON；**零行为变更**回退面）：与既有 <c>PersistenceHelper.JsonOptions</c> 同源的 System.Text.Json 实现。
/// </summary>
public sealed class JsonDocumentCodec : IDocumentCodec
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly JsonDocumentCodec Instance = new();

    /// <inheritdoc />
    public string Format => "json";

    /// <inheritdoc />
    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <inheritdoc />
    public T? Deserialize<T>(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        try { return JsonSerializer.Deserialize<T>(text, JsonOptions); }
        catch (JsonException) { return default; }
    }

    /// <inheritdoc />
    public bool TryDeserialize<T>(string text, out T? value)
    {
        value = Deserialize<T>(text);
        return value is not null;
    }

    // ---- 记录实例端口（N0）：JSON 回退实现（头字段 __id/__table/__key；体为中立值 JSON 渲染）。----

    /// <inheritdoc />
    public string SerializeRecord(string recordId, string? table, string? key, UsharpRecord body)
        => "{\"__id\":" + JsonSerializer.Serialize(recordId)
         + ",\"__table\":" + (table is null ? "null" : JsonSerializer.Serialize(table))
         + ",\"__key\":" + (key is null ? "null" : JsonSerializer.Serialize(key))
         + ",\"__body\":" + UsharpValueJson.ToJson(body) + "}";

    /// <inheritdoc />
    public bool TryDeserializeRecord(string text, out string recordId, out string? table, out string? key, out UsharpRecord body)
    {
        recordId = string.Empty;
        table = null;
        key = null;
        body = new UsharpRecord(System.Array.Empty<KeyValuePair<string, UsharpValue>>());
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            recordId = root.TryGetProperty("__id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? string.Empty : string.Empty;
            if (recordId.Length == 0) return false;
            table = root.TryGetProperty("__table", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            key = root.TryGetProperty("__key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            if (root.TryGetProperty("__body", out var b) && UsharpValueJson.Parse(b.GetRawText()) is UsharpRecord rec) body = rec;
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <inheritdoc />
    public string SerializeRecord<T>(string recordId, string? table, string? key, T body, IReadOnlyList<KeyValuePair<string, UsharpValue>>? extraFields = null)
        => "{\"__id\":" + JsonSerializer.Serialize(recordId)
         + ",\"__table\":" + (table is null ? "null" : JsonSerializer.Serialize(table))
         + ",\"__key\":" + (key is null ? "null" : JsonSerializer.Serialize(key))
         + ",\"__extra\":" + (extraFields is { Count: > 0 } ? UsharpValueJson.ToJson(new UsharpRecord(extraFields)) : "{}")
         + ",\"__body\":" + JsonSerializer.Serialize(body, JsonOptions) + "}";

    /// <inheritdoc />
    public bool TryDeserializeRecord<T>(string text, out T? body)
    {
        body = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("__body", out var b)) return false;
            body = JsonSerializer.Deserialize<T>(b.GetRawText(), JsonOptions);
            return body is not null;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>与 <c>PersistenceHelper.JsonOptions</c> 同口径的序列化选项。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
