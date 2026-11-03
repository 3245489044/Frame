using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>表的一列声明：<c>&lt;name&gt; : &lt;type&gt; | &lt;flag&gt; …</c>。</summary>
/// <param name="Name">列名（坐标标识符集）。</param>
/// <param name="Type">列类型（<see cref="UsharpLanguage.ColumnTypes"/> 受控词）。</param>
/// <param name="Flags">列标志集（<see cref="UsharpLanguage.ColumnFlags"/> 受控词）。</param>
public sealed record UsharpColumnDecl(string Name, string Type, IReadOnlyList<string> Flags);

/// <summary>
/// <c>[Table] … write:</c> 写面声明（行写通用化的声明层）：把「哪些表可写 / 可写哪些字段 / 谁校验 / 写后做什么」
/// 从 C# 硬编码提升为声明。缺省 fail-closed（未声明 ⇒ 不可写）。
/// </summary>
/// <param name="Editable">是否可写（缺省 false）。</param>
/// <param name="Editor">编辑器形态（<c>record</c> / <c>fields</c>）。</param>
/// <param name="KeyImmutable">键不可改（缺省 true）。</param>
/// <param name="WritableFields">可写字段集。</param>
/// <param name="AllowTypes">类别白名单。</param>
/// <param name="AfterWrite">写后动作。</param>
/// <param name="Reason">不可写时的可见化原因。</param>
public sealed record UsharpTableWriteSpec(
    bool Editable,
    string? Editor,
    bool KeyImmutable,
    IReadOnlyList<string>? WritableFields,
    IReadOnlyList<string>? AllowTypes,
    string? AfterWrite,
    string? Reason);

/// <summary>
/// <c>[Table] Name</c> 语法视图（数据层：让数据库表结构由声明派生）。
/// <para>书写：头 = <c>[Table] &lt;tableId&gt;: container=&lt;c&gt; | key=&lt;col&gt; | index=(&lt;a&gt;,&lt;b&gt;)</c>；
/// 续行 = <c>&lt;col&gt; : &lt;type&gt; | &lt;flag&gt; …</c>（列声明序即 DDL 列序）。</para>
/// </summary>
public sealed record UsharpTableDecl(
    string TableId,
    string Container,
    string KeyColumn,
    IReadOnlyList<string> IndexColumns,
    IReadOnlyList<UsharpColumnDecl> Columns,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber,
    UsharpTableWriteSpec? WriteSpec = null)
{
    /// <summary>投影单条 <c>[Table]</c>（无名 ⇒ null）。</summary>
    public static UsharpTableDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var container = (d.Field(UsharpLanguage.FieldContainer) ?? string.Empty).Trim();
        var keyColumn = (d.Field(UsharpLanguage.FieldKey) ?? string.Empty).Trim();
        var indexRaw = (d.Field(UsharpLanguage.FieldIndex) ?? string.Empty).Trim();
        var indexColumns = SplitIndex(indexRaw);

        var columns = new List<UsharpColumnDecl>();
        var unparsed = new List<string>();
        var writeLines = new List<string>();
        var inWrite = false;
        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // 写面声明行（write: …）先行分流：避免被当作「名为 write 的列」而污染 DDL。
            if (UsharpLanguage.TryReadWriteMarker(line, out var rest))
            {
                inWrite = true;
                if (rest.Length > 0) writeLines.Add(rest);
                continue;
            }
            if (inWrite && line.StartsWith('|'))
            {
                writeLines.Add(line.TrimStart('|').Trim());
                continue;
            }

            inWrite = false;
            if (!TryParseColumn(line, out var column)) { unparsed.Add(line); continue; }
            columns.Add(column!);
        }

        var writeSpec = writeLines.Count == 0 ? null : ParseWriteSpec(writeLines);
        return new UsharpTableDecl(d.Name, container, keyColumn, indexColumns, columns,
            unparsed, d.SourceFile, d.LineNumber, writeSpec);
    }

    /// <summary>写面字段正文（多行以 <c>|</c> 合流）→ <see cref="UsharpTableWriteSpec"/>。</summary>
    private static UsharpTableWriteSpec ParseWriteSpec(IReadOnlyList<string> lines)
    {
        var parts = string.Join(" | ", lines)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var map = UsharpLineReader.ParseFieldBag(parts, 0, StringComparer.OrdinalIgnoreCase);

        var editable = map.TryGetValue(UsharpLanguage.WriteFieldEditable, out var e) && IsTrue(e);
        var editor = Get(UsharpLanguage.WriteFieldEditor);
        var keyImmutable = !map.TryGetValue(UsharpLanguage.WriteFieldKeyImmutable, out var ki) || IsTrue(ki);
        var fields = UsharpLineReader.SplitListOrNull(Get(UsharpLanguage.WriteFieldFields), ',', ';');
        var allowTypes = UsharpLineReader.SplitListOrNull(Get(UsharpLanguage.WriteFieldAllowTypes), ',', ';');
        var afterWrite = Get(UsharpLanguage.WriteFieldAfterWrite);
        var reason = Get(UsharpLanguage.WriteFieldReason);
        return new UsharpTableWriteSpec(editable, editor, keyImmutable, fields, allowTypes, afterWrite, reason);

        string? Get(string key) => map.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
    }

    private static bool IsTrue(string? value)
        => UsharpLineReader.TryBool(value, UsharpBoolLexicon.NumericYes, out var parsed) && parsed;

    /// <summary><c>index=(a,b)</c> / <c>index=a,b</c> 两形态均取（去括号、逗号分段）。</summary>
    private static IReadOnlyList<string> SplitIndex(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
            trimmed = trimmed[1..^1];
        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>列行：<c>&lt;name&gt; : &lt;type&gt; | &lt;flag&gt; …</c>（type 缺省 ⇒ 形状非法）。</summary>
    private static bool TryParseColumn(string line, out UsharpColumnDecl? column)
    {
        column = null;
        var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var (name, type) = UsharpLanguage.SplitPair(parts[0]);
        if (name.Length == 0 || type.Length == 0) return false;
        if (!UsharpIdentifier.Matches(name)) return false;

        var flags = parts.Skip(1).ToArray();
        column = new UsharpColumnDecl(name, type.ToLowerInvariant(), flags);
        return true;
    }
}
