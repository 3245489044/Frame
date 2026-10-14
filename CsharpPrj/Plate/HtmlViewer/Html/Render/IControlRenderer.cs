using TConfig;

namespace UiBackend.Html;

/// <summary>控件渲染后端契约——同一份声明在不同后端（WinForms/HTML）产出形态；HTML 后端实现见 <see cref="HtmlControlRenderer"/>。
/// <paramref name="rect"/> 为控件在父容器中的像素布局矩形（子控件按 rect 尺寸递归换算）。</summary>
public interface IControlRenderer
{
    string Render(ControlConfig control, LayoutRect rect);
}
