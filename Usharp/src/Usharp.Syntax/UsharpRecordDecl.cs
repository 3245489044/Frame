using Plate.DslMethod.Model;
using Plate.DslMethod.Usharp;

namespace Usharp.Syntax;

/// <summary>
/// <c>[Record] Name</c> 语法视图（数据层：行/文档载荷的 USharp 化载体）。
/// <para>书写：头 = <c>[Record] &lt;recordId&gt;: table=&lt;tableId&gt; | key=&lt;businessKey&gt;</c>；
/// 续行 = <c>&lt;field&gt; = &lt;value&gt;</c>（值文法见 <see cref="UsharpValueParser"/>；字段序 = 声明序 = 规范形键序）。</para>
/// <para><c>Body</c> 为记录值；续行中无法解析的行原文落入 <see cref="UnparsedLines"/>（闸A 据其报码）。</para>
/// </summary>
public sealed record UsharpRecordDecl(
    string RecordId,
    string? Table,
    string? Key,
    UsharpRecord Body,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[Record]</c>（无名 ⇒ null）。</summary>
    public static UsharpRecordDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var table = UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldTable));
        var key = UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldKey));

        var unparsed = new List<string>();
        var body = UsharpValueParser.ParseRecordBlock(d.Continuation, unparsed);

        return new UsharpRecordDecl(d.Name, table, key, body, unparsed, d.SourceFile, d.LineNumber);
    }

    /// <summary>
    /// 渲染为规范形声明文本（写盘 / 对拍单源；<c>[Record] id: table=… | key=…</c> + 缩进字段块）。
    /// </summary>
    public string ToCanonical()
    {
        var header = "[Record] " + RecordId;
        var fields = new List<string>();
        if (!string.IsNullOrWhiteSpace(Table)) fields.Add(UsharpLanguage.FieldTable + "=" + Table);
        if (!string.IsNullOrWhiteSpace(Key)) fields.Add(UsharpLanguage.FieldKey + "=" + Key);
        if (fields.Count > 0) header += ": " + string.Join(" | ", fields);

        var sb = new System.Text.StringBuilder();
        sb.Append(header).Append('\n');
        foreach (var f in Body.Fields)
            sb.Append("  ").Append(f.Key).Append(" = ").Append(f.Value.ToCanonical()).Append('\n');
        return sb.ToString();
    }
}
