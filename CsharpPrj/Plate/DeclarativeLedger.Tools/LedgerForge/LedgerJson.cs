using System.Text;
using System.Text.Json;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// JSON 规范化文本（对拍口径）：**压缩且保留键序**，数值取原文 —— 与 PowerShell
/// <c>ConvertTo-Json -Compress</c> 的比较口径同向（键序参与比较，只排除缩进 / 换行差异）。
/// <para>判定「同一份声明」的语义：两侧各自压缩后逐字比较，避免因排版差异产生的假阴性。</para>
/// </summary>
internal static class LedgerJson
{
    /// <summary>元素的规范化压缩文本（键序保留）。</summary>
    internal static string Minify(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = true }))
            Write(writer, element);

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Write(writer, item);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer); // 标量：字符串 / 数值（原文）/ true / false / null
                break;
        }
    }
}
