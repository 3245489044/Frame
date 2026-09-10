using System;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// 压力治理机（纯数值，无副作用，canonical 单一实现）。
/// 上下文压力 = Clamp(估算占用 / (极限 × 水位))；信息增益压力 = 1 - (速率-下限)/(1-下限)；
/// 复杂度压力 = Clamp(轮次/上限)（token 耗尽且未成功 → 打满）；Score = 三压力加权和（Clamp 归一）。
/// 迟滞（防打转）：上次已触发且 Score 未回落到复位阈值 → HysteresisHeld，抑制重构后立刻再次触发。
/// </summary>
public static class PressureGovernor
{
    /// <summary>
    /// 计算三维压力与加权 Score。参数自配置具名解耦传入，保持纯函数签名。
    /// </summary>
    public static PressureResult Compute(
        RegulationSignal signal,
        ContextEstimate estimate,
        int priorResultLength,
        int contextLimitChars,
        double contextWatermark,
        double infoGainMinRate,
        int maxRounds,
        PressureWeights weights,
        bool wasTriggered = false,
        double hysteresisReleaseThreshold = 0d,
        double? smoothedRate = null,
        int complexityMaxSteps = 25)
    {
        // 上下文压力：估算占用 / (极限 × 水位)（单一实现，见 ContextPressureOf）
        double contextPressure = ContextPressureOf(estimate, contextLimitChars, contextWatermark);

        // 信息增益压力：速率 = 本轮产物长度 / 上轮长度（归一）；有 EWMA 平滑速率（rate_t）时优先用其抗单轮抖动
        double rate = smoothedRate ?? (
            priorResultLength > 0
                ? (double)signal.ResultTextLength / priorResultLength
                : 1.0);
        double minRate = Math.Clamp(infoGainMinRate, 0.001d, 1d);
        double infoPressure = rate <= minRate
            ? 1.0
            : Math.Clamp(1.0 - (rate - minRate) / (1.0 - minRate), 0d, 1d);

        // 复杂度压力：轮次占比（浅） + 累计无进展步数占比（深度，消费 ComplexitySteps 权重）；
        // token 耗尽且无进展 → 打满
        double roundsMax = Math.Max(1, maxRounds);
        double complexityPressure = Math.Clamp((double)signal.Round / roundsMax, 0d, 1d);
        if (weights.ComplexitySteps > 0)
        {
            double stepsMax = Math.Max(1, complexityMaxSteps);
            double stepsRatio = Math.Clamp((double)signal.NoProgressSteps / stepsMax, 0d, 1d);
            // ComplexitySteps 作为深度项的真实权重（默认 0 = 不启用；1.0 时等价于全量计入，行为不变）
            complexityPressure = Math.Clamp(complexityPressure + weights.ComplexitySteps * stepsRatio, 0d, 1d);
        }
        if (signal.TokenMax > 0 && signal.TokenValue >= signal.TokenMax && !signal.EvalIsPass)
            complexityPressure = 1.0;

        // 加权 Score：三压力加权和（Clamp 归一）
        double score = Math.Clamp(EwmaSmoother.WeightedAverage(
            new[] { contextPressure, infoPressure, complexityPressure },
            new[] { weights.Context, weights.InfoGain, weights.Complexity }), 0d, 1d);

        // 迟滞保持：上次已触发 且 Score 未回落到复位阈值 → 抑制重构后立刻再次触发（防打转）
        double release = Math.Clamp(hysteresisReleaseThreshold, 0d, 1d);
        bool hysteresisHeld = wasTriggered && score >= release;

        return new PressureResult(contextPressure, infoPressure, rate, complexityPressure, score, hysteresisHeld);
    }

    /// <summary>上下文压力单一实现（供门控场景按需取单一维度）。</summary>
    public static double ContextPressureOf(ContextEstimate estimate, int contextLimitChars, double watermark)
    {
        double contextLimit = Math.Max(1, (double)contextLimitChars * watermark);
        return Math.Clamp((double)estimate.EstimatedChars / contextLimit, 0d, 1d);
    }

}
