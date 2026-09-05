using System;
using System.Drawing;
using System.Text.RegularExpressions;

namespace Plate.HtmlViewer;

/// <summary>
/// 从主题 CSS 提取背景色（--bg）供 WebView2 <c>DefaultBackgroundColor</c> 锚定——使控件周边底色与页面一致，
/// 消除深色系统下"页面 body 深色、周边 WebView2 默认白/黑"的视觉不一致。
/// 纯函数（CSS 解析），不触碰 DOM；解析失败返回 null（回退默认，安全降级）。
/// </summary>
public static class HtmlThemeBackground
{
    /// <summary>按模式取 CSS 中的背景色：Dark 优先取 @media (prefers-color-scheme: dark) 块内 :root 的 --bg，其余取默认 :root 的 --bg。</summary>
    public static Color? Resolve(string? css, ThemeMode mode)
    {
        if (string.IsNullOrWhiteSpace(css))
            return null;
        var value = mode == ThemeMode.Dark ? ExtractBg(css, dark: true) : null;
        value ??= ExtractBg(css, dark: false);
        return ParseHex(value);
    }

    private static string? ExtractBg(string css, bool dark)
    {
        string segment = css;
        if (dark)
        {
            int idx = css.IndexOf("@media (prefers-color-scheme: dark)", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int end = css.IndexOf('}', idx);
                if (end > idx)
                    segment = css.Substring(idx, end - idx + 1);
            }
        }
        var m = Regex.Match(segment, "--bg\\s*:\\s*(#[0-9a-fA-F]{3,8})\\b");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static Color? ParseHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s[0] != '#')
            return null;
        var hex = s.Substring(1);
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length != 6 && hex.Length != 8)
            return null;
        try
        {
            var r = Convert.ToByte(hex.Substring(0, 2), 16);
            var g = Convert.ToByte(hex.Substring(2, 2), 16);
            var b = Convert.ToByte(hex.Substring(4, 2), 16);
            var a = hex.Length == 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : 255;
            return Color.FromArgb(a, r, g, b);
        }
        catch
        {
            return null;
        }
    }
}
