#nullable enable

using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>
/// 可折叠段落（通用件）—— 统一 <c>&lt;details&gt;</c> 结构的类名与稳定键，供各渲染器复用。
/// <para>刷新页面的展开态恢复依赖稳定键：调用方必须给出 <paramref name="dataName"/>（写入 <c>data-name</c>）。</para>
/// <para>实现单源 = <see cref="MarkupDisclosure"/>（属性顺序/编码/闭合统一）。</para>
/// </summary>
public static class HtmlFoldSection
{
    /// <summary>渲染折叠段：<paramref name="summary"/> 经编码；<paramref name="innerHtml"/> 为已渲染片段。</summary>
    public static string Render(
        string summary,
        string? innerHtml,
        string? cssClass = null,
        string? dataName = null)
        => MarkupDisclosure.Render(
            MarkupTag.Text(summary),
            innerHtml,
            detailsClass: cssClass,
            detailsFallback: "fold",
            dataName: dataName);
}
