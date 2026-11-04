using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// <c>[Product] &lt;productId&gt;: level=… | role=… | in=… | out=… | idempotency=…</c> 语法视图。
/// <para>只装声明事实（产品键 / 层 / 角色 / 字段契约 / 幂等键 / 面接受表）；层存在性、字段闭合、
/// 幂等键结构由闸A 校验，消费方（发射器 / 声明源）按此产出 <c>DeclaredGraph</c>。</para>
/// </summary>
public sealed record UsharpProductDecl(
    string ProductId,
    string LevelId,
    string Role,
    IReadOnlyList<string> InFields,
    IReadOnlyList<string> OutFields,
    string IdempotencyKey,
    int Version,
    string? Stage,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Accepts,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[Product]</c>（无名 ⇒ null）。</summary>
    public static UsharpProductDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var unparsed = new List<string>();
        foreach (var raw in d.Continuation)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length > 0) unparsed.Add(line);
        }

        var version = 1;
        if (int.TryParse(d.Field(UsharpLanguage.ProductFieldVersion), out var parsed) && parsed > 0)
            version = parsed;

        return new UsharpProductDecl(
            d.Name.Trim(),
            (d.Field(UsharpLanguage.ProductFieldLevel) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.ProductFieldRole) ?? string.Empty).Trim(),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.ProductFieldIn)),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.ProductFieldOut)),
            (d.Field(UsharpLanguage.ProductFieldIdempotency) ?? string.Empty).Trim(),
            version,
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.ProductFieldStage)),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.ProductFieldDependsOn)),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.ProductFieldAccepts)),
            unparsed,
            d.SourceFile,
            d.LineNumber);
    }
}

/// <summary>
/// <c>[Face] &lt;faceId&gt;: product=… | owner=… | channel=… | fields=…</c> 语法视图。
/// <para>面 = 产品的**肯定式可见性白名单**（owner/channel 一致即渲染，禁按 Role 二次猜测）；
/// 产品引用存在性与字段子集由闸A 校验。</para>
/// </summary>
public sealed record UsharpFaceDecl(
    string FaceId,
    string ProductId,
    IReadOnlyList<string> Owners,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> Fields,
    bool FieldsDeclared,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[Face]</c>（无名 ⇒ null）。</summary>
    public static UsharpFaceDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var unparsed = new List<string>();
        foreach (var raw in d.Continuation)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length > 0) unparsed.Add(line);
        }

        var rawFields = UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.FaceFieldFields));

        return new UsharpFaceDecl(
            d.Name.Trim(),
            (d.Field(UsharpLanguage.FaceFieldProduct) ?? string.Empty).Trim(),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.FaceFieldOwner)),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.FaceFieldChannel)),
            UsharpLineReader.SplitList(rawFields),
            rawFields is not null,
            unparsed,
            d.SourceFile,
            d.LineNumber);
    }
}