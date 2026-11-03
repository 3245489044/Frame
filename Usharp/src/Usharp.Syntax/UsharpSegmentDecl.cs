using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// <c>[Segment] key: group=… | order=… | when=… | requirement=… | allow_empty=… | aliases=… | toggle=… | provider=… | field=… | type=…</c>
/// 的语法视图（提示词段声明面）。
/// <para>
/// 段键/组/序/适用性/需求量/可空/别名/开关/<b>DTO 字段名/字段类型</b>的唯一声明点；契约判定、前置门、
/// 快照、必填端口派生、读模型字段表全部由本声明派生，禁止在流程里手写第二份（对齐 <c>Strategy.SingleImplementation</c>）。
/// </para>
/// <para>
/// <c>expand=band</c> 表示「L0 分段族模板行」（<c>prompt.l0.*</c>）：由档案 bands 展开为逐段声明，
/// 声明面不穷举动态段集合。
/// </para>
/// <para>
/// 归属 = 语法层（与 <c>[ReadModel]</c>/<c>[Command]</c> 同层）：任何一次编译（含 DeclaredBundle 单点编译）
/// 都能读到本 facet，`[ReadModel] fields=` 的跨声明闭合因此可判。
/// </para>
/// </summary>
public sealed record UsharpSegmentDecl(
    string Key,
    string? Group,
    int Order,
    string? When,
    string? Requirement,
    bool AllowEmpty,
    IReadOnlyList<string> Aliases,
    string? Toggle,
    string? Provider,
    int Band,
    bool ExpandBands,
    string? Field,
    string? Type,
    string SourceFile,
    int LineNumber)
{
    /// <summary>由 <c>[Segment]</c> 声明投影（无名 → null，由闸A 报出）。</summary>
    public static UsharpSegmentDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var band = ParseInt(d.Field(UsharpLanguage.SegmentFieldBand), -1);
        var order = ParseInt(d.Field(UsharpLanguage.SegmentFieldOrder), band >= 0 ? band : -1);

        return new UsharpSegmentDecl(
            d.Name.Trim(),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldGroup))
                ?? UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldTarget)),
            order,
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldWhen)),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldRequirement)),
            IsOn(d.Field(UsharpLanguage.SegmentFieldAllowEmpty)),
            SplitList(d.Field(UsharpLanguage.SegmentFieldAliases)),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldToggle)),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldProvider)),
            band,
            string.Equals(UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldExpand)),
                UsharpLanguage.SegmentExpandBands, StringComparison.OrdinalIgnoreCase),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldField)),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.SegmentFieldType)),
            d.SourceFile,
            d.LineNumber);
    }

    private static bool IsOn(string? value)
        => UsharpLineReader.TryBool(value, UsharpBoolLexicon.OnOff, out var parsed) && parsed;

    /// <summary>数值字段（未声明/非法 ⇒ <paramref name="fallback"/>）。</summary>
    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value?.Trim(), out var n) ? n : fallback;

    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
