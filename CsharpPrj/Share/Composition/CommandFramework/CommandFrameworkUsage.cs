using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using CommandFramework.Models;
using CommandFramework.Parsing;
using CommandFramework.Pipeline;
using CommandFramework.Pipeline.Stages;
using CommandFramework.Registration;
using FrameworkAbstractions.Controls;
using Modules.Registry;
using BP = Modules.Pipeline;
using TConfig;

using LoadResult = CommandFramework.Models.LoadResult;

namespace CommandFramework;

public static class CommandFrameworkUsage
{

    private static readonly object _lock = new();

    private static CommandFrameworkHost? _host;

    private static CommandFrameworkOptions? _options;

    private static readonly List<EventHandler<StateChangedEventArgs>> _stateChangedHandlers = new();

    private static readonly ConcurrentDictionary<string, BP.IStagePipeline<CommandContext>> _pipelineCache = new();

    public static event EventHandler<StateChangedEventArgs>? StateChanged
    {
        add
        {
            lock (_lock)
            {
                if (value != null)
                {
                    _stateChangedHandlers.Add(value);
                    if (_host?.StateStore is ObservableStateStore observable)
                    {
                        observable.StateChanged += value;
                    }
                }
            }
        }
        remove
        {
            lock (_lock)
            {
                if (value != null)
                {
                    _stateChangedHandlers.Remove(value);
                    if (_host?.StateStore is ObservableStateStore observable)
                    {
                        observable.StateChanged -= value;
                    }
                }
            }
        }
    }

    public static void Initialize(Action<CommandFrameworkOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        InitializeInternal(() => CommandFrameworkHost.Create(configure));
    }

    public static void Initialize(CommandFrameworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        InitializeInternal(() => CommandFrameworkHost.Create(options: options));
    }

    private static void SetHost(Func<CommandFrameworkHost> hostFactory)
    {
        _host = hostFactory();
        _options = _host.Options;
    }

    private static void InitializeInternal(Func<CommandFrameworkHost> hostFactory)
    {
        if (_host != null)
        {
            throw new InvalidOperationException(
                "CommandFrameworkUsage has already been initialized. " +
                "Call Shutdown() first if you need to reinitialize, or use Reset().");
        }

        lock (_lock)
        {
            if (_host != null)
            {
                throw new InvalidOperationException(
                    "CommandFrameworkUsage has already been initialized. " +
                    "Call Shutdown() first if you need to reinitialize, or use Reset().");
            }

            SetHost(hostFactory);
        }
    }


    public static bool IsInitialized => _host != null;

    public static event Action? OnShutdown;

    public static void Shutdown()
    {
        lock (_lock)
        {
            OnShutdown?.Invoke();

            if (_host != null)
            {
                _host.Dispose();
                _host = null;
                _options = null;
            }
        }
    }

    public static void Reset()
    {
        Reset(_ => { });
    }

    public static void Reset(Action<CommandFrameworkOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ResetInternal(() => CommandFrameworkHost.Create(configure));
    }

    public static void Reset(CommandFrameworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ResetInternal(() => CommandFrameworkHost.Create(options: options));
    }

    private static void ResetInternal(Func<CommandFrameworkHost> hostFactory)
    {
        lock (_lock)
        {
            if (_host != null)
            {
                _host.Dispose();
                _host = null;
                _options = null;
            }

            SetHost(hostFactory);

            if (_host?.StateStore is ObservableStateStore observable)
            {
                foreach (var handler in _stateChangedHandlers)
                {
                    observable.StateChanged += handler;
                }
            }
        }
    }



    public static CommandParser Parser => RequireHost().Parser;

    public static CommandEngine Engine => RequireComponent(h => h.Engine, "CommandEngine", "EnableEngine");

    public static IStateStore StateStore => RequireComponent(h => h.StateStore, "StateStore", "EnableStateStore");

