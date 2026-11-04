#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 页脚分页模型：前导动作（首页/上一页）+ 页码文本 + 后续动作（下一页/末页）+ 跳转输入与按钮。
/// </summary>
/// <param name="Page">当前页码（0 基；仅诊断属性）。</param>
/// <param name="PageCount">总页数（仅诊断属性）。</param>
/// <param name="PageText">页码文本（如 <c>第 1/1 页</c>；本组件 HtmlEncode）。</param>
/// <param name="LeadingActions">页码文本之前的动作（首页/上一页）。</param>
/// <param name="TrailingActions">页码文本之后的动作（下一页/末页）。</param>
/// <param name="GotoAction">跳转按钮（读 <paramref name="GotoInputName"/>）；null ⇒ 不渲染跳转区。</param>
/// <param name="GotoInputName">跳转输入的 <c>data-name</c>。</param>
/// <param name="GotoPlaceholder">跳转输入占位文本。</param>
public sealed record HtmlPagerModel(
    int Page,
    int PageCount,
    string PageText,
    IReadOnlyList<HtmlActionDescriptor> LeadingActions,
    IReadOnlyList<HtmlActionDescriptor> TrailingActions,
    HtmlActionDescriptor? GotoAction = null,
    string? GotoInputName = "dbPageInput",
    string? GotoPlaceholder = "页码");

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
        sb.Append("<div class=\"").Append(Safe(cssClass, "pager"))
          .Append("\" role=\"navigation\" aria-label=\"分页\" data-page=\"")
          .Append(model.Page).Append("\" data-page-count=\"").Append(model.PageCount).Append("\">");

        sb.Append("<span class=\"pager-group pager-leading\">");
        AppendActions(sb, model.LeadingActions, buttonClass);
        sb.Append("</span>");

        sb.Append("<span class=\"pager-info\">")
          .Append(WebUtility.HtmlEncode(model.PageText ?? string.Empty)).Append("</span>");

        sb.Append("<span class=\"pager-group pager-trailing\">");
        AppendActions(sb, model.TrailingActions, buttonClass);
        sb.Append("</span>");

        if (model.GotoAction is not null)
        {
            sb.Append("<span class=\"pager-goto\">");
            if (!string.IsNullOrEmpty(model.GotoInputName))
                HtmlToolbar.AppendInput(sb, new HtmlActionInput(
                    model.GotoInputName!,
                    Placeholder: model.GotoPlaceholder,
                    CssClass: "input input-page",
                    AriaLabel: "页码"));
            HtmlToolbar.AppendDescriptor(sb, model.GotoAction, buttonClass);
            sb.Append("</span>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private static void AppendActions(
        StringBuilder sb, IReadOnlyList<HtmlActionDescriptor>? actions, string buttonClass)
    {
        if (actions is null) return;
        foreach (var action in actions)
            if (action is not null) HtmlToolbar.AppendDescriptor(sb, action, buttonClass);
    }

    private static string Safe(string? cssClass, string fallback) => TextUtil.CssClass(cssClass, fallback);
}
