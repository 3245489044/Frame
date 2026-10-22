#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 筛选/排序条模型（输入框 + 有序动作组）。筛选条与主工具条分区，避免输入与分页混排。
/// </summary>
/// <param name="InputName">过滤输入框的 <c>data-name</c>；null/空 ⇒ 不渲染输入框。</param>
/// <param name="Placeholder">输入占位文本（本组件 HtmlEncode）。</param>
/// <param name="Value">输入初值（本组件 HtmlEncode）。</param>
/// <param name="Groups">有序动作组（如 filter / sort），组间自动分隔。</param>
/// <param name="InputLabel">输入框无障碍标签（<c>aria-label</c>）。</param>
public sealed record HtmlFilterBarModel(
    string? InputName,
    string? Placeholder,
    string? Value,
    IReadOnlyList<HtmlToolbarGroup> Groups,
    string InputLabel = "筛选");

/// <summary>通用筛选/排序条构建器（区域语义：输入 + 过滤/排序动作）。</summary>
public static class HtmlFilterBar
{
    /// <summary>渲染筛选条；模型为 null ⇒ 空串。</summary>
    public static string Render(
        HtmlFilterBarModel? model,
        string cssClass = "filter-bar",
        string buttonClass = "btn")
    {
        if (model is null) return string.Empty;

        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(Safe(cssClass, "filter-bar"))
          .Append("\" role=\"group\" aria-label=\"").Append(WebUtility.HtmlEncode("筛选与排序")).Append("\">");
        if (!string.IsNullOrEmpty(model.InputLabel))
            sb.Append("<span class=\"filter-label\">").Append(WebUtility.HtmlEncode(model.InputLabel)).Append("</span>");
        if (!string.IsNullOrEmpty(model.InputName))
            HtmlToolbar.AppendInput(sb, new HtmlActionInput(
                model.InputName!,
                Placeholder: model.Placeholder,
                Value: model.Value,
                CssClass: "input",
                AriaLabel: model.InputLabel));
        HtmlToolbar.AppendGroupSequence(sb, model.Groups, buttonClass);
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Safe(string? cssClass, string fallback)
        => string.IsNullOrWhiteSpace(cssClass) ? fallback : cssClass!;
}
