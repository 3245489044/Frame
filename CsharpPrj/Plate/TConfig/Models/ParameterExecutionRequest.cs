
namespace TConfig;

public class ParameterExecutionRequest
{
    public required string FunctionName { get; init; }

    public string? InputControls { get; init; }

    public string? OutputTarget { get; init; }

    public bool IsGuiMode { get; init; }

    public object? TargetForm { get; init; }

    public Dictionary<string, string?>? VirtualStore { get; init; }

    public object? ControlFinder { get; init; }

    public TConfig.Controls.IControlValueAccessor? ControlValueWriter { get; init; }

    public Dictionary<string, System.Type>? ParameterTypes { get; init; }
}
