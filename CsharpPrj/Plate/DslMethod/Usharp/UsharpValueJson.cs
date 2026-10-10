using System.Text;
using System.Text.Json;

namespace Plate.DslMethod.Usharp;

/// <summary>
/// USharp 值 ⟷ JSON 文本 的**兼容/迁移桥**（非内部表示）。
/// <para>
/// 用途（权威：路线 B-3「JSON 仅可存在于外部兼容读出/调试渲染」）：
/// ① <see cref="ToJson"/>：把原生 USharp 值渲染为 JSON，供既有 JSON 消费方（外部兼容读出/调试）读取；
/// ② <see cref="Parse"/>：迁移期把既有 JSON 生产者机械换产中立值（JSON → 值）。
/// </para>
/// <para>内部真源一律 <see cref="UsharpValue"/>；本桥不得用于存储真源或声明边界。数字保留原始文本（不经十进制转换），保证往返保真。</para>
/// </summary>
public static class UsharpValueJson
{
    /// <summary>JSON 文本 → 中立 USharp 值（空/非法 ⇒ null 标量）。</summary>
    public static UsharpValue Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return UsharpScalar.Null;
        using var doc = JsonDocument.Parse(json);
        return FromElement(doc.RootElement);
    }

    /// <summary>中立 USharp 值 → JSON 文本（紧凑、无多余空白；键序 = 记录序）。</summary>
    public static string ToJson(UsharpValue? value)
    {
        var sb = new StringBuilder();
        WriteJson(sb, value ?? UsharpScalar.Null);
        return sb.ToString();
    }

    private static UsharpValue FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new UsharpRecord(
            element.EnumerateObject()
                .Select(p => new KeyValuePair<string, UsharpValue>(p.Name, FromElement(p.Value)))
                .ToList()),
        JsonValueKind.Array => new UsharpList(
            element.EnumerateArray().Select(FromElement).ToList()),
        JsonValueKind.String => UsharpScalar.Str(element.GetString()),
        JsonValueKind.True => UsharpScalar.Bool(true),
        JsonValueKind.False => UsharpScalar.Bool(false),
        JsonValueKind.Number => NumberFromRaw(element.GetRawText()),
        _ => UsharpScalar.Null,
    };

    /// <summary>数字按原始文本保真：含小数点/指数 ⇒ Decimal，否则 Int（避免经 decimal 转换丢精度）。</summary>
    private static UsharpValue NumberFromRaw(string raw)
        => raw.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0
            ? new UsharpScalar(UsharpScalarKind.Decimal, raw)
            : new UsharpScalar(UsharpScalarKind.Int, raw);

    private static void WriteJson(StringBuilder sb, UsharpValue value)
    {
        switch (value)
        {
            case UsharpRecord record:
                sb.Append('{');
                for (var i = 0; i < record.Fields.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonSerializer.Serialize(record.Fields[i].Key)).Append(':');
                    WriteJson(sb, record.Fields[i].Value);
                }
                sb.Append('}');
                break;
            case UsharpList list:
                sb.Append('[');
                for (var i = 0; i < list.Items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteJson(sb, list.Items[i]);
                }
                sb.Append(']');
                break;
            case UsharpScalar scalar:
                switch (scalar.Kind)
                {
                    case UsharpScalarKind.Null: sb.Append("null"); break;
                    case UsharpScalarKind.Bool: sb.Append(scalar.Text == "true" ? "true" : "false"); break;
                    case UsharpScalarKind.Int:
                    case UsharpScalarKind.Decimal: sb.Append(scalar.Text); break;
                    default: sb.Append(JsonSerializer.Serialize(scalar.Text)); break;
                }
                break;
            default:
                sb.Append("null");
                break;
        }
    }
}