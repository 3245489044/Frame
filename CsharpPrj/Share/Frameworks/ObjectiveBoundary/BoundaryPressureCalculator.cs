using BoundaryContextEstimate = Share.AiScheduling.Regulation.ContextEstimate;
using Share.TaskStatusPool.Loops;
using Share.ObjectiveBoundary;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界压力计算器（纯数值，无副作用）—— 委托共享内核 <see cref="Modules.Regulation.PressureGovernor"/>
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
        BoundaryContextEstimate estimate,
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
        var sig = new Modules.Regulation.RegulationSignal(
            Round: signal.Round,
            ResultTextLength: signal.ResultTextLength,
            NoProgressSteps: signal.NoProgressSteps,
            TokenValue: signal.TokenValue,
            TokenMax: signal.TokenMax,
            EvalIsPass: signal.Eval == EvalOutcome.Pass);

        var moduleEstimate = new Modules.Regulation.ContextEstimate(estimate.EstimatedChars, estimate.Tokens);

        var w = new Modules.Regulation.PressureWeights
        {
            Context = weights.Context,
            InfoGain = weights.InfoGain,
            Complexity = weights.Complexity,
            ComplexitySteps = weights.ComplexitySteps,
        };

        var r = Modules.Regulation.PressureGovernor.Compute(
            sig, moduleEstimate, priorResultLength, contextLimitChars, contextWatermark,
            infoGainMinRate, maxRounds, w, wasTriggered, hysteresisReleaseThreshold,
            smoothedRate, complexityMaxSteps);

        return new BoundaryPressures(r.ContextPressure, r.InfoGainPressure, r.InfoGainRate, r.ComplexityPressure, r.Score, r.HysteresisHeld);
    }

    /// <summary>上下文压力单一实现（供门控场景按需取单一维度）。</summary>
    public static double ContextPressureOf(BoundaryContextEstimate estimate, int contextLimitChars, double watermark)
        => Modules.Regulation.PressureGovernor.ContextPressureOf(
            new Modules.Regulation.ContextEstimate(estimate.EstimatedChars, estimate.Tokens),
            contextLimitChars, watermark);
}
