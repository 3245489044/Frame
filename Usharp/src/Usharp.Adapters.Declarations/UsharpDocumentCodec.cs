using System.Collections.Generic;
using System.Text;
using Operation.DataStoreDriven.Abstractions;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// USharp 文档编解码（<see cref="IDocumentCodec"/> 的 USharp 实现）：
/// <c>对象 ⟷ UsharpValue ⟷ USharp 文本</c>（**无 JSON 中间层**，路线 B）。
/// <para>
/// 落库形态：<c>[Record] &lt;TypeName&gt;</c> + 缩进 <c>field = value</c> 块（规范形 = <see cref="UsharpValue.ToCanonical"/>）。
/// 兼容读：裸值（<c>{ … }</c> / <c>[ … ]</c>）亦可解析（便于手工/迁移）。
/// </para>
/// <para>对象映射 = <see cref="UsharpValueMapper"/>（反射直接映射；不再经 System.Text.Json）。</para>
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
        var usharp = UsharpValueMapper.ToValue(value);

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

        value = UsharpValueMapper.ToObject<T>(usharp);
        return value is not null;
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
