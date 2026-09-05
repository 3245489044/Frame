#nullable enable

using TConfig;

namespace UiBackend.Html;

/// <summary>
/// HTML UI 投影组合入口——UIConfiguration → HTML 渲染结果（取首个 view 的控件树，
/// 容器尺寸取 form.Width/Height）。输出可直接喂 HtmlViewer.SetHtmlAsync（body 片段经 HtmlDocumentRenderer 包页壳）。
/// </summary>
public static class HtmlUiProjector
{
    public static HtmlRenderResult Project(UIConfiguration config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));

        var width = config.Form?.Width > 0 ? config.Form.Width : 1200;
        var height = config.Form?.Height > 0 ? config.Form.Height : 800;
        var controls = config.Views is { Count: > 0 } ? config.Views[0].Controls ?? [] : [];

        return new HtmlLayoutProjector().Project(controls, width, height);
    }
}
