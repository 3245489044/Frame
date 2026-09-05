using ChainGraph.Core;
using ChainGraph.Core.Expansion;
using Share.ScopeFramework;

namespace WorkFlowMethod.GraphUsing.Expansion;

/// <summary>
/// GraphKey 侧 ITemplateExpander 调用方绑定（策略 C 双实现之一）：把 ChainTemplateExpander 的"模板→线性工作流节点序列"展开
/// 适配为通用展开契约。作为"调用方"适配器放置在同时引用 GraphFramework 与 ScopeFramework 的聚合层，
/// 保持 GraphFramework 自身不依赖 ScopeFramework（依赖方向由调用方承载绑定）。
/// 原位于 Share.Expansion（Share 聚合层），现下沉至 Operation 层 WorkFlowMethod —— GraphFramework 的实际消费/编排层。
/// </summary>
public sealed class GraphChainTemplateExpander<TNode> : ITemplateExpander<DataFlowGraphWithChains, object>
    where TNode : class
{
    private readonly ChainTemplateExpander<TNode> _inner;

    public GraphChainTemplateExpander(ChainTemplateExpander<TNode> inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>实现 ITemplateExpander：展开模板为线性工作流节点序列（委托内部 ExpandToWorkflowNodes，dry-run 不执行）。</summary>
    public IReadOnlyList<object> Expand(DataFlowGraphWithChains graph)
        => _inner.ExpandToWorkflowNodes(graph);
}