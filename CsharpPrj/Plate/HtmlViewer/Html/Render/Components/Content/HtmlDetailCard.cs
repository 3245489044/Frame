#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>详情字段（标签 + 值；均经编码）。</summary>
public sealed record HtmlDetailField(string Label, string? Value);

/// <summary>
/// 通用「详情卡」HTML 构建器 —— 「字段网格 + 分区注记 + 可编辑文本域 + 命令按钮」片段。
/// 纯函数、零宿主依赖：类名/字段/编辑器由调用方提供，文本一律经 <see cref="MarkupTag.Text"/>；
/// 命令按钮复用 <see cref="HtmlActionButton"/> 渲染（同 <see cref="HtmlToolbar"/> 纪律）。
/// </summary>
public static class HtmlDetailCard
{
    /// <summary>
    /// 渲染详情卡：<c>&lt;div class="{cls}-body"&gt;&lt;div class="{cls}-grid"&gt;字段…&lt;/div&gt;[&lt;div class="{cls}-field"&gt;…&lt;/div&gt;]
    /// [&lt;textarea …&gt;][命令按钮…]&lt;/div&gt;</c>。
    /// </summary>
    public static string Render(
        string cssClass,
        IReadOnlyList<HtmlDetailField>? fields,
        string? sectionNote,
        string? editorDataName,
        string? editorValue,
        IReadOnlyList<HtmlActionButton>? commands = null,
        string editorClass = "edit mono",
        int editorRows = 8,
        string buttonClass = "btn")
    {
        var cls = MarkupClass.Fallback(cssClass, "detail");
        var sb = new StringBuilder();

        var grid = new StringBuilder();
        if (fields is not null)
        {
            foreach (var f in fields)
            {
                grid.Append(MarkupTag.Element("span", null, UiMarkup.Trusted(MarkupTag.Text(f.Label))));
                grid.Append(MarkupTag.Element("span", null, UiMarkup.Trusted(MarkupTag.Text(f.Value))));
            }
        }

        sb.Append(MarkupTag.Element(
            "div",
            new MarkupAttrBatch().Class(cls + "-grid", cls + "-grid"),
            UiMarkup.Trusted(grid.ToString())));

        if (!string.IsNullOrEmpty(sectionNote))
            sb.Append(MarkupTag.Element(
                "div",
                new MarkupAttrBatch().Class(cls + "-field", cls + "-field"),
                UiMarkup.Trusted(MarkupTag.Element("span", null, UiMarkup.Trusted(MarkupTag.Text(sectionNote))))));

        if (!string.IsNullOrEmpty(editorDataName))
        {
            var attrs = new MarkupAttrBatch()
                .Class(editorClass, "edit mono")
                .Add("rows", editorRows.ToString(CultureInfo.InvariantCulture))
                .Name(editorDataName);
            sb.Append(MarkupTag.Element("textarea", attrs, UiMarkup.Trusted(MarkupTag.Text(editorValue))));
        }

        if (commands is not null)
        {
            var buttonCss = TextUtil.CssClass(buttonClass, "btn");
            foreach (var cmd in commands) HtmlToolbar.AppendButton(sb, cmd, buttonCss);
        }

        return MarkupTag.Element(
            "div",
            new MarkupAttrBatch().Class(cls + "-body", cls + "-body"),
            UiMarkup.Trusted(sb.ToString()));
    }
}

