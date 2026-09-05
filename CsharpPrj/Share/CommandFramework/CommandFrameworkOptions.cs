
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using FrameworkAbstractions.Controls;


namespace CommandFramework;

public class CommandFrameworkOptions
{

    public bool EnableEngine { get; set; } = true;

    public bool EnablePipeline { get; set; } = true;

    public bool EnableStateStore { get; set; } = true;

    public bool EnableRegistry { get; set; } = false;

    public bool EnableControlValueAccessor { get; set; } = false;

    public bool EnableEnvironmentAdapter { get; set; } = false;

    public bool EnableControlFinder { get; set; } = false;


    public string Mode { get; set; } = "CLI";


    public IEnvironmentAdapter? EnvironmentAdapter { get; set; }

    public TConfig.Controls.IControlValueAccessor? ControlValueAccessor { get; set; }

    public INonEventCommandLookup? NonEventCommandLookup { get; set; }


    public IControlFinder? ControlFinder { get; set; }

    public TConfig.IParameterExecutionWorkflow? ParameterExecutionWorkflow { get; set; }

    public object? ExecutionCell { get; set; }
}
