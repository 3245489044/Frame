using System.Collections.Generic;

namespace Operation.LoopFoldMethod.FoldHead;

/// <summary>恒等折叠投影：取状态带首个状态点（初始点 s₀）作为判定点。空带回退 default。</summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public sealed class IdentityHeadProjector<TState> : IFoldHeadProjector<TState>
{
    public TState Head(IReadOnlyList<TState> trace) => trace.Count > 0 ? trace[0] : default!;
}
