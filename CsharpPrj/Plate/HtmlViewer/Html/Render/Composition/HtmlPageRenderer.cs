#nullable enable

using TConfig;

namespace UiBackend.Html;

/// <summary>
/// 页面渲染器——由「声明源（UIConfiguration 或 htmlviewer 内嵌内部页面声明）」投影布局壳 + 注入数据。
/// 布局结构与占位由 JSON 决定，C# 只产数据（数据提供者模式）。
/// [合并自 ConversationPageRenderer 通用部分：LoadPageLayout + RenderPage]
/// </summary>
public sealed class HtmlPageRenderer
{
    private readonly UIConfiguration _declaration;

    public HtmlPageRenderer(UIConfiguration declaration)
        => _declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));

    public HtmlPageRenderer(ControlConfig host, string pageName)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        _declaration = HtmlDeclarationParser.ExtractHtmlPage(host, pageName)
            ?? throw new ArgumentException(
                $"控件 '{host.Name}' 未声明内嵌页面 '{pageName}'（ControlConfig.HtmlPage 缺省 null）", nameof(host));
    }

    /// <summary>投影布局壳（BodyHtml + 主题 Css）。</summary>
    public HtmlRenderResult Project() => HtmlUiProjector.Project(_declaration);

    /// <summary>投影（布局壳 + 主题 style）+ 占位注入 → 布局壳 HTML（格式与既有 ConversationPageRenderer 输出一致）。</summary>
    public string Render(IReadOnlyDictionary<string, string>? data)
    {
        var result = HtmlUiProjector.Project(_declaration);
        var layout = $"<style>{result.Css}</style>{result.BodyHtml}";
        return HtmlDeclarationParser.InjectData(layout, data);
    }
}
