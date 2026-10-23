using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 收割端口（D05）：轮末回写跨轮状态 + 终态补写的单一契约面。
/// 供 <see cref="IRoundStatePort"/> 继承，使「只收割不播种」的实现（runner 装饰器）可单独依赖。
/// </summary>
public interface IHarvestPort
{
    /// <summary>一轮结束：把轮内数据空间回写跨轮状态（实现方负责终态信封补写）。</summary>
    Task HarvestAsync(RoundSpec spec, NodeRoundContext ctx, CancellationToken ct);
}

/// <summary>
/// 轮状态端口（Tasks 层复用；seed/harvest 钩子）—— 一轮开始注入种子状态、结束时回写跨轮状态。
/// Agent 侧以 RoundStateMapper（StateCenter/AxiomContext 适配）+ 终态信封补写实现。
/// 未装配 → seed/harvest 均 no-op（纯内存 DataSpace 直跑/测试路径）。
/// </summary>
public interface IRoundStatePort : IHarvestPort
{
    /// <summary>一轮开始：把种子状态注入编排数据空间（Vars），供步骤消费。</summary>
    Task SeedAsync(RoundSpec spec, NodeRoundContext ctx, CancellationToken ct);
}
