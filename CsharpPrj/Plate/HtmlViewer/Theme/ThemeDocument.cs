#nullable enable

using System.Collections.Generic;
using System.Text;

namespace Plate.HtmlViewer;

/// <summary>
/// 主题文档（主题单一源 IR）——token 值（浅/深两套）+ 组件样式 + 引用 CSS 清单。
/// <see cref="ToCss"/> 为唯一产出 CSS 的收敛点：全仓主题消费方（<see cref="HtmlDocumentRenderer"/>/<see cref="HtmlViewer"/>）
/// 经 <see cref="IThemeProvider"/> 解析后取本模型，不再拼多份 CSS（收敛 DefaultCss/theme/BaseCss/htmlShell 四份）。
/// </summary>
public sealed record ThemeDocument(
    IReadOnlyDictionary<string, string>? TokensLight,
    IReadOnlyDictionary<string, string>? TokensDark,
    string? ComponentCss,
    IReadOnlyList<string>? CssFiles)
{
    /// <summary>空主题（未装配/无内容时兜底）。</summary>
    public static ThemeDocument Empty { get; } = new(null, null, null, null);

    /// <summary>从裸 CSS 构造主题文档（componentCss 承载；兼容 IHtmlTheme 透传 / JSON 裸 CSS theme 迁移，不动格式）。</summary>
    public static ThemeDocument FromCss(string? css) => new(null, null, css, null);

    /// <summary>
    /// 合并为可直接内嵌 &lt;style&gt; 的 CSS 片段：浅色 <c>:root{--token:...}</c> + 深色
    /// <c>@media (prefers-color-scheme: dark){:root{...}}</c> + 组件样式。CssFiles（引用 CSS 清单）当前不在此内联，
    /// 由页面资产加载层按需解析（保留在模型中供后续接线）。
    /// </summary>
    public string ToCss()
    {
        var sb = new StringBuilder(512);
        AppendTokens(sb, TokensLight, dark: false);
        AppendTokens(sb, TokensDark, dark: true);
        if (!string.IsNullOrWhiteSpace(ComponentCss))
            sb.Append(ComponentCss);
        return sb.ToString();
    }

    private static void AppendTokens(StringBuilder sb, IReadOnlyDictionary<string, string>? tokens, bool dark)
    {
        if (tokens is not { Count: > 0 }) return;
        if (dark)
        {
            sb.Append("@media (prefers-color-scheme: dark){:root{");
            foreach (var kv in tokens)
                sb.Append("--").Append(kv.Key).Append(':').Append(kv.Value).Append(';');
            sb.Append("}}");
        }
        else
        {
            sb.Append(":root{");
            foreach (var kv in tokens)
                sb.Append("--").Append(kv.Key).Append(':').Append(kv.Value).Append(';');
            sb.Append('}');
        }
    }
}
