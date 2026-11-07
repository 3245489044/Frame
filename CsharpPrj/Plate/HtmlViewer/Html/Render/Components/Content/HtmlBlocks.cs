#nullable enable

using System.Text;
using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>
/// 通用基础片段块构建器 —— 容器 / 预格式文本 / 注记面板三类基础 HTML 片段。
/// 纯函数、零宿主依赖：类名参数化，文本一律 <see cref="MarkupTag.Text"/>（XSS 防线）。
/// 实现单源 = <see cref="MarkupTag"/> + <see cref="MarkupAttrBatch"/>。
/// </summary>
public static class HtmlBlocks
{
    /// <summary>渲染容器块：<c>&lt;div class="{cls}"&gt;{inner}&lt;/div&gt;</c>（<paramref name="innerHtml"/> 为已渲染片段，不再转义）。</summary>
    public static string Panel(string cssClass, string? innerHtml)
        => MarkupBlock.Plain("div", cssClass, "panel", innerHtml ?? string.Empty);

    /// <summary>渲染预格式文本块：<c>&lt;pre class="{cls}"&gt;{text}&lt;/pre&gt;</c>（<paramref name="text"/> 经编码）。</summary>
    public static string Pre(string cssClass, string? text)
        => MarkupTag.Element(
            "pre",
            new MarkupAttrBatch().Class(cssClass, "mono"),
            UiMarkup.Trusted(MarkupTag.Text(text)));

    /// <summary>
    /// 渲染注记面板：<c>&lt;div class="{cls}"&gt;&lt;div&gt;{head}&lt;/div&gt;&lt;pre class="{bodyClass}"&gt;{body}&lt;/pre&gt;[&lt;div&gt;{note}&lt;/div&gt;]&lt;/div&gt;</c>。
    /// <paramref name="note"/> 为 null/空 ⇒ 不渲染注记行；文本一律经编码。
    /// </summary>
    public static string Notice(string cssClass, string head, string? body, string? note = null, string bodyClass = "mono")
    {
        var sb = new StringBuilder();
        sb.Append(MarkupTag.Element("div", null, UiMarkup.Trusted(MarkupTag.Text(head))));
        sb.Append(MarkupTag.Element(
            "pre",
            new MarkupAttrBatch().Class(bodyClass, "mono"),
            UiMarkup.Trusted(MarkupTag.Text(body))));
        if (!string.IsNullOrEmpty(note))
            sb.Append(MarkupTag.Element("div", null, UiMarkup.Trusted(MarkupTag.Text(note))));
        return MarkupTag.Element(
            "div",
            new MarkupAttrBatch().Class(cssClass, "notice"),
            UiMarkup.Trusted(sb.ToString()));
    }
}

