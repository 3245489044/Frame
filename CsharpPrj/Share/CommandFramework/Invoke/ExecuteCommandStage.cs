
using CommandFramework.Invoke;
using CommandFramework.Invoke.Handlers;
using CommandFramework.Models;
using CommandFramework.Parsing;
using Container.Abstractions;
using FrameworkAbstractions.Controls;
using FrameworkAbstractions.ComponentRegistry;
using Microsoft.Extensions.Logging;
using IFunctionExecutor = TConfig.IFunctionExecutor;

namespace CommandFramework.Pipeline.Stages;

public class ExecuteCommandStage : CommandStageBase
{
    private readonly IFunctionExecutor? _functionExecutor;
    private readonly IControlFinder? _controlFinder;
    private readonly TConfig.Controls.IControlValueAccessor? _controlValueWriter;

    private readonly IParameterExecutionWorkflow? _parameterExecutionWorkflow;

    private readonly object? _executionCell;

    private readonly Func<string, string, BindingResult?>? _bindingFinder;

    private readonly INonEventCommandLookup? _nonEventCommandLookup;

    private readonly ILogger? _logger;

    private readonly TConfig.IEventExecutionService? _executionService;

    public ExecuteCommandStage(
        IFunctionExecutor? functionExecutor = null,
        Func<string, string, BindingResult?>? bindingFinder = null,
        INonEventCommandLookup? nonEventCommandLookup = null,
        IControlFinder? controlFinder = null,
        TConfig.Controls.IControlValueAccessor? controlValueWriter = null,
        IParameterExecutionWorkflow? parameterExecutionWorkflow = null,
        object? executionCell = null,
        TConfig.IEventExecutionService? executionService = null,
        ILogger? logger = null)

        : base("ExecuteCommand", "Execute command (set/click/non-event)")
    {
        _functionExecutor = functionExecutor;
        _bindingFinder = bindingFinder;
        _nonEventCommandLookup = nonEventCommandLookup;
        _controlFinder = controlFinder;
        _controlValueWriter = controlValueWriter;
        _parameterExecutionWorkflow = parameterExecutionWorkflow;
        _executionCell = executionCell;
        _executionService = executionService;
        _logger = logger;
    }

    private static readonly Dictionary<string, Func<CommandContext, string, ExecuteCommandStage, CancellationToken, Task>> _eventHandlers =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["set"] = async (ctx, target, stage, ct) =>
            await SetCommandHandler.HandleSet(ctx, target, stage.Name, stage._controlFinder, stage._controlValueWriter, stage._logger),

        ["click"] = async (ctx, target, stage, ct) =>
            await ClickCommandHandler.HandleClick(ctx, target, stage.Name, stage._bindingFinder, stage._parameterExecutionWorkflow!, ct, stage._logger),
    };

    protected override async Task ExecuteCoreAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var parsed = context.ParsedCommand;
        if (parsed == null)
        {
            context.SetStageResult(Name, false, "No parsed command available");
            return;
        }

        var action = parsed.Action?.ToLowerInvariant();
        var verb = parsed.Verb?.ToLowerInvariant();
        var target = parsed.Target;

        if (action != null)
        {
            if (_executionCell != null && _eventHandlers.ContainsKey(action))
            {
                await ExecuteViaExecutionCellAsync(context, action, target ?? string.Empty, cancellationToken);
                return;
            }

            if (_eventHandlers.TryGetValue(action, out var handler))
            {
                await handler(context, target ?? string.Empty, this, cancellationToken);
                return;
            }

            if (await TryExecuteViaWorkflowAsync(context, action ?? verb ?? "", target, cancellationToken))
                return;

            await NonEventCommandHandler.HandleNonEvent(context, action, target, Name, _nonEventCommandLookup, _logger);
            return;

        }

        if (verb != null && NonEventCommandHelper.IsNonEventCommand(verb, _nonEventCommandLookup?.Contains(verb)))
        {
            if (await TryExecuteViaWorkflowAsync(context, verb, target, cancellationToken))
                return;

            await NonEventCommandHandler.HandleNonEvent(context, verb, target, Name, _nonEventCommandLookup, _logger);
            return;
        }

        var unknownCmd = action ?? verb ?? parsed.NormalizedCommand ?? "(empty)";
        context.SetStageResult(Name, false, $"Unknown command: {unknownCmd}");
    }

    private async Task<bool> TryExecuteViaWorkflowAsync(
        CommandContext context,
        string verb,
        string? target,
        CancellationToken cancellationToken)
    {
        if (_executionService == null) return false;

        try
        {
            var namedParams = new Dictionary<string, object>
            {
                ["verb"] = verb,
                ["target"] = target ?? "",
            };
            var result = await _executionService.ExecuteFunctionAsync(verb, namedParams);

            context.FunctionResultValue = result.Value;
            context.FunctionExecutionTime = TimeSpan.Zero;
            context.SetStageResult(Name, result.IsSuccess, result.ErrorMessage);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TryExecuteViaWorkflowAsync failed for {Verb}, falling back", verb);
            return false;
        }
    }

    private async Task ExecuteViaExecutionCellAsync(
        CommandContext context,
        string action,
        string target,
        CancellationToken cancellationToken)
    {
        try
        {
            var commandText = $"{action} {target}";

            var runtimeContext = CreateRuntimeContext(context);

            var request = new ExecutionRequest(
                CommandText: commandText,
                Parameters: null,
                Source: context.IsGuiMode ? "gui" : "cli",
                ScriptPath: null,
                Mode: context.IsGuiMode ? ExecutionMode.Gui : ExecutionMode.Cli,
                RuntimeContext: runtimeContext);

            var cell = _executionCell as Container.Abstractions.IExecutionCell;
            if (cell == null)
            {
                context.SetStageResult(Name, false, "ExecutionCell is not an IExecutionCell instance");
                return;
            }
            var result = await cell.ExecuteAsync(request, cancellationToken);

            if (result.Success)
            {
                context.FunctionResultValue = result.OutputValue;
                context.FunctionExecutionTime = result.ExecutionTime;
                context.SetStageResult(Name, true);
            }
            else
            {
                context.SetStageResult(Name, false, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            context.SetStageResult(Name, false,
                $"ExecutionCell error: {ex.Message}");
        }
    }

    private static IRuntimeContext? CreateRuntimeContext(CommandContext context)
    {
        if (context.SharedData == null || context.SharedData.Count == 0)
            return null;

        return new CommandFrameworkRuntimeContext(context);
    }

    private class CommandFrameworkRuntimeContext : IRuntimeContext
    {
        private readonly CommandContext _context;
        private readonly Dictionary<string, object?> _items;

        public IDictionary<string, object?> Items => _items;

        public CommandFrameworkRuntimeContext(CommandContext context)
        {
            _context = context;
            _items = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (context.SharedData.TryGetValue("TargetForm", out var form))
                _items["TargetForm"] = form;

            if (context.SharedData.TryGetValue("__Logger", out var logger))
                _items["__Logger"] = logger;

            if (context.SharedData.TryGetValue("__ServiceProvider", out var sp))
                _items["__ServiceProvider"] = sp;
        }

        public object? GetService(Type serviceType)
        {
            if (_context.SharedData.TryGetValue("__ServiceProvider", out var spObj)
                && spObj is IServiceProvider sp)
            {
                return sp.GetService(serviceType);
            }
            return null;
        }

        public T? GetService<T>() where T : class
        {
            return GetService(typeof(T)) as T;
        }
    }
}
