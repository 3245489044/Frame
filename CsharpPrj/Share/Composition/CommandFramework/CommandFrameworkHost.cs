
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using CommandFramework.Models;
using CommandFramework.Parsing;
using CommandFramework.Pipeline;
using CommandFramework.Pipeline.Stages;
using CommandFramework.Registration;
using FrameworkAbstractions.Controls;
using Microsoft.Extensions.Logging;
using Modules.Registry;
using TConfig;
using BP = Modules.Pipeline;
using LoadResult = CommandFramework.Models.LoadResult;



namespace CommandFramework;

public class CommandFrameworkHost : IDisposable
{
    private bool _disposed;

    private readonly INonEventCommandLookup? _nonEventCommandLookup;

    private readonly IParameterExecutionWorkflow? _parameterExecutionWorkflow;

    private readonly object? _executionCell;

    private readonly ILoggerFactory? _loggerFactory;

    private readonly IServiceProvider? _serviceProvider;


    public CommandParser Parser { get; private set; }

    public CommandEngine? Engine { get; private set; }

    public IStateStore? StateStore { get; private set; }

    public PipelineEntryInstance? PipelineInstance { get; private set; }

    public Registration.CommandRegistry? CommandRegistry { get; private set; }

    public TConfig.Controls.IControlValueAccessor? ControlValueAccessor { get; private set; }

    public IControlFinder? ControlFinder { get; private set; }

    public IEnvironmentAdapter? EnvironmentAdapter { get; private set; }

    public CommandFrameworkOptions Options { get; }

    internal INonEventCommandLookup? GetNonEventCommandLookup() => _nonEventCommandLookup;


    internal CommandFrameworkHost(
        CommandFrameworkOptions options,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Parser = null!;
        _nonEventCommandLookup = options.NonEventCommandLookup;
        _parameterExecutionWorkflow = options.ParameterExecutionWorkflow;
        _executionCell = options.ExecutionCell;
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        Initialize();
    }


    public static CommandFrameworkHost CreateDefault()
        => new(new CommandFrameworkOptions());

    public static CommandFrameworkHost Create(
        Action<CommandFrameworkOptions>? configure = null,
        CommandFrameworkOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
    {
        if (options != null)
            return new CommandFrameworkHost(options, loggerFactory, serviceProvider);

        var opts = new CommandFrameworkOptions();
        configure?.Invoke(opts);
        return new CommandFrameworkHost(opts, loggerFactory, serviceProvider);
    }



    private void Initialize()
    {
        Parser = new CommandParser();

        if (Options.EnableStateStore)
        {
            StateStore = new InMemoryStateStore();
        }

        if (Options.EnableRegistry)
        {
            CommandRegistry = new Registration.CommandRegistry();
        }

        if (Options.EnableEngine)
        {
            var engineCommandRegistry = new EngineCommandRegistry();
            Engine = new CommandEngine(
                engineCommandRegistry,
                CommandRegistry);
        }

        if (Options.EnableControlFinder)
        {
            ControlFinder = Options.ControlFinder;
        }

        if (Options.EnableControlValueAccessor)
        {
            ControlValueAccessor = Options.ControlValueAccessor ?? new ControlValueAccessorBase();
        }

        if (Options.EnablePipeline)
        {
            var pipelineOptions = new PipelineEntryOptions(Parser)
            {
                NonEventLookup = _nonEventCommandLookup,
                ControlFinder = ControlFinder,
                ControlValueAccessor = ControlValueAccessor,
                ParameterExecutionWorkflow = _parameterExecutionWorkflow,
                ExecutionCell = _executionCell,
                LoggerFactory = _loggerFactory,
                ServiceProvider = _serviceProvider
            };
            PipelineInstance = new PipelineEntryInstance(pipelineOptions);
        }

        if (Options.EnableEnvironmentAdapter)
        {
            EnvironmentAdapter = Options.EnvironmentAdapter;
        }
    }


    public async Task<CommandContext> ExecuteCommandAsync(string command,
        CancellationToken ct = default)
    {
        if (PipelineInstance == null)
            throw new InvalidOperationException(
                "Pipeline is not enabled. Set EnablePipeline=true in options.");

        return await PipelineInstance.RunCommandAsync(command, ct: ct);
    }

    public async Task<List<CommandContext>> ExecuteCommandsAsync(IEnumerable<string> commands,
        CancellationToken ct = default)
    {
        var results = new List<CommandContext>();
        foreach (var command in commands)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ExecuteCommandAsync(command, ct);
            results.Add(result);
        }
        return results;
    }

