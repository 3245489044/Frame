using System;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// 上下文占用估算器（纯函数）：有真实 token 用量时透传（精确优先），否则由已知尺寸派生。
/// 派生公式：EstimatedChars = baseChars + ResultTextLength + MessageCount × PerMessageChars；
/// Tokens = ceil(Chars / CharsPerToken)（中英混合场景经验系数）。
/// </summary>
public static class ContextEstimator
{
    /// <summary>每条消息估算常数字符数（LLM 往返的固定开销：角色/系统字段等）。</summary>
    public const int PerMessageChars = 128;

    /// <summary>字符→token 经验系数（中英混合场景近似）。</summary>
    public const double CharsPerToken = 4.0;

    /// <param name="realTokens">真实输入 token 用量（null/≤0 = 缺失，走派生）。</param>
    public static ContextEstimate Estimate(long? realTokens, long resultTextLength, int messageCount, long baseChars = 0)
    {
        if (realTokens is long t && t > 0)
            return new ContextEstimate((long)(t * CharsPerToken), t);

        long chars = baseChars + resultTextLength + (long)messageCount * PerMessageChars;
        long tokens = (long)Math.Ceiling(chars / CharsPerToken);
        return new ContextEstimate(chars, tokens);
    }
}
