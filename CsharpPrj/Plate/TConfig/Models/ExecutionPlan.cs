
namespace TConfig;

public enum ResultCollectionMode
{
    Single,
    Collection
}

public class ExecutionPlan
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<StepDefinition> Steps { get; set; } = new();

    public ResultCollectionMode ResultMode { get; set; } = ResultCollectionMode.Collection;

    public Dictionary<string, object> Metadata { get; set; } = new();

    public static ExecutionPlan FromFunction(string functionName, Dictionary<string, object?>? parameters = null)
    {
        var step = new StepDefinition
        {
            Id = $"step_{functionName}",
            Name = functionName,
            StepType = "Function",
            FunctionName = functionName,
            Parameters = parameters
        };

        return new ExecutionPlan
        {
            Id = $"plan_{functionName}",
            Name = functionName,
            Steps = new List<StepDefinition> { step },
            ResultMode = ResultCollectionMode.Single
        };
    }

    public static ExecutionPlan FromWorkflowNodes(string workflowName, List<StepDefinition> steps)
    {
        return new ExecutionPlan
        {
            Id = $"plan_{workflowName}",
            Name = workflowName,
            Steps = steps,
            ResultMode = ResultCollectionMode.Collection
        };
    }
}

public class StepDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string StepType { get; set; } = "Function";

    public string? FunctionName { get; set; }

    public object? Parameters { get; set; }

    public int TimeoutMs { get; set; } = 30000;

    public bool IsEnabled { get; set; } = true;

    public bool IsSkipped { get; set; } = false;

    public List<StepDefinition> Children { get; set; } = new();

    public Dictionary<string, object> Metadata { get; set; } = new();

    public int RetryCount { get; set; } = 0;

    public int RetryDelayMs { get; set; } = 1000;

    public List<StepDefinition> FalseChildren { get; set; } = new();

    public LoopConfig? LoopConfig { get; set; }
}

public class LoopConfig
{
    public string LoopType { get; set; } = "Count";

    public int MaxIterations { get; set; } = 10;

    public string? SourceExpression { get; set; }

    public string? ItemVariableName { get; set; }
}
