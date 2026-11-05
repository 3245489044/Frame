
using System.Collections.Concurrent;

namespace Modules.EventFramework.Bus;

public class DefaultCommandExecutor : TConfig.ICommandExecutor
{
    private readonly ConcurrentDictionary<string, Func<string, Task<bool>>> _asyncHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<string, bool>> _syncHandlers = new(StringComparer.OrdinalIgnoreCase);

    public bool Execute(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var commandName = ExtractCommandName(command);

        if (_syncHandlers.TryGetValue(commandName, out var handler))
            return handler(command);

        if (_asyncHandlers.TryGetValue(commandName, out _))
            return Task.Run(() => ExecuteAsync(command)).GetAwaiter().GetResult();

        return false;
    }

    public async Task<bool> ExecuteAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var commandName = ExtractCommandName(command);

        if (_asyncHandlers.TryGetValue(commandName, out var asyncHandler))
            return await asyncHandler(command);

        if (_syncHandlers.TryGetValue(commandName, out var syncHandler))
            return syncHandler(command);

        return false;
    }

    public bool HasHandler(string commandName)
    {
        return _syncHandlers.ContainsKey(commandName) || _asyncHandlers.ContainsKey(commandName);
    }

    public IEnumerable<string> GetRegisteredCommands()
    {
        return _syncHandlers.Keys.Union(_asyncHandlers.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public void RegisterHandler(string commandName, Func<string, bool> handler)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            throw new ArgumentException("命令名称不能为空", nameof(commandName));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _syncHandlers[commandName] = handler;
    }

    public void RegisterHandler(string commandName, Func<string, Task<bool>> handler)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            throw new ArgumentException("命令名称不能为空", nameof(commandName));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _asyncHandlers[commandName] = handler;
    }

    private static string ExtractCommandName(string command)
    {
        var colonIndex = command.IndexOf(':');
        return colonIndex > 0 ? command[..colonIndex].Trim() : command.Trim();
    }
}
