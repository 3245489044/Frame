using System;
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Lexing;

/// <summary>
/// 区块内字符偏移 → 绝对源位置的换算（以区块起始位置 + 原始区块文本为准）。
/// </summary>
public static class SourceOffsetMap
{
    /// <summary>把区块内相对偏移还原为绝对源位置。</summary>
    /// <param name="blockStart">区块起始位置（须 <see cref="SourceSpan.HasPosition"/>）。</param>
    /// <param name="blockText">区块原始文本（跨行内容以 <c>\n</c> 连接）。</param>
    /// <param name="offset">区块内 0 基字符偏移。</param>
    public static SourceSpan ToSpan(SourceSpan blockStart, string blockText, int offset)
    {
        if (!blockStart.HasPosition || blockText is null || offset <= 0) return blockStart;

        var line = blockStart.Line;
        var column = blockStart.Column;
        var limit = Math.Min(offset, blockText.Length);

        for (var i = 0; i < limit; i++)
        {
            if (blockText[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return SourceSpan.At(blockStart.File, line, column);
    }
}
