namespace Operation.DataStoreDriven.Graph;

/// <summary>图库删除结果状态。</summary>
public enum GraphDeleteStatus
{
    /// <summary>墓碑已写 **且** 物理删除已完成。</summary>
    Ok = 0,

    /// <summary>目标已不存在且墓碑已存在（幂等重试）。</summary>
    Skipped = 1,

    /// <summary>目标不存在且未写墓碑。</summary>
    NotFound = 2,

    /// <summary>写墓碑 / 物理删除失败（可见化原因）。</summary>
    Rejected = 3,
}

/// <summary>图库删除结果（逐项可见化；禁静默成功）。</summary>
/// <param name="Key">删除目标键。</param>
/// <param name="Status">结果状态。</param>
/// <param name="Message">可见化消息（失败原因 / 命中数量等）。</param>
/// <param name="TombstoneKey">本次写入的墓碑键（可空）。</param>
public sealed record GraphDeleteOutcome(
    string Key,
    GraphDeleteStatus Status,
    string? Message = null,
    string? TombstoneKey = null);
