namespace Share.AiScheduling.Regulation;

/// <summary>
/// 压力治理计算结果（纯数据）：上下文压力 / 信息增益压力（含归一速率）/ 复杂度压力 / 加权总分。
/// HysteresisHeld：迟滞保持位——上次已触发且 Score 未回落到复位阈值以下，抑制重构后立刻再次触发（防打转）。
/// </summary>
public sealed record PressureResult(
    double ContextPressure,
    double InfoGainPressure,
    double InfoGainRate,
    double ComplexityPressure,
    double Score,
    bool HysteresisHeld = false);
