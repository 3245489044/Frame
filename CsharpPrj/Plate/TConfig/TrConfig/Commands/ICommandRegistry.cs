
namespace TConfig;

public interface ICommandRegistry
{
    void Register(string commandName, Action<ICommandExecutionContext> handler, CommandCriticality criticality = CommandCriticality.Important, string? description = null);

    void RegisterAsync(string commandName, Func<ICommandExecutionContext, Task> handler, CommandCriticality criticality = CommandCriticality.Important, string? description = null);

    bool TryGetHandler(string commandName, out Action<ICommandExecutionContext>? handler);

    bool TryGetAsyncHandler(string commandName, out Func<ICommandExecutionContext, Task>? handler);

    object? Execute(string command);

    Task<object?> ExecuteAsync(string command);

    IEnumerable<string> GetRegisteredCommands();

    bool IsRegistered(string commandName);

    int Count { get; }
}

public enum CommandCriticality
{
    Low = 0,
    Normal = 1,
    Important = 2,
    Critical = 3
}

