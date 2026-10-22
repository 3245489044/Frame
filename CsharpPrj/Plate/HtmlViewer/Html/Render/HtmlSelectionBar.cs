#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 选择条模型：摘要 + 有序动作组（选择组 / danger 组）。
/// <para>交互页常驻渲染（0 选择时危险动作由调用方置 disabled），保证“全选”入口不消失。</para>
/// </summary>
public sealed record HtmlSelectionBarModel(
    int Count,
    string Summary,
    IReadOnlyList<HtmlToolbarGroup> Groups);

/// <summary>通用选择条构建器。</summary>
public static class HtmlSelectionBar
{
    /// <summary>渲染选择条；模型为 null ⇒ 空串。</summary>
    public static string Render(
        HtmlSelectionBarModel? model,
        string cssClass = "selection-bar",
        string buttonClass = "btn")
    {
        if (model is null) return string.Empty;

        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(Safe(cssClass, "selection-bar"))
          .Append("\" role=\"group\" aria-label=\"选择\" data-selection-count=\"")
          .Append(model.Count).Append("\">");
        sb.Append("<span class=\"selection-summary\">")
          .Append(WebUtility.HtmlEncode(model.Summary ?? string.Empty)).Append("</span>");
        HtmlToolbar.AppendGroupSequence(sb, model.Groups, buttonClass);
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Safe(string? cssClass, string fallback)
        => string.IsNullOrWhiteSpace(cssClass) ? fallback : cssClass!;
}
