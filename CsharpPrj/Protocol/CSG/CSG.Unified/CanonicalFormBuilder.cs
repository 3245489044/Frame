using CSG.Unified;

namespace CSG.Unified;

public class CanonicalFormBuilder
{
    private readonly CanonicalForm _form = new();

    public CanonicalForm Build()
    {
        if (_form.Nodes.Count > 0 && _form.State == FormState.Empty)
            _form.State = FormState.Populated;
        return _form;
    }

    public CanonicalFormBuilder WithMetadata(string key, object? value)
    {
        _form.Metadata[key] = value;
        return this;
    }

    public CanonicalFormBuilder WithMetadataRange(IEnumerable<KeyValuePair<string, object?>> metadata)
    {
        foreach (var kv in metadata)
            _form.Metadata[kv.Key] = kv.Value;
        return this;
    }

    public CanonicalFormBuilder AddNode(CanonicalNode node)
    {
        _form.Nodes.Add(node);
        return this;
    }

    public CanonicalFormBuilder AddNode(
        string id,
        string label = "",
        CanonicalNodeType type = CanonicalNodeType.Default,
        Dictionary<string, object?>? properties = null)
    {
        _form.Nodes.Add(new CanonicalNode
        {
            Id = id,
            Label = label,
            Type = type,
            Properties = properties ?? new()
        });
        return this;
    }

    public CanonicalFormBuilder AddNodes(IEnumerable<CanonicalNode> nodes)
    {
        _form.Nodes.AddRange(nodes);
        return this;
    }

    public CanonicalFormBuilder AddEdge(CanonicalEdge edge)
    {
        _form.Edges.Add(edge);
        return this;
    }

    public CanonicalFormBuilder AddEdge(
        string id,
        string sourceId,
        string targetId,
        string label = "",
        CanonicalEdgeType type = CanonicalEdgeType.Default,
        string? condition = null,
        double weight = 1.0,
        Dictionary<string, object?>? properties = null)
    {
        _form.Edges.Add(new CanonicalEdge
        {
            Id = id,
            SourceId = sourceId,
            TargetId = targetId,
            Label = label,
            Type = type,
            Condition = condition,
            Weight = weight,
            Properties = properties ?? new()
        });
        return this;
    }

    public CanonicalFormBuilder AddEdges(IEnumerable<CanonicalEdge> edges)
    {
        _form.Edges.AddRange(edges);
        return this;
    }

    public CanonicalFormBuilder FromJson(string json)
    {
        var parsed = CanonicalForm.FromJson(json);
        if (parsed != null)
        {
            _form.Nodes.AddRange(parsed.Nodes);
            _form.Edges.AddRange(parsed.Edges);
            foreach (var kv in parsed.Metadata)
                _form.Metadata[kv.Key] = kv.Value;
        }
        return this;
    }

    public CanonicalFormBuilder Clear()
    {
        _form.Nodes.Clear();
        _form.Edges.Clear();
        _form.Metadata.Clear();
        return this;
    }
}
