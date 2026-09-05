namespace Operation.HierarchyMethod.Nodes.Discovery;

/// <summary>候选节点索引项（projection 输出，不携带载荷）。</summary>
public sealed class CandidateItem
{
    public string NodeKey { get; set; } = string.Empty;
    public int Order { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int EstTokens { get; set; }
}

/// <summary>node-project 输出（候选清单 + 累计预算外露）。</summary>
public sealed class ProjectionResult
{
    public string TaskKey { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public List<CandidateItem> Candidates { get; set; } = new();
    public int TotalEstTokens { get; set; }
}

/// <summary>node-select 输出（预算内注入文本 + 命中/丢弃清单）。</summary>
public sealed class InjectionResult
{
    public string TaskKey { get; set; } = string.Empty;
    public int BudgetTokens { get; set; }
    public int UsedTokens { get; set; }
    public List<string> Loaded { get; set; } = new();
    public List<string> Dropped { get; set; } = new();
    public string Injected { get; set; } = string.Empty;
}
