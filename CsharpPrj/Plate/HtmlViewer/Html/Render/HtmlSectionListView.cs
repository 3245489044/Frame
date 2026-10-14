#nullable enable

using System.Collections.Generic;
using System.Net;

namespace UiBackend.Html;

/// <summary>段列表项（<paramref name="Head"/> 标题行 + <paramref name="Body"/> 正文；均经本视图 HTML 编码）。</summary>
public readonly record struct HtmlSectionItem(string Head, string Body);

/// <summary>
/// 通用「带注记的段列表」HTML 构建器 —— 下沉自领域侧手写拼接的「注记 + 逐项 [头] 正文」片段：
/// <c>&lt;div class="{cls}"&gt;&lt;div class="{cls}-note"&gt;{note}&lt;/div&gt;{items…}&lt;/div&gt;</c>，
/// 每项 <c>&lt;div class="{cls}-item"&gt;&lt;div class="{cls}-head"&gt;{head}&lt;/div&gt;&lt;pre class="{cls}-body"&gt;{body}&lt;/pre&gt;&lt;/div&gt;</c>。
/// 纯函数、零宿主依赖：类名参数化，文本一律 <see cref="WebUtility.HtmlEncode"/>（XSS 防线）。
/// </summary>
public static class HtmlSectionListView
{
    /// <summary>构建段列表 HTML（<paramref name="note"/> 为 null/空 ⇒ 不渲染注记行）。</summary>
    public static string Render(string cssClass, string? note, IReadOnlyList<HtmlSectionItem>? items)
    {
        var cls = string.IsNullOrWhiteSpace(cssClass) ? "section-list" : cssClass;
        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"").Append(cls).Append("\">");
        if (!string.IsNullOrEmpty(note))
            sb.Append("<div class=\"").Append(cls).Append("-note\">")
              .Append(WebUtility.HtmlEncode(note)).Append("</div>");
        if (items is not null)
            foreach (var item in items)
                sb.Append("<div class=\"").Append(cls).Append("-item\"><div class=\"").Append(cls)
                  .Append("-head\">").Append(WebUtility.HtmlEncode(item.Head ?? string.Empty))
                  .Append("</div><pre class=\"").Append(cls).Append("-body\">")
                  .Append(WebUtility.HtmlEncode(item.Body ?? string.Empty))
                  .Append("</pre></div>");
        sb.Append("</div>");
        return sb.ToString();
    }
}
