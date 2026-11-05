
namespace Modules.Parsing.Abstractions;

public interface IStringCommandExecutor
{
    bool Execute(string command);

    Task<bool> ExecuteAsync(string command);

    bool HasHandler(string commandName);

    IEnumerable<string> GetRegisteredCommands();

    void RegisterHandler(string commandName, Func<string, bool> handler);

    void RegisterHandler(string commandName, Func<string, Task<bool>> handler);
}
