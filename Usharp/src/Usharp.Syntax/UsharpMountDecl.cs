using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// <c>[Mount] &lt;mountId&gt;: &lt;显示名&gt; | relation=… | trigger=… | stage=… | order=… | family=… | coalesce=… | posture=… | projection=…</c> 语法视图。
/// <para>只装绑定事实：edge/src/tgt 一律来自被引用的 <c>[Relation]</c>，本声明禁止重复（闸A 判红）。</para>
/// </summary>
public sealed record UsharpMountDecl(
    string MountId,
    string? Display,
    string Relation,
    IReadOnlyList<string> Triggers,
    string Stage,
    int Order,
    string Family,
    string Coalesce,
    string Posture,
    string? Projection,
    IReadOnlyList<string> ForbiddenFields,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[Mount]</c>（无名 ⇒ null）。</summary>
    public static UsharpMountDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var forbidden = new List<string>();
        foreach (var field in new[] { "edge", "src", "tgt", "container", "scope" })
        {
            if (d.HasField(field)) forbidden.Add(field);
        }

        return new UsharpMountDecl(
            d.Name.Trim(),
            UsharpLineReader.NullIfEmpty(d.Body),
            (d.Field(UsharpLanguage.FieldRelation) ?? string.Empty).Trim(),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.FieldTrigger)),
            (d.Field(UsharpLanguage.FieldStage) ?? string.Empty).Trim(),
            ParseOrder(d.Field("order")),
            (d.Field("family") ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldCoalesce) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldPosture) ?? string.Empty).Trim(),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.FieldProjection)),
            forbidden,
            UsharpRelationDecl.Unparsed(d),
            d.SourceFile,
            d.LineNumber);
    }

    private static int ParseOrder(string? value)
        => int.TryParse((value ?? string.Empty).Trim(), out var order) ? order : 0;
}
