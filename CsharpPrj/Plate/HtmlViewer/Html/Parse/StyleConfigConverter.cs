#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// <see cref="StyleConfig"/> 双格式转换器：接受「命名样式字符串」（如 <c>"readonly-bg"</c>）或
/// 「内联样式对象」两种 JSON 形态，统一落到 <see cref="StyleConfig"/>。字符串形态写入
/// <see cref="StyleConfig.StyleReference"/>（命名样式引用，样式合并阶段由 StyleSystem/主题解析），
/// 对象形态按默认反序列化。
/// 修复 <see cref="HtmlDeclarationParser"/> 裸反序列化遇 <c>style: "readonly-bg"</c> 抛
/// <c>JsonException: The JSON value could not be converted to TConfig.StyleConfig</c> 的缺口——
/// 与 TemplateExpansion/UniversalMiddleware 侧 style 双格式（字符串引用/对象）语义对齐。
/// </summary>
public sealed class StyleConfigConverter : JsonConverter<StyleConfig>
{
    public override StyleConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
            {
                var styleRef = reader.GetString();
                return string.IsNullOrWhiteSpace(styleRef)
                    ? null
                    : new StyleConfig { StyleReference = styleRef };
            }
            default:
                return JsonSerializer.Deserialize<StyleConfig>(ref reader, ObjectOptions(options));
        }
    }

    public override void Write(Utf8JsonWriter writer, StyleConfig value, JsonSerializerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(value.StyleReference))
        {
            writer.WriteStringValue(value.StyleReference);
            return;
        }
        JsonSerializer.Serialize(writer, value, ObjectOptions(options));
    }

    /// <summary>剥离本转换器后的对象形态 options，避免对象反序列化/序列化递归命中本转换器。</summary>
    private static JsonSerializerOptions ObjectOptions(JsonSerializerOptions options)
    {
        var copy = new JsonSerializerOptions(options);
        for (int i = copy.Converters.Count - 1; i >= 0; i--)
        {
            if (copy.Converters[i] is StyleConfigConverter)
                copy.Converters.RemoveAt(i);
        }
        return copy;
    }
}
