using System;
using System.Collections.Generic;
using Operation.LoopFoldMethod.Operator;

namespace Operation.LoopFoldMethod.Signal;

/// <summary>
/// 链式信号解析器（通用表驱动，零 LLM）：按「已完成步骤键 → 下一步骤执行键」转移表推进
/// （对齐声明式链的 on_success 语义）。键不在表内 → 无下一步 → 返回 null（链终点，收敛成功 run_to_end）。
/// 首条 ExternalInput 信号 StepKey 取链入口（如 start），映射到第一个实际执行节点。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public sealed class ChainSignalResolver<TState, TVertex> : ISignalResolver<TState, TVertex>
{
    private readonly IReadOnlyDictionary<string, string> _next;

    /// <param name="next">已完成步骤键 → 下一步骤执行键 的转移表（如 {start→collect, collect→analyze, analyze→act}）。</param>
    public ChainSignalResolver(IReadOnlyDictionary<string, string> next)
        => _next = next ?? throw new ArgumentNullException(nameof(next));

    /// <inheritdoc />
    public SignalDirective<TState, TVertex>? Resolve(FoldSignal<TVertex> signal, TState state, FoldContext<TVertex> ctx)
    {
        // Terminal 信号恒终止。
        if (signal.Kind == FoldSignalKind.Terminal) return null;
        // 已完成步骤键不在转移表 → 链终点（无 on_success）→ 终止。
        if (signal.StepKey is null || !_next.TryGetValue(signal.StepKey, out var next)) return null;
        return new SignalDirective<TState, TVertex>(next, state, signal.Vertex);
    }
}
