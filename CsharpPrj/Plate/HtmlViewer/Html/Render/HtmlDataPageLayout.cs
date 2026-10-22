#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

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

/// <summary>数据页区域容器类名参数（默认值与既有数据页一致）。</summary>
public sealed record HtmlDataPageLayoutOptions(
    string ToolbarClass = "toolbar",
    string ActionsClass = "toolbar-actions",
    string ButtonClass = "btn",
    string ResultPanelClass = "callout",
    string ResultPreClass = "mono",
    string PanelClass = "data-view")
{
    /// <summary>默认选项（无状态）。</summary>
    public static HtmlDataPageLayoutOptions Default { get; } = new();
}

/// <summary>
/// 通用数据页区域装配器 —— 固定「工具条 → 筛选条 → 选择条 → 结果横幅 → 分组行/空态 → 页脚分页」。
/// 纯函数、零领域类型；文本由下游组件编码。
/// </summary>
public static class HtmlDataPageLayout
{
    /// <summary>渲染区域化数据页（整块面板）。</summary>
    public static string Render(
        IReadOnlyDictionary<string, string> templates,
        HtmlDataPageLayoutModel model,
        HtmlDataPageLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var o = options ?? HtmlDataPageLayoutOptions.Default;

        var body = new StringBuilder();
        body.Append(HtmlToolbar.RenderGroups(
            o.ToolbarClass, model.LeadingText, model.Toolbar, o.ActionsClass, o.ButtonClass));
        body.Append(HtmlFilterBar.Render(model.FilterBar, "filter-bar", o.ButtonClass));
        body.Append(HtmlSelectionBar.Render(model.SelectionBar, "selection-bar", o.ButtonClass));

        if (!string.IsNullOrWhiteSpace(model.ResultText))
            body.Append(HtmlBlocks.Panel(o.ResultPanelClass, HtmlBlocks.Pre(o.ResultPreClass, model.ResultText)));

        if (model.Groups is not { Count: > 0 })
        {
            if (model.EmptyText is not null)
                body.Append(HtmlRenderFaces.Empty(templates, model.EmptyText));
        }
        else
        {
            body.Append(HtmlGroupedRowList.Render(model.Groups));
        }

        body.Append(HtmlPager.Render(model.Pager, "pager", o.ButtonClass));
        return HtmlBlocks.Panel(o.PanelClass, body.ToString());
    }
}
