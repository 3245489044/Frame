using System;

namespace Modules.Parsing.Markup;

/// <summary>
/// 标签写入（**引号与闭合的唯一出口**）—— 元素 / 空元素 / 文本三种形态各恰一份实现。
/// <para>纪律：内容位（<c>inner</c>）是**已渲染片段**，本类不再转义；需要编码的文本经 <see cref="Text"/>。</para>
/// </summary>
public static class MarkupTag
{
    /// <summary>元素：<c>&lt;tag attrs&gt;inner&lt;/tag&gt;</c>。</summary>
    /// <param name="tag">标签名（按方言文法校验）。</param>
    /// <param name="attrs">属性批（null ⇒ 无属性）。</param>
    /// <param name="inner">已渲染内容片段。</param>
    /// <param name="dialect">方言；null ⇒ <paramref name="attrs"/> 的方言或 HTML 预置。</param>
    [Obsolete("内容位请改用 UiMarkup 重载（空内容用 UiMarkup.Empty）", error: false)]
    public static string Element(string tag, MarkupAttrBatch? attrs, string? inner, MarkupDialect? dialect = null)
    {
        var d = dialect ?? attrs?.Dialect ?? MarkupDialects.Html;
        d.EnsureTagName(tag);
        return "<" + tag + (attrs?.Render() ?? string.Empty) + ">" + (inner ?? string.Empty) + "</" + tag + ">";
    }

    /// <summary>空元素：<c>&lt;tag attrs/&gt;</c>（闭合形态由方言声明）。</summary>
    public static string Void(string tag, MarkupAttrBatch? attrs, MarkupDialect? dialect = null)
    {
        var d = dialect ?? attrs?.Dialect ?? MarkupDialects.Html;
        d.EnsureTagName(tag);
        var close = d.VoidStyle == MarkupVoidStyle.Slash ? "/>" : ">";
        return "<" + tag + (attrs?.Render() ?? string.Empty) + close;
    }

    /// <summary>开始标签：<c>&lt;tag attrs&gt;</c>。</summary>
    public static string Open(string tag, MarkupAttrBatch? attrs, MarkupDialect? dialect = null)
    {
        var d = dialect ?? attrs?.Dialect ?? MarkupDialects.Html;
        d.EnsureTagName(tag);
        return "<" + tag + (attrs?.Render() ?? string.Empty) + ">";
    }

    /// <summary>结束标签：<c>&lt;/tag&gt;</c>。</summary>
    public static string Close(string tag, MarkupDialect? dialect = null)
    {
        (dialect ?? MarkupDialects.Html).EnsureTagName(tag);
        return "</" + tag + ">";
    }

    /// <summary>文本编码（元素内容位）。**只接受未编码文本**（<see cref="UiText"/>；string 隐式转入）。
    /// 已编码片段不得走本方法（否则二次转义）。</summary>
    public static string Text(UiText text, MarkupDialect? dialect = null)
        => (dialect ?? MarkupDialects.Html).EncodeText(text.Value);

    /// <summary>属性串（null ⇒ 空串）。</summary>
    public static string Attrs(MarkupAttrBatch? attrs) => attrs?.Render() ?? string.Empty;
}
