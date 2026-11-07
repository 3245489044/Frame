using System;

namespace Modules.Parsing.Markup;

/// <summary>
/// 折叠段写入（<c>&lt;details&gt;&lt;summary&gt;…&lt;/summary&gt;body&lt;/details&gt;</c>）—— 折叠壳的**唯一实现**。
/// <para>HTML 预置即浏览器原生 <c>details/summary</c>；其它方言经 <see cref="MarkupDialect"/> 声明替换仍可用同一壳结构。</para>
/// <para>纪律：<c>summaryHtml</c> 是**已渲染片段**（文本请先经 <see cref="MarkupTag.Text"/>）；本类只负责壳与属性顺序。</para>
/// </summary>
public static class MarkupDisclosure
{
    /// <summary>渲染折叠段。</summary>
    /// <param name="summaryHtml">摘要位已渲染片段。</param>
    /// <param name="bodyHtml">展开位已渲染片段；null ⇒ 无。</param>
    /// <param name="detailsClass">details 类；空/空白 ⇒ 回落 <paramref name="detailsFallback"/>（空串 ⇒ 不产 class）。</param>
    /// <param name="detailsFallback">details 类回落值。</param>
    /// <param name="summaryClass">summary 类；空/空白 ⇒ 回落 <paramref name="summaryFallback"/>（空串 ⇒ 不产 class）。</param>
    /// <param name="summaryFallback">summary 类回落值。</param>
    /// <param name="open">初始展开。</param>
    /// <param name="dataName">details 稳定名；null/空 ⇒ 不产。</param>
    /// <param name="summaryAttrs">摘要位追加属性（在 class 之后发射，保持既有顺序）。</param>
    /// <param name="dialect">方言；null ⇒ HTML 预置。</param>
    public static string Render(
        string summaryHtml,
        string? bodyHtml = null,
        string? detailsClass = null,
        string detailsFallback = "",
        string? summaryClass = null,
        string summaryFallback = "",
        bool open = false,
        string? dataName = null,
        Action<MarkupAttrBatch>? summaryAttrs = null,
        MarkupDialect? dialect = null)
    {
        var d = dialect ?? MarkupDialects.Html;

        var details = new MarkupAttrBatch(d);
        if (!string.IsNullOrEmpty(detailsFallback) || !string.IsNullOrWhiteSpace(detailsClass))
            details.Class(detailsClass, detailsFallback);
        details.Bool("open", open);
        details.Name(dataName);

        var summary = new MarkupAttrBatch(d);
        if (!string.IsNullOrEmpty(summaryFallback) || !string.IsNullOrWhiteSpace(summaryClass))
            summary.Class(summaryClass, summaryFallback);
        summaryAttrs?.Invoke(summary);

        var inner = MarkupTag.Element("summary", summary, UiMarkup.Trusted(summaryHtml), d) + (bodyHtml ?? string.Empty);
        return MarkupTag.Element("details", details, UiMarkup.Trusted(inner), d);
    }
}
