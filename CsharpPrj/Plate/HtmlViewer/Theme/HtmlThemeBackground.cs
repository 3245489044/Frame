using System;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using UiBackend;

namespace Plate.HtmlViewer;

/// <summary>
/// 从主题 CSS 提取背景色（--bg）供 WebView2 <c>DefaultBackgroundColor</c> 锚定——使控件周边底色与页面一致，
/// 消除深色系统下"页面 body 深色、周边 WebView2 默认白/黑"的视觉不一致。
/// 纯函数（CSS 解析），不触碰 DOM；解析失败返回 null（回退默认，安全降级）。
/// </summary>
public static class HtmlThemeBackground
{
    /// <summary>按模式取 CSS 中的背景色（级联仿真：Dark 全量源序最后一条；Light 剔除深色媒体块后最后一条）。</summary>
    public static Color? Resolve(string? css, ThemeMode mode)
    {
        if (string.IsNullOrWhiteSpace(css))
            return null;

        // 级联顺序即真相：运行期模块（如玻璃档 `:root{--bg:transparent}`）排在主题之后，必须参与取最后一条；
        // Light 若在整段 CSS 上取值会命中 `@media (prefers-color-scheme: dark)` 内的深色值，故先剔除该块。
        var scoped = mode == ThemeMode.Dark ? css : StripDarkMedia(css);
        return ParseColor(LastBg(scoped));
    }

    private const string DarkMedia = "@media (prefers-color-scheme: dark)";

    /// <summary>移除全部深色媒体块（花括号配对扫描，不做 `IndexOf('}')` 截断）。</summary>
    private static string StripDarkMedia(string css)
    {
        var builder = new StringBuilder(css.Length);
        var index = 0;
        while (true)
        {
            var start = css.IndexOf(DarkMedia, index, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                builder.Append(css, index, css.Length - index);
                break;
            }

            builder.Append(css, index, start - index);
            index = BlockEnd(css, start);
        }

        return builder.ToString();
    }

    /// <summary>从 at-rule 起点返回配对 `{…}` 之后的下标（未闭合 ⇒ 串尾）。</summary>
    private static int BlockEnd(string css, int start)
    {
        var open = css.IndexOf('{', start);
        if (open < 0) return css.Length;
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}')
            {
                depth--;
                if (depth == 0) return i + 1;
            }
        }

        return css.Length;
    }

    private static string? LastBg(string segment)
    {
        // CSS 级联：同一段内后声明的 `--bg` 覆盖先声明的 ⇒ 取**最后一条**（运行期模块同理参与覆盖）。
        var matches = Regex.Matches(
            segment,
            "--bg\\s*:\\s*(#[0-9a-fA-F]{3,8}|transparent|none|rgb\\([^)]*\\)|rgba\\([^)]*\\))",
            RegexOptions.IgnoreCase);
        return matches.Count > 0 ? matches[matches.Count - 1].Groups[1].Value.Trim() : null;
    }

    /// <summary>色值 ⇒ Color：统一走 <see cref="SurfaceColor"/>（CSS 8 位 = RRGGBBAA，禁在此另写一套）。</summary>
    private static Color? ParseColor(string? s)
        => SurfaceColor.TryParseCssColor(s, out var argb) ? Color.FromArgb(argb) : null;
}
