using System;
using System.Collections.Generic;
using Operation.LoopFoldMethod.Operator;

namespace Operation.LoopFoldMethod.Signal;

/// <summary>
/// 规则信号解析器 —— 决策信号化：当信号携带 <see cref="FoldSignal{TVertex}.Decision"/> 时按决策裁决，
/// 否则回退纯表驱动（既有 <see cref="ChainSignalResolver{TState,TVertex}"/> 语义）。零 LLM。
/// 语义对齐：<see cref="FoldDecision.Continue"/> → 查链转移表推进（StepCompleted）；
/// <see cref="FoldDecision.Stop"/> / <see cref="FoldDecision.Error"/> → 链终点（返回 null，收敛）。
/// 决策词→决策的单一映射权威在 Agent.Operation.Loops.DecisionWordClassifier；本解析器只消费信号上的 <see cref="FoldDecision"/>。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public sealed class RuleSignalResolver<TState, TVertex> : ISignalResolver<TState, TVertex>
{
    private readonly ChainSignalResolver<TState, TVertex> _chain;

    /// <param name="next">已完成步骤键 → 下一步骤执行键 的转移表（如 {start→collect, collect→analyze, analyze→act}）。</param>
    public RuleSignalResolver(IReadOnlyDictionary<string, string> next)
        => _chain = new ChainSignalResolver<TState, TVertex>(next ?? throw new ArgumentNullException(nameof(next)));

    /// <inheritdoc />
    public SignalDirective<TState, TVertex>? Resolve(FoldSignal<TVertex> signal, TState state, FoldContext<TVertex> ctx)
    {
        // 显式决策载荷：Stop/Error → 链终点（终止）；Continue → 链推进。
        if (signal.Decision is { } decision)
        {
            if (decision != FoldDecision.Continue) return null;
            return _chain.Resolve(signal, state, ctx);
        }
        // 无决策载荷 → 回退纯表驱动（既有 ChainSignalResolver 语义；Terminal 恒终止由链解析器保证）。
        return _chain.Resolve(signal, state, ctx);
    }
}
