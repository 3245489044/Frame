using System.Collections.Generic;
using Modules.Node.Materialization;

namespace Operation.HierarchyMethod.History.Content;

/// <summary>一次物化的聚合结果（按位置状态计票 + 可见化原因清单；禁静默）。</summary>
/// <param name="Key">身份键。</param>
/// <param name="Status">聚合状态。</param>
/// <param name="Written">写入位置数。</param>
/// <param name="Unchanged">幂等跳过位置数。</param>
/// <param name="Conflict">冲突位置数。</param>
/// <param name="Rejected">拒写位置数。</param>
/// <param name="Unbound">未绑定位置数。</param>
/// <param name="Reasons">可见化原因清单。</param>
public sealed record NodeContentMaterializeOutcome(
    string Key,
    MaterializeStatus Status,
    int Written,
    int Unchanged,
    int Conflict,
    int Rejected,
    int Unbound,
    IReadOnlyList<string> Reasons)
{
    /// <summary>是否已落库（含幂等跳过）。</summary>
    public bool IsOk => Status is MaterializeStatus.Written or MaterializeStatus.Unchanged;

    /// <summary>单行摘要（诊断/命令回显用）。</summary>
    public string Describe()
        => $"{Key}: {Status}（written={Written} unchanged={Unchanged} conflict={Conflict} rejected={Rejected} unbound={Unbound}）"
           + (Reasons.Count == 0 ? string.Empty : " | " + string.Join("; ", Reasons));
}
