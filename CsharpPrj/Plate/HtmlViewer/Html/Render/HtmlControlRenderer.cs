using System.Text;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// HTML 控件渲染器——<see cref="IControlRenderer"/> 的 HTML 后端实现：单控件（含递归子控件）按布局 rect 绝对定位 +
/// <see cref="HtmlControlFactory"/> 控件词表映射。布局换算经 <see cref="HtmlLayoutProjector.ResolveRect"/>（比例/锚定语义对齐 WinForms 后端）。
/// </summary>
public sealed class HtmlControlRenderer : IControlRenderer
{
    private readonly HtmlLayoutProjector _layout = new();

    public string Render(ControlConfig control, LayoutRect rect)
    {
        var sb = new StringBuilder();
        RenderControl(sb, control, rect.Width, rect.Height);
        return sb.ToString();
    }

    /// <summary>流式渲染（P5 布局下沉浏览器原生）：不产绝对像素，由组件 CSS（flex/grid）承担布局；
    /// 声明 layout.intent 已由 data-layout 交给页面 CSS/JS 解释（<see cref="HtmlControlFactory.RenderElement"/> 输出）。</summary>
    public string RenderFlow(ControlConfig control)
    {
        var sb = new StringBuilder();
        RenderFlowControl(sb, control);
        return sb.ToString();
    }

    private void RenderFlowControl(StringBuilder sb, ControlConfig config)
    {
        string childrenHtml = string.Empty;
        if (config.Controls is { Count: > 0 })
            childrenHtml = RenderFlowChildren(config);
        // 流式：style 交给 CSS 类承担（不产 position:absolute 像素换算）。
        sb.Append(HtmlControlFactory.RenderElement(config, string.Empty, childrenHtml));
    }

    private string RenderFlowChildren(ControlConfig config)
    {
        var inner = new StringBuilder();
        foreach (var child in config.Controls)
            RenderFlowControl(inner, child);
        return inner.ToString();
    }

    private void RenderControl(StringBuilder sb, ControlConfig config, int parentW, int parentH)
    {
        var rect = _layout.ResolveRect(config.Layout, parentW, parentH);
        var style = $"position:absolute;left:{rect.X}px;top:{rect.Y}px;width:{rect.Width}px;height:{rect.Height}px;";

        string childrenHtml = string.Empty;
        if (config.Controls is { Count: > 0 })
            childrenHtml = RenderChildren(config, rect.Width, rect.Height);

        sb.Append(HtmlControlFactory.RenderElement(config, style, childrenHtml));
    }

    /// <summary>递归渲染子控件；tabcontrol 的每页（controls 每项）隔离为独立 pane，避免多页面板叠显、支持 tab 切换。</summary>
    private string RenderChildren(ControlConfig config, int parentW, int parentH)
    {
        var isTab = string.Equals(config.Type, "tabcontrol", StringComparison.OrdinalIgnoreCase);
        var inner = new StringBuilder();
        for (int i = 0; i < config.Controls.Count; i++)
        {
            var part = new StringBuilder();
            RenderControl(part, config.Controls[i], parentW, parentH);
            if (isTab)
            {
                // 每页内容包一层 .pane（首项 active），配合 RenderTabControl 内联脚本按 data-pane 切换显隐。
                inner.Append($"<div class=\"pane{(i == 0 ? " active" : string.Empty)}\" data-pane=\"{i}\">").Append(part).Append("</div>");
            }
            else
            {
                inner.Append(part);
            }
        }
        return inner.ToString();
    }
}
