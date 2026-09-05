namespace ExecutionCenter.Messages;

public class LoadConfigMessage : TaskMessageBase
{
    public override string TaskType => "LoadConfig";
    public string ConfigPath { get; init; } = "";
    public bool ShowForm { get; init; }

    public LoadConfigMessage()
    {
        Priority = 1;
    }
}
