#nullable enable

using System.Collections.Generic;
using System.Text;
using Modules.Parsing.Markup;

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
/// <paramref name="Session"/> = 桥命令键（**写身份第二段 = 真键**）；<paramref name="Domain"/> 落 <c>data-type</c>（**写身份第一段 = 写域**）；
/// <paramref name="SourceKey"/> 落 <c>data-source</c>，**非桥身份**（仅供脚本读取的诊断属性，点击桥不回投 source）。
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
        var inner = new StringBuilder();
        if (row.Check is { } chk) inner.Append(RenderCheckbox(chk));
        if (row.Expandable)
        {
            var cells = new StringBuilder();
            AppendCells(cells, row.Cells);
            inner.Append(MarkupDisclosure.Render(
                cells.ToString(),
                string.IsNullOrEmpty(row.DetailHtml) ? null : row.DetailHtml,
                detailsClass: row.DetailsClass,
                detailsFallback: "node",
                summaryClass: row.SummaryClass,
                summaryFallback: "node-sum",
                open: row.Open,
                summaryAttrs: attrs => AppendAttrs(attrs, row.SummaryAttrs)));
        }
        else
        {
            AppendCells(inner, row.Cells);
        }

        return MarkupTag.Element("div", new MarkupAttrBatch().Class(row.RowClass, "row"), UiMarkup.Trusted(inner.ToString()));
    }

    private static string RenderCheckbox(HtmlRowCheckbox c)
    {
        var input = MarkupTag.Void("input", new MarkupAttrBatch()
            .Add("type", "checkbox")
            .Add("data-key", c.Key ?? string.Empty)
            .Bool("checked", c.Checked));

        if (string.IsNullOrEmpty(c.Cmd)) return input;

        var label = new MarkupAttrBatch()
            .Class(c.LabelClass, "row-check")
            .Add("data-cmd", c.Cmd!)
            .AddIfAny("data-session", c.Session)
            .Add("data-type", c.Domain)
            .Add("data-source", c.SourceKey)
            .Name(c.Name)
            .DataAll(c.ExtraData);

        return MarkupTag.Element("label", label, UiMarkup.Trusted(input));
    }

    private static void AppendAttrs(MarkupAttrBatch attrs, HtmlInteractiveAttrs? source)
    {
        if (source is null) return;
        attrs.Add("data-cmd", source.Cmd ?? string.Empty)
             .Name(source.Name)
             .DataAll(source.Data);
    }

    private static void AppendCells(StringBuilder sb, IReadOnlyList<HtmlDataCell>? cells)
    {
        if (cells is null) return;
        foreach (var cell in cells)
        {
            sb.Append(MarkupTag.Element(
                "span",
                new MarkupAttrBatch().Class(cell.CssClass, "cell"),
                UiMarkup.Trusted(MarkupTag.Text(cell.Text))));
        }
    }
}