    public static PipelineEntryInstance PipelineInstance => RequireComponent(h => h.PipelineInstance, "Pipeline", "EnablePipeline");

    public static Registration.CommandRegistry CommandRegistry => RequireComponent(h => h.CommandRegistry, "CommandRegistry", "EnableRegistry");

    public static TConfig.Controls.IControlValueAccessor ControlValueAccessor => RequireComponent(h => h.ControlValueAccessor, "ControlValueAccessor", "EnableControlValueAccessor");

    public static IEnvironmentAdapter EnvironmentAdapter => RequireComponent(h => h.EnvironmentAdapter, "EnvironmentAdapter", "EnableEnvironmentAdapter");

    public static CommandFrameworkOptions Options
    {
        get
        {
            EnsureInitialized();
            return _options!;
        }
    }



    public static async Task<CommandContext> ExecuteCommandAsync(string command)
    {
        EnsureInitialized();
        return await _host!.ExecuteCommandAsync(command);
    }

    public static async Task<List<CommandContext>> ExecuteCommandsAsync(IEnumerable<string> commands,
        CancellationToken ct = default)
    {
        EnsureInitialized();
        return await _host!.ExecuteCommandsAsync(commands, ct);
    }

    public static async Task<(int SuccessCount, int FailCount)> ExecuteFileAsync(string filePath)
    {
        EnsureInitialized();
        return await _host!.ExecuteFileAsync(filePath);
    }



    public static void RegisterCommand(string name,
        Func<CommandExecutionContext, CancellationToken, Task> execute,
        Func<CommandExecutionContext, CancellationToken, Task>? undo = null,
        string? description = null)
    {
        EnsureInitialized();
        _host!.RegisterCommand(name, execute, undo, description);
    }

    public static void RegisterCommand(ICommand command)
    {
        EnsureInitialized();
        _host!.RegisterCommand(command.Name,
            async (ctx, ct) => await command.ExecuteAsync(ct),
            command.CanUndo ? async (ctx, ct) => await command.UndoAsync(ct) : null,
            command.Description);
    }

    public static BP.IStagePipeline<CommandContext> GetOrCreatePipeline(
        string name = "DefaultCommandPipeline",
        Func<string, string, BindingResult?>? bindingFinder = null,
        Action<BP.IStagePipeline<CommandContext>>? configure = null)
    {
        EnsureInitialized();
        var cacheKey = BuildPipelineCacheKey(name, bindingFinder, configure);
        return _pipelineCache.GetOrAdd(cacheKey, _ =>
        {
            return PipelineEntry.CreateDefaultPipeline(
                name,
                configure: configure,
                bindingFinder: bindingFinder,
                parser: _host!.Parser,
                nonEventCommandLookup: _host!.GetNonEventCommandLookup(),
                controlFinder: _host!.ControlFinder,
                controlValueWriter: _host!.ControlValueAccessor,
                parameterExecutionWorkflow: _host!.Options.ParameterExecutionWorkflow);
        });
    }

    private static string BuildPipelineCacheKey(
        string name,
        Func<string, string, BindingResult?>? bindingFinder,
        Action<BP.IStagePipeline<CommandContext>>? configure)
    {
        var bindingHash = bindingFinder?.GetHashCode().ToString() ?? "null";
        var configureHash = configure?.GetHashCode().ToString() ?? "null";
        return $"{name}|binding={bindingHash}|cfg={configureHash}";
    }

    public static void RegisterCommandType<T>(string name) where T : ICommand, new()
    {
        CommandRegistry.Register<T>(name);
    }



    public static BP.IStagePipeline<CommandContext> GetOrCreateCliPipeline(
        string name = "CliCommandPipeline")
    {
        EnsureInitialized();
        return _pipelineCache.GetOrAdd(name, _ =>
        {
            return PipelineEntry.CreateDefaultPipeline(
                name,
                parser: _host!.Parser,
                nonEventCommandLookup: _host!.GetNonEventCommandLookup());
        });
    }


