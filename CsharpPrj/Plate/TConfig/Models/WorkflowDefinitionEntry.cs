
namespace TConfig;

public class WorkflowDefinitionEntry
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>工作流级输出聚合映射（json <c>outputMapping</c>）：key=节点 OutputKey，value=聚合结果键。</summary>
    public Dictionary<string, string>? OutputMapping { get; init; }

    /// <summary>工作流默认输入（json <c>defaultInput</c>），供输入参数缺失时兜底。</summary>
    public Dictionary<string, object>? DefaultInput { get; init; }

    /// <summary>默认起点步骤名（json <c>stepSelector</c>），等价 <c>_stepSelector</c> 的静态承载。</summary>
    public string? StepSelector { get; init; }

    public List<WorkflowNodeEntry> Nodes { get; init; } = new();

    public ExecutionPlan ToExecutionPlan() => new()
    {
        Id = Id,
        Name = Name,
        Steps = Nodes.Select(n => n.ToStepDefinition()).ToList(),
        Metadata = new Dictionary<string, object>
        {
            ["OutputMapping"] = OutputMapping ?? new Dictionary<string, string>(),
            ["DefaultInput"] = DefaultInput ?? new Dictionary<string, object>(),
            ["StepSelector"] = StepSelector ?? string.Empty
        }
    };
}

public class WorkflowNodeEntry
{
    public string Id { get; init; } = string.Empty;

    public string? FunctionName { get; init; }

    public object? Parameters { get; init; }

    /// <summary>步骤输入过滤键（json <c>inputKeys</c>）：仅白名单键从命名参数取值注入步骤。</summary>
    public List<string>? InputKeys { get; init; }

    public string? OutputKey { get; init; }

    public string? OutputTarget { get; init; }

    public StepDefinition ToStepDefinition() => new()
    {
        Id = $"step_{FunctionName ?? Id}",
        Name = FunctionName ?? Id,
        StepType = "Function",
        FunctionName = FunctionName,
        Parameters = Parameters,
        Metadata = new Dictionary<string, object>
        {
            ["OutputKey"] = OutputKey ?? string.Empty,
            ["OutputTarget"] = OutputTarget ?? string.Empty,
            ["NodeId"] = Id,
            ["InputKeys"] = InputKeys ?? new List<string>()
        }
    };
}
