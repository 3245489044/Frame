using Modules.Parsing.Models;

namespace Modules.Parsing.Abstractions;

public interface ICommandExecutor
{
    Task<OperationResult<object>> DoAsync(string action, string? target,
                                           Dictionary<string, object>? parameters,
                                           CancellationToken cancellationToken = default);

    Task<OperationResult<object>> GetAsync(string entity,
                                            Dictionary<string, object>? queryParams,
                                            CancellationToken cancellationToken = default);

    Task<OperationResult<WorldModel>> LookAsync(string? scope,
                                                 Dictionary<string, object>? options,
                                                 CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> WaitAsync(string condition, int timeoutMs,
                                           CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> AssertAsync(string predicate,
                                             Dictionary<string, object>? parameters,
                                             CancellationToken cancellationToken = default);

    Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(string? scope,
                                                                 CancellationToken cancellationToken = default);
}
