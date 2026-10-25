
using System.Linq;
using System.Text.Json.Serialization;

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

/// <summary>
/// 工作流节点声明（B0：暴露既有 RGE 能力——条件/循环/重试/超时/子节点）。
/// 缺省（未声明新字段）时行为等价旧单节点映射。
/// </summary>
public class WorkflowNodeEntry
{
    public string Id { get; init; } = string.Empty;

    public string? FunctionName { get; init; }

    public object? Parameters { get; init; }

    /// <summary>步骤输入过滤键（json <c>inputKeys</c>）：仅白名单键从命名参数取值注入步骤。</summary>
    public List<string>? InputKeys { get; init; }

    public string? OutputKey { get; init; }

    public string? OutputTarget { get; init; }

    /// <summary>步骤类型（json <c>stepType</c>）：Function（缺省）/ Condition / Loop。</summary>
    public string? StepType { get; init; }

    /// <summary>真分支/循环体子节点（json <c>children</c>）。Condition 的 <c>children</c> 为真分支。</summary>
    public List<WorkflowNodeEntry>? Children { get; init; }

    /// <summary>假分支子节点（json <c>elseChildren</c>）；仅 Condition 使用。</summary>
    [JsonPropertyName("elseChildren")]
    public List<WorkflowNodeEntry>? ElseChildren { get; init; }

    /// <summary>循环配置（json <c>loop</c>）；仅 Loop 使用。</summary>
    public LoopConfig? Loop { get; init; }

    /// <summary>重试次数（json <c>retryCount</c>）；null = 继承默认（0）。</summary>
    public int? RetryCount { get; init; }

    /// <summary>重试间隔毫秒（json <c>retryDelayMs</c>）；null = 继承默认（1000）。</summary>
    public int? RetryDelayMs { get; init; }

    /// <summary>单步超时毫秒（json <c>timeoutMs</c>）；null = 继承默认（30000）。</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>附加元数据（json <c>metadata</c>）；与框架键合并（框架键优先）。</summary>
    public Dictionary<string, object>? Metadata { get; init; }

    public StepDefinition ToStepDefinition()
    {
        var metadata = new Dictionary<string, object>();
        if (Metadata != null)
        {
            foreach (var kvp in Metadata)
                metadata[kvp.Key] = kvp.Value;
        }
        metadata["OutputKey"] = OutputKey ?? string.Empty;
        metadata["OutputTarget"] = OutputTarget ?? string.Empty;
        metadata["NodeId"] = Id;
        metadata["InputKeys"] = InputKeys ?? new List<string>();

        return new StepDefinition
        {
            Id = $"step_{FunctionName ?? Id}",
            Name = FunctionName ?? Id,
            StepType = string.IsNullOrWhiteSpace(StepType) ? "Function" : StepType!,
            FunctionName = FunctionName,
            Parameters = Parameters,
            TimeoutMs = TimeoutMs ?? 30000,
            RetryCount = RetryCount ?? 0,
            RetryDelayMs = RetryDelayMs ?? 1000,
            LoopConfig = Loop,
            Children = MapChildren(Children),
            FalseChildren = MapChildren(ElseChildren),
            Metadata = metadata
        };
    }

    private static List<StepDefinition> MapChildren(List<WorkflowNodeEntry>? children)
        => children is { Count: > 0 }
            ? children.Where(c => c != null).Select(c => c!.ToStepDefinition()).ToList()
            : new List<StepDefinition>();
}
