using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>
/// 写边裁决形态（中性，替代 Operation 侧 <c>GraphWriteVerdict</c>）。
/// <para><c>WrittenWithoutPolicy</c> = 该实现无写前门能力（结构性降级；显式可见，禁伪装成 Written）。</para>
/// </summary>
public enum ReferenceWriteVerdict
{
    Written = 0,
    Skipped = 1,
    Denied = 2,
    WrittenWithoutPolicy = 3,
}

/// <summary>写边结构结果（唯一判据载体）：区分「真写 / 策略跳过 / 策略拒绝 / 无门实现」。</summary>
public readonly record struct ReferenceWriteOutcome(
    bool Wrote,
    ReferenceWriteVerdict Verdict,
    string? Reason = null,
    bool TombstoneCleared = false)
{
    public bool Skipped => !Wrote;

    public static ReferenceWriteOutcome Written(bool tombstoneCleared = false, string? reason = null)
        => new(true, ReferenceWriteVerdict.Written, reason, tombstoneCleared);

    public static ReferenceWriteOutcome Blocked(ReferenceWriteVerdict verdict, string reason)
        => new(false, verdict, reason, false);
}

/// <summary>删除边结果状态（中性，替代 Operation 侧 <c>GraphDeleteStatus</c>）。</summary>
public enum ReferenceDeleteStatus
{
    Ok = 0,
    Skipped = 1,
    NotFound = 2,
    Rejected = 3,
}

/// <summary>删除边结果（逐项可见化；禁静默成功）。</summary>
public sealed record ReferenceDeleteOutcome(
    string Key,
    ReferenceDeleteStatus Status,
    string? Message = null);

/// <summary>关系写入角色（开放受控词；桥接层映射到具体图挂载角色）。</summary>
public static class RelationWriteRoles
{
    public const string Business = "business";
    public const string Reconcile = "reconcile";
    public const string Declare = "declare";
}
