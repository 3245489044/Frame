#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace UiBackend.Html;

/// <summary>工具条项（按钮 / 输入框）抽象基；渲染顺序由列表顺序决定。</summary>
public abstract record HtmlToolbarItem;

/// <summary>动作视觉权重（只影响样式与语义，不改变命令）。</summary>
public enum HtmlActionVariant
{
    /// <summary>普通次操作。</summary>
    Default = 0,
    /// <summary>页面主操作。</summary>
    Primary = 1,
    /// <summary>低权重动作（分页/清过滤）。</summary>
    Ghost = 2,
    /// <summary>破坏性操作（独立分组/二次确认）。</summary>
    Danger = 3,
}

/// <summary>动作所在槽位（由页面声明，渲染器不做测量）。</summary>
public enum HtmlToolbarSlot
{
    /// <summary>主工具条可见。</summary>
    Primary = 0,
    /// <summary>「更多」菜单。</summary>
    More = 1,
    /// <summary>页脚（分页）。</summary>
    Footer = 2,
    /// <summary>选择条。</summary>
    SelectionBar = 3,
}

/// <summary>
/// 动作身份/分组/样式/优先级描述 —— 领域只描述「有什么动作」，框架决定「怎么排」。
/// <para><c>Name</c> 保留旧 <c>data-name</c>（脚本定位稳定）；<c>Id</c> 仅增强定位与测试。</para>
/// </summary>
public sealed record HtmlActionDescriptor(
    string Id,
    string Text,
    string Cmd,
    string Group,
    HtmlActionVariant Variant = HtmlActionVariant.Default,
    HtmlToolbarSlot Slot = HtmlToolbarSlot.Primary,
    int Order = 100,
    bool Toggle = false,
    bool Pressed = false,
    bool Destructive = false,
    bool RequiresSelection = false,
    bool Enabled = true,
    string? Name = null,
    string? Title = null,
    string? AriaLabel = null,
    IReadOnlyDictionary<string, string>? Data = null,
    string? InputName = null);

/// <summary>逻辑动作组（<paramref name="Order"/> 决定组序；<paramref name="SeparatedBefore"/> 强化危险组隔离）。</summary>
public sealed record HtmlToolbarGroup(
    string Id,
    string? Label,
    IReadOnlyList<HtmlActionDescriptor> Items,
    int Order = 100,
    bool SeparatedBefore = false);

