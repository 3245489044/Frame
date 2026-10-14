#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>详情字段（标签 + 值；均经 HtmlEncode）。</summary>
public sealed record HtmlDetailField(string Label, string? Value);

/// <summary>
/// 通用「详情卡」HTML 构建器 —— 下沉自领域侧手写拼接的「字段网格 + 分区注记 + 可编辑文本域 + 命令按钮」片段
/// （原 app 侧 <c>AppendNodeDetail</c>）。纯函数、零宿主依赖：类名/字段/编辑器由调用方提供，文本一律
/// <see cref="WebUtility.HtmlEncode"/>；命令按钮复用 <see cref="HtmlActionButton"/> 渲染（同 <see cref="HtmlToolbar"/> 纪律）。
/// </summary>
public static class HtmlDetailCard
{
    /// <summary>
    /// 渲染详情卡：<c>&lt;div class="{cls}-body"&gt;&lt;div class="{cls}-grid"&gt;字段…&lt;/div&gt;[&lt;div class="{cls}-field"&gt;…&lt;/div&gt;]
    /// [&lt;textarea …&gt;][命令按钮…]&lt;/div&gt;</c>。
    /// </summary>
    /// <param name="cssClass">类前缀（如 <c>db-node</c> → <c>db-node-body</c>/<c>db-node-grid</c>/<c>db-node-field</c>）。</param>
    /// <param name="fields">只读字段网格。</param>
    /// <param name="sectionNote">分区注记（可选）。</param>
    /// <param name="editorDataName">文本域 <c>data-name</c>（脚本定位/桥取值）；null/空 ⇒ 不渲染文本域。</param>
    /// <param name="editorValue">文本域初值（经 HtmlEncode）。</param>
    /// <param name="commands">命令按钮（复用 <see cref="HtmlActionButton"/>）。</param>
    /// <param name="editorClass">文本域样式类（如 <c>db-edit mono</c>）。</param>
    /// <param name="editorRows">文本域行数。</param>
    /// <param name="buttonClass">按钮样式类（如 <c>db-btn</c>）。</param>
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
        var cls = string.IsNullOrWhiteSpace(cssClass) ? "detail" : cssClass;
        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(cls).Append("-body\">");
        sb.Append("<div class=\"").Append(cls).Append("-grid\">");
        if (fields is not null)
            foreach (var f in fields)
                sb.Append("<span>").Append(WebUtility.HtmlEncode(f.Label ?? string.Empty)).Append("</span>")
                  .Append("<span>").Append(WebUtility.HtmlEncode(f.Value ?? string.Empty)).Append("</span>");
        sb.Append("</div>");
        if (!string.IsNullOrEmpty(sectionNote))
            sb.Append("<div class=\"").Append(cls).Append("-field\"><span>")
              .Append(WebUtility.HtmlEncode(sectionNote)).Append("</span></div>");
        if (!string.IsNullOrEmpty(editorDataName))
        {
            sb.Append("<textarea class=\"").Append(string.IsNullOrWhiteSpace(editorClass) ? "edit mono" : editorClass)
              .Append("\" rows=\"").Append(editorRows).Append("\" data-name=\"")
              .Append(WebUtility.HtmlEncode(editorDataName!)).Append("\">")
              .Append(WebUtility.HtmlEncode(editorValue ?? string.Empty)).Append("</textarea>");
        }
        if (commands is not null)
            foreach (var cmd in commands)
                HtmlToolbar.AppendButton(sb, cmd, string.IsNullOrWhiteSpace(buttonClass) ? "btn" : buttonClass);
        sb.Append("</div>");
        return sb.ToString();
    }
}
