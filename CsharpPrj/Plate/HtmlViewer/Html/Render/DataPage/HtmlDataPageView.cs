#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 通用数据页装配（工具条 → 结果横幅 → 分组行列表 → 面板容器）——「数据页」组合件。
/// <para>只负责**拼装顺序与容器**：过滤/排序/分页运算见 <see cref="DataPagePlan"/>，行片段由调用方
/// 经 <see cref="HtmlDataRowView"/> 等构建。文本一律由下游组件编码，本类零领域类型。</para>
/// </summary>
public sealed record HtmlDataPageModel(
    string? LeadingText,
    IReadOnlyList<HtmlToolbarItem>? ToolbarItems,
    string? ResultText,
    IReadOnlyList<HtmlRowGroup>? Groups,
    string? EmptyText);

/// <summary>数据页容器类名参数（单一 options，供 <see cref="HtmlDataPageView"/> 与 <see cref="HtmlDataPageLayout"/> 共用）。</summary>
public sealed record HtmlDataPageOptions(
    string ToolbarClass = "toolbar",
    string ActionsClass = "toolbar-actions",
    string ButtonClass = "btn",
    string ResultPanelClass = "callout",
    string ResultPreClass = "mono",
    string PanelClass = "data-view",
    string FilterBarClass = "filter-bar",
    string SelectionBarClass = "selection-bar",
    string PagerClass = "pager")
{
    /// <summary>默认选项（单例；无状态）。</summary>
    public static HtmlDataPageOptions Default { get; } = new();
}

/// <summary>
/// 数据页组合体（唯一实现）—— 两个公开入口（<see cref="HtmlDataPageView"/> / <see cref="HtmlDataPageLayout"/>）
/// 只做模型适配，装配顺序只此一份。
/// </summary>
internal static class HtmlDataPageComposer
{
    public static string Render(
        IReadOnlyDictionary<string, string> templates,
        string toolbarHtml,
        string filterHtml,
        string selectionHtml,
        string? resultText,
        IReadOnlyList<HtmlRowGroup>? groups,
        string? emptyText,
        string pagerHtml,
        HtmlDataPageOptions o)
    {
        var body = new StringBuilder();
        body.Append(toolbarHtml);
        body.Append(filterHtml);
        body.Append(selectionHtml);

        if (!string.IsNullOrWhiteSpace(resultText))
            body.Append(HtmlBlocks.Panel(o.ResultPanelClass, HtmlBlocks.Pre(o.ResultPreClass, resultText)));

        if (groups is not { Count: > 0 })
        {
            // 空态可见性由调用方声明（null/空 = 不可见）：源分组为空与「过滤后为空」语义不同，不可混判。
            if (!string.IsNullOrEmpty(emptyText))
                body.Append(HtmlRenderFaces.Empty(templates, emptyText!));
        }
        else
        {
            body.Append(HtmlGroupedRowList.Render(groups));
        }

        body.Append(pagerHtml);
        return HtmlBlocks.Panel(o.PanelClass, body.ToString());
    }
}

/// <summary>
/// 通用数据页 HTML 构建器 —— 工具条/结果/空态/分组/面板拼装骨架（非交互入口）。
/// </summary>
public static class HtmlDataPageView
{
    /// <summary>渲染数据页（整块面板）。</summary>
    public static string Render(
        IReadOnlyDictionary<string, string> templates,
        HtmlDataPageModel model,
        HtmlDataPageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var o = options ?? HtmlDataPageOptions.Default;

        var toolbar = HtmlToolbar.Render(
            o.ToolbarClass, model.LeadingText, model.ToolbarItems, o.ActionsClass, o.ButtonClass);

        return HtmlDataPageComposer.Render(
            templates,
            toolbar,
            filterHtml: string.Empty,
            selectionHtml: string.Empty,
            model.ResultText,
            model.Groups,
            model.EmptyText,
            pagerHtml: string.Empty,
            o);
    }
}

