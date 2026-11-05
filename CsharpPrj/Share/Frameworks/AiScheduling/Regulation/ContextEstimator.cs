using System;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// 上下文占用估算器（兼容门面）：公式单源在 <see cref="Modules.Regulation.ContextEstimator"/>，
/// 本类型仅做本程序集既有 API 的类型映射，禁止复制派生公式。
/// </summary>
public static class ContextEstimator
{
    /// <summary>每条消息估算常数字符数（LLM 往返的固定开销：角色/系统字段等）。</summary>
    public const int PerMessageChars = Modules.Regulation.ContextEstimator.PerMessageChars;

    /// <summary>字符→token 经验系数（中英混合场景近似）。</summary>
    public const double CharsPerToken = Modules.Regulation.ContextEstimator.CharsPerToken;

    /// <param name="realTokens">真实输入 token 用量（null/≤0 = 缺失，走派生）。</param>
    public static ContextEstimate Estimate(long? realTokens, long resultTextLength, int messageCount, long baseChars = 0)
    {
        var estimate = Modules.Regulation.ContextEstimator.Estimate(realTokens, resultTextLength, messageCount, baseChars);
        return new ContextEstimate(estimate.EstimatedChars, estimate.Tokens);
    }
}
