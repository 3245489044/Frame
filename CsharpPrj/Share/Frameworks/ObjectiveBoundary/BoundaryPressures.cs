using Share.TaskStatusPool.Loops;
namespace Share.ObjectiveBoundary;

/// <summary>
/// 逻辑边界三维压力 + 加权 Score 的计算结果（纯数据）。承载文档 Φ(Ω,J;Context) 的工程近似：
/// 上下文压力 / 信息增益压力（含归一速率）/ 复杂度压力 / 加权总分。
/// HysteresisHeld：迟滞保持位——上次已触发且 Score 未回落到复位阈值以下，抑制重构后立刻再次触发（防打转）。
/// </summary>
public sealed record BoundaryPressures(
    double ContextPressure,
    double InfoGainPressure,
    double InfoGainRate,
    double ComplexityPressure,
    double Score,
    bool HysteresisHeld = false);
