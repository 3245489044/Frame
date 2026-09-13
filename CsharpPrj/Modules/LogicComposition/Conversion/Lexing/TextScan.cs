using System;

namespace Share.LogicComposition.Conversion.Lexing;

/// <summary>
/// 文本扫描原语（**字符串边界与括号深度的唯一实现**）：行内词法、括号区块扫描、JSON 字面量解析、命令分词共用。
/// <para>
/// 转义语义为「反斜杠转义其后一个字符」：JSON 的 <c>\uXXXX</c> 四位十六进制均非引号，
/// 故"转义一个字符"足以正确定位字符串边界（与逐字符解码器等价）。
/// </para>
/// </summary>
public static class TextScan
{
    /// <summary>未闭合标记（<see cref="FindStringEnd"/> / <see cref="FindUnterminatedQuote"/> 的返回值）。</summary>
    public const int Unterminated = -1;

    /// <summary>
    /// 查找字符串结束位置：<paramref name="quoteIndex"/> 须指向起始引号，返回闭合引号的**下一位**；
    /// 未闭合（扫描到文本结束）返回 <see cref="Unterminated"/>。
    /// </summary>
    /// <param name="text">待扫描文本。</param>
    /// <param name="quoteIndex">起始引号的 0 基下标。</param>
    /// <param name="quote">引号字符（默认双引号）。</param>
    public static int FindStringEnd(string text, int quoteIndex, char quote = '"')
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (quoteIndex < 0 || quoteIndex >= text.Length) return Unterminated;

        for (var i = quoteIndex + 1; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch == '\\') { i++; continue; }
            if (ch == quote) return i + 1;
        }

        return Unterminated;
    }

    /// <summary>
    /// 未闭合引号的起始偏移（**只回报不裁决**）：扫描结束时仍处于引号开启态（含末尾反斜杠造成的转义态），
    /// 返回**该引号的起始偏移**；否则返回 <see cref="Unterminated"/>。
    /// </summary>
    /// <param name="text">待扫描文本（null/空 → <see cref="Unterminated"/>）。</param>
    /// <param name="quote">主引号字符（默认双引号）。</param>
    /// <param name="alternativeQuote">可选次引号字符（如单引号；null = 不启用）。</param>
    public static int FindUnterminatedQuote(string? text, char quote = '"', char? alternativeQuote = null)
    {
        if (string.IsNullOrEmpty(text)) return Unterminated;

        var openQuote = '\0';
        var quoteStart = Unterminated;

        for (var i = 0; i < text!.Length; i++)
        {
            var ch = text[i];

            if (openQuote != '\0')
            {
                // 转义其后一个字符；末尾反斜杠使 i 越界 → 循环结束，引号仍为开启态（未闭合）
                if (ch == '\\') { i++; continue; }
                if (ch == openQuote) openQuote = '\0';

                continue;
            }

            if (ch == quote || (alternativeQuote.HasValue && ch == alternativeQuote.Value))
            {
                openQuote = ch;
                quoteStart = i;
            }
        }

        return openQuote == '\0' ? Unterminated : quoteStart;
    }

    /// <summary>括号深度（跳过字符串内的括号；仅计 <c>{}</c> 与 <c>[]</c>）。</summary>
    /// <param name="text">待扫描文本（可为 null/空 → 0）。</param>
    public static int BracketDepth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var depth = 0;

        for (var i = 0; i < text!.Length; i++)
        {
            var ch = text[i];

            if (ch == '"')
            {
                var end = FindStringEnd(text, i);
                if (end == Unterminated) break;

                i = end - 1;
                continue;
            }

            if (ch == '{' || ch == '[') depth++;
            else if (ch == '}' || ch == ']') depth--;
        }

        return depth;
    }

    /// <summary>指定括号对的深度（跳过字符串内的括号；其余括号不计）。</summary>
    /// <param name="text">待扫描文本（可为 null/空 → 0）。</param>
    /// <param name="openChar">开括号字符。</param>
    /// <param name="closeChar">闭括号字符。</param>
    public static int BracketDepth(string? text, char openChar, char closeChar)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var depth = 0;

        for (var i = 0; i < text!.Length; i++)
        {
            var ch = text[i];

            if (ch == '"')
            {
                var end = FindStringEnd(text, i);
                if (end == Unterminated) break;

                i = end - 1;
                continue;
            }

            if (ch == openChar) depth++;
            else if (ch == closeChar) depth--;
        }

        return depth;
    }
}
