namespace Plate.HtmlViewer;

/// <summary>
/// body HTML 片段 → 完整 HTML 文档壳（内嵌气泡式对话 CSS，浅/深双主题，CJK 排版）。
/// 纯函数，供 <see cref="HtmlViewer"/> 控件与测试复用；生产端渲染器负责 encode 文本后再拼入 body。
/// </summary>
public static class HtmlDocumentRenderer
{
    /// <summary>body HTML 片段 → 完整 HTML 页面。null/空片段 → 空文档（仅壳）。主题经 <see cref="HtmlTheme.Resolve"/> 取当前生效主题（缺省内嵌默认）。</summary>
    /// <param name="bodyHtml">body 内容片段（已由生产端渲染器完成文本 encode，本方法不做任何转义/解析）。</param>
    /// <param name="fontSizePx">文档基础字号（px）。默认 null = 使用 <see cref="DefaultCss"/> 中的 15px；传正值覆盖。</param>
    public static string ToHtml(string? bodyHtml, double? fontSizePx = null)
        => ToHtml(bodyHtml, HtmlTheme.ResolveProvider().Resolve(ThemeMode.System, ThemeScope.Document), ThemeMode.System, fontSizePx);

    /// <summary>body HTML 片段 → 完整 HTML 页面（IoC：主题由外部注入，缺省回退默认主题；System 模式）。</summary>
    /// <param name="bodyHtml">body 内容片段（已由生产端渲染器完成文本 encode，本方法不做任何转义/解析）。</param>
    /// <param name="theme">文档主题（IHtmlTheme 兼容形态，转 <see cref="ThemeDocument"/>）；null 回退 <see cref="DefaultCss"/>。</param>
    /// <param name="fontSizePx">文档基础字号（px）；传正值覆盖。</param>
    public static string ToHtml(string? bodyHtml, IHtmlTheme? theme, double? fontSizePx = null)
        => ToHtml(bodyHtml, theme is null ? null : ThemeDocument.FromCss(theme.Css), ThemeMode.System, fontSizePx);

    /// <summary>body HTML 片段 → 完整 HTML 页面（主题单一源：显式 <see cref="ThemeDocument"/> + 深浅色模式；生产路径直接消费，不隐式读全局单例）。</summary>
    /// <param name="bodyHtml">body 内容片段（已由生产端渲染器完成文本 encode，本方法不做任何转义/解析）。</param>
    /// <param name="theme">主题文档（经 <see cref="HtmlTheme.ResolveProvider"/> 解析）；null 回退 <see cref="DefaultCss"/>。</param>
    /// <param name="mode">深浅色模式：决定 <c>color-scheme</c> meta，与 WebView2 锚定联动（System 才随系统）。</param>
    /// <param name="fontSizePx">文档基础字号（px）；传正值覆盖。</param>
    public static string ToHtml(string? bodyHtml, ThemeDocument? theme, ThemeMode mode, double? fontSizePx = null)
    {
        // 字号覆盖：内联一层后置 style（晚于主题 CSS 定义，保证覆盖其 body 字号）。
        var fontStyle = fontSizePx is > 0
            ? $"<style>body{{font-size:{fontSizePx.Value:F1}px;}}</style>"
            : string.Empty;
        var css = theme is null ? DefaultCss : theme.ToCss();
        var colorScheme = mode switch
        {
            ThemeMode.Light => "light",
            ThemeMode.Dark => "dark",
            _ => "light dark",
        };
        return $"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<meta name="color-scheme" content="{colorScheme}" />
<style>
{css}
</style>
{fontStyle}
</head>
<body>{bodyHtml ?? string.Empty}
</body>
</html>
""";
    }

    /// <summary>默认主题 CSS：气泡式对话布局（user 右蓝 / agent 左灰）+ 浅/深双主题（跟随系统 prefers-color-scheme）+ CJK 排版。
    /// 内容唯一真源在 L1 资产层 <see cref="UiBackend.Html.HtmlAssetRegistry"/>（按 <see cref="UiBackend.Html.HtmlAssetKeys.DefaultCss"/> 具名引用）。</summary>
    public static string DefaultCss => UiBackend.Html.HtmlAssetRegistry.GetStyle(UiBackend.Html.HtmlAssetKeys.DefaultCss);
}
