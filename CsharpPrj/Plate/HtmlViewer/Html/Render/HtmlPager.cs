#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>
/// 页脚分页模型：前导动作（首页/上一页）+ 页码文本 + 后续动作（下一页/末页）+ 跳转输入与按钮。
/// </summary>
public sealed record HtmlPagerModel(
    int Page,
    int PageCount,
    string PageText,
    IReadOnlyList<HtmlActionDescriptor> LeadingActions,
    IReadOnlyList<HtmlActionDescriptor> TrailingActions,
    HtmlActionDescriptor? GotoAction = null,
    string? GotoInputName = "dbPageInput",
    string? GotoPlaceholder = "页码",
    string AriaLabel = "分页",
    string GotoAriaLabel = "页码");

/// <summary>通用页脚分页构建器（分页固定页脚，不与筛选/删除混排）。</summary>
public static class HtmlPager
{
    /// <summary>渲染分页条；模型为 null ⇒ 空串。</summary>
    public static string Render(
        HtmlPagerModel? model,
        string cssClass = "pager",
        string buttonClass = "btn")
    {
        if (model is null) return string.Empty;

        var sb = new StringBuilder();

        var leading = new StringBuilder();
        AppendActions(leading, model.LeadingActions, buttonClass);
        sb.Append(MarkupTag.Element(
            "span",
            new MarkupAttrBatch().Class("pager-group pager-leading", "pager-leading"),
            leading.ToString()));

        sb.Append(MarkupTag.Element(
            "span",
            new MarkupAttrBatch().Class("pager-info", "pager-info"),
            MarkupTag.Text(model.PageText)));

        var trailing = new StringBuilder();
        AppendActions(trailing, model.TrailingActions, buttonClass);
        sb.Append(MarkupTag.Element(
            "span",
            new MarkupAttrBatch().Class("pager-group pager-trailing", "pager-trailing"),
            trailing.ToString()));

        if (model.GotoAction is not null)
        {
            var gotoInner = new StringBuilder();
            if (!string.IsNullOrEmpty(model.GotoInputName))
                HtmlToolbar.AppendInput(gotoInner, new HtmlActionInput(
                    model.GotoInputName!,
                    Placeholder: model.GotoPlaceholder,
                    CssClass: "input input-page",
                    AriaLabel: model.GotoAriaLabel));
            HtmlToolbar.AppendDescriptor(gotoInner, model.GotoAction, buttonClass);
            sb.Append(MarkupTag.Element(
                "span",
                new MarkupAttrBatch().Class("pager-goto", "pager-goto"),
                gotoInner.ToString()));
        }

        var attrs = new MarkupAttrBatch()
            .Class(cssClass, "pager")
            .Add("role", "navigation")
            .AddIfAny("aria-label", model.AriaLabel)
            .Add("data-page", model.Page.ToString(CultureInfo.InvariantCulture))
            .Add("data-page-count", model.PageCount.ToString(CultureInfo.InvariantCulture));
        return MarkupTag.Element("div", attrs, sb.ToString());
    }

    private static void AppendActions(
        StringBuilder sb, IReadOnlyList<HtmlActionDescriptor>? actions, string buttonClass)
    {
        if (actions is null) return;
        foreach (var action in actions)
            if (action is not null) HtmlToolbar.AppendDescriptor(sb, action, buttonClass);
    }
}

