using System.Text.Json;
using System.Text.Json.Serialization;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Serialization;

/// <summary>
/// 电路即数据：电路是纯声明数据结构，可序列化、可 diff、可持久化。执行是它的派生行为。
/// 使用 System.Text.Json 做默认往返（JSON 是声明面与外部工具的最小公共约定）。
/// </summary>
public static class CircuitJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new LiteralSymbolConverter() },
    };

    /// <summary>将电路序列化为 JSON 文本。</summary>
    public static string Serialize(Circuit circuit) => JsonSerializer.Serialize(circuit, Options);

    /// <summary>从 JSON 文本反序列化为电路。</summary>
    public static Circuit Deserialize(string json) =>
        JsonSerializer.Deserialize<Circuit>(json, Options)
        ?? throw new InvalidOperationException("电路 JSON 反序列化结果为 null。");
}

/// <summary>
/// #6 类型判别：序列化时用 Value 的具体 CLR 类型填充 TypeDiscriminator；
/// 反序列化时按 TypeDiscriminator 把 JSON 值还原为具体类型，杜绝 object? 退化为 JsonElement。
/// </summary>
public sealed class LiteralSymbolConverter : JsonConverter<LiteralSymbol>
{
    public override LiteralSymbol Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var targetNodeId = GetString(root, GetName(options, nameof(LiteralSymbol.TargetNodeId)));
        var port = GetString(root, GetName(options, nameof(LiteralSymbol.Port)));
        var valueType = GetString(root, GetName(options, nameof(LiteralSymbol.ValueType)));
        var disc = GetString(root, GetName(options, nameof(LiteralSymbol.TypeDiscriminator)));

        object? value = null;
        if (root.TryGetProperty(GetName(options, nameof(LiteralSymbol.Value)), out var valueEl) && valueEl.ValueKind != JsonValueKind.Null)
        {
            if (!string.IsNullOrWhiteSpace(disc) && Type.GetType(disc) is { } targetType)
                value = valueEl.Deserialize(targetType, options);
            else
                value = valueEl.ValueKind == JsonValueKind.String ? valueEl.GetString()
                        : valueEl.ValueKind == JsonValueKind.Number ? valueEl.GetDecimal()
                        : valueEl.ValueKind == JsonValueKind.True ? true
                        : valueEl.ValueKind == JsonValueKind.False ? false
                        : valueEl.Clone();
        }

        return new LiteralSymbol
        {
            TargetNodeId = targetNodeId,
            Port = port,
            Value = value,
            ValueType = valueType,
            TypeDiscriminator = disc,
        };
    }

    public override void Write(Utf8JsonWriter writer, LiteralSymbol value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(GetName(options, nameof(LiteralSymbol.TargetNodeId)), value.TargetNodeId);
        writer.WriteString(GetName(options, nameof(LiteralSymbol.Port)), value.Port);
        WriteNullableString(writer, GetName(options, nameof(LiteralSymbol.ValueType)), value.ValueType);

        var disc = value.TypeDiscriminator ?? value.Value?.GetType().FullName;
        WriteNullableString(writer, GetName(options, nameof(LiteralSymbol.TypeDiscriminator)), disc);

        writer.WritePropertyName(GetName(options, nameof(LiteralSymbol.Value)));
        if (value.Value is null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Value, value.Value.GetType(), options);

        writer.WriteEndObject();
    }

    private static string GetName(JsonSerializerOptions options, string prop) =>
        options.PropertyNamingPolicy?.ConvertName(prop) ?? prop;

    private static string? GetString(JsonElement root, string camel) =>
        root.TryGetProperty(camel, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? v)
    {
        if (v is null) return;
        writer.WriteString(name, v);
    }
}