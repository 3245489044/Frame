using CSG.Unified;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFramework.Snapshots;

namespace WorkFlowMethod.CSG.Adapters;

public class GraphSnapshotGraphAdapter : IGraph
{
    private readonly GraphSnapshot<string, NodeConfig, object> _snapshot;

    public IEnumerable<string> NodeIds => _snapshot.NodeKeys;

    public IEnumerable<(string Source, string Target)> EdgePairs =>
        _snapshot.Edges.Select(e => (e.SourceNodeKey, e.TargetNodeKey));

    public GraphSnapshotGraphAdapter(GraphSnapshot<string, NodeConfig, object> snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool HasNode(string nodeId)
    {
        return _snapshot.Nodes.ContainsKey(nodeId);
    }

    public IEnumerable<string> GetNeighbors(string nodeId)
    {
        return _snapshot.GetNextNodeKeys(nodeId);
    }

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();

        foreach (var nodeKey in _snapshot.NodeKeys)
        {
            var nodeDef = _snapshot.GetNodeData(nodeKey);
            form.Nodes.Add(new CanonicalNode
            {
                Id = nodeKey,
                Label = nodeDef?.Label ?? nodeKey,
                Type = CanonicalNodeType.Default,
                Properties = new Dictionary<string, object?>
                {
                    ["nodeType"] = "function",
                    ["symbol"] = nodeDef?.Symbol ?? nodeKey,
                    ["isEntry"] = _snapshot.GetIncomingEdges(nodeKey).Count == 0,
                    ["isExit"] = _snapshot.GetOutgoingEdges(nodeKey).Count == 0
                }
            });
        }

        foreach (var edge in _snapshot.Edges)
        {
            form.Edges.Add(new CanonicalEdge
            {
                Id = edge.EdgeId ?? $"edge_{edge.SourceNodeKey}_to_{edge.TargetNodeKey}",
                SourceId = edge.SourceNodeKey,
                TargetId = edge.TargetNodeKey,
                Label = edge.EdgeId ?? "edge",
                Type = CanonicalEdgeType.Default,
                Properties = new Dictionary<string, object?>
                {
                    ["isDirected"] = edge.IsDirected
                }
            });
        }

        form.Metadata["csgVersion"] = "1.0.0";
        form.Metadata["source"] = "WorkFlowMethod.GraphSnapshot";
        form.Metadata["graphId"] = _snapshot.GraphId;
        form.Metadata["snapshotId"] = _snapshot.SnapshotId;
        form.Metadata["version"] = _snapshot.Version.ToString();
        form.Metadata["nodeCount"] = _snapshot.NodeCount.ToString();
        form.Metadata["edgeCount"] = _snapshot.EdgeCount.ToString();
        form.Metadata["created"] = DateTime.UtcNow.ToString("O");

        return form;
    }
}
