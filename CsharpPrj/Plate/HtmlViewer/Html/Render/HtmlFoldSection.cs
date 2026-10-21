#nullable enable

using System.Net;

namespace UiBackend.Html;

/// <summary>
/// 可折叠段落（通用件）—— 统一 <c>&lt;details&gt;</c> 结构的类名与稳定键，供各渲染器复用。
/// <para>刷新页面的展开态恢复依赖稳定键：调用方必须给出 <paramref name="dataName"/>（写入 <c>data-name</c>）。</para>
/// </summary>
public static class HtmlFoldSection
{
    /// <summary>渲染折叠段：<paramref name="summary"/> 经编码；<paramref name="innerHtml"/> 为已渲染片段。</summary>
    public static string Render(
        string summary,
        string? innerHtml,
        string? cssClass = null,
        string? dataName = null)
    {
        var cls = string.IsNullOrWhiteSpace(cssClass) ? "fold" : cssClass!;
        var open = string.IsNullOrWhiteSpace(dataName) ? string.Empty : " data-name=\"" + WebUtility.HtmlEncode(dataName) + "\"";
        return "<details class=\"" + WebUtility.HtmlEncode(cls) + "\"" + open + "><summary>"
               + WebUtility.HtmlEncode(summary) + "</summary>"
               + (innerHtml ?? string.Empty)
               + "</details>";
    }
}
