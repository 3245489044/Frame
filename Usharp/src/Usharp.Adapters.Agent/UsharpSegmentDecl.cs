using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Adapters.Agent;

/// <summary>
/// <c>[Segment] key: group=… | order=… | when=… | requirement=… | allow_empty=… | aliases=… | toggle=… | provider=…</c>
/// 的语法视图（提示词段声明面）。
/// <para>
/// 段键/组/序/适用性/需求量/可空/别名/开关的<b>唯一声明点</b>；契约判定、前置门、快照、
/// 必填端口派生全部由本声明派生，禁止在流程里手写第二份（对齐 <c>ax-StructSingleDeclaration</c>）。
/// </para>
/// <para>
/// <c>expand=band</c> 表示「L0 分段族模板行」（<c>prompt.l0.*</c>）：由档案 bands 展开为逐段声明，
/// 声明面不穷举动态段集合。
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
    string SourceFile,
    int LineNumber)
{
    /// <summary>由 <c>[Segment]</c> 声明投影（无名 → null，由闸A 报出）。</summary>
    public static UsharpSegmentDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var band = ParseInt(d.Field("band"), -1);
        var order = ParseInt(d.Field("order"), band >= 0 ? band : -1);

        return new UsharpSegmentDecl(
            d.Name.Trim(),
            UsharpLineReader.NullIfEmpty(d.Field("group")) ?? UsharpLineReader.NullIfEmpty(d.Field("target")),
            order,
            UsharpLineReader.NullIfEmpty(d.Field("when")),
            UsharpLineReader.NullIfEmpty(d.Field("requirement")),
            IsOn(d.Field("allow_empty")),
            SplitList(d.Field("aliases")),
            UsharpLineReader.NullIfEmpty(d.Field("toggle")),
            UsharpLineReader.NullIfEmpty(d.Field("provider")),
            band,
            string.Equals(UsharpLineReader.NullIfEmpty(d.Field("expand")), "band", StringComparison.OrdinalIgnoreCase),
            d.SourceFile,
            d.LineNumber);
    }

    private static bool IsOn(string? value)
    {
        var v = UsharpLineReader.NullIfEmpty(value);
        return string.Equals(v, "on", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "1", StringComparison.Ordinal);
    }

    /// <summary>数值字段（未声明/非法 ⇒ <paramref name="fallback"/>）。</summary>
    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value?.Trim(), out var n) ? n : fallback;

    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
