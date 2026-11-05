using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Lexing;

/// <summary>
/// 多行括号区块扫描（<c>{}</c> / <c>[]</c>，跳过字符串内的括号）：
/// 从首个片段起累积后续原始行，直到括号深度归零。
/// </summary>
public static class BracketBlockScanner
{
    /// <summary>括号深度（跳过字符串内的括号；仅计 <c>{}</c> 与 <c>[]</c>）。</summary>
    public static int Depth(string text) => TextScan.BracketDepth(text);

    /// <summary>
    /// 读取跨行区块（括号归零即止）；累积**原始行** → 行内列号精确。
    /// </summary>
    /// <param name="lines">全部原始行。</param>
    /// <param name="index">当前游标（读取后续行时前进）。</param>
    /// <param name="fragment">区块首个片段（首行内已出现的部分）。</param>
    /// <param name="blockText">out：完整区块文本（跨行以 <c>\n</c> 连接）。</param>
    /// <param name="error">out：失败原因（无领域语义的通用描述）。</param>
    public static bool TryReadBlock(
        IReadOnlyList<string> lines,
        ref int index,
        string fragment,
        out string blockText,
        out string? error)
    {
        if (lines is null) throw new ArgumentNullException(nameof(lines));

        blockText = fragment ?? string.Empty;
        error = null;

        if (!(blockText.StartsWith("{", StringComparison.Ordinal) || blockText.StartsWith("[", StringComparison.Ordinal)))
        {
            error = "值为 JSON 字面量（以 { 或 [ 开头）";
            return false;
        }

        while (Depth(blockText) > 0)
        {
            if (index >= lines.Count)
            {
                error = "JSON 区块未闭合";
                return false;
            }

            blockText += "\n" + lines[index];
            index++;
        }

        if (Depth(blockText) != 0)
        {
            error = "JSON 括号不匹配";
            return false;
        }

        return true;
    }
}
