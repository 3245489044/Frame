
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Core;

namespace GraphFoundation.Chain
{
    public class ChainProcessingResult
    {
        public int ChainCount { get; set; }
        public int NodeCount { get; set; }
        public int EdgeCount { get; set; }
        public List<string> Log { get; } = new();

        public override string ToString()
            => $"ChainProcessing: Chains={ChainCount}, Nodes={NodeCount}, Edges={EdgeCount}";
    }

    public class ChainProcessor<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly Func<string, TKey> _keyFactory;
        private readonly Func<TKey, TNodeData> _nodeDataFactory;
        private readonly Func<TKey, TKey, TEdgeData> _edgeDataFactory;

        public ChainProcessor(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            Func<string, TKey> keyFactory,
            Func<TKey, TNodeData> nodeDataFactory,
            Func<TKey, TKey, TEdgeData> edgeDataFactory)
        {
            _graph = graph;
            _keyFactory = keyFactory;
            _nodeDataFactory = nodeDataFactory;
            _edgeDataFactory = edgeDataFactory;
        }

        public ChainProcessingResult ProcessChains(ChainCollection<TKey> chainCollection)
        {
            var result = new ChainProcessingResult();
            result.Log.Add("=== Chain Processing Start ===");

            foreach (var chain in chainCollection.ToReadOnlyList())
            {
                ProcessSingleChain(chain, result);
            }

            result.Log.Add($"Total: {result.ChainCount} chains, {result.NodeCount} nodes, {result.EdgeCount} edges");
            result.Log.Add("=== Chain Processing Complete ===");

            return result;
        }

        private void ProcessSingleChain(Chain<TKey> chain, ChainProcessingResult result)
        {
            result.ChainCount++;
            result.Log.Add($"Processing chain: {chain.Name} (Index={chain.Index}, Count={chain.Count})");

            TKey? prev = default;
            bool first = true;

            foreach (var node in chain)
            {
                if (!_graph.ContainsNode(node))
                {
                    _graph.AddNode(node, _nodeDataFactory(node));
                    result.NodeCount++;
                    result.Log.Add($"  Added node: {node}");
                }

                if (!first && prev != null)
                {
                    var edgeId = $"chain_{chain.Name}_{prev}_{node}";
                    if (!_graph.ContainsEdge(edgeId))
                    {
                        _graph.AddEdge(edgeId, prev, node, true, _edgeDataFactory(prev, node));
                        result.EdgeCount++;
                        result.Log.Add($"  Added edge: {prev} -> {node}");
                    }
                }

                prev = node;
                first = false;
            }
        }

        public ICoreGraph<TKey, TNodeData, TEdgeData> ConvertToGraph(ChainCollection<TKey> chainCollection)
        {
            var newGraph = GraphFrame.Create<TKey, TNodeData, TEdgeData>();
            var processor = new ChainProcessor<TKey, TNodeData, TEdgeData>(
                newGraph, _keyFactory, _nodeDataFactory, _edgeDataFactory);
            processor.ProcessChains(chainCollection);
            return newGraph;
        }
    }

    public class ChainGraphBridge<TNode, TChain, TKey, TNodeData, TEdgeData>
        where TNode : class
        where TChain : class
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly IChainNodeHandler<TNode, TChain> _handler;
        private readonly Func<TNode, TKey> _keySelector;
        private readonly Func<TNode, TNodeData> _dataSelector;

        public ChainGraphBridge(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IChainNodeHandler<TNode, TChain> handler,
            Func<TNode, TKey> keySelector,
            Func<TNode, TNodeData> dataSelector)
        {
            _graph = graph;
            _handler = handler;
            _keySelector = keySelector;
            _dataSelector = dataSelector;
        }

        public void SyncToGraph(TChain chain)
        {
            foreach (var node in _handler.GetNodes(chain))
            {
                var key = _keySelector(node);
                if (!_graph.ContainsNode(key))
                {
                    _graph.AddNode(key, _dataSelector(node));
                }
            }
        }

        public void SyncFromGraph(TChain chain, IEnumerable<TKey> keys)
        {
            foreach (var key in keys)
            {
                var data = _graph.GetNodeData(key);
                if (data != null)
                {
                    var node = _handler.GetNode(chain, key.ToString()!);
                    if (node == null)
                    {
                        var newNode = _handler.CreateNode(key.ToString()!, data);
                        _handler.SetNode(chain, newNode);
                    }
                }
            }
        }
    }
}
