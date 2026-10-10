using System.Text;
using System.Text.Json;
using Operation.DataStoreDriven.Abstractions;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// USharp 文档编解码（<see cref="IDocumentCodec"/> 的 USharp 实现）：
/// <c>对象 ⟷ JsonNode ⟷ UsharpValue ⟷ USharp 文本</c>。
/// <para>
/// 落库形态：<c>[Record] &lt;TypeName&gt;</c> + 缩进 <c>field = value</c> 块（规范形 = <see cref="UsharpValue.ToCanonical"/>）。
/// 兼容读：裸值（<c>{ … }</c> / <c>[ … ]</c>）亦可解析（便于手工/迁移）。
/// </para>
/// <para>本实现只承担**文本格式**；对象映射复用 System.Text.Json（<see cref="UsharpJsonBridge"/>），零行为漂移风险。</para>
/// </summary>
public sealed class UsharpDocumentCodec : IDocumentCodec
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly UsharpDocumentCodec Instance = new();

    /// <inheritdoc />
    public string Format => "usharp";

    /// <inheritdoc />
    public string Serialize<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonDocumentCodec.JsonOptions);
        var usharp = UsharpJsonBridge.ToValue(node);

        var sb = new StringBuilder();
        sb.Append(UsharpLanguage.RecordPrefix).Append(' ').Append(typeof(T).Name).Append('\n');
        if (usharp is UsharpRecord record)
        {
            foreach (var f in record.Fields)
                sb.Append("  ").Append(f.Key).Append(" = ").Append(f.Value.ToCanonical()).Append('\n');
        }
        else
        {
            sb.Append("  value = ").Append(usharp.ToCanonical()).Append('\n');
        }
        return sb.ToString();
    }

    /// <inheritdoc />
    public T? Deserialize<T>(string text) => TryDeserialize(text, out T? value) ? value : default;

    /// <inheritdoc />
    public bool TryDeserialize<T>(string text, out T? value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var usharp = ParseDocument(text);
        if (usharp is null) return false;

        var node = UsharpJsonBridge.ToJson(usharp);
        if (node is null) return false;

        try { value = node.Deserialize<T>(JsonDocumentCodec.JsonOptions); return value is not null; }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    /// <inheritdoc />
    public string? ExtractScalar(string document, string memberName)
    {
        if (string.IsNullOrWhiteSpace(document) || string.IsNullOrWhiteSpace(memberName)) return null;

        var usharp = ParseDocument(document);
        if (usharp is UsharpRecord record && record.TryGet(memberName, out var v) && v is UsharpScalar scalar)
            return scalar.Kind == UsharpScalarKind.String ? scalar.Text : scalar.ToCanonical();
        return null;
    }

    /// <summary>解析 USharp 文档：<c>[Record]</c> 头块 或 裸值（解析失败 ⇒ null）。</summary>
    private static UsharpValue? ParseDocument(string text)
    {
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith(UsharpLanguage.RecordPrefix, StringComparison.Ordinal))
            return UsharpValueParser.TryParse(text, out var bare, out _) ? bare : null;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var unparsed = new List<string>();
        return UsharpValueParser.ParseRecordBlock(lines.Skip(1), unparsed);
    }
}
