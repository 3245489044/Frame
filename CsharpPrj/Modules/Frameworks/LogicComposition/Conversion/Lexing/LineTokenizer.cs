using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Lexing;

/// <summary>行内 token（<c>Index</c> 为原始行内 0 基偏移；1 基列号 = Index+1）。</summary>
public readonly struct TextToken
{
    /// <summary>构造 token。</summary>
    public TextToken(string text, int index, bool quoted)
    {
        Text = text;
        Index = index;
        Quoted = quoted;
    }

    /// <summary>原始文本（引号串含首尾引号）。</summary>
    public string Text { get; }

    /// <summary>原始行内 0 基偏移。</summary>
    public int Index { get; }

    /// <summary>是否为双引号串。</summary>
    public bool Quoted { get; }
}

/// <summary>词法结果：token 序列 + 未闭合引号位置。</summary>
public sealed class LineTokenizeResult
{
    /// <summary>构造结果。</summary>
    public LineTokenizeResult(IReadOnlyList<TextToken> tokens, int unterminatedQuoteIndex)
    {
        Tokens = tokens;
        UnterminatedQuoteIndex = unterminatedQuoteIndex;
    }

    /// <summary>已识别 token（按出现顺序）。</summary>
    public IReadOnlyList<TextToken> Tokens { get; }

    /// <summary>未闭合双引号的起始偏移；-1 表示无（由调用方据此报格式级诊断）。</summary>
    public int UnterminatedQuoteIndex { get; }
}

/// <summary>
/// 行内词法：双引号串（保留内部空白）或空白分隔的裸 token。
/// 只做词法，不定义错误码/消息——未闭合引号经 <see cref="LineTokenizeResult.UnterminatedQuoteIndex"/> 交调用方裁决。
/// </summary>
public static class LineTokenizer
{
    /// <summary>切词（未闭合引号即停止并回报其起始偏移）。</summary>
    public static LineTokenizeResult Tokenize(string raw)
    {
        var tokens = new List<TextToken>();
        if (string.IsNullOrEmpty(raw)) return new LineTokenizeResult(tokens, -1);

        var i = 0;
        while (i < raw!.Length)
        {
            if (char.IsWhiteSpace(raw[i])) { i++; continue; }

            if (raw[i] == '"')
            {
                var start = i;
                var end = TextScan.FindStringEnd(raw, start);
                if (end == TextScan.Unterminated) return new LineTokenizeResult(tokens, start);

                tokens.Add(new TextToken(raw.Substring(start, end - start), start, true));
                i = end;
                continue;
            }

            var begin = i;
            while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
            tokens.Add(new TextToken(raw.Substring(begin, i - begin), begin, false));
        }

        return new LineTokenizeResult(tokens, -1);
    }
}
