using System;
using System.Drawing;
using UiBackend;

namespace Plate.HtmlViewer;

/// <summary>
/// 从主题 CSS 提取背景色（<c>--bg</c>）供 WebView2 <c>DefaultBackgroundColor</c> 锚定——使控件周边底色与页面一致，
/// 消除深色系统下"页面 body 深色、周边 WebView2 默认白/黑"的视觉不一致。
/// 纯函数（CSS 扫描），不触碰 DOM；解析失败返回 null（回退默认，安全降级）。
/// </summary>
public static class HtmlThemeBackground
{
    /// <summary>背景变量名（唯一字面量；取值入口是 <see cref="CssVars"/>）。</summary>
    private const string BackgroundVariable = "--bg";

    private static readonly string[] TokenKeys = { "bg", "surface_plain" };

    /// <summary>按模式取生效 CSS 中的背景色（级联仿真：Dark 全量源序最后一条；Light 剔除深色媒体块后最后一条）。</summary>
    public static Color? Resolve(string? css, ThemeMode mode)
    {
        // 级联顺序即真相：运行期模块（如玻璃档 `--bg:transparent`）排在主题之后，必须参与取最后一条；
        // Light 若在整段 CSS 上取值会命中 `@media (prefers-color-scheme: dark)` 内的深色值，故先剔除该块。
        return ParseColor(CssVars.TryLast(css, BackgroundVariable, excludeDarkMedia: mode != ThemeMode.Dark));
    }

    /// <summary>无 CSS 场景（主题 IR 直读）：Dark → TokensDark，其余 → TokensLight；键序 bg → surface_plain。
    /// <para>仅作无 CSS 时的兜底，**不**替代完整 CSS 路径（运行期模块覆盖必须经 <see cref="Resolve(string?, ThemeMode)"/>）。</para></summary>
    public static Color? Resolve(ThemeDocument? theme, ThemeMode mode)
    {
        var tokens = mode == ThemeMode.Dark ? theme?.TokensDark : theme?.TokensLight;
        if (tokens is not { Count: > 0 }) return null;

        foreach (var key in TokenKeys)
        {
            if (!tokens.TryGetValue(key, out var value)) continue;
            var color = ParseColor(value);
            if (color.HasValue) return color;
        }

        return null;
    }

    /// <summary>色值 ⇒ Color：统一走 <see cref="SurfaceColor"/>（CSS 8 位 = RRGGBBAA，禁在此另写一套）。</summary>
    private static Color? ParseColor(string? s)
        => SurfaceColor.TryParseCssColor(s, out var argb) ? Color.FromArgb(argb) : null;
}
