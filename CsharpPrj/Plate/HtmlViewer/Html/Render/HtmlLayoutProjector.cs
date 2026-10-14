#nullable enable

using System.Text;
using TConfig;

namespace UiBackend.Html;

/// <summary>像素布局矩形（HTML 定位输出；与 <see cref="IUiElementNode.GetBounds"/> 同形）。</summary>
public readonly record struct LayoutRect(int X, int Y, int Width, int Height);

/// <summary>HTML 渲染结果：body 片段 + 主题 CSS（可经 HtmlDocumentRenderer 包页壳）。</summary>
public sealed record HtmlRenderResult(string BodyHtml, string Css);

/// <summary>
/// HTML 布局投影器——递归控件树，把 <see cref="ControlConfig.Layout"/>（TConfig.LayoutPosition）换算为
/// 像素 rect → 绝对定位 CSS。换算语义对齐 WinForms 侧（ControlFactory.AddLayoutComponents +
/// LayoutSystem.TryComputeEntityLayout）：缺省 proportional 比例×父容器、bottom/right 负值锚定换算、
/// fixed 像素直定位、dock/anchor 近似填满；无 layout 默认填满父容器（对齐 container 缺省 Fill）。
/// </summary>
public sealed class HtmlLayoutProjector
{
    /// <summary>基础主题 CSS（组件主题，不承担布局）。内容唯一真源在 L1 资产层 <see cref="HtmlAssetRegistry"/>（按 <see cref="HtmlAssetKeys.BaseCss"/> 具名引用）。</summary>
    public static string BaseCss => HtmlAssetRegistry.GetStyle(HtmlAssetKeys.BaseCss);

    /// <summary>换算布局声明为像素 rect（父容器尺寸为基准）。</summary>
    public LayoutRect ResolveRect(TConfig.LayoutPosition? layout, int parentW, int parentH)
    {
        if (layout is null)
            return new LayoutRect(0, 0, Math.Max(0, parentW), Math.Max(0, parentH));

        var intent = string.IsNullOrWhiteSpace(layout.Intent)
            ? "proportional"
            : layout.Intent!.Trim().ToLowerInvariant();

        // 原生接管意图（dock/anchor）：HTML 无原生 dock 语义，近似填满父容器
        if (intent is "dock" or "anchor")
            return new LayoutRect(0, 0, Math.Max(0, parentW), Math.Max(0, parentH));

        if (intent == "fixed")
            return new LayoutRect((int)layout.X, (int)layout.Y, (int)Math.Max(0, layout.Width), (int)Math.Max(0, layout.Height));

        // proportional（缺省）：比例×父容器；bottom/right 锚定换算（对齐 ControlFactory：Top<0 哨兵时换算）
        var left = layout.Left;
        var top = layout.Top;
        if (layout.Bottom >= 0 && top < 0 && layout.Height > 0)
            top = 1.0 - layout.Bottom - layout.Height;
        if (layout.Right >= 0 && left < 0 && layout.Width > 0)
            left = 1.0 - layout.Right - layout.Width;

        var x = left * parentW;
        var y = top * parentH;
        var w = layout.Width > 0 ? layout.Width * parentW : Math.Max(0, parentW - x);
        var h = layout.Height > 0 ? layout.Height * parentH : Math.Max(0, parentH - y);
        return new LayoutRect((int)x, (int)y, (int)w, (int)h);
    }

    /// <summary>投影控件树为 HTML：根容器 ui-root（relative）内绝对定位全部节点；子坐标相对父容器。</summary>
    public HtmlRenderResult Project(IEnumerable<ControlConfig> controls, int containerW, int containerH)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"ui-root\" style=\"position:relative;width:")
          .Append(containerW).Append("px;height:").Append(containerH)
          .Append("px;overflow:hidden;\">");
        var renderer = new HtmlControlRenderer();
        foreach (var control in controls)
            sb.Append(renderer.Render(control, new LayoutRect(0, 0, containerW, containerH)));
        sb.Append("</div>");
        return new HtmlRenderResult(sb.ToString(), BaseCss);
    }

    /// <summary>流式投影（P5 布局下沉浏览器原生）：ui-root.flow 内由组件 CSS（flex/grid/%）承担布局，C# 不产绝对像素；
    /// 声明 layout.intent 经 data-layout 交给页面 CSS/JS 解释。<see cref="ResolveRect"/>（比例→px）保留为兼容层（像素级一致目标）。</summary>
    public HtmlRenderResult ProjectFlow(IEnumerable<ControlConfig> controls)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"ui-root flow\">");
        var renderer = new HtmlControlRenderer();
        foreach (var control in controls)
            sb.Append(renderer.RenderFlow(control));
        sb.Append("</div>");
        return new HtmlRenderResult(sb.ToString(), BaseCss);
    }

}
