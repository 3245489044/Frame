
using CommandFramework.Abstractions;
using CommandFramework.Execution;
using CommandFramework.Models;
using CommandFramework.Parsing;
using Modules.Registry;

namespace CommandFramework.Registration;

public class CommandRegistry : FactoryRegistryBase<string, ICommand>
{
    public CommandRegistry()
        : base(StringComparer.OrdinalIgnoreCase) { }

    public void Register<T>() where T : ICommand, new()
    {
        var name = typeof(T).FullName ?? typeof(T).Name;
        Register(name, _ => new T());
    }

    public void Register<T>(string name) where T : ICommand, new()
    {
        Register(name, _ => new T());
    }

    public void Register(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("命令名称不能为空");
        Register(command.Name, _ => command);
    }

    public new ICommand? Resolve(string name, IServiceProvider? serviceProvider = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        try
        {
            return base.Resolve(name, serviceProvider);
        }
        catch (Exception ex)
        {
            CommandFramework.Diagnostics.CommandFrameworkDiagnosticContext.Record("command.resolve.failed", new { commandName = name, error = ex.Message });
            return null;
        }
    }

    public bool IsRegistered(string name) => Contains(name);

    public string[] GetRegisteredCommands() => GetKeys().ToArray();

    public void RegisterRange(IEnumerable<KeyValuePair<string, Func<IServiceProvider?, ICommand>>> commands)
    {
        foreach (var kvp in commands)
            Register(kvp.Key, kvp.Value);
    }
}

public class EngineCommandRegistry : MapRegistryBase<string, CommandEngine.CommandRegistration>
{
    public EngineCommandRegistry(IEqualityComparer<string>? comparer = null)
        : base(comparer ?? StringComparer.OrdinalIgnoreCase) { }

    public void RegisterCommand(string name,
        Func<CommandExecutionContext, CancellationToken, Task> execute,
        Func<CommandExecutionContext, CancellationToken, Task>? undo = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        Register(name, new CommandEngine.CommandRegistration
        {
            Name = name,
            Description = description,
            Execute = execute,
            Undo = undo
        });
    }

    public void RegisterCommand(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Register(command.Name, new CommandEngine.CommandRegistration
        {
            Name = command.Name,
            Description = command.Description,
            Execute = async (ctx, ct) => await command.ExecuteAsync(ct),
            Undo = command.CanUndo ? async (ctx, ct) => await command.UndoAsync(ct) : null
        });
    }

    public bool TryGetCommand(string name, out CommandEngine.CommandRegistration? registration)
    {
        return TryGet(name, out registration);
    }

    public bool IsCommandRegistered(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && Contains(name);
    }

    public string[] GetRegisteredCommandNames() => GetKeys().ToArray();

    public new void RegisterRange(
        IEnumerable<KeyValuePair<string, CommandEngine.CommandRegistration>> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var kvp in commands)
            Register(kvp.Key, kvp.Value);
    }
}

public class HandlerRegistry : MapRegistryBase<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>>
{
public HandlerRegistry(IEqualityComparer<string>? comparer = null)
    : base(comparer ?? StringComparer.OrdinalIgnoreCase)
{
}

    public new bool Contains(string actionName)
    {
        return !string.IsNullOrEmpty(actionName) && base.Contains(actionName);
    }

    public string[] GetRegisteredNames()
    {
        return GetKeys().ToArray();
    }
}
