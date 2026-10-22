using Share.AiScheduling.Regulation;
using Share.TaskStatusPool.Loops;
using Share.ObjectiveBoundary;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界压力计算器（纯数值，无副作用）—— 委托共享框架 <see cref="Share.AiScheduling.Regulation.PressureGovernor"/>
/// 单一实现（canonical）。上下文压力 = Clamp(估算占用/(极限×水位))；信息增益压力 = 1-(速率-下限)/(1-下限)；
/// 复杂度压力 = Clamp(轮次/上限)（token 耗尽且未成功 → 打满）；Score = 三压力加权和（Clamp 归一）；
/// 迟滞（防打转）：上次已触发且 Score 未回落复位阈值 → HysteresisHeld。
/// 供 LogicalBoundaryEvaluator（触发/动作决策）与 ProactiveBoundaryTrigger（上下文压力门控）共用。
/// </summary>
public static class BoundaryPressureCalculator
{
    /// <summary>计算三维压力与加权 Score。参数自 LogicalBoundaryConfig 具名解耦传入，保持纯函数签名。</summary>
    public static BoundaryPressures Compute(
        BoundarySignal signal,
        ContextEstimate estimate,
        int priorResultLength,
        int contextLimitChars,
        double contextWatermark,
        double infoGainMinRate,
        int maxRounds,
        BoundaryWeights weights,
        bool wasTriggered = false,
        double hysteresisReleaseThreshold = 0d,
        double? smoothedRate = null,
        int complexityMaxSteps = 25)
    {
        var sig = new Share.AiScheduling.Regulation.RegulationSignal(
            Round: signal.Round,
            ResultTextLength: signal.ResultTextLength,
            NoProgressSteps: signal.NoProgressSteps,
            TokenValue: signal.TokenValue,
            TokenMax: signal.TokenMax,
            EvalIsPass: signal.Eval == EvalOutcome.Pass);

        var w = new Share.AiScheduling.Regulation.PressureWeights
        {
            Context = weights.Context,
            InfoGain = weights.InfoGain,
            Complexity = weights.Complexity,
            ComplexitySteps = weights.ComplexitySteps,
        };

        var r = Share.AiScheduling.Regulation.PressureGovernor.Compute(
            sig, estimate, priorResultLength, contextLimitChars, contextWatermark,
            infoGainMinRate, maxRounds, w, wasTriggered, hysteresisReleaseThreshold,
            smoothedRate, complexityMaxSteps);

        return new BoundaryPressures(r.ContextPressure, r.InfoGainPressure, r.InfoGainRate, r.ComplexityPressure, r.Score, r.HysteresisHeld);
    }

    /// <summary>上下文压力单一实现（供门控场景按需取单一维度）。</summary>
    public static double ContextPressureOf(ContextEstimate estimate, int contextLimitChars, double watermark)
    {
        return Share.AiScheduling.Regulation.PressureGovernor.ContextPressureOf(estimate, contextLimitChars, watermark);
    }
}