/// <summary>工具条模型：主区组 + 「更多」组（同一动作只渲染一次）。</summary>
public sealed record HtmlToolbarModel(
    IReadOnlyList<HtmlToolbarGroup> Groups,
    IReadOnlyList<HtmlToolbarGroup>? MoreGroups = null);

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
    string Type = "text",
    string? AriaLabel = null) : HtmlToolbarItem;

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

    /// <summary>
    /// 渲染分组工具条：<c>&lt;div class="{cssClass}"&gt;前导 + 动作组 + 更多&lt;/div&gt;</c>。
    /// <para>组序/组内序由 <c>Order</c> 决定；同一动作（Name ?? Id）只渲染一次。</para>
    /// </summary>
    public static string RenderGroups(
        string cssClass,
        string? leadingText,
        HtmlToolbarModel? model,
        string actionsClass = "toolbar-actions",
        string buttonClass = "toolbar-btn")
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(Safe(cssClass, "toolbar")).Append("\">");
        if (!string.IsNullOrEmpty(leadingText))
            sb.Append("<span class=\"toolbar-leading\">").Append(WebUtility.HtmlEncode(leadingText)).Append("</span>");
        sb.Append("<div class=\"").Append(Safe(actionsClass, "toolbar-actions")).Append("\">");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        AppendGroupSequence(sb, model?.Groups, buttonClass, seen);
        if (model?.MoreGroups is { Count: > 0 })
        {
            sb.Append("<span class=\"toolbar-sep toolbar-sep-strong\" role=\"separator\"></span>");
            sb.Append("<details class=\"toolbar-more\" data-group=\"more\">")
              .Append("<summary class=\"toolbar-more-summary\" aria-haspopup=\"menu\">更多</summary>")
              .Append("<span class=\"toolbar-more-panel\">");
            AppendGroupSequence(sb, model.MoreGroups, buttonClass, seen);
            sb.Append("</span></details>");
        }

        sb.Append("</div></div>");
        return sb.ToString();
    }

    /// <summary>渲染有序动作组序列（供工具条/选择条/筛选条共用；组间自动分隔）。</summary>
    internal static void AppendGroupSequence(
        StringBuilder sb,
        IReadOnlyList<HtmlToolbarGroup>? groups,
        string buttonClass,
        HashSet<string>? seen = null)
    {
        if (groups is null || groups.Count == 0) return;
        var rendered = 0;
        foreach (var group in groups.Where(static g => g is not null).OrderBy(static g => g.Order))
        {
            var items = group.Items?
                .Where(a => a is not null && (seen is null || seen.Add(KeyOf(a))))
                .OrderBy(static a => a.Order)
                .ToList();
            if (items is not { Count: > 0 }) continue;

            if (rendered > 0)
            {
                sb.Append("<span class=\"toolbar-sep");
                if (group.SeparatedBefore) sb.Append(" toolbar-sep-strong");
                sb.Append("\" role=\"separator\"></span>");
            }

            sb.Append("<span class=\"toolbar-group\" data-group=\"")
              .Append(WebUtility.HtmlEncode(group.Id ?? string.Empty)).Append("\">");
            if (!string.IsNullOrEmpty(group.Label))
                sb.Append("<span class=\"toolbar-group-label\">")
                  .Append(WebUtility.HtmlEncode(group.Label!)).Append("</span>");
            foreach (var item in items) AppendDescriptor(sb, item, buttonClass);
            sb.Append("</span>");
            rendered++;
        }
    }

    /// <summary>渲染单个动作描述（按钮 + 语义属性）。</summary>
    internal static void AppendDescriptor(StringBuilder sb, HtmlActionDescriptor a, string buttonClass)
    {
        var cls = Safe(buttonClass, "toolbar-btn");
        sb.Append("<button type=\"button\" class=\"").Append(cls);
        var variantClass = a.Variant switch
        {
            HtmlActionVariant.Primary => "btn-action-primary",
            HtmlActionVariant.Ghost => "btn-ghost",
            HtmlActionVariant.Danger => "btn-danger",
            _ => string.Empty,
        };
        if (variantClass.Length > 0) sb.Append(' ').Append(variantClass);
        sb.Append("\" data-cmd=\"").Append(WebUtility.HtmlEncode(a.Cmd ?? string.Empty)).Append('"');
        if (!string.IsNullOrEmpty(a.Name))
            sb.Append(" data-name=\"").Append(WebUtility.HtmlEncode(a.Name!)).Append('"');
        sb.Append(" data-action-id=\"").Append(WebUtility.HtmlEncode(a.Id ?? string.Empty)).Append('"');
        sb.Append(" data-group=\"").Append(WebUtility.HtmlEncode(a.Group ?? string.Empty)).Append('"');
        sb.Append(" data-variant=\"").Append(VariantName(a.Variant)).Append('"');
        if (a.Destructive) sb.Append(" data-destructive=\"1\"");
        if (a.Data is not null)
            foreach (var (k, v) in a.Data)
                if (!string.IsNullOrWhiteSpace(k))
                    sb.Append(" data-").Append(k).Append("=\"").Append(WebUtility.HtmlEncode(v ?? string.Empty)).Append('"');
        if (!string.IsNullOrEmpty(a.InputName))
            sb.Append(" data-input=\"").Append(WebUtility.HtmlEncode(a.InputName!)).Append('"');
        if (!string.IsNullOrEmpty(a.Title))
            sb.Append(" title=\"").Append(WebUtility.HtmlEncode(a.Title!)).Append('"');
        if (!string.IsNullOrEmpty(a.AriaLabel))
            sb.Append(" aria-label=\"").Append(WebUtility.HtmlEncode(a.AriaLabel!)).Append('"');
        if (a.Toggle) sb.Append(" aria-pressed=\"").Append(a.Pressed ? "true" : "false").Append('"');
        if (!a.Enabled) sb.Append(" disabled aria-disabled=\"true\"");
        sb.Append('>').Append(WebUtility.HtmlEncode(a.Text ?? string.Empty)).Append("</button>");
    }

    private static string VariantName(HtmlActionVariant variant) => variant switch
    {
        HtmlActionVariant.Primary => "primary",
        HtmlActionVariant.Ghost => "ghost",
        HtmlActionVariant.Danger => "danger",
        _ => "default",
    };

    private static string KeyOf(HtmlActionDescriptor a)
        => string.IsNullOrEmpty(a.Name) ? a.Id : a.Name!;

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

    internal static void AppendInput(StringBuilder sb, HtmlActionInput i)
    {
        sb.Append("<input type=\"").Append(Safe(i.Type, "text")).Append("\" class=\"")
          .Append(Safe(i.CssClass, "toolbar-input")).Append("\" data-name=\"")
          .Append(WebUtility.HtmlEncode(i.DataName ?? string.Empty)).Append('"');
        if (!string.IsNullOrEmpty(i.Placeholder))
            sb.Append(" placeholder=\"").Append(WebUtility.HtmlEncode(i.Placeholder!)).Append('"');
        if (i.Value is not null)
            sb.Append(" value=\"").Append(WebUtility.HtmlEncode(i.Value)).Append('"');
        if (!string.IsNullOrEmpty(i.AriaLabel))
            sb.Append(" aria-label=\"").Append(WebUtility.HtmlEncode(i.AriaLabel!)).Append('"');
        sb.Append("/>");
    }

    // DUP-03：CSS 类名兜底单源 = TextUtil.CssClass（本处仅为可读别名，不再自写口径）。
    private static string Safe(string? cssClass, string fallback) => TextUtil.CssClass(cssClass, fallback);
}
