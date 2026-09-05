using System;
using System.Threading;
using Operation.LoopFoldMethod.Fold;

namespace Operation.LoopFoldMethod.Cutoff;

/// <summary>
/// 取消截断策略：包装取消令牌 → <see cref="FoldCut.Cancelled"/>；否则委托内层策略裁决。
/// 把「取消」纳入折叠级截断路由（对齐 ICutoffPolicy 契约：预算/轮次/收敛/取消 → FoldCut）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public sealed class CancellationCutoffPolicy<TState> : ICutoffPolicy<TState>
{
    private readonly CancellationToken _ct;
    private readonly ICutoffPolicy<TState> _inner;

    public CancellationCutoffPolicy(CancellationToken ct, ICutoffPolicy<TState> inner)
    {
        _ct = ct;
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public FoldCut Decide(TState head, int coordinate, FoldBudget budget)
    {
        if (_ct.IsCancellationRequested) return FoldCut.Cancelled;
        return _inner.Decide(head, coordinate, budget);
    }
}
