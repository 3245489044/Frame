
using CommandFramework.Abstractions;
using CommandFramework.Parsing;
using FrameworkAbstractions.Controls;
using Microsoft.Extensions.Logging;


namespace CommandFramework.Pipeline;

public class PipelineEntryOptions
{
    public CommandParser Parser { get; set; }

    public INonEventCommandLookup? NonEventLookup { get; set; }

    public IControlFinder? ControlFinder { get; set; }

    public TConfig.Controls.IControlValueAccessor? ControlValueAccessor { get; set; }

    public IParameterExecutionWorkflow? ParameterExecutionWorkflow { get; set; }

    public object? ExecutionCell { get; set; }

    public ILoggerFactory? LoggerFactory { get; set; }

    public IServiceProvider? ServiceProvider { get; set; }

    public PipelineEntryOptions(CommandParser parser)
    {
        Parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public PipelineEntryOptions WithNonEventLookup(INonEventCommandLookup? nonEventLookup)
    {
        NonEventLookup = nonEventLookup;
        return this;
    }

    public PipelineEntryOptions WithControlFinder(IControlFinder? controlFinder)
    {
        ControlFinder = controlFinder;
        return this;
    }

    public PipelineEntryOptions WithControlValueAccessor(TConfig.Controls.IControlValueAccessor? controlValueAccessor)
    {
        ControlValueAccessor = controlValueAccessor;
        return this;
    }

    public PipelineEntryOptions WithParameterExecutionWorkflow(IParameterExecutionWorkflow? parameterExecutionWorkflow)
    {
        ParameterExecutionWorkflow = parameterExecutionWorkflow;
        return this;
    }

    public PipelineEntryOptions WithExecutionCell(object? executionCell)
    {
        ExecutionCell = executionCell;
        return this;
    }

    public PipelineEntryOptions WithLoggerFactory(ILoggerFactory? loggerFactory)
    {
        LoggerFactory = loggerFactory;
        return this;
    }

    public PipelineEntryOptions WithServiceProvider(IServiceProvider? serviceProvider)
    {
        ServiceProvider = serviceProvider;
        return this;
    }
}
