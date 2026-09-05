
namespace BackEndDriven.CommandExecution.Context;

public class ExecutionOptions
{
    public string? ConfigPath { get; set; }

    public string? CommandFile { get; set; }

    public bool AutoClose { get; set; }

    public string? PipeName { get; set; }

    public bool PipeMode { get; set; }

    public string Mode { get; set; } = "CLI";
}
