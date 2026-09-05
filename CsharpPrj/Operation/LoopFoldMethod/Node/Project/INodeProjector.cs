using Operation.LoopFoldMethod.Node.Assemble;

namespace Operation.LoopFoldMethod.Node.Project;

/// <summary>
/// 节点投影端口 —— 把 <see cref="NodeAssembly"/>（头/中/尾节点带）投影为消费域的具体形态。
/// 框架只提供端口，不解释节点语义；消费域实现投影器（如提示词域 → LLM 消息列表 / 引用域 → 文档块）。
/// 投影为纯读（只消费装配、不写库），对齐 Reference 投影的只读纪律。
/// </summary>
/// <typeparam name="TTarget">投影目标类型（消费形态）。</typeparam>
public interface INodeProjector<TTarget>
{
    /// <summary>投影：节点装配 → 消费形态。</summary>
    TTarget Project(NodeAssembly assembly);
}
