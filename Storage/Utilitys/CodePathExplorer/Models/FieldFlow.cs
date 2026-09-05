namespace CodePathExplorer.Models;

public class FieldFlowNode
{
    public FieldOperationType OperationType { get; set; }

    public string MethodFullName { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    public int LineNumber { get; set; }

    public string? PassedToMethod { get; set; }

    public override string ToString() => $"[{OperationType}] {MethodFullName}";
}

public enum FieldOperationType
{
    Declaration,
    Read,
    Write,
    PassAsArgument
}

public class FieldFlow
{
    public int FlowId { get; set; }

    public string FieldName { get; set; } = "";

    public string FieldType { get; set; } = "";

    public string ClassName { get; set; } = "";

    public List<FieldFlowNode> Nodes { get; set; } = new();

    public string FlowDescription => string.Join(" → ", Nodes.Select(n => $"[{n.OperationType}] {n.MethodName}"));

    public override string ToString() => $"Flow {FlowId}: {FlowDescription}";
}

public class FieldFlowSet
{
    public string FieldName { get; set; } = "";

    public string FieldType { get; set; } = "";

    public string ClassName { get; set; } = "";

    public List<FieldFlow> Flows { get; set; } = new();

    private bool? _isUnused;
    private bool? _isSingleMethodUse;

    public bool IsUnused
    {
        get
        {
            if (_isUnused.HasValue) return _isUnused.Value;
            _isUnused = Flows.Count == 0 || Flows.All(f => f.Nodes.All(n => n.OperationType == FieldOperationType.Declaration));
            return _isUnused.Value;
        }
    }

    public bool IsSingleMethodUse
    {
        get
        {
            if (_isSingleMethodUse.HasValue) return _isSingleMethodUse.Value;
            _isSingleMethodUse = Flows.SelectMany(f => f.Nodes)
                .Where(n => n.OperationType != FieldOperationType.Declaration)
                .Select(n => n.MethodName)
                .Distinct()
                .Count() <= 1;
            return _isSingleMethodUse.Value;
        }
    }

    public void InvalidateCache()
    {
        _isUnused = null;
        _isSingleMethodUse = null;
    }
}
