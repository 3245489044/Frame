namespace ExecutionFramework.Engine;

public sealed class GraphExecutionResult
{
    public List<StepFailureRecord> FailedSteps { get; } = new();

    public bool HasErrors => FailedSteps.Count > 0;

    public Dictionary<string, object?> Outputs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public object? Value { get; set; }

    public bool IsSuccess => !HasErrors;

    public void AddFailure(string stepName, string funcName, string error, bool isDependencyError = false)
    {
        FailedSteps.Add(new StepFailureRecord
        {
            StepName = stepName,
            FuncName = funcName,
            Error = error,
            IsDependencyError = isDependencyError
        });
    }

    public void MergeFailures(GraphExecutionResult other)
    {
        if (other is null)
            return;
        FailedSteps.AddRange(other.FailedSteps);
        foreach (var kvp in other.Outputs)
            Outputs[kvp.Key] = kvp.Value;
        Value ??= other.Value;
    }

    public string BuildErrorMessage()
    {
        var realFailures = FailedSteps.Where(f => !f.IsDependencyError).ToList();
        var dependencyErrors = FailedSteps.Where(f => f.IsDependencyError).ToList();

        var parts = new List<string>();
        if (realFailures.Count > 0)
        {
            parts.Add(string.Join("; ",
                realFailures.Select(f => $"[{f.StepName}] {f.Error}")));
        }
        if (dependencyErrors.Count > 0)
        {
            parts.Add($"依赖错误: {string.Join("; ",
                dependencyErrors.Select(f => $"[{f.StepName}] {f.Error}"))}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : Value?.ToString() ?? "图执行失败";
    }
}

public sealed class StepFailureRecord
{
    public string StepName { get; set; } = string.Empty;

    public string FuncName { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;

    public bool IsDependencyError { get; set; }
}