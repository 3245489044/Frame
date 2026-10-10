using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <inheritdoc />
    public string? ExtractScalar(string document, string memberName)
    {
        if (string.IsNullOrWhiteSpace(document) || string.IsNullOrWhiteSpace(memberName)) return null;
        try
        {
            var node = JsonNode.Parse(document);
            var value = node?[memberName];
            return ScalarText(value);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>JSON 节点 → 标量文本（对象/数组 ⇒ null）。</summary>
    internal static string? ScalarText(JsonNode? node)
    {
        if (node is null) return null;
        var value = node.AsValue();
        if (value.TryGetValue<string>(out var s)) return s;
        if (value.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        if (value.TryGetValue<long>(out var l)) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value.TryGetValue<double>(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>与 <c>PersistenceHelper.JsonOptions</c> 同口径的序列化选项。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
