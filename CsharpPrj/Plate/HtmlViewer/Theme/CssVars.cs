#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace Plate.HtmlViewer;

/// <summary>
/// CSS 自定义属性（<c>--name: value</c>）的**单方言扫描器**——替代「成品 CSS 正则回读」。
/// <para>按声明边界（<c>;</c> / <c>}</c>）与括号深度取值；剔除注释，并可按需剔除深色媒体块。
/// 级联语义：同一变量取**最后一条**声明（运行期模块后写覆盖主题）。</para>
/// <para>纯函数、零依赖：只认 CSS 变量声明语法，不解释色值（色值仍由 <see cref="SurfaceColor"/> 单点解析）。</para>
/// </summary>
public static class CssVars
{
    private const string DarkMedia = "@media (prefers-color-scheme: dark)";

    /// <summary>取变量最后一条声明值（裸值，未解释语义）；未声明 ⇒ null。</summary>
    /// <param name="css">完整 CSS（可含注释 / at-rule）。</param>
    /// <param name="variable">变量名（可带或不带前导 <c>--</c>）。</param>
    /// <param name="excludeDarkMedia">true ⇒ 剔除 <c>@media (prefers-color-scheme: dark)</c> 块后再取值（浅色档）。</param>
    public static string? TryLast(string? css, string variable, bool excludeDarkMedia)
    {
        var declarations = Collect(css, excludeDarkMedia);
        return declarations.TryGetValue(NormalizeName(variable), out var value) ? value : null;
    }

    /// <summary>扫描全部变量声明（变量名不带前导 <c>--</c>）；同名取最后一条。</summary>
    /// <param name="css">完整 CSS。</param>
    /// <param name="excludeDarkMedia">true ⇒ 剔除深色媒体块后再扫描。</param>
    public static IReadOnlyDictionary<string, string> Collect(string? css, bool excludeDarkMedia)
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(css)) return declarations;

        var text = StripComments(css);
        if (excludeDarkMedia) text = StripDarkMedia(text);

        Scan(text, declarations);
        return declarations;
    }

    private static string NormalizeName(string variable)
        => variable.StartsWith("--", StringComparison.Ordinal) ? variable[2..] : variable;

    /// <summary>线性扫描声明：只在语句起点识别 <c>--name</c>，避免命中 <c>var(--name)</c> 内的引用。</summary>
    private static void Scan(string css, Dictionary<string, string> sink)
    {
        var index = 0;
        while (index < css.Length)
        {
            var dash = css.IndexOf("--", index, StringComparison.Ordinal);
            if (dash < 0) return;

            if (!IsStatementStart(css, dash))
            {
                index = dash + 2;
                continue;
            }

            var nameEnd = dash + 2;
            while (nameEnd < css.Length && IsNameChar(css[nameEnd])) nameEnd++;
            if (nameEnd == dash + 2)
            {
                index = dash + 2;
                continue;
            }

            var colon = nameEnd;
            while (colon < css.Length && char.IsWhiteSpace(css[colon])) colon++;
            if (colon >= css.Length || css[colon] != ':')
            {
                index = nameEnd;
                continue;
            }

            var valueStart = colon + 1;
            var valueEnd = ValueEnd(css, valueStart);
            var value = css[valueStart..valueEnd].Trim();
            if (value.Length > 0) sink[css[(dash + 2)..nameEnd]] = value;
            index = valueEnd + 1;
        }
    }

    /// <summary>语句起点 = 前一个非空白字符为 <c>{</c> 或 <c>;</c>（或串首）。</summary>
    private static bool IsStatementStart(string css, int dash)
    {
        for (var i = dash - 1; i >= 0; i--)
        {
            var c = css[i];
            if (char.IsWhiteSpace(c)) continue;
            return c is '{' or ';';
        }

        return true;
    }

    private static bool IsNameChar(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_';

    /// <summary>值结束位置：括号深度 0 处的 <c>;</c> / <c>}</c> / <c>{</c>（忽略引号内文本）。</summary>
    private static int ValueEnd(string css, int start)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = start; i < css.Length; i++)
        {
            var c = css[i];
            if (quote != '\0')
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    if (depth > 0) depth--;
                    break;
                case ';' or '}' or '{' when depth == 0:
                    return i;
            }
        }

        return css.Length;
    }

    /// <summary>剔除 <c>/* … */</c> 注释（保留引号内文本，避免误删字符串里的斜杠）。</summary>
    private static string StripComments(string css)
    {
        var builder = new StringBuilder(css.Length);
        var quote = '\0';
        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];
            if (quote != '\0')
            {
                builder.Append(c);
                if (c == '\\' && i + 1 < css.Length) builder.Append(css[++i]);
                else if (c == quote) quote = '\0';
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                builder.Append(c);
                continue;
            }

            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) break;
                i = end + 1;
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>移除全部深色媒体块（花括号配对扫描，不做 <c>IndexOf('}')</c> 截断）。</summary>
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

    /// <summary>从 at-rule 起点返回配对 <c>{…}</c> 之后的下标（未闭合 ⇒ 串尾）。</summary>
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
}
