
using ExecutionCenter.Abstractions;

namespace ExecutionCenter.Messages;

public class FileDrivenPipelineMessage : TaskMessageBase
{
    public override string TaskType => "FileDrivenPipeline";

    public string CommandFile { get; init; }

    public string Mode { get; init; }

    public string? ConfigPath { get; init; }

    public bool AutoClose { get; init; }

    public string? PipeName { get; init; }

    public bool PipeMode { get; init; }

    public FileDrivenPipelineMessage(
        string commandFile,
        string mode = "CLI",
        int priority = 2)
    {
        CommandFile = commandFile ?? throw new ArgumentNullException(nameof(commandFile));
        Mode = mode;
        Priority = priority;
    }
}
