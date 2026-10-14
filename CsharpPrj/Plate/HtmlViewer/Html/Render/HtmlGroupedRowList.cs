#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>可折叠分组（组标题 + 计数文本 + 已渲染行片段列表）。</summary>
/// <param name="Label">组显示标签（本组件 HtmlEncode 后入 DOM）。</param>
/// <param name="CountText">计数文本（含括号，如 <c>（3/10）</c>；本组件 HtmlEncode）。null/空 ⇒ 不渲染。</param>
/// <param name="RowsHtml">组内行片段（已渲染，不再转义）。</param>
/// <param name="Open">初始是否展开（<c>&lt;details open&gt;</c>）。</param>
public sealed record HtmlRowGroup(
    string Label,
    string? CountText,
    IReadOnlyList<string> RowsHtml,
    bool Open = true);

/// <summary>
/// 通用「可折叠分组行列表」HTML 构建器 —— 把 <c>&lt;details&gt;&lt;summary&gt;组标题&lt;/summary&gt;行…&lt;/details&gt;</c>
/// 的分组壳下沉为纯函数组件（行片段由调用方经 <see cref="HtmlDataRowView"/> 等构建）。
/// 零宿主依赖：类名/计数文本/组标题由调用方提供，文本一律 <see cref="WebUtility.HtmlEncode"/>。
/// [下沉自 Agent.Launcher ConversationViewService.RenderNodePanelHtml 的分组手写拼接，已通用化]
/// </summary>
public static class HtmlGroupedRowList
{
    /// <summary>渲染分组行列表（无外层容器；调用方按需用 <see cref="HtmlBlocks.Panel"/> 包裹）。</summary>
    public static string Render(IReadOnlyList<HtmlRowGroup>? groups)
    {
        if (groups is null || groups.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var g in groups)
        {
            if (g is null) continue;
            sb.Append("<details");
            if (g.Open) sb.Append(" open");
            sb.Append("><summary>").Append(WebUtility.HtmlEncode(g.Label ?? string.Empty));
            if (!string.IsNullOrEmpty(g.CountText)) sb.Append(WebUtility.HtmlEncode(g.CountText));
            sb.Append("</summary>");
            if (g.RowsHtml is not null)
                foreach (var row in g.RowsHtml)
                    sb.Append(row);
            sb.Append("</details>");
        }
        return sb.ToString();
    }
}