    public static ParsedCommand Parse(string command)
    {
        EnsureInitialized();
        return _host!.Parser.Parse(command);
    }

    public static string? Normalize(string command)
    {
        EnsureInitialized();
        return _host!.Parser.Normalize(command);
    }

    public static void RegisterNormalization(string shorthand, string fullCommand)
    {
        EnsureInitialized();
        _host!.Parser.RegisterNormalization(shorthand, fullCommand);
    }

    public static void RegisterNonEventCommand(string commandName)
    {
        EnsureInitialized();
        _host!.Parser.RegisterNonEventCommand(commandName);
    }

    public static JsonCommand ParseAsJsonCommand(string text)
    {
        var result = TryParseAsJsonCommand(text);
        return result ?? throw new InvalidOperationException(
            $"Failed to parse '{text}' as JsonCommand.");
    }

    public static JsonCommand? TryParseAsJsonCommand(string text)
    {
        EnsureInitialized();
        return _host!.Parser.TryParseAsJsonCommand(text);
    }


    public static async Task<bool> UndoAsync()
    {
        return await Engine.UndoAsync();
    }

    public static async Task<bool> RedoAsync()
    {
        return await Engine.RedoAsync();
    }

    public static void ClearHistory()
    {
        Engine.ClearHistory();
    }

    public static string[] GetRegisteredCommands()
    {
        return Engine.GetRegisteredCommands();
    }

    public static bool CanUndo => Engine.CanUndo;

    public static bool CanRedo => Engine.CanRedo;

    public static bool IsCommandRegistered(string name)
    {
        return Engine.IsCommandRegistered(name);
    }



    public static string SerializeResult(ICommandExecutionResult result, bool prettyPrint = false)
    {
        return Execution.JsonCommandResult.Serialize(result, prettyPrint);
    }

    public static string SerializeBatchResult(ICommandBatchResult batchResult, bool prettyPrint = false)
    {
        return Execution.JsonCommandResult.SerializeBatch(batchResult, prettyPrint);
    }

    public static CommandExecutionResult<T>? DeserializeResult<T>(string json)
    {
        return Execution.JsonCommandResult.Deserialize<T>(json);
    }


    public static void RegisterServices(
        IConfigLoader? configLoader = null,
        IEventBindingResolver? bindingResolver = null,
        IUIController? uiController = null,
        IStateQuery? stateQuery = null)
    {
        EnsureInitialized();
        _host!.RegisterServices(configLoader, bindingResolver, uiController, stateQuery);
    }


    public static LoadResult LoadConfig(string configPath, bool showForm = false)
    {
        EnsureInitialized();
        return _host!.LoadConfig(configPath, showForm);
    }


    public static IReadOnlyDictionary<string, object> GetLoadedConfigs()
    {
        EnsureInitialized();
        return _host!.PipelineInstance?.GetLoadedConfigsDictionary()
            ?? new Dictionary<string, object>();
    }

    public static bool CloseConfig(string configName)
    {
        EnsureInitialized();
        return _host!.PipelineInstance?.RemoveConfig(configName) ?? false;
    }


    private static CommandFrameworkHost RequireHost()
    {
        EnsureInitialized();
        return _host!;
    }

    private static T RequireComponent<T>(Func<CommandFrameworkHost, T?> selector, string componentName, string? optionName) where T : class
    {
        var host = RequireHost();
        var component = selector(host);
        if (component == null)
        {
            var message = optionName != null
                ? $"{componentName} is not enabled. Set {optionName}=true in options."
                : $"{componentName} is not configured.";
            throw new InvalidOperationException(message);
        }
        return component;
    }

    private static void EnsureInitialized()
    {
        if (_host == null)
        {
            throw new InvalidOperationException(
                "CommandFrameworkUsage has not been initialized. " +
                "Call Initialize() first.");
        }
    }

}
