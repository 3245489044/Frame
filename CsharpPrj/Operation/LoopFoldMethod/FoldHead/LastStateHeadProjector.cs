using System.Collections.Generic;

namespace Operation.LoopFoldMethod.FoldHead;

/// <summary>末位折叠投影：取状态带最后一个状态点作为判定点（最常用默认）。空带回退 default。</summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public sealed class LastStateHeadProjector<TState> : IFoldHeadProjector<TState>
{
    public TState Head(IReadOnlyList<TState> trace) => trace.Count > 0 ? trace[^1] : default!;
}
