using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>表的一列声明：<c>&lt;name&gt; : &lt;type&gt; | &lt;flag&gt; …</c>。</summary>
/// <param name="Name">列名（坐标标识符集）。</param>
/// <param name="Type">列类型（<see cref="UsharpLanguage.ColumnTypes"/> 受控词）。</param>
/// <param name="Flags">列标志集（<see cref="UsharpLanguage.ColumnFlags"/> 受控词）。</param>
public sealed record UsharpColumnDecl(string Name, string Type, IReadOnlyList<string> Flags);

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
    int LineNumber)
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
        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (!TryParseColumn(line, out var column)) { unparsed.Add(line); continue; }
            columns.Add(column!);
        }

        return new UsharpTableDecl(d.Name, container, keyColumn, indexColumns, columns,
            unparsed, d.SourceFile, d.LineNumber);
    }

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
