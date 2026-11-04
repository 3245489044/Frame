#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 数据页区域模型：Toolbar / FilterBar / SelectionBar / ResultBanner / Table / Pager。
/// <para>区域顺序固定；领域只填数据，不拼区域结构。</para>
/// </summary>
public sealed record HtmlDataPageLayoutModel(
    string? LeadingText,
    HtmlToolbarModel? Toolbar,
    HtmlFilterBarModel? FilterBar,
    HtmlSelectionBarModel? SelectionBar,
    string? ResultText,
    IReadOnlyList<HtmlRowGroup>? Groups,
    string? EmptyText,
    HtmlPagerModel? Pager);

/// <summary>
/// 通用数据页区域装配器 —— 固定「工具条 → 筛选条 → 选择条 → 结果横幅 → 分组行/空态 → 页脚分页」。
/// 纯函数、零领域类型；组合体单源 = <see cref="HtmlDataPageComposer"/>，类名参数单源 = <see cref="HtmlDataPageOptions"/>。
/// </summary>
public static class HtmlDataPageLayout
{
    /// <summary>渲染区域化数据页（整块面板）。</summary>
    public static string Render(
        IReadOnlyDictionary<string, string> templates,
        HtmlDataPageLayoutModel model,
        HtmlDataPageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var o = options ?? HtmlDataPageOptions.Default;

        var toolbar = HtmlToolbar.RenderGroups(
            o.ToolbarClass, model.LeadingText, model.Toolbar, o.ActionsClass, o.ButtonClass);
        var filter = HtmlFilterBar.Render(model.FilterBar, o.FilterBarClass, o.ButtonClass);
        var selection = HtmlSelectionBar.Render(model.SelectionBar, o.SelectionBarClass, o.ButtonClass);
        var pager = HtmlPager.Render(model.Pager, o.PagerClass, o.ButtonClass);

        return HtmlDataPageComposer.Render(
            templates,
            toolbar,
            filter,
            selection,
            model.ResultText,
            model.Groups,
            model.EmptyText,
            pager,
            o);
    }
}

