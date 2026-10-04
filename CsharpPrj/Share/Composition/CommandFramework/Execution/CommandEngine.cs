
using System.Collections.Concurrent;
using System.Diagnostics;
using CommandFramework.Abstractions;
using CommandFramework.Models;
using CommandFramework.Parsing;
using CommandFramework.Registration;

namespace CommandFramework.Execution;



public class CommandEngine : ICommandEngine
{
    private readonly EngineCommandRegistry _commands;
    private readonly Stack<CommandRegistration> _history = new();
    private readonly Stack<CommandRegistration> _redoStack = new();
    private readonly CommandRegistry? _commandRegistry;
    private bool _disposed;

    private const int MaxCommandRecursionDepth = 5;

    public CommandEngine(
        EngineCommandRegistry engineCommandRegistry,
        CommandRegistry? commandRegistry = null)
    {
        // 必填端口（T）：命令注册表由装配点显式提供；禁 ?? new 默认冒充装配。
        _commands = engineCommandRegistry ?? throw new ArgumentNullException(nameof(engineCommandRegistry));
        _commandRegistry = commandRegistry;
    }

    public static CommandEngine CreateDefault()
        // 组合点：默认构造在此显式装配命令注册表（引擎构造端口为必填 T）。
        => new CommandEngine(new EngineCommandRegistry());

    public bool CanUndo => _history.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public void RegisterCommand(string name, Func<CommandExecutionContext, CancellationToken, Task> execute,
        Func<CommandExecutionContext, CancellationToken, Task>? undo = null, string? description = null)
    {
        _commands.RegisterCommand(name, execute, undo, description);
    }

    public void RegisterCommand(ICommand command)
    {
        _commands.RegisterCommand(command);
    }

    async Task<bool> ICommandEngine.ExecuteAsync(string name, CommandExecutionContext? context,
        CancellationToken ct)
        => await ExecuteAsync(name, context, ct, recursionDepth: 0);

    public async Task<bool> ExecuteAsync(string name, CommandExecutionContext? context = null,
        CancellationToken ct = default, int recursionDepth = 0)
    {
        context ??= new CommandExecutionContext();

        if (_commands.TryGet(name, out var registration))
        {
            try
            {
                await registration.Execute(context, ct);
                registration.LastContext = context;
                _history.Push(registration);
                _redoStack.Clear();
                return true;
            }
            catch (Exception ex)
            {
                context.LastError = ex.Message;
                return false;
            }
        }

        if (recursionDepth >= MaxCommandRecursionDepth)
        {
            context.LastError = $"Command '{name}' not found (max recursion depth reached)";
            return false;
        }

        if (_commandRegistry != null)
        {
            var command = _commandRegistry.Resolve(name);
            if (command != null)
            {
                RegisterCommand(command);
                return await ExecuteAsync(name, context, ct, recursionDepth + 1);
            }
        }

        context.LastError = $"Command '{name}' not found";
        return false;
    }

    public async Task<bool> UndoAsync(CancellationToken ct = default)
    {
        if (_history.Count == 0)
            return false;

        while (_history.Count > 0)
        {
            var registration = _history.Peek();
            if (registration.Undo != null)
                break;
            _history.Pop();
        }

        if (_history.Count == 0)
            return false;

        var undoRegistration = _history.Pop();
        try
        {
            var undoContext = undoRegistration.LastContext ?? new CommandExecutionContext();
            await undoRegistration.Undo(undoContext, ct);
            _redoStack.Push(undoRegistration);
            return true;
        }
        catch (Exception ex)
        {
            CommandFramework.Diagnostics.CommandFrameworkDiagnosticContext.Record("command.undo.failed", new { commandName = undoRegistration.Name, error = ex.Message });
            return false;
        }
    }

    public async Task<bool> RedoAsync(CancellationToken ct = default)
    {
        if (_redoStack.Count == 0)
            return false;

        var registration = _redoStack.Pop();
        try
        {
            var redoContext = registration.LastContext ?? new CommandExecutionContext();
            await registration.Execute(redoContext, ct);
            registration.LastContext = redoContext;
            _history.Push(registration);
            return true;
        }
        catch (Exception ex)
        {
            CommandFramework.Diagnostics.CommandFrameworkDiagnosticContext.Record("command.redo.failed", new { commandName = registration.Name, error = ex.Message });
            return false;
        }
    }

