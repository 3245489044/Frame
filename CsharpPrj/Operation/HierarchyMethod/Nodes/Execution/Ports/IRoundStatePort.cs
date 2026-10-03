using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 轮状态端口（Tasks 层复用；seed/harvest 钩子）—— 一轮开始注入种子状态、结束时回写跨轮状态。
/// Agent 侧以 RoundStateMapper（StateCenter/AxiomContext 适配）+ 终态信封补写实现。
/// 未装配 → seed/harvest 均 no-op（纯内存 DataSpace 直跑/测试路径）。
/// </summary>
public interface IRoundStatePort
{
    /// <summary>一轮开始：把种子状态注入编排数据空间（Vars），供步骤消费。</summary>
    Task SeedAsync(RoundSpec spec, NodeRoundContext ctx, CancellationToken ct);

    /// <summary>一轮结束：把轮内数据空间回写跨轮状态 + 终态信封补写（节点输出端口）。</summary>
    Task HarvestAsync(RoundSpec spec, NodeRoundContext ctx, CancellationToken ct);
}
