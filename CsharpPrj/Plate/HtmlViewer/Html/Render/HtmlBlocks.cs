#nullable enable

using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 通用基础片段块构建器 —— 把领域侧手写拼接的「容器 / 预格式文本 / 注记面板」三类基础 HTML 片段下沉为纯函数组件。
/// 零宿主依赖：类名参数化，文本一律 <see cref="WebUtility.HtmlEncode"/>（XSS 防线）。
/// [下沉自 Agent.Launcher ConversationViewService 的 empty/mono/就绪矩阵手写拼接，已通用化]
/// </summary>
public static class HtmlBlocks
{
    /// <summary>渲染容器块：<c>&lt;div class="{cls}"&gt;{inner}&lt;/div&gt;</c>（<paramref name="innerHtml"/> 为已渲染片段，不再转义）。</summary>
    public static string Panel(string cssClass, string? innerHtml)
        => "<div class=\"" + Safe(cssClass, "panel") + "\">" + (innerHtml ?? string.Empty) + "</div>";

    /// <summary>渲染预格式文本块：<c>&lt;pre class="{cls}"&gt;{text}&lt;/pre&gt;</c>（<paramref name="text"/> 经 HtmlEncode）。</summary>
    public static string Pre(string cssClass, string? text)
        => "<pre class=\"" + Safe(cssClass, "mono") + "\">" + WebUtility.HtmlEncode(text ?? string.Empty) + "</pre>";

    /// <summary>
    /// 渲染注记面板：<c>&lt;div class="{cls}"&gt;&lt;div&gt;{head}&lt;/div&gt;&lt;pre class="{bodyClass}"&gt;{body}&lt;/pre&gt;[&lt;div&gt;{note}&lt;/div&gt;]&lt;/div&gt;</c>。
    /// <paramref name="note"/> 为 null/空 ⇒ 不渲染注记行；文本一律经 HtmlEncode。
    /// </summary>
    public static string Notice(string cssClass, string head, string? body, string? note = null, string bodyClass = "mono")
    {
        var cls = Safe(cssClass, "notice");
        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(cls).Append("\">");
        sb.Append("<div>").Append(WebUtility.HtmlEncode(head ?? string.Empty)).Append("</div>");
        sb.Append("<pre class=\"").Append(Safe(bodyClass, "mono")).Append("\">")
          .Append(WebUtility.HtmlEncode(body ?? string.Empty)).Append("</pre>");
        if (!string.IsNullOrEmpty(note))
            sb.Append("<div>").Append(WebUtility.HtmlEncode(note)).Append("</div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Safe(string? cssClass, string fallback)
        => string.IsNullOrWhiteSpace(cssClass) ? fallback : cssClass!;
}
