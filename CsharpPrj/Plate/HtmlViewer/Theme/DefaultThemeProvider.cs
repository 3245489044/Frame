#nullable enable

namespace Plate.HtmlViewer;

/// <summary>
/// 默认主题提供者（未装配时的兜底）——按作用域产出内嵌默认主题：
/// Document = 对话气泡 CSS（原 <see cref="HtmlDocumentRenderer.DefaultCss"/>），Component = 组件基础 CSS（原 <see cref="HtmlLayoutProjector.BaseCss"/>）。
/// 使 DefaultCss/BaseCss 从散落的 AssetRegistry 常量收敛为 <see cref="IThemeProvider"/> 按 scope 产出（消费方只依赖本接口）。
/// </summary>
public sealed class DefaultThemeProvider : IThemeProvider
{
    public ThemeDocument Resolve(ThemeMode mode, ThemeScope scope) => scope switch
    {
        ThemeScope.Component => ThemeDocument.FromCss(
            UiBackend.Html.HtmlAssetRegistry.GetStyle(UiBackend.Html.HtmlAssetKeys.BaseCss)),
        _ => ThemeDocument.FromCss(
            UiBackend.Html.HtmlAssetRegistry.GetStyle(UiBackend.Html.HtmlAssetKeys.DefaultCss)),
    };
}
