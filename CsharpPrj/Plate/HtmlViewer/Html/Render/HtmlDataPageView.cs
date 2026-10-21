#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace UiBackend.Html;

/// <summary>
/// 通用数据页装配（工具条 → 结果横幅 → 分组行列表 → 面板容器）——「数据页」组合件。
/// <para>
/// 只负责**拼装顺序与容器**：过滤/排序/分页运算见 <see cref="DataPagePlan"/>，行片段由调用方
/// 经 <see cref="HtmlDataRowView"/> 等构建。文本一律由下游组件编码，本类零领域类型。
/// </para>
/// </summary>
/// <param name="LeadingText">工具条前导文本（null/空 ⇒ 不渲染）。</param>
/// <param name="ToolbarItems">工具条项（顺序渲染）。</param>
/// <param name="ResultText">结果横幅正文（null/空 ⇒ 不渲染横幅）。</param>
/// <param name="Groups">分组行（已渲染行片段）；空 ⇒ 走 <paramref name="EmptyText"/> 或仅工具条。</param>
/// <param name="EmptyText">空态文案；null ⇒ 分组为空时不渲染空态（显式「空 = 空」，不伪造）。</param>
public sealed record HtmlDataPageModel(
    string? LeadingText,
    IReadOnlyList<HtmlToolbarItem>? ToolbarItems,
    string? ResultText,
    IReadOnlyList<HtmlRowGroup>? Groups,
    string? EmptyText);

/// <summary>数据页容器类名参数（默认值与既有数据页输出一致）。</summary>
public sealed record HtmlDataPageOptions(
    string ToolbarClass = "toolbar",
    string ActionsClass = "toolbar-actions",
    string ButtonClass = "btn",
    string ResultPanelClass = "callout",
    string ResultPreClass = "mono",
    string PanelClass = "data-view")
{
    /// <summary>默认选项（单例；无状态）。</summary>
    public static HtmlDataPageOptions Default { get; } = new();
}

/// <summary>
/// 通用数据页 HTML 构建器 —— 下沉自领域侧 <c>RenderNodePanelHtml</c> 的工具条/结果/空态/分组/面板拼装骨架。
/// 纯函数、零宿主依赖；结构固定为「工具条 + 可选结果横幅 + 空态或分组列表」，容器由参数注入。
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

        var body = new StringBuilder();
        body.Append(HtmlToolbar.Render(
            o.ToolbarClass, model.LeadingText, model.ToolbarItems, o.ActionsClass, o.ButtonClass));

        if (!string.IsNullOrWhiteSpace(model.ResultText))
            body.Append(HtmlBlocks.Panel(o.ResultPanelClass, HtmlBlocks.Pre(o.ResultPreClass, model.ResultText)));

        if (model.Groups is not { Count: > 0 })
        {
            // 空态是否可见由调用方声明（null = 不可见）：源分组为空与「过滤后为空」语义不同，不可混判。
            if (model.EmptyText is not null)
                body.Append(HtmlRenderFaces.Empty(templates, model.EmptyText));
            return HtmlBlocks.Panel(o.PanelClass, body.ToString());
        }

        body.Append(HtmlGroupedRowList.Render(model.Groups));
        return HtmlBlocks.Panel(o.PanelClass, body.ToString());
    }
}
