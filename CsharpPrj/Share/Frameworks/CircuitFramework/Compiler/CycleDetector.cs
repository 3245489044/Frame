using Share.CircuitFramework.Core;
using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 环检测：图上的环只允许由 Recursion 符号产生，其余任何边不得成环。
/// 对 Wire 图（数据流依赖）求简单环集合。Recursion 的"回边"是符号声明的，不落成 Wire，因此 Wire 图环即为非法环。
/// <para>实现委托 <see cref="Projection.GraphProcessingAdapter.FindCycles"/>（→ Modules.GraphProcessing.Algorithms.DefaultCycleDetector&lt;string&gt;）。</para>
/// </summary>
public static class CycleDetector
{
    /// <summary>返回 Wire 上构成的有向环，每个环是一组节点 id（沿依赖顺序排列，首尾相接）。</summary>
    public static IReadOnlyList<IReadOnlyList<string>> FindCycles(Circuit circuit) =>
        GraphProcessingAdapter.FindCycles(circuit);
}