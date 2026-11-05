using Share.AiScheduling.Regulation;
using Share.TaskStatusPool.Loops;
using System;
using System.Linq;

namespace Share.ObjectiveBoundary;

/// <summary>
/// 逻辑边界评估器（二级评估的纯函数，无副作用）。由每轮 BoundarySignal + 上下文估算 + 上一轮产物长度
/// 计算三维压力（上下文/信息增益/复杂度），加权 Score ≈ 文档 Φ(Ω,J;Context) 的工程近似；
/// 触发判定 = 硬信号短路，或（无迟滞保持时）任一维度按各自量纲越界 / Score≥阈值；
/// 建议动作按触发种类从 Actions 白名单选取（非恒取 Actions[0]）。
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
        bool wasTriggered = false,
        double priorPhi = 0d)
    {
        var logical = config.Logical;

        // 压力数值计算仍收敛于单一计算内核（AS-01 口径合并由独立会话负责，此处不改换算）。
        var p = BoundaryPressureCalculator.Compute(
            signal, estimate, priorResultLength,
            logical.ContextLimitChars, logical.ContextWatermark,
            logical.InfoGainMinRate, logical.MaxRounds, logical.SignalWeights,
            wasTriggered: wasTriggered,
            hysteresisReleaseThreshold: logical.HysteresisReleaseThreshold,
            smoothedRate: signal.SmoothedRate,
            complexityMaxSteps: logical.ComplexityMaxSteps);

        bool stopBreach = signal.StopReason?.Equals(StopReasonContextExceeded, StringComparison.OrdinalIgnoreCase) == true;
        bool tokenExhaust = signal.TokenMax > 0 && signal.TokenValue >= signal.TokenMax && signal.Eval != EvalOutcome.Pass;
        bool errorClass = signal.ErrorClass is ErrorClass.Irrecoverable or ErrorClass.ExecutionFailure;

        // 分维度阈值（ObjectiveBoundary.md §4.1：同一 threshold 判三种量纲是错的）。
        // 上下文/复杂度：压力越大越坏；信息增益：速率越小越坏（对齐 §4.7 与 InfoGainMinRate 名称语义）。
        double scoreThreshold = Math.Clamp(logical.ReconstructThreshold, 0d, 1d);
        double contextThreshold = ResolveThreshold(logical.ContextThreshold, scoreThreshold);
        double complexityThreshold = ResolveThreshold(logical.ComplexityThreshold, scoreThreshold);
        bool contextBreach = p.ContextPressure >= contextThreshold;
        bool infoGainBreach = p.InfoGainRate <= logical.InfoGainMinRate;
        bool complexityBreach = p.ComplexityPressure >= complexityThreshold;

        // 硬信号（协议硬断 / token 耗尽 / 错误熔断）不受迟滞抑制（§4.2：迟滞只应压软阈值，不能压硬信号）。
        bool hardSignal = stopBreach || tokenExhaust || errorClass;
        bool softBreach = contextBreach || infoGainBreach || complexityBreach || p.Score >= scoreThreshold;
        bool triggered = hardSignal || (!p.HysteresisHeld && softBreach);

        BoundaryTriggerKind kind = triggered
            ? ClassifyKind(stopBreach, tokenExhaust, errorClass, contextBreach, infoGainBreach)
            : BoundaryTriggerKind.None;

        string action = triggered
            ? SelectAction(kind, config)
            : BoundaryActionKind.None;

        var reason = triggered
            ? BuildReason(stopBreach, tokenExhaust, errorClass,
                contextBreach, infoGainBreach, complexityBreach,
                p.ContextPressure, p.InfoGainRate, p.ComplexityPressure, p.Score,
                contextThreshold, complexityThreshold, logical.InfoGainMinRate)
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
            // 上一轮 Φ 由调用方传入；不得用本轮分数覆盖（§4.4）。
            PhiOld: priorPhi,
            Channel: TriggerChannel.Reactive,
            TriggerKind: kind,
            HysteresisHeld: p.HysteresisHeld);
    }

    private static double ResolveThreshold(double configured, double fallback)
        => configured > 0d ? Math.Clamp(configured, 0d, 1d) : fallback;

    /// <summary>脊神经短路口归类（判定顺序与触发判定一致：硬信号优先，随后按分维度阈值）。</summary>
    private static BoundaryTriggerKind ClassifyKind(
        bool stopBreach, bool tokenExhaust, bool errorClass,
        bool contextBreach, bool infoGainBreach)
    {
        if (stopBreach) return BoundaryTriggerKind.ProtocolStop;
        if (tokenExhaust) return BoundaryTriggerKind.TokenExhaust;
        if (errorClass) return BoundaryTriggerKind.ErrorClass;
        if (contextBreach) return BoundaryTriggerKind.ContextPressure;
        if (infoGainBreach) return BoundaryTriggerKind.InfoGain;
        return BoundaryTriggerKind.Complexity;
    }

    /// <summary>
    /// 按触发种类选取动作（§4.3：原实现恒取 Actions[0]，使其余动作与 Fallback 永不可达）。
    /// 首选动作不在允许清单时回退 Policy.Fallback；再不行取清单首个非 None。
    /// </summary>
    private static string SelectAction(BoundaryTriggerKind kind, BoundaryConfig config)
    {
        var logical = config.Logical;
        var actions = logical.Actions ?? new System.Collections.Generic.List<string>();
        string preferred = kind switch
        {
            BoundaryTriggerKind.ProtocolStop or BoundaryTriggerKind.TokenExhaust or BoundaryTriggerKind.ContextPressure
                => BoundaryActionKind.Compact,
            BoundaryTriggerKind.InfoGain => BoundaryActionKind.ResubmitWithInfo,
            BoundaryTriggerKind.Complexity => BoundaryActionKind.Rescope,
            BoundaryTriggerKind.ErrorClass => BoundaryActionKind.Escalate,
            _ => config.Policy.Fallback ?? BoundaryActionKind.Compact,
        };

        if (IsAllowed(actions, preferred)) return preferred;

        // Fallback 可达：首选动作被配置移出白名单时，按策略兜底。
        var fallback = config.Policy.Fallback;
        if (!string.IsNullOrWhiteSpace(fallback) && IsAllowed(actions, fallback)) return fallback;

        return actions.FirstOrDefault(a => !IsNone(a)) ?? BoundaryActionKind.Compact;
    }

    private static bool IsAllowed(System.Collections.Generic.List<string> actions, string? action)
        => !string.IsNullOrWhiteSpace(action)
           && !IsNone(action)
           && actions.Any(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));

    private static bool IsNone(string? action)
        => string.Equals(action, BoundaryActionKind.None, StringComparison.OrdinalIgnoreCase);

    private static string? BuildReason(
        bool stopBreach, bool tokenExhaust, bool errorClass,
        bool contextBreach, bool infoGainBreach, bool complexityBreach,
        double ctxP, double infoRate, double compP, double score,
        double contextThreshold, double complexityThreshold, double infoGainMinRate)
    {
        if (stopBreach) return "模型上下文窗口已满（model_context_window_exceeded），需压缩/重构边界";
        if (tokenExhaust) return "token 耗尽且本轮未通过，需压缩/重构边界";
        if (errorClass) return "错误熔断（不可恢复/执行失败），需升级边界";

        var parts = new System.Collections.Generic.List<string>();
        if (contextBreach)
            parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "上下文压力超阈({0:0.00}≥{1:0.00})", ctxP, contextThreshold));
        if (infoGainBreach)
            parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "信息增益趋零(rate {0:0.00}≤{1:0.00})", infoRate, infoGainMinRate));
        if (complexityBreach)
            parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "复杂度超阈({0:0.00}≥{1:0.00})", compP, complexityThreshold));

        return parts.Count > 0
            ? string.Join("；", parts) + string.Format(System.Globalization.CultureInfo.InvariantCulture, "，压力总分 {0:0.00}", score)
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, "边界压力 {0:0.00} 达阈值", score);
    }
}