    public async Task<(int SuccessCount, int FailCount)> ExecuteFileAsync(string filePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Command file not found: {filePath}");

        var lines = await File.ReadAllLinesAsync(filePath, ct);
        int successCount = 0, failCount = 0;

        foreach (var line in lines)
        {
            if (ct.IsCancellationRequested)
                break;

            var normalized = Parser.Normalize(line);
            if (normalized == null)
                continue;

            var result = await ExecuteCommandAsync(normalized, ct);
            if (!result.HasError)
                successCount++;
            else
                failCount++;
        }

        return (successCount, failCount);
    }


    public void RegisterCommand(string name,
        Func<CommandExecutionContext, CancellationToken, Task> execute,
        Func<CommandExecutionContext, CancellationToken, Task>? undo = null,
        string? description = null)
    {
        if (Engine == null)
            throw new InvalidOperationException("Engine is not enabled. Set EnableEngine=true in options.");
        Engine.RegisterCommand(name, execute, undo, description);
    }

    public void RegisterHandler(string commandName, Func<string, Task<bool>> handler)
    {
        if (Engine == null)
            throw new InvalidOperationException("Engine is not enabled. Set EnableEngine=true in options.");
        Engine.RegisterHandler(commandName, handler);
    }




    public void RegisterServices(
        IConfigLoader? configLoader = null,
        IEventBindingResolver? bindingResolver = null,
        IUIController? uiController = null,
        IStateQuery? stateQuery = null)
    {
        if (PipelineInstance == null)
        {
            _loggerFactory?.CreateLogger<CommandFrameworkHost>()
                ?.LogWarning("Pipeline is not enabled. Services will not be registered. " +
                    "Set EnablePipeline=true in options to enable service registration.");
            return;
        }

        PipelineInstance.RegisterServices(
            configLoader, bindingResolver, uiController, stateQuery);
    }


    public LoadResult LoadConfig(string configPath, bool showForm = false)
    {
        if (PipelineInstance == null)
            return LoadResult.Failure("Pipeline is not enabled. Set EnablePipeline=true in options.");

        return PipelineInstance.LoadConfig(configPath, showForm);
    }

    public BP.IStagePipeline<CommandContext> GetOrCreatePipeline(
        string name = "DefaultCommandPipeline",
        Func<string, string, BindingResult?>? bindingFinder = null,
        Action<BP.IStagePipeline<CommandContext>>? configure = null)
    {
        return PipelineEntry.CreateDefaultPipeline(
            name,
            configure: configure,
            bindingFinder: bindingFinder,
            parser: Parser,
            nonEventCommandLookup: _nonEventCommandLookup,
            controlFinder: ControlFinder,
            controlValueWriter: ControlValueAccessor,
            parameterExecutionWorkflow: _parameterExecutionWorkflow,
            executionCell: _executionCell);
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Engine?.Dispose();
        PipelineInstance?.Dispose();

        if (ControlFinder is IDisposable disposableFinder)
            disposableFinder.Dispose();
        if (ControlValueAccessor is IDisposable disposableAccessor)
            disposableAccessor.Dispose();

        Parser = null!;
        Engine = null;
        StateStore = null;
        PipelineInstance = null;
        CommandRegistry = null;
        ControlValueAccessor = null;
        ControlFinder = null;
        EnvironmentAdapter = null;
    }
}
