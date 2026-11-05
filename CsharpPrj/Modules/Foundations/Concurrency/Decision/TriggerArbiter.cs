using System;

namespace Modules.Concurrency;

/// <summary>触发紧迫度：硬信号短路优先于软阈值，且不受通道 enabled 影响。</summary>
public enum TriggerUrgency
{
    Soft = 0,
    Hard = 1,
}

/// <summary>单一触发候选（中性事实，由宿主把业务评估映射为本类型）。</summary>
public sealed record TriggerCandidate(
    string Name,
    bool Enabled,
    bool Triggered,
    int Priority,
    TriggerUrgency Urgency,
    string? Reason = null)
{
    public bool Eligible => Enabled && Triggered;
}

/// <summary>仲裁结果：胜出通道名 / 是否仅 advisory（未触发但保留提示）。</summary>
public sealed record TriggerArbitration(
    string? Winner,
    bool Advisory,
    string? Reason)
{
    public static readonly TriggerArbitration None = new(null, false, null);
}

/// <summary>
/// 触发仲裁唯一内核（原 ObjectiveBoundary.BoundaryTriggerArbiter 的多分支逻辑上移）：
///  1. 硬信号永远短路（不读 enabled）；
///  2. 双通道可触发 → Priority 高者胜（相等时先声明的通道胜，保证确定性）；
///  3. 仅开启通道生效；
///  4. 未触发但携带 Reason 的候选按 advisory 透传（不丢弃提示）。
/// 零 Share 依赖：通道名与业务槽位由调用方映射。
/// </summary>
public static class TriggerArbiter
{
    public static TriggerArbitration Resolve(TriggerCandidate first, TriggerCandidate second)
    {
        if (first == null || second == null)
            return TriggerArbitration.None;

        // 1) 硬信号：短路优先。
        if (first.Triggered && first.Urgency == TriggerUrgency.Hard)
            return new TriggerArbitration(first.Name, false, first.Reason);
        if (second.Triggered && second.Urgency == TriggerUrgency.Hard)
            return new TriggerArbitration(second.Name, false, second.Reason);

        // 2) 双通道可触发 → Priority 裁决（相等时通道 1 胜，确定性）。
        if (first.Eligible && second.Eligible)
            return first.Priority >= second.Priority
                ? new TriggerArbitration(first.Name, false, first.Reason)
                : new TriggerArbitration(second.Name, false, second.Reason);

        // 3) 单通道可触发。
        if (first.Eligible) return new TriggerArbitration(first.Name, false, first.Reason);
        if (second.Eligible) return new TriggerArbitration(second.Name, false, second.Reason);

        // 4) advisory：未触发但有意说明。
        if (first.Enabled && !first.Triggered && !string.IsNullOrEmpty(first.Reason))
            return new TriggerArbitration(first.Name, true, first.Reason);
        if (second.Enabled && !second.Triggered && !string.IsNullOrEmpty(second.Reason))
            return new TriggerArbitration(second.Name, true, second.Reason);

        return TriggerArbitration.None;
    }
}
