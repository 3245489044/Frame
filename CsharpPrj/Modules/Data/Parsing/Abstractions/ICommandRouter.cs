using Modules.Parsing.Models;

namespace Modules.Parsing.Abstractions;

public interface ICommandRouter
{
    Task<OperationResult<object>> ExecuteCommandAsync(string command,
                                                       CancellationToken cancellationToken = default);

    Task<OperationResult<object>> ExecuteMetaCommandAsync(Modules.Parsing.Models.MetaCommand command,
                                                           CancellationToken cancellationToken = default);

    void RegisterExecutor(string executorId, ICommandExecutor executor);
    void SetActiveExecutor(string executorId);
    string? GetActiveExecutorId();
    IEnumerable<string> GetRegisteredExecutors();
    bool TryGetExecutor(string executorId, out ICommandExecutor? executor);
    void UnregisterExecutor(string executorId);
    CommandMetrics GetMetrics();
    void ResetMetrics();
}
