using System;

namespace Share.LogicComposition.Conversion.Text;

/// <summary>
/// 文本 → token 粗估（**全库唯一 char→token 系数**）：CJK/全角字符 ≈ 1 token，其余每 4 字符 ≈ 1 token（向上取整）。
/// <para>
/// 口径刻意保守（宁高不低）：本层为零依赖叶模块，不引 tokenizer；精确计数由执行端承担。
/// 消费方（Agent 技能注入预算 / 响应治理 / 守卫）一律经此，禁各处自建第二套系数。
/// </para>
/// </summary>
public static class TokenEstimate
{
    private const int AsciiCharsPerToken = 4;

    /// <summary>粗估文本 token 数（null/空 → 0）。</summary>
    /// <param name="text">待估算文本。</param>
    public static int Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var wide = 0;
        var narrow = 0;

        foreach (var ch in text!)
        {
            if (IsWide(ch)) wide++;
            else narrow++;
        }

        return wide + (narrow + AsciiCharsPerToken - 1) / AsciiCharsPerToken;
    }

    /// <summary>CJK / 全角判定（东亚宽字符区起点 0x2E80 起一律按 1 token 计）。</summary>
    private static bool IsWide(char ch) => ch >= '\u2E80';
}
