using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Fold;

namespace Operation.LoopFoldMethod.Operator;

/// <summary>
/// 外部算子端口：<c>FoldState + FoldContext → (FoldState, FoldVertex)</c> —— 唯一外部驱动面。
/// LLM / 规则表 / 人审都只是本端口的一种实现；折叠器不感知具体实现（LLM-free）。
/// <para>
/// 设计文档契约 <c>IFoldOperator&lt;in TState, in TContext, out TNextState, out TVertex&gt;</c> 收敛：
/// TNextState=TState（类型化 <see cref="FoldTrace{TState}"/> 要求同构带，全部状态点同一类型）；
/// TContext=<see cref="FoldContext{TVertex}"/>（坐标+预算+前置顶点，折叠器可构造注入）。
/// TState/TVertex 因分别出现在输入（ctx.PreviousVertices 携带 TVertex）与输出（FoldStep.Next/Vertex）两侧，
/// 取不变方差。
/// </para>
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
public interface IFoldOperator<TState, TVertex>
{
    /// <summary>把当前状态点展开出下一状态点 + 该步顶点（算子结论）。</summary>
    /// <param name="state">当前状态点（只读）。</param>
    /// <param name="ctx">折叠上下文（坐标 + 预算 + 前置顶点，只读）。</param>
    /// <param name="ct">取消令牌（探索终止载体；算子应观察并在取消时抛 OperationCanceledException）。</param>
    Task<FoldStep<TState, TVertex>> Expand(TState state, FoldContext<TVertex> ctx, CancellationToken ct);
}
