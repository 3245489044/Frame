using System;
using System.Collections.Generic;
using Operation.LoopFoldMethod.Fold;

namespace Operation.LoopFoldMethod.Cutoff;

/// <summary>
/// 收敛截断策略（唯一权威，SingleImplementation）—— 合并原相等比较版与 IConverger 适配版为单一实现：
/// 折叠头与上一折叠头满足收敛谓词（默认相等）→ <see cref="FoldCut.Success"/>；
/// 预算耗尽为强制执法底线 → <see cref="FoldCut.BudgetExhausted"/>。
/// 判定「前缀已收敛」—— 状态点收敛即达到判定点。有状态（记录上一折叠头），非线程共享安全，供单次折叠游程使用。
/// 收敛谓词（current, previous → bool）由调用方提供，以复用 <c>IConverger.HasConverged</c> 等外部收敛语义
/// （Converge 的「朝不动点变换」与 attempts 计数不混入截断，诚实边界由调用方谓词保证）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public sealed class ConvergenceCutoffPolicy<TState> : ICutoffPolicy<TState>
{
    private readonly Func<TState, TState, bool> _converged;
    private bool _hasPrev;
    private TState? _prev;

    /// <summary>默认相等比较收敛（EqualityComparer）。</summary>
    public ConvergenceCutoffPolicy(IEqualityComparer<TState>? comparer = null)
        => _converged = comparer is null
            ? static (a, b) => EqualityComparer<TState>.Default.Equals(a, b)
            : (a, b) => comparer.Equals(a, b);

    /// <summary>自定义收敛谓词（current, previous）→ bool（如 <c>(a, b) =&gt; converger.HasConverged(a, b)</c>）。</summary>
    public ConvergenceCutoffPolicy(Func<TState, TState, bool> converged)
        => _converged = converged ?? throw new ArgumentNullException(nameof(converged));

    public FoldCut Decide(TState head, int coordinate, FoldBudget budget)
    {
        // 预算执法是强制底线（不信任外部收敛判定）：即使收敛谓词不触发，预算耗尽也必须熔断，避免无限展开。
        if (budget.Exhausted(coordinate))
            return FoldCut.BudgetExhausted;

        // 收敛判定须建立在「确有上一折叠头」之上（_hasPrev），避免 TState 为可空引用类型且 head 为 null 时
        // 谓词 null==null 的误判收敛；同时消除 null 解引用歧义。
        if (_hasPrev && _converged(head, _prev!))
            return FoldCut.Success;

        _prev = head;
        _hasPrev = true;
        return FoldCut.Continue;
    }
}
