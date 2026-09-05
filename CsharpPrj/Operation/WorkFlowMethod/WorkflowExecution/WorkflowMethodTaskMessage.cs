using ExecutionCenter.Abstractions;
using ExecutionCenter.Messages;

namespace WorkFlowMethod.Execution.Messages;

public class WorkflowMethodTaskMessage : TaskMessageBase
{
    public override string TaskType => "WorkflowMethod";

    public string FunctionName { get; init; }

    public string? WorkflowJson { get; init; }

    public Dictionary<string, object> Parameters { get; init; }

    public WorkflowMethodTaskMessage(
        string functionName,
        Dictionary<string, object>? parameters = null,
        int priority = 2)
    {
        FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
        Parameters = parameters ?? new Dictionary<string, object>();
        Priority = priority;
    }
}