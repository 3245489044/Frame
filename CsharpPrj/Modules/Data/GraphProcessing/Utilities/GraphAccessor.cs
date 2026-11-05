
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Core;

namespace Modules.GraphProcessing.Utilities
{
    public class GraphAccessor<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;

        public GraphAccessor(ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public ICoreGraph<TKey, TNodeData, TEdgeData> Graph => _graph;

        public TNodeData? GetNodeData(TKey key) => _graph.GetNodeData(key);

        public string? GetNodeSymbol(TKey key) => _graph.GetNodeSymbol(key);

        public TEdgeData? GetEdgeData(string edgeId) => _graph.GetEdgeData(edgeId);

        public double GetEdgeWeight(string edgeId) => _graph.GetEdgeWeight(edgeId);

        public IReadOnlyList<TKey> GetAllNodeKeys() => _graph.GetAllNodeKeys();

        public IReadOnlyList<string> GetAllEdgeIds() => _graph.GetAllEdgeIds();

        public IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetOutgoingEdges(TKey source)
            => _graph.GetOutgoingEdges(source);

        public IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetIncomingEdges(TKey target)
            => _graph.GetIncomingEdges(target);

        public bool ContainsNode(TKey key) => _graph.ContainsNode(key);

        public bool ContainsEdge(string edgeId) => _graph.ContainsEdge(edgeId);

        public IReadOnlyList<TKey> GetNeighbors(TKey key) => _graph.GetNeighbors(key);
    }

    public class DefaultCommandFactory<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public ICommand CreateAddNodeCommand(TKey key, TNodeData data, string? symbol = null)
            => new GraphFoundation.Core.Commands.AddNodeCommand<TKey, TNodeData, TEdgeData>(key, data, symbol);

        public ICommand CreateRemoveNodeCommand(TKey key)
            => new GraphFoundation.Core.Commands.RemoveNodeCommand<TKey, TNodeData, TEdgeData>(key);

        public ICommand CreateAddEdgeCommand(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight = 1.0)
            => new GraphFoundation.Core.Commands.AddEdgeCommand<TKey, TNodeData, TEdgeData>(edgeId, source, target, isDirected, data, weight);

        public ICommand CreateRemoveEdgeCommand(string edgeId)
            => new GraphFoundation.Core.Commands.RemoveEdgeCommand<TKey, TNodeData, TEdgeData>(edgeId);
    }
}
