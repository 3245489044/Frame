namespace Rge.Unified.Models;

public sealed class Entity
{
    public string Uid { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public ComponentCollection Components { get; } = new();

    public List<ParamRef> ParamRefs { get; } = new();

    public ExecutionGraph? SubGraph { get; set; }

    public Dictionary<string, object?> Outputs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public object? Value { get; set; }

    public bool Executed { get; set; }

    public string? ExecutionError { get; set; }

    public string? FuncRef => Components.Get<string>(ComponentKeys.FuncRef);

    public string StepType => Components.Get<string>(ComponentKeys.StepType) ?? ComponentKeys.DefaultStepType;

    public string? OutputKey => Components.Get<string>(ComponentKeys.OutputKey);

    public ResiliencePolicy? Policy => Components.Get<ResiliencePolicy>(ComponentKeys.Policy);

    public Entity WithParamRef(string target, string path, bool required = false, object? fallback = null)
    {
        ParamRefs.Add(new ParamRef { Target = target, Path = path, Required = required, Fallback = fallback });
        return this;
    }

    public Entity WithComponent(string key, object? value)
    {
        Components.Set(key, value);
        return this;
    }

    public Entity WithSubGraph(ExecutionGraph subGraph)
    {
        SubGraph = subGraph;
        return this;
    }

    public override string ToString() =>
        $"Entity({Uid}, name={Name}, stepType={StepType})";
}