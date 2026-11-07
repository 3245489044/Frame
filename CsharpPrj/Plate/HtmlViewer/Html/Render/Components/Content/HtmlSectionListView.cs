#nullable enable

using System.Collections.Generic;
using System.Text;
using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>段列表项（<paramref name="Head"/> 标题行 + <paramref name="Body"/> 正文；均经编码）。</summary>
public readonly record struct HtmlSectionItem(string Head, string Body);

/// <summary>
/// 通用「带注记的段列表」HTML 构建器 —— <c>&lt;div class="{cls}"&gt;&lt;div class="{cls}-note"&gt;{note}&lt;/div&gt;{items…}&lt;/div&gt;</c>，
/// 每项 <c>&lt;div class="{cls}-item"&gt;&lt;div class="{cls}-head"&gt;{head}&lt;/div&gt;&lt;pre class="{cls}-body"&gt;{body}&lt;/pre&gt;&lt;/div&gt;</c>。
/// 纯函数、零宿主依赖：类名参数化，文本一律经 <see cref="MarkupTag.Text"/>（XSS 防线）。
/// </summary>
public static class HtmlSectionListView
{
    /// <summary>构建段列表 HTML（<paramref name="note"/> 为 null/空 ⇒ 不渲染注记行）。</summary>
    public static string Render(string cssClass, string? note, IReadOnlyList<HtmlSectionItem>? items)
    {
        var cls = MarkupClass.Fallback(cssClass, "section-list");
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(note))
            sb.Append(MarkupTag.Element(
                "div",
                new MarkupAttrBatch().Class(MarkupClass.Suffix(cls, "-note", "section-list"), "section-list-note"),
                UiMarkup.Trusted(MarkupTag.Text(note))));

        if (items is not null)
        {
            foreach (var item in items)
            {
                var inner = MarkupTag.Element(
                                   "div",
                                   new MarkupAttrBatch().Class(MarkupClass.Suffix(cls, "-head", "section-list"), "section-list-head"),
                                   UiMarkup.Trusted(MarkupTag.Text(item.Head)))
                               + MarkupTag.Element(
                                   "pre",
                                   new MarkupAttrBatch().Class(MarkupClass.Suffix(cls, "-body", "section-list"), "section-list-body"),
                                   UiMarkup.Trusted(MarkupTag.Text(item.Body)));
                sb.Append(MarkupTag.Element(
                    "div",
                    new MarkupAttrBatch().Class(MarkupClass.Suffix(cls, "-item", "section-list"), "section-list-item"),
                    UiMarkup.Trusted(inner)));
            }
        }

        return MarkupTag.Element("div", new MarkupAttrBatch().Class(cls, "section-list"), UiMarkup.Trusted(sb.ToString()));
    }
}

