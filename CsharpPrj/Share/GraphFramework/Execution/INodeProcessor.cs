
#nullable enable

using System;
using System.Threading.Tasks;
using GraphFoundation.Abstractions;
using GraphFramework.Models;

namespace GraphFramework.Execution
{
    public interface INodeProcessor<TKey, TNode, TEdge, TContext>
        where TKey : notnull
        where TNode : class
        where TEdge : class
        where TContext : class
    {
        string Name { get; }

        bool CanProcess(TKey nodeKey, TNode nodeData, TContext context);

        Result<TNode> Process(TKey nodeKey, TNode nodeData, TContext context);

        Task<Result<TNode>> ProcessAsync(TKey nodeKey, TNode nodeData, TContext context);
    }

    public abstract class NodeProcessorBase<TKey, TNode, TEdge, TContext>
        : INodeProcessor<TKey, TNode, TEdge, TContext>
        where TKey : notnull
        where TNode : class
        where TEdge : class
        where TContext : class
    {
        public string Name { get; }

        protected NodeProcessorBase(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public abstract bool CanProcess(TKey nodeKey, TNode nodeData, TContext context);

        public abstract Result<TNode> Process(TKey nodeKey, TNode nodeData, TContext context);

        public virtual Task<Result<TNode>> ProcessAsync(TKey nodeKey, TNode nodeData, TContext context)
        {
            return Task.FromResult(Process(nodeKey, nodeData, context));
        }
    }

    public interface IChainExecutor<TKey, TNode, TEdge, TContext>
        where TKey : notnull
        where TNode : class
        where TEdge : class
        where TContext : class
    {
        string Name { get; }

        Result<Chain<TKey, TNode>> ExecuteChain(
            Chain<TKey, TNode> chain,
            IGraph<TKey, TNode, TEdge> graph,
            TContext context);

        Result<TNode> ExecuteNode(
            TKey nodeKey,
            TNode nodeData,
            TContext context);
    }

    public class DefaultChainExecutor<TKey, TNode, TEdge, TContext>
        : IChainExecutor<TKey, TNode, TEdge, TContext>
        where TKey : notnull
        where TNode : class
        where TEdge : class
        where TContext : class
    {
        private readonly INodeProcessor<TKey, TNode, TEdge, TContext> _nodeProcessor;
        private readonly Func<TNode, TKey> _keyExtractor;

        public string Name { get; }

        public DefaultChainExecutor(
            string name,
            INodeProcessor<TKey, TNode, TEdge, TContext> nodeProcessor,
            Func<TNode, TKey>? keyExtractor = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _nodeProcessor = nodeProcessor ?? throw new ArgumentNullException(nameof(nodeProcessor));
            _keyExtractor = keyExtractor ?? throw new ArgumentNullException(nameof(keyExtractor),
                "必须提供键提取器，因为 Chain<TKey,TNode> 不存储节点键");
        }

        public Result<Chain<TKey, TNode>> ExecuteChain(
            Chain<TKey, TNode> chain,
            IGraph<TKey, TNode, TEdge> graph,
            TContext context)
        {
            if (chain == null)
                return Result<Chain<TKey, TNode>>.Failure("链为空");

            var processedNodes = new Chain<TKey, TNode>(chain.ChainId, label: chain.Label);

            foreach (var node in chain.AllNodes)
            {
                var nodeKey = chain.GetNodeKey(node, _keyExtractor);
                var result = ExecuteNode(nodeKey, node, context);

                if (!result.IsSuccess)
                    return Result<Chain<TKey, TNode>>.Failure(
                        $"节点 {nodeKey} 执行失败: {result.ErrorMessage}");

                processedNodes.AllNodes.Add(result.Value!);
            }

            processedNodes.StartNode = processedNodes.AllNodes.FirstOrDefault();
            processedNodes.EndNode = processedNodes.AllNodes.LastOrDefault();

            return Result<Chain<TKey, TNode>>.Success(processedNodes);
        }

        public Result<TNode> ExecuteNode(TKey nodeKey, TNode nodeData, TContext context)
        {
            if (!_nodeProcessor.CanProcess(nodeKey, nodeData, context))
                return Result<TNode>.Failure($"处理器 {_nodeProcessor.Name} 无法处理节点 {nodeKey}");

            return _nodeProcessor.Process(nodeKey, nodeData, context);
        }
    }
}
