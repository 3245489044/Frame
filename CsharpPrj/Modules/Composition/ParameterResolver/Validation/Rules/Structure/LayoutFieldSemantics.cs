namespace ParameterResolver.Validation.Rules.Structure;

/// <summary>布局数值字段的坐标系（同一对象承载多套语义；校验按字段查表，不按「所有数字都是比例」判）。</summary>
public enum LayoutFieldKind
{
    /// <summary>比例（0~1）。</summary>
    Ratio,

    /// <summary>DIP（96 dpi 参考像素，&gt;=0）。</summary>
    Dip,

    /// <summary>像素（&gt;=0）。</summary>
    Pixel,

    /// <summary>让出优先级（0=刚性，&gt;0 越大越先让出）。</summary>
    Priority,

    /// <summary>序号（&gt;=0）。</summary>
    Index,

    /// <summary>跨度（&gt;=1）。</summary>
    Span,
}

/// <summary>
/// 布局字段语义表（**单一来源**；与 <c>TConfig.Models.LayoutPosition</c> 的字段语义逐项对齐）。
/// 未登记字段 ⇒ 调用方报 unknown-layout-field，禁默认按比例校验。
/// </summary>
public static class LayoutFieldSemantics
{
    private static readonly Dictionary<string, LayoutFieldKind> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = LayoutFieldKind.Ratio,
        ["top"] = LayoutFieldKind.Ratio,
        ["width"] = LayoutFieldKind.Ratio,
        ["height"] = LayoutFieldKind.Ratio,
        ["right"] = LayoutFieldKind.Ratio,
        ["bottom"] = LayoutFieldKind.Ratio,
        ["x"] = LayoutFieldKind.Ratio,
        ["y"] = LayoutFieldKind.Ratio,
        ["minWidth"] = LayoutFieldKind.Dip,
        ["minHeight"] = LayoutFieldKind.Dip,
        ["maxWidth"] = LayoutFieldKind.Pixel,
        ["maxHeight"] = LayoutFieldKind.Pixel,
        ["shrinkPriority"] = LayoutFieldKind.Priority,
        ["row"] = LayoutFieldKind.Index,
        ["column"] = LayoutFieldKind.Index,
        ["rowSpan"] = LayoutFieldKind.Span,
        ["columnSpan"] = LayoutFieldKind.Span,
    };

    /// <summary>取字段语义（未登记 ⇒ false）。</summary>
    public static bool TryGet(string field, out LayoutFieldKind kind)
        => Fields.TryGetValue(field, out kind);

    /// <summary>已登记字段（诊断/门禁用）。</summary>
    public static IReadOnlyCollection<string> KnownFields => Fields.Keys;
}
