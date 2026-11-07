#nullable enable

using System.Collections.Generic;
using System.Text;
using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>
/// 筛选/排序条模型（输入框 + 有序动作组）。筛选条与主工具条分区，避免输入与分页混排。
/// </summary>
/// <param name="InputName">过滤输入框的 <c>data-name</c>；null/空 ⇒ 不渲染输入框。</param>
/// <param name="Placeholder">输入占位文本（本组件编码）。</param>
/// <param name="Value">输入初值（本组件编码）。</param>
/// <param name="Groups">有序动作组（如 filter / sort），组间自动分隔。</param>
/// <param name="InputLabel">输入框无障碍标签（<c>aria-label</c>）。</param>
/// <param name="AriaLabel">筛选条区域标签（<c>aria-label</c>；默认「筛选与排序」）。</param>
public sealed record HtmlFilterBarModel(
    string? InputName,
    string? Placeholder,
    string? Value,
    IReadOnlyList<HtmlToolbarGroup> Groups,
    string InputLabel = "筛选",
    string AriaLabel = "筛选与排序");

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
        if (!string.IsNullOrEmpty(model.InputLabel))
            sb.Append(MarkupTag.Element(
                "span",
                new MarkupAttrBatch().Class("filter-label", "filter-label"),
                UiMarkup.Trusted(MarkupTag.Text(model.InputLabel))));
        if (!string.IsNullOrEmpty(model.InputName))
            HtmlToolbar.AppendInput(sb, new HtmlActionInput(
                model.InputName!,
                Placeholder: model.Placeholder,
                Value: model.Value,
                CssClass: "input",
                AriaLabel: model.InputLabel));
        HtmlToolbar.AppendGroupSequence(sb, model.Groups, buttonClass);

        var attrs = new MarkupAttrBatch()
            .Class(cssClass, "filter-bar")
            .Add("role", "group")
            .AddIfAny("aria-label", model.AriaLabel);
        return MarkupTag.Element("div", attrs, UiMarkup.Trusted(sb.ToString()));
    }
}

