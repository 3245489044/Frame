using System;

namespace Modules.Parsing.Markup;

/// <summary>
/// 块写入（常见形态的薄封装：类属性 + 稳定名）—— 属性顺序固定为 <c>class</c> → <c>data-name</c>，
/// 与既有渲染产物逐字对齐；需要别的顺序/额外属性时直接用 <see cref="MarkupAttrBatch"/> + <see cref="MarkupTag"/>。
/// </summary>
public static class MarkupBlock
{
    /// <summary>类 + 稳定名的块：<c>&lt;tag class="{cls}"[ data-name="{name}"]&gt;inner&lt;/tag&gt;</c>。</summary>
    /// <param name="tag">标签名。</param>
    /// <param name="cssClass">类名；空/空白 ⇒ 回落 <paramref name="fallbackClass"/>。</param>
    /// <param name="fallbackClass">类名回落值。</param>
    /// <param name="dataName">稳定名；null/空 ⇒ 不产属性。</param>
    /// <param name="inner">已渲染内容片段。</param>
    /// <param name="dialect">方言；null ⇒ HTML 预置。</param>
    public static string Render(
        string tag,
        string? cssClass,
        string fallbackClass,
        string? dataName,
        string? inner,
        MarkupDialect? dialect = null)
    {
        var d = dialect ?? MarkupDialects.Html;
        var attrs = new MarkupAttrBatch(d).Class(cssClass, fallbackClass).Name(dataName);
        return MarkupTag.Element(tag, attrs, inner, d);
    }

    /// <summary>仅类名的块（无稳定名）。</summary>
    public static string Plain(
        string tag,
        string? cssClass,
        string fallbackClass,
        string? inner,
        MarkupDialect? dialect = null)
        => Render(tag, cssClass, fallbackClass, dataName: null, inner, dialect);
}
