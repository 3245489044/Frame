#nullable enable

using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>工具条项（按钮 / 输入框）抽象基；渲染顺序由列表顺序决定。</summary>
public abstract record HtmlToolbarItem;

/// <summary>工具条按钮（<c>data-cmd</c> + 可选 <c>data-name</c> / 附加 <c>data-*</c> / <c>data-input</c> 读取源）。</summary>
/// <param name="Text">按钮文案（本组件 HtmlEncode）。</param>
/// <param name="Cmd">点击桥命令（<c>data-cmd</c>；代码常量，无特殊字符）。</param>
/// <param name="Name">脚本定位名（<c>data-name</c>）；null ⇒ 不渲染。</param>
/// <param name="Title">悬浮提示（<c>title</c>，本组件 HtmlEncode）；null ⇒ 不渲染。</param>
/// <param name="Data">附加 <c>data-*</c> 属性（键→值，值经 HtmlEncode）。</param>
/// <param name="InputName">同页输入值读取源（<c>data-input</c>）；null ⇒ 不渲染。</param>
public sealed record HtmlActionButton(
    string Text,
    string Cmd,
    string? Name = null,
    string? Title = null,
    IReadOnlyDictionary<string, string>? Data = null,
    string? InputName = null) : HtmlToolbarItem;

/// <summary>工具条输入框（<c>data-name</c> + 占位 + 初值 + 样式类）。</summary>
public sealed record HtmlActionInput(
    string DataName,
    string? Placeholder = null,
    string? Value = null,
    string CssClass = "toolbar-input",
    string Type = "text") : HtmlToolbarItem;

/// <summary>
/// 通用工具条（前导文本 + 顺序动作按钮/输入框）HTML 构建器 —— 下沉自领域侧手写拼接的 <c>data-cmd</c> 工具条
/// （原 app 侧 <c>RenderDbToolbar</c> + <c>AppendDbButton(Session)</c>）。纯函数、零宿主依赖：类名参数化，
/// 文本一律 <see cref="WebUtility.HtmlEncode"/>；按钮/输入框顺序即列表顺序（保留原位混排）。
/// </summary>
public static class HtmlToolbar
{
    /// <summary>
    /// 渲染工具条：<c>&lt;div class="{cssClass}"&gt;{leading}&lt;span class="{actionsClass}"&gt;items…&lt;/span&gt;&lt;/div&gt;</c>。
    /// </summary>
    /// <param name="cssClass">工具条容器类（如 <c>db-toolbar</c>）。</param>
    /// <param name="leadingText">前导文本（本组件 HtmlEncode）；null/空 ⇒ 不渲染。</param>
    /// <param name="items">顺序项（按钮 / 输入框混排）。</param>
    /// <param name="actionsClass">动作区 span 类（如 <c>db-actions</c>）。</param>
    /// <param name="buttonClass">按钮样式类（如 <c>db-btn</c>）。</param>
    public static string Render(
        string cssClass,
        string? leadingText,
        IReadOnlyList<HtmlToolbarItem>? items,
        string actionsClass = "toolbar-actions",
        string buttonClass = "toolbar-btn")
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(Safe(cssClass, "toolbar")).Append("\">");
        if (!string.IsNullOrEmpty(leadingText)) sb.Append(WebUtility.HtmlEncode(leadingText));
        sb.Append("<span class=\"").Append(Safe(actionsClass, "toolbar-actions")).Append("\">");
        if (items is not null)
            foreach (var item in items)
            {
                switch (item)
                {
                    case HtmlActionButton b: AppendButton(sb, b, Safe(buttonClass, "toolbar-btn")); break;
                    case HtmlActionInput i: AppendInput(sb, i); break;
                }
            }
        sb.Append("</span></div>");
        return sb.ToString();
    }

    /// <summary>渲染单个按钮（供详情卡等复用；<paramref name="buttonClass"/> 为样式类）。</summary>
    internal static void AppendButton(StringBuilder sb, HtmlActionButton b, string buttonClass)
    {
        sb.Append("<button type=\"button\" class=\"").Append(buttonClass).Append("\" data-cmd=\"")
          .Append(WebUtility.HtmlEncode(b.Cmd ?? string.Empty)).Append('"');
        if (!string.IsNullOrEmpty(b.Name))
            sb.Append(" data-name=\"").Append(WebUtility.HtmlEncode(b.Name!)).Append('"');
        if (b.Data is not null)
            foreach (var (k, v) in b.Data)
                if (!string.IsNullOrWhiteSpace(k))
                    sb.Append(" data-").Append(k).Append("=\"").Append(WebUtility.HtmlEncode(v ?? string.Empty)).Append('"');
        if (!string.IsNullOrEmpty(b.InputName))
            sb.Append(" data-input=\"").Append(WebUtility.HtmlEncode(b.InputName!)).Append('"');
        if (!string.IsNullOrEmpty(b.Title))
            sb.Append(" title=\"").Append(WebUtility.HtmlEncode(b.Title!)).Append('"');
        sb.Append('>').Append(WebUtility.HtmlEncode(b.Text ?? string.Empty)).Append("</button>");
    }

    private static void AppendInput(StringBuilder sb, HtmlActionInput i)
    {
        sb.Append("<input type=\"").Append(Safe(i.Type, "text")).Append("\" class=\"")
          .Append(Safe(i.CssClass, "toolbar-input")).Append("\" data-name=\"")
          .Append(WebUtility.HtmlEncode(i.DataName ?? string.Empty)).Append('"');
        if (!string.IsNullOrEmpty(i.Placeholder))
            sb.Append(" placeholder=\"").Append(WebUtility.HtmlEncode(i.Placeholder!)).Append('"');
        if (i.Value is not null)
            sb.Append(" value=\"").Append(WebUtility.HtmlEncode(i.Value)).Append('"');
        sb.Append("/>");
    }

    private static string Safe(string? cssClass, string fallback)
        => string.IsNullOrWhiteSpace(cssClass) ? fallback : cssClass!;
}
