namespace Modules.Observe;

/// <summary>变更结果。Committed=已收敛（I3 成立）；Degraded=显式降级（收敛界内未完成，I4，绝非静默成功）；Failed=发送/执行失败。</summary>
public enum ChangeResult
{
    Committed,
    Degraded,
    Failed
}
