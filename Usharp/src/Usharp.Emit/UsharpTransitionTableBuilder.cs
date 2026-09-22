using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 链转移表构造：<c>[Flow]</c> 的有序步骤序列 → 「已完成步骤键 → 下一步骤键」表。
/// 尾步不在表内 = 链终点（对齐既有链语义「键不在表内 → 无下一步 → run_to_end 收敛」）。
/// <para>表可直接作为 <c>ChainSignalResolver</c> 的构造实参（<c>IReadOnlyDictionary&lt;string,string&gt;</c>，泛型实参由消费方定）。</para>
/// <para>派生逻辑唯一权威 = <see cref="UsharpStepSequence"/>（本类只做 <c>[Flow]</c> → 步骤键序列的适配）。</para>
/// </summary>
public static class UsharpTransitionTableBuilder
{
    /// <summary>按步骤声明序构造转移表（重复步骤键保留首个后继，与链邻接语义一致）。</summary>
    public static IReadOnlyDictionary<string, string> Build(UsharpFlowDecl flow)
        => UsharpStepSequence.ToTransitionTable(UsharpStepSequence.Keys(flow));
}
