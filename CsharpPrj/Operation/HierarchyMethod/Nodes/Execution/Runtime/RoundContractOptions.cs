using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>执行契约严格度（F5）：<c>Report</c> = 违规可见化不中断；<c>Throw</c> = 违规即抛。</summary>
public enum RoundContractStrictness
{
    /// <summary>只可见化（默认；迁移期不误杀存量流）。</summary>
    Report,

    /// <summary>违规即抛（生产姿态）。</summary>
    Throw,
}

/// <summary>
/// 轮执行契约选项（F5）：由 <see cref="NodeRoundContext"/> 携带，取代进程级静态开关
/// （静态开关无法按装配姿态/测试隔离）。
/// </summary>
/// <param name="Strictness">严格度（默认 Report）。</param>
/// <param name="InternalKeys">实现内部键（非端口）：E2 判据据此不误报 KeyMapping 补填键。</param>
public sealed record RoundContractOptions(
    RoundContractStrictness Strictness = RoundContractStrictness.Report,
    IReadOnlyCollection<string>? InternalKeys = null)
{
    /// <summary>默认选项（Report + 空豁免）。</summary>
    public static RoundContractOptions Default { get; } = new();
}
