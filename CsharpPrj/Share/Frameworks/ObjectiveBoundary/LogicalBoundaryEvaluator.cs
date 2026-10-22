using Share.AiScheduling.Regulation;
using Share.TaskStatusPool.Loops;
using System;
using Share.ObjectiveBoundary;
using System.Collections.Generic;
using System.Linq;
using Share.ObjectiveBoundary;
using Share.ObjectiveBoundary;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 逻辑边界评估器（二级评估的纯函数，无副作用）。由每轮 BoundarySignal + 上下文估算 + 上一轮产物长度
/// 计算三维压力（上下文/信息增益/复杂度），加权 Score ≈ 文档 Φ(Ω,J;Context) 的工程近似；
/// 触发判定 = 任一维度压力越界 或 加权 Score≥阈值，并从 Actions 顺序选取建议动作。
/// stopReason=model_context_window_exceeded 为强信号短路（对齐协议级硬截断语义）。
/// </summary>
public static class LogicalBoundaryEvaluator
{
    /// <summary>强信号：模型响应填满上下文窗口（协议层硬截断）。</summary>
    public const string StopReasonContextExceeded = "model_context_window_exceeded";

    public static BoundaryAssessment Evaluate(
        BoundarySignal signal,
        BoundaryConfig config,
        ContextEstimate estimate,
        int priorResultLength,
        bool wasTriggered = false)
    {
        var logical = config.Logical;

        // 三维压力 + 加权 Score 的纯数值计算收敛到计算内核（Agent.Calculate，单一实现）；
        // 本层仅保留触发判定与动作选择（逻辑运算）。
        var p = BoundaryPressureCalculator.Compute(
            signal, estimate, priorResultLength,
            logical.ContextLimitChars, logical.ContextWatermark,
            logical.InfoGainMinRate, logical.MaxRounds, logical.SignalWeights,
            wasTriggered: wasTriggered,
            hysteresisReleaseThreshold: logical.HysteresisReleaseThreshold,
            smoothedRate: signal.SmoothedRate,
            complexityMaxSteps: logical.ComplexityMaxSteps);

        bool stopBreach = signal.StopReason?.Equals(StopReasonContextExceeded, StringComparison.OrdinalIgnoreCase) == true;
        double threshold = Math.Clamp(logical.ReconstructThreshold, 0d, 1d);
        // 触发判定（对齐文档"信息增益速率低于阈值即触发重构"）：任一维度压力越界，或加权总分越界，即触发。
        bool anyDimension = p.ContextPressure >= threshold || p.InfoGainPressure >= threshold || p.ComplexityPressure >= threshold;
        // 迟滞：刚重构后（wasTriggered）Score 未回落到复位阈值 → 抑制非强信号触发（防打转）。
        bool triggered = stopBreach || (!p.HysteresisHeld && (anyDimension || p.Score >= threshold));

        string action = BoundaryActionKind.None;
        if (triggered)
        {
            action = stopBreach
                ? BoundaryActionKind.Compact
                : logical.Actions.FirstOrDefault(a => !string.Equals(a, BoundaryActionKind.None, StringComparison.OrdinalIgnoreCase))
                    ?? config.Policy.Fallback ?? BoundaryActionKind.Compact;
        }

        var reason = triggered
            ? BuildReason(stopBreach, p.ContextPressure, p.InfoGainPressure, p.ComplexityPressure, p.Score)
            : null;

        return new BoundaryAssessment(
            Triggered: triggered,
            Score: p.Score,
            ContextPressure: p.ContextPressure,
            InfoGainRate: p.InfoGainRate,
            ComplexityPressure: p.ComplexityPressure,
            Action: action,
            Reason: reason,
            EffectiveRoot: null,
            SpatialAllowed: true,
            SpatialDenyReason: null,
            PhiOld: p.Score,
            HysteresisHeld: p.HysteresisHeld,
            // 种类归类与触发判定同源（同一 threshold/pressure）：Reactive 侧不再重算阈值（D5 单源）。
            TriggerKind: ClassifyKind(signal, threshold, p, stopBreach));
    }

    /// <summary>脊神经短路口归类（判定顺序与触发/动作选择一致：硬信号短路优先）。</summary>
    private static BoundaryTriggerKind ClassifyKind(
        BoundarySignal signal, double threshold, BoundaryPressures p, bool stopBreach)
    {
        if (stopBreach) return BoundaryTriggerKind.ProtocolStop;
        if (signal.TokenMax > 0 && signal.TokenValue >= signal.TokenMax && signal.Eval != EvalOutcome.Pass)
            return BoundaryTriggerKind.TokenExhaust;
        if (signal.ErrorClass is ErrorClass.Irrecoverable or ErrorClass.ExecutionFailure)
            return BoundaryTriggerKind.ErrorClass;
        if (p.ContextPressure >= threshold) return BoundaryTriggerKind.ContextPressure;
        if (p.InfoGainPressure >= threshold) return BoundaryTriggerKind.InfoGain;
        return BoundaryTriggerKind.Complexity;
    }

    private static string? BuildReason(
        bool stopBreach, double ctxP, double infoP, double compP, double score)
    {
        if (stopBreach) return "模型上下文窗口已满（model_context_window_exceeded），需压缩/重构边界";
        var parts = new List<string>();
        if (ctxP >= 0.9) parts.Add($"上下文压力高({ctxP:0.00})");
        if (infoP >= 0.9) parts.Add($"信息增益趋零({infoP:0.00})");
        if (compP >= 0.9) parts.Add($"复杂度超界({compP:0.00})");
        return parts.Count > 0 ? string.Join("；", parts) + $"，压力总分 {score:0.00}" : $"边界压力 {score:0.00} 达阈值";
    }
}
