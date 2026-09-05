using System.Collections.Generic;
using BasicMethod.Calculus;
using Operation.LoopFoldMethod.FoldHead;

namespace Operation.LoopFoldMethod.Adapters;

/// <summary>
/// 算子层复用适配器（方案 A 薄适配）：<see cref="IIntegrator{TState}"/> → <see cref="IFoldHeadProjector{TState}"/>。
/// 把只增带用二目合并折叠成当前判定点：Head(trace) = fold(trace, Reset, Integrate)。
/// 纯端口包装，无引擎行为 —— 折叠头投影只是把带收敛成点，循环推进/轨迹产出仍在 FoldEngine 侧。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public sealed class IntegratorFoldHead<TState>(
    IIntegrator<TState> integrator)
    : IFoldHeadProjector<TState>
{
    /// <summary>折叠只增前缀 → 当前判定点（accumulate via Integrate，Reset 起累积器）。</summary>
    public TState Head(IReadOnlyList<TState> trace)
    {
        var head = integrator.Reset();
        foreach (var point in trace)
            head = integrator.Integrate(head, point);
        return head;
    }
}
