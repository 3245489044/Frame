
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Topology;

namespace GraphFoundation.ErrorHandling
{
    public class GraphValidator<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IReadOnlyGraph<TKey, TNodeData, TEdgeData> _graph;

        public GraphValidator(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph;
        }

        public class ValidationResult
        {
            public bool IsValid => Errors.Count == 0;
            public List<GraphError> Errors { get; } = new();
            public List<string> Warnings { get; } = new();

            public override string ToString()
                => IsValid ? "Graph is valid." : $"Graph has {Errors.Count} error(s), {Warnings.Count} warning(s).";
        }

        public ValidationResult Validate()
        {
            var result = new ValidationResult();

            ValidateEdgeReferences(result);

            ValidateNodeKeys(result);

            ValidateEdgeIds(result);

            ValidateWeights(result);

            return result;
        }

        private void ValidateEdgeReferences(ValidationResult result)
        {
            foreach (var edgeId in _graph.GetAllEdgeIds())
            {
                try
                {
                    var source = _graph.GetEdgeSource(edgeId);
                    var target = _graph.GetEdgeTarget(edgeId);

                    if (!_graph.ContainsNode(source))
                    {
                        result.Errors.Add(new GraphError(
                            GraphErrorType.NodeNotFound,
                            $"Edge '{edgeId}' references non-existent source node '{source}'."));
                    }

                    if (!_graph.ContainsNode(target))
                    {
                        result.Errors.Add(new GraphError(
                            GraphErrorType.NodeNotFound,
                            $"Edge '{edgeId}' references non-existent target node '{target}'."));
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new GraphError(
                        GraphErrorType.InternalError,
                        $"Error validating edge '{edgeId}': {ex.Message}", ex));
                }
            }
        }

        private void ValidateNodeKeys(ValidationResult result)
        {
            var keys = _graph.GetAllNodeKeys();
            if (keys.Count != keys.Distinct().Count())
            {
                result.Errors.Add(new GraphError(
                    GraphErrorType.InvalidOperation,
                    "Duplicate node keys detected."));
            }
        }

        private void ValidateEdgeIds(ValidationResult result)
        {
            var ids = _graph.GetAllEdgeIds();
            if (ids.Count != ids.Distinct().Count())
            {
                result.Errors.Add(new GraphError(
                    GraphErrorType.InvalidOperation,
                    "Duplicate edge IDs detected."));
            }
        }

        private void ValidateWeights(ValidationResult result)
        {
            foreach (var edgeId in _graph.GetAllEdgeIds())
            {
                try
                {
                    var weight = _graph.GetEdgeWeight(edgeId);
                    if (weight < 0)
                    {
                        result.Warnings.Add($"Edge '{edgeId}' has negative weight: {weight}");
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new GraphError(
                        GraphErrorType.InternalError,
                        $"Error reading weight for edge '{edgeId}': {ex.Message}", ex));
                }
            }
        }
    }

    public class GraphStatistics<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IReadOnlyGraph<TKey, TNodeData, TEdgeData> _graph;

        public GraphStatistics(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            _graph = graph;
        }

        public class Stats
        {
            public int NodeCount { get; set; }
            public int EdgeCount { get; set; }
            public double AvgOutDegree { get; set; }
            public double AvgInDegree { get; set; }
            public double AvgWeight { get; set; }
            public int IsolatedNodeCount { get; set; }
            public int ConnectedComponentCount { get; set; }
            public bool HasCycle { get; set; }

            public override string ToString()
                => $"Stats: Nodes={NodeCount}, Edges={EdgeCount}, " +
                   $"AvgOutDegree={AvgOutDegree:F2}, AvgInDegree={AvgInDegree:F2}, " +
                   $"AvgWeight={AvgWeight:F2}, Isolated={IsolatedNodeCount}, " +
                   $"Components={ConnectedComponentCount}, HasCycle={HasCycle}";
        }

        public Stats Compute()
        {
            var stats = new Stats();
            var nodes = _graph.GetAllNodeKeys();
            var edges = _graph.GetAllEdgeIds();

            stats.NodeCount = nodes.Count;
            stats.EdgeCount = edges.Count;

            if (nodes.Count > 0)
            {
                double totalOut = 0, totalIn = 0;
                int isolated = 0;

                foreach (var key in nodes)
                {
                    var outDeg = _graph.GetOutgoingEdges(key).Count;
                    var inDeg = _graph.GetIncomingEdges(key).Count;
                    totalOut += outDeg;
                    totalIn += inDeg;
                    if (outDeg == 0 && inDeg == 0)
                        isolated++;
                }

                stats.AvgOutDegree = totalOut / nodes.Count;
                stats.AvgInDegree = totalIn / nodes.Count;
                stats.IsolatedNodeCount = isolated;
            }

            if (edges.Count > 0)
            {
                double totalWeight = 0;
                foreach (var edgeId in edges)
                {
                    totalWeight += _graph.GetEdgeWeight(edgeId);
                }
                stats.AvgWeight = totalWeight / edges.Count;
            }

            var topoResult = GraphTopology.TopologicalSort(_graph);
            stats.HasCycle = topoResult.HasCycle;

            var components = GraphTopology.GetConnectedComponents(_graph);
            stats.ConnectedComponentCount = components.Count;

            return stats;
        }
    }
}
