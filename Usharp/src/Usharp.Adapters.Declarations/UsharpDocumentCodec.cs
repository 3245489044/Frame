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
    public bool TryDeserialize<T>(string text, out T? value) => TryDeserialize(text, out value, out _);

    /// <summary>
    /// 失败可见的反序列化（E-4）：失败原因（<c>parse-failed</c> / <c>enum-invalid(...)</c> 等）经
    /// <paramref name="reason"/> 透出，不再静默返回 default。
    /// </summary>
    /// <param name="text">USharp 文本。</param>
    /// <param name="value">目标对象（失败为 default）。</param>
    /// <param name="reason">失败原因（成功为 null）。</param>
    public bool TryDeserialize<T>(string text, out T? value, out string? reason)
    {
        value = default;
        reason = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            reason = "empty-text";
            return false;
        }

        var usharp = ParseDocument(text, out var complete);
        if (usharp is null)
        {
            reason = "parse-failed";
            return false;
        }

        if (!complete)
        {
            reason = "document-incomplete";
            return false;
        }

        var converted = UsharpValueMapper.TryToObject<T>(usharp);
        if (!converted.Ok)
        {
            reason = converted.Reason ?? "mapping-failed";
            return false;
        }

        value = converted.Value;
        return true;
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
        return unparsed.Count == 0;
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
    public bool TryDeserializeRecord<T>(string text, out T? body) => TryDeserializeRecord(text, out body, out _);

    /// <summary>失败可见的记录反序列化（E-4）：失败原因经 <paramref name="reason"/> 透出。</summary>
    /// <param name="text">USharp 记录文本。</param>
    /// <param name="body">目标对象（失败为 default）。</param>
    /// <param name="reason">失败原因（成功为 null）。</param>
    public bool TryDeserializeRecord<T>(string text, out T? body, out string? reason)
    {
        body = default;
        reason = null;
        if (!TryDeserializeRecord(text, out _, out _, out _, out var record))
        {
            reason = "record-parse-failed";
            return false;
        }

        var converted = UsharpValueMapper.TryToObject<T>(record);
        if (!converted.Ok)
        {
            reason = converted.Reason ?? "mapping-failed";
            return false;
        }

        body = converted.Value;
        return true;
    }

    /// <summary>解析记录头（<c>[Record] &lt;id&gt;: table=&lt;t&gt; | key=&lt;k&gt;</c>）：以**首个** <c>:</c> 分隔 id 与头字段。</summary>
    private static (string Id, string? Table, string? Key) ParseRecordHeader(string head)
    {
        var rest = head[UsharpLanguage.RecordPrefix.Length..].TrimStart();
        var sep = rest.IndexOf(':');
        var id = sep >= 0 ? rest[..sep].Trim() : rest.Trim();
        string? table = null, key = null;
        if (sep >= 0)
        {
            var parts = rest[(sep + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var bag = UsharpFieldBag.Parse(parts, skip: 0, comparer: StringComparer.Ordinal);
            if (bag.Fields.TryGetValue(UsharpLanguage.FieldTable, out var tableValue)) table = UsharpLanguage.NullIfEmpty(tableValue);
            if (bag.Fields.TryGetValue(UsharpLanguage.FieldKey, out var keyValue)) key = UsharpLanguage.NullIfEmpty(keyValue);
        }
        return (id, table, key);
    }

    /// <summary>解析 USharp 文档：<c>[Record]</c> 头块 或 裸值（解析失败/有未解析行 ⇒ null + complete=false）。</summary>
    private static UsharpValue? ParseDocument(string text, out bool complete)
    {
        complete = false;
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith(UsharpLanguage.RecordPrefix, StringComparison.Ordinal))
        {
            if (!UsharpValueParser.TryParse(text, out var bare, out var unparsed)) return null;
            complete = string.IsNullOrEmpty(unparsed);
            return bare;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var recordUnparsed = new List<string>();
        var value = UsharpValueParser.ParseRecordBlock(lines.Skip(1), recordUnparsed);
        complete = recordUnparsed.Count == 0;
        return value;
    }
}
