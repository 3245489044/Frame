using System;

namespace Share.AiScheduling.Regulation;

/// <summary>
/// 压力治理机（兼容门面）：三维压力与迟滞语义单源在 <see cref="Modules.Regulation.PressureGovernor"/>，
/// 本类型仅把本程序集既有 DTO 映射到模块口径，禁止复制公式。
/// </summary>
public static class PressureGovernor
{
    /// <summary>
    /// 计算三维压力与加权 Score。参数自配置具名解耦传入，保持纯函数签名。
    /// </summary>
    /// <param name="hysteresisReleaseThreshold">迟滞复位阈值（0~1）；≤0 表示未启用迟滞。</param>
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
        var moduleSignal = new Modules.Regulation.RegulationSignal(
            signal.Round, signal.ResultTextLength, signal.NoProgressSteps,
            signal.TokenValue, signal.TokenMax, signal.EvalIsPass);
        var moduleEstimate = new Modules.Regulation.ContextEstimate(estimate.EstimatedChars, estimate.Tokens);
        var moduleWeights = new Modules.Regulation.PressureWeights
        {
            Context = weights.Context,
            InfoGain = weights.InfoGain,
            Complexity = weights.Complexity,
            ComplexitySteps = weights.ComplexitySteps,
        };

        var result = Modules.Regulation.PressureGovernor.Compute(
            moduleSignal, moduleEstimate, priorResultLength, contextLimitChars, contextWatermark,
            infoGainMinRate, maxRounds, moduleWeights, wasTriggered, hysteresisReleaseThreshold,
            smoothedRate, complexityMaxSteps);

        return new PressureResult(
            result.ContextPressure, result.InfoGainPressure, result.InfoGainRate,
            result.ComplexityPressure, result.Score, result.HysteresisHeld);
    }

    /// <summary>上下文压力单一实现（供门控场景按需取单一维度）。</summary>
    public static double ContextPressureOf(ContextEstimate estimate, int contextLimitChars, double watermark)
        => Modules.Regulation.PressureGovernor.ContextPressureOf(
            new Modules.Regulation.ContextEstimate(estimate.EstimatedChars, estimate.Tokens),
            contextLimitChars, watermark);
}
