#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>行内单元格（样式类 + 文本；文本经 HtmlEncode）。</summary>
public sealed record HtmlDataCell(string CssClass, string? Text);

/// <summary>交互元素属性袋（<c>data-cmd</c> + 可选 <c>data-name</c> + 附加 <c>data-*</c>）。</summary>
public sealed record HtmlInteractiveAttrs(
    string Cmd,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// 行首复选框：<c>&lt;input type="checkbox" data-key="{Key}"[ checked]/&gt;</c>；
/// 带 <paramref name="Cmd"/> 时包一层带交互属性的 <c>&lt;label class="{LabelClass}"&gt;</c>（交互行），否则裸 input（只读行）。
/// <paramref name="Domain"/>/<paramref name="SourceKey"/> 分别落 <c>data-type</c>（行来源域）/ <c>data-source</c>（真键回写）。
/// </summary>
public sealed record HtmlRowCheckbox(
    string Key,
    bool Checked = false,
    string? Cmd = null,
    string? Name = null,
    string? Session = null,
    string? Domain = null,
    string? SourceKey = null,
    IReadOnlyDictionary<string, string>? ExtraData = null,
    string LabelClass = "row-check");

/// <summary>可折叠数据行模型（复选 + 单元格 [+ 可折叠摘要/详情]）。</summary>
/// <param name="Cells">行内单元格。</param>
/// <param name="RowClass">行容器类（如 <c>data-row</c>）。</param>
/// <param name="Check">行首复选框；null ⇒ 无。</param>
/// <param name="Expandable">true ⇒ 单元格包进 <c>&lt;details&gt;&lt;summary&gt;</c>（交互行）；false ⇒ 单元格直接入行（只读行）。</param>
/// <param name="Open">可折叠时初始展开。</param>
/// <param name="SummaryAttrs">摘要点击属性（<c>data-cmd</c> 等）；null ⇒ 无。</param>
/// <param name="DetailsClass">details 类（如 <c>data-item</c>）。</param>
/// <param name="SummaryClass">summary 类（如 <c>data-summary</c>）。</param>
/// <param name="DetailHtml">展开详情片段（已渲染）；null/空 ⇒ 无。</param>
public sealed record HtmlDataRow(
    IReadOnlyList<HtmlDataCell> Cells,
    string RowClass = "row",
    HtmlRowCheckbox? Check = null,
    bool Expandable = false,
    bool Open = false,
    HtmlInteractiveAttrs? SummaryAttrs = null,
    string DetailsClass = "detail",
    string SummaryClass = "detail-summary",
    string? DetailHtml = null);

/// <summary>
/// 通用数据行 HTML 构建器 —— 下沉自领域侧手写拼接的「行 = 复选 + 单元格 (+ 可折叠详情)」片段
/// （原 app 侧 <c>RenderNodePanelHtml</c> 行分支）。纯函数、零宿主依赖：类名/单元格由调用方提供，
/// 文本与属性值一律 <see cref="WebUtility.HtmlEncode"/>（XSS 防线）。
/// </summary>
public static class HtmlDataRowView
{
    /// <summary>渲染单行。</summary>
    public static string Render(HtmlDataRow row)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(Safe(row.RowClass, "row")).Append("\">");
        if (row.Check is { } chk) AppendCheckbox(sb, chk);
        if (row.Expandable)
        {
            sb.Append("<details class=\"").Append(Safe(row.DetailsClass, "node")).Append('"');
            if (row.Open) sb.Append(" open");
            sb.Append("><summary class=\"").Append(Safe(row.SummaryClass, "node-sum")).Append('"');
            AppendAttrs(sb, row.SummaryAttrs);
            sb.Append('>');
            AppendCells(sb, row.Cells);
            sb.Append("</summary>");
            if (!string.IsNullOrEmpty(row.DetailHtml)) sb.Append(row.DetailHtml);
            sb.Append("</details>");
        }
        else
        {
            AppendCells(sb, row.Cells);
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static void AppendCheckbox(StringBuilder sb, HtmlRowCheckbox c)
    {
        if (string.IsNullOrEmpty(c.Cmd))
        {
            sb.Append("<input type=\"checkbox\" data-key=\"").Append(WebUtility.HtmlEncode(c.Key ?? string.Empty)).Append('"');
            if (c.Checked) sb.Append(" checked");
            sb.Append("/>");
            return;
        }

        sb.Append("<label class=\"").Append(Safe(c.LabelClass, "row-check")).Append("\" data-cmd=\"")
          .Append(WebUtility.HtmlEncode(c.Cmd!)).Append('"');
        if (!string.IsNullOrEmpty(c.Session))
            sb.Append(" data-session=\"").Append(WebUtility.HtmlEncode(c.Session!)).Append('"');
        if (c.Domain is not null)
            sb.Append(" data-type=\"").Append(WebUtility.HtmlEncode(c.Domain)).Append('"');
        if (c.SourceKey is not null)
            sb.Append(" data-source=\"").Append(WebUtility.HtmlEncode(c.SourceKey)).Append('"');
        if (!string.IsNullOrEmpty(c.Name))
            sb.Append(" data-name=\"").Append(WebUtility.HtmlEncode(c.Name!)).Append('"');
        if (c.ExtraData is not null)
            foreach (var (k, v) in c.ExtraData)
                if (!string.IsNullOrWhiteSpace(k))
                    sb.Append(" data-").Append(k).Append("=\"").Append(WebUtility.HtmlEncode(v ?? string.Empty)).Append('"');
        sb.Append("><input type=\"checkbox\" data-key=\"").Append(WebUtility.HtmlEncode(c.Key ?? string.Empty)).Append('"');
        if (c.Checked) sb.Append(" checked");
        sb.Append("/></label>");
    }

    private static void AppendAttrs(StringBuilder sb, HtmlInteractiveAttrs? attrs)
    {
        if (attrs is null) return;
        sb.Append(" data-cmd=\"").Append(WebUtility.HtmlEncode(attrs.Cmd ?? string.Empty)).Append('"');
        if (!string.IsNullOrEmpty(attrs.Name))
            sb.Append(" data-name=\"").Append(WebUtility.HtmlEncode(attrs.Name!)).Append('"');
        if (attrs.Data is not null)
            foreach (var (k, v) in attrs.Data)
                if (!string.IsNullOrWhiteSpace(k))
                    sb.Append(" data-").Append(k).Append("=\"").Append(WebUtility.HtmlEncode(v ?? string.Empty)).Append('"');
    }

    private static void AppendCells(StringBuilder sb, IReadOnlyList<HtmlDataCell>? cells)
    {
        if (cells is null) return;
        foreach (var cell in cells)
        {
            sb.Append("<span class=\"").Append(Safe(cell.CssClass, "cell")).Append("\">")
              .Append(WebUtility.HtmlEncode(cell.Text ?? string.Empty)).Append("</span>");
        }
    }

    private static string Safe(string? cssClass, string fallback)
        => string.IsNullOrWhiteSpace(cssClass) ? fallback : cssClass!;
}
