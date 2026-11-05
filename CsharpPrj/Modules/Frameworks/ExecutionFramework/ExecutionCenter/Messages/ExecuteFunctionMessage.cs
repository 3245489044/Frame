
using System.Collections.Generic;

namespace ExecutionCenter.Messages;

public class ExecuteFunctionMessage : TaskMessageBase
{
    public override string TaskType => "ExecuteFunction";

    public string FunctionName { get; init; } = string.Empty;

    public Dictionary<string, object>? Parameters { get; init; }

    public int SourceEntityId { get; init; }
}