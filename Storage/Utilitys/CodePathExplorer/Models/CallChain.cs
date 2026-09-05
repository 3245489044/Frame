namespace CodePathExplorer.Models;

public class CallChainNode
{
    public string MethodFullName { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    public int LineNumber { get; set; }

    public bool IsBranchPoint { get; set; }

    public string? BranchCondition { get; set; }

    public bool IsLoop { get; set; }

    public string? LoopDescription { get; set; }

    public override string ToString() => MethodFullName;
}

public class CallChain
{
    public int ChainId { get; set; }

    public string EntryMethod { get; set; } = "";

    public List<CallChainNode> Nodes { get; set; } = new();

    public int Depth => Nodes.Count;

    public string PathDescription => string.Join(" → ", Nodes.Select(n => n.MethodName));

    public override string ToString() => $"Chain {ChainId}: {PathDescription}";
}
