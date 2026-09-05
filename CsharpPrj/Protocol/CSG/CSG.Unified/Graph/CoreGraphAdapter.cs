using CSG.Unified;

namespace CSG.Unified.Graph;

public class CoreGraphAdapter<TKey, TNodeData, TEdgeData> : IGraph
    where TKey : notnull
    where TNodeData : class
{
    private readonly object _graph;
    private readonly Func<object, IEnumerable<string>> _getNodeIds;
    private readonly Func<object, IEnumerable<(string Source, string Target)>> _getEdgePairs;
    private readonly Func<object, string, string, CanonicalNode> _nodeFactory;
    private readonly Func<object, string, string, string, CanonicalEdge> _edgeFactory;

    public CoreGraphAdapter(
        object graph,
        Func<object, IEnumerable<string>> getNodeIds,
        Func<object, IEnumerable<(string Source, string Target)>> getEdgePairs,
        Func<object, string, string, CanonicalNode>? nodeFactory = null,
        Func<object, string, string, string, CanonicalEdge>? edgeFactory = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _getNodeIds = getNodeIds ?? throw new ArgumentNullException(nameof(getNodeIds));
        _getEdgePairs = getEdgePairs ?? throw new ArgumentNullException(nameof(getEdgePairs));
        _nodeFactory = nodeFactory ?? DefaultNodeFactory;
        _edgeFactory = edgeFactory ?? DefaultEdgeFactory;
    }

    public IEnumerable<string> NodeIds => _getNodeIds(_graph);

    public IEnumerable<(string Source, string Target)> EdgePairs => _getEdgePairs(_graph);

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();
        form.Metadata["source"] = "CoreGraph";
        form.Metadata["adapter"] = GetType().Name;

        foreach (var nodeId in NodeIds)
        {
            form.Nodes.Add(_nodeFactory(_graph, nodeId, ""));
        }

        foreach (var (source, target) in EdgePairs)
        {
            form.Edges.Add(_edgeFactory(_graph, $"{source}->{target}", source, target));
        }

        return form;
    }

    private static CanonicalNode DefaultNodeFactory(object graph, string id, string label)
    {
        return new CanonicalNode
        {
            Id = id,
            Label = string.IsNullOrEmpty(label) ? id : label,
            Type = CanonicalNodeType.Default
        };
    }

    private static CanonicalEdge DefaultEdgeFactory(object graph, string id, string source, string target)
    {
        return new CanonicalEdge
        {
            Id = id,
            SourceId = source,
            TargetId = target,
            Type = CanonicalEdgeType.Default
        };
    }
}
