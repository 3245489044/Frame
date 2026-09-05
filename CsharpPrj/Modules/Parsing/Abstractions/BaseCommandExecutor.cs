using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Parsing.Models;

namespace Modules.Parsing.Abstractions;

public abstract class BaseCommandExecutor : ICommandExecutor
{
    public string ExecutorId { get; }

    public string Name { get; }

    public string Version { get; }

    protected BaseCommandExecutor(string executorId, string name, string version)
    {
        ExecutorId = executorId ?? throw new System.ArgumentNullException(nameof(executorId));
        Name = name ?? throw new System.ArgumentNullException(nameof(name));
        Version = version ?? throw new System.ArgumentNullException(nameof(version));
    }

    public abstract Task<OperationResult<object>> DoAsync(string action, string? target,
                                                           Dictionary<string, object>? parameters,
                                                           CancellationToken cancellationToken = default);

    public abstract Task<OperationResult<object>> GetAsync(string entity,
                                                            Dictionary<string, object>? queryParams,
                                                            CancellationToken cancellationToken = default);

    public abstract Task<OperationResult<WorldModel>> LookAsync(string? scope,
                                                                 Dictionary<string, object>? options,
                                                                 CancellationToken cancellationToken = default);

    public abstract Task<OperationResult<bool>> WaitAsync(string condition, int timeoutMs,
                                                           CancellationToken cancellationToken = default);

    public abstract Task<OperationResult<bool>> AssertAsync(string predicate,
                                                             Dictionary<string, object>? parameters,
                                                             CancellationToken cancellationToken = default);

    public abstract Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(string? scope,
                                                                                 CancellationToken cancellationToken = default);

    public override string ToString() => $"{Name} (v{Version})";
}
