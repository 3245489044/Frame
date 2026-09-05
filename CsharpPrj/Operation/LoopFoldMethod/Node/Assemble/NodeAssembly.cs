using System.Collections.Generic;
using System.Linq;
using Operation.LoopFoldMethod.Node.Model;

namespace Operation.LoopFoldMethod.Node.Assemble;

/// <summary>
/// 节点装配（编排结果）—— 头/中/尾三段有序节点带，不落库。
/// 由 <see cref="NodeBuilder"/> 依 <see cref="INodeSlotPolicy"/> 把一组节点稳定排布到三段；
/// 消费域经 <see cref="Operation.LoopFoldMethod.Node.Project.INodeProjector{T}"/> 投影为具体形态。
/// </summary>
/// <param name="Head">头部段（稳定前缀）。</param>
/// <param name="Body">中部段（动态区）。</param>
/// <param name="Tail">尾部段（多变后缀）。</param>
public sealed record NodeAssembly(
    IReadOnlyList<NodeItem> Head,
    IReadOnlyList<NodeItem> Body,
    IReadOnlyList<NodeItem> Tail)
{
    /// <summary>空装配（三段皆空；空输入兜底）。</summary>
    public static readonly NodeAssembly Empty =
        new(System.Array.Empty<NodeItem>(), System.Array.Empty<NodeItem>(), System.Array.Empty<NodeItem>());

    /// <summary>按 Head→Body→Tail 展开的全量有序节点序列（消费形态的稳定前缀→后缀顺序）。</summary>
    public IEnumerable<NodeItem> All => Head.Concat(Body).Concat(Tail);

    /// <summary>是否无任何节点。</summary>
    public bool IsEmpty => Head.Count == 0 && Body.Count == 0 && Tail.Count == 0;
}
