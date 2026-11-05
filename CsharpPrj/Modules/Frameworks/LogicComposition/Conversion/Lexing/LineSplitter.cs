using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Lexing;

/// <summary>
/// 行切分（保留原行内容，不做 trim——列号须精确）。
/// </summary>
public static class LineSplitter
{
    /// <summary>按 <c>\n</c> 切分，行尾 <c>\r</c> 剔除。</summary>
    public static List<string> Split(string text)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        var start = 0;
        for (var i = 0; i < text!.Length; i++)
        {
            if (text[i] != '\n') continue;

            var end = i;
            if (end > start && text[end - 1] == '\r') end--;
            lines.Add(text.Substring(start, end - start));
            start = i + 1;
        }

        if (start < text.Length) lines.Add(text.Substring(start));
        return lines;
    }
}
