using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// <c>[ReadModel] &lt;modelId&gt;: source=… | version=… | ttl=… | projector=… | args=…</c> 语法视图。
/// <para>只装声明事实：版本源/投影器名/参数名表由闸A 校验，消费方（生成器）按此发射 <c>readModels</c> 段。</para>
/// </summary>
public sealed record UsharpReadModelDecl(
    string ModelId,
    IReadOnlyList<string> Sources,
    string Version,
    int? TtlMs,
    string? Projector,
    IReadOnlyList<string> Args,
    IReadOnlyList<string> Fields,
    bool FieldsDeclared,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[ReadModel]</c>（无名 ⇒ null）。</summary>
    public static UsharpReadModelDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var unparsed = new List<string>();
        foreach (var raw in d.Continuation)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length > 0) unparsed.Add(line);
        }

        int? ttl = null;
        if (int.TryParse(d.Field(UsharpLanguage.ReadModelFieldTtl), out var parsed) && parsed > 0)
            ttl = parsed;

        var rawFields = UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.ReadModelFieldFields));

        return new UsharpReadModelDecl(
            d.Name.Trim(),
            SplitSources(d.Field(UsharpLanguage.ReadModelFieldSource)),
            (d.Field(UsharpLanguage.ReadModelFieldVersion) ?? string.Empty).Trim(),
            ttl,
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.ReadModelFieldProjector)),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.ReadModelFieldArgs)),
            UsharpLineReader.SplitList(rawFields),
            rawFields is not null,
            unparsed,
            d.SourceFile,
            d.LineNumber);
    }

    /// <summary>源键列表：逗号或 <c>+</c> 分隔（两种书写均接受，归一去空白）。</summary>
    private static IReadOnlyList<string> SplitSources(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
