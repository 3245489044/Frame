using System.Collections.Generic;
using System.Text;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
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
    public string SerializeRecord(string recordId, string? table, string? key, UsharpRecord body)
        => new UsharpRecordDecl(recordId, table, key, body, Array.Empty<string>(), string.Empty, 0).ToCanonical();

    /// <inheritdoc />
    public bool TryDeserializeRecord(string text, out string recordId, out string? table, out string? key, out UsharpRecord body)
    {
        recordId = string.Empty;
        table = null;
        key = null;
        body = new UsharpRecord(Array.Empty<KeyValuePair<string, UsharpValue>>());
        if (string.IsNullOrWhiteSpace(text)) return false;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var head = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        if (!head.StartsWith(UsharpLanguage.RecordPrefix, StringComparison.Ordinal)) return false;

        (recordId, table, key) = ParseRecordHeader(head);
        if (recordId.Length == 0) return false;

        var unparsed = new List<string>();
        body = UsharpValueParser.ParseRecordBlock(lines.Skip(1), unparsed);
        return true;
    }

    /// <inheritdoc />
    public string SerializeRecord<T>(string recordId, string? table, string? key, T body, IReadOnlyList<KeyValuePair<string, UsharpValue>>? extraFields = null)
    {
        var record = UsharpValueMapper.ToValue(body) as UsharpRecord
                     ?? new UsharpRecord(Array.Empty<KeyValuePair<string, UsharpValue>>());
        if (extraFields is { Count: > 0 })
        {
            var merged = new List<KeyValuePair<string, UsharpValue>>(extraFields.Count + record.Fields.Count);
            merged.AddRange(extraFields);
            merged.AddRange(record.Fields);
            record = new UsharpRecord(merged);
        }
        return SerializeRecord(recordId, table, key, record);
    }

    /// <inheritdoc />
    public bool TryDeserializeRecord<T>(string text, out T? body)
    {
        body = default;
        if (!TryDeserializeRecord(text, out _, out _, out _, out var record)) return false;
        body = UsharpValueMapper.ToObject<T>(record);
        return body is not null;
    }

    /// <summary>解析记录头（<c>[Record] &lt;id&gt;: table=&lt;t&gt; | key=&lt;k&gt;</c>）：以首个 <c>": "</c> 分隔 id 与头字段。</summary>
    private static (string Id, string? Table, string? Key) ParseRecordHeader(string head)
    {
        var rest = head[UsharpLanguage.RecordPrefix.Length..].TrimStart();
        var sep = rest.IndexOf(": ", StringComparison.Ordinal);
        var id = sep >= 0 ? rest[..sep].Trim() : rest.Trim();
        string? table = null, key = null;
        if (sep >= 0)
        {
            foreach (var part in rest[(sep + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var name = part[..eq].Trim();
                var value = UsharpLanguage.NullIfEmpty(part[(eq + 1)..]);
                if (string.Equals(name, UsharpLanguage.FieldTable, StringComparison.Ordinal)) table = value;
                else if (string.Equals(name, UsharpLanguage.FieldKey, StringComparison.Ordinal)) key = value;
            }
        }
        return (id, table, key);
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