    public void ClearHistory()
    {
        _history.Clear();
        _redoStack.Clear();
    }

    public bool IsCommandRegistered(string name)
    {
        return _commands.IsCommandRegistered(name);
    }

    public string[] GetRegisteredCommands()
    {
        return _commands.GetKeys().ToArray();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _commands.Clear();
            _history.Clear();
            _redoStack.Clear();
            _disposed = true;
        }
    }

    public class CommandRegistration
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Func<CommandExecutionContext, CancellationToken, Task> Execute { get; set; } = null!;
        public Func<CommandExecutionContext, CancellationToken, Task>? Undo { get; set; }
        public CommandExecutionContext? LastContext { get; set; }
    }
}


public static class CommandEngineExtensions
{
    public static void RegisterHandler(this CommandEngine engine, string commandName, Func<string, Task<bool>> handler)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(handler);

        engine.RegisterCommand(commandName, async (context, ct) =>
        {
            var commandText = context?.Data?.ToString() ?? commandName;
            var success = await handler(commandText);
            if (!success)
            {
                throw new InvalidOperationException($"Handler for '{commandName}' returned false");
            }
        });
    }

    public static void RegisterHandler(this CommandEngine engine, string commandName, Func<string, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        engine.RegisterHandler(commandName, cmd => Task.FromResult(handler(cmd)));
    }

    public static void RegisterHandlers(this CommandEngine engine, IDictionary<string, Func<string, Task<bool>>> handlers)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(handlers);

        foreach (var kvp in handlers)
        {
            engine.RegisterHandler(kvp.Key, kvp.Value);
        }
    }

}


public class DelegateCommand : ICommand
{
    private readonly Func<CommandExecutionContext, CancellationToken, Task> _executeWithContext;
    private readonly Func<CommandExecutionContext, CancellationToken, Task>? _undoWithContext;

    internal CommandExecutionContext? LastContext { get; private set; }

    public string Name { get; }

    public string? Description { get; }

    public bool CanUndo => _undoWithContext != null;

    public DelegateCommand(
        string name,
        Func<CancellationToken, Task> execute,
        string? description = null,
        Func<CancellationToken, Task>? undo = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _executeWithContext = (_, ct) => execute(ct);
        _undoWithContext = undo != null ? (_, ct) => undo(ct) : null;
        Description = description;
    }

    public DelegateCommand(
        string name,
        Func<CommandExecutionContext, CancellationToken, Task> execute,
        string? description = null,
        Func<CommandExecutionContext, CancellationToken, Task>? undo = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _executeWithContext = execute ?? throw new ArgumentNullException(nameof(execute));
        _undoWithContext = undo;
        Description = description;
    }

    private CommandExecutionContext GetEffectiveContext(CommandExecutionContext? context)
    {
        return context ?? new CommandExecutionContext { Name = Name };
    }

    public async Task ExecuteAsync(CancellationToken ct = default, CommandExecutionContext? context = null)
    {
        var effectiveContext = GetEffectiveContext(context);
        LastContext = effectiveContext;
        await _executeWithContext(effectiveContext, ct);
    }

    async Task ICommand.ExecuteAsync(CancellationToken ct)
    {
        await ExecuteAsync(ct, null);
    }

    public async Task UndoAsync(CancellationToken ct = default)
    {
        await UndoAsync(ct, LastContext);
    }

    public async Task UndoAsync(CancellationToken ct, CommandExecutionContext? context)
    {
        if (_undoWithContext != null)
        {
            await _undoWithContext(GetEffectiveContext(context), ct);
        }
    }

}


public class CompositeCommand : ICommand
{
    private readonly List<ICommand> _commands = new();

    public string Name { get; }

    public string? Description { get; }

    public bool CanUndo => _commands.Count > 0 && _commands.All(c => c.CanUndo);

    public CompositeCommand(string name, string? description = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
    }

    public void AddCommand(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        foreach (var command in _commands)
        {
            ct.ThrowIfCancellationRequested();
            await command.ExecuteAsync(ct);
        }
    }

    public async Task UndoAsync(CancellationToken ct = default)
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            await _commands[i].UndoAsync(ct);
        }
    }
}
