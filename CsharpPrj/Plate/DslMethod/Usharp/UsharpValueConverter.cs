using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plate.DslMethod.Usharp;

/// <summary>
/// <see cref="UsharpValue"/> 的 System.Text.Json 转换器（**回退编解码自包含**）：
/// 把 USharp 值以结构化 JSON 往返（记录 → 字段数组，列表 → 元素数组，标量 → kind+text）。
/// <para>
/// 用途：当存储层使用**缺省 JSON codec**（非 USharp 适配层注入）时仍能承载中立值模型；
/// USharp 适配层的 <c>UsharpDocumentCodec</c> 不依赖 System.Text.Json，二者互不影响。
/// </para>
/// </summary>
public sealed class UsharpValueConverter : JsonConverter<UsharpValue>
{
    public override UsharpValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return ReadValue(doc.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, UsharpValue value, JsonSerializerOptions options)
        => WriteValue(writer, value ?? UsharpScalar.Null);

    private static void WriteValue(Utf8JsonWriter writer, UsharpValue value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case UsharpRecord record:
                writer.WriteString("$u", "r");
                writer.WritePropertyName("f");
                writer.WriteStartArray();
                foreach (var field in record.Fields)
                {
                    writer.WriteStartObject();
                    writer.WriteString("n", field.Key);
                    writer.WritePropertyName("v");
                    WriteValue(writer, field.Value);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;
            case UsharpList list:
                writer.WriteString("$u", "l");
                writer.WritePropertyName("i");
                writer.WriteStartArray();
                foreach (var item in list.Items) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            case UsharpScalar scalar:
                writer.WriteString("$u", "s");
                writer.WriteString("k", scalar.Kind.ToString());
                writer.WriteString("t", scalar.Text);
                break;
            default:
                writer.WriteString("$u", "n");
                break;
        }
        writer.WriteEndObject();
    }

    private static UsharpValue ReadValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("$u", out var tag))
            return UsharpScalar.Null;
        switch (tag.GetString())
        {
            case "r":
                var fields = new List<KeyValuePair<string, UsharpValue>>();
                if (element.TryGetProperty("f", out var arr))
                    foreach (var f in arr.EnumerateArray())
                        fields.Add(new KeyValuePair<string, UsharpValue>(
                            f.TryGetProperty("n", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                            f.TryGetProperty("v", out var v) ? ReadValue(v) : UsharpScalar.Null));
                return new UsharpRecord(fields);
            case "l":
                var items = new List<UsharpValue>();
                if (element.TryGetProperty("i", out var it))
                    foreach (var x in it.EnumerateArray()) items.Add(ReadValue(x));
                return new UsharpList(items);
            case "s":
                var kind = element.TryGetProperty("k", out var k) && Enum.TryParse<UsharpScalarKind>(k.GetString(), out var parsed)
                    ? parsed : UsharpScalarKind.String;
                var text = element.TryGetProperty("t", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                return new UsharpScalar(kind, text);
            default:
                return UsharpScalar.Null;
        }
    }
}