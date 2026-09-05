namespace ExecutionCenter.Messages;

public class ExecuteCommandMessage : TaskMessageBase
{
    public override string TaskType => "ExecuteCommand";
    public string Command { get; init; } = "";
    public string? ControlName { get; init; }
    public string? EventType { get; init; }

    public static ExecuteCommandMessage CreateGuiClick(string controlName, string eventType)
        => new()
        {
            ControlName = controlName,
            EventType = eventType,
            Command = $"{eventType} {controlName}",
            Priority = 0
        };
}
