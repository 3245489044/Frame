using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;

namespace Modules.Parsing.Adapters;

public class NullCommandExecutor : Abstractions.BaseCommandExecutor
{
    public NullCommandExecutor() : base("null", "Null Executor", "1.0") { }

    public override Task<OperationResult<object>> DoAsync(string action, string? target,
Dictionary<string, object>? parameters,
CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<object>.SuccessResult(null, "No-op: action skipped"));
    }

    public override Task<OperationResult<object>> GetAsync(string entity,
                                                            Dictionary<string, object>? queryParams,
                                                            CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<object>.SuccessResult(null, "No-op: get skipped"));
    }

    public override Task<OperationResult<WorldModel>> LookAsync(string? scope,
Dictionary<string, object>? options,
CancellationToken cancellationToken = default)
    {
        var model = new WorldModel
        {
            AdapterId = ExecutorId,
            Scope = scope ?? "default",
            State = new Dictionary<string, object>()
        };
        return Task.FromResult(OperationResult<WorldModel>.SuccessResult(model, "No-op: look skipped"));
    }

    public override Task<OperationResult<bool>> WaitAsync(string condition, int timeoutMs,
                                                           CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<bool>.SuccessResult(true, "No-op: wait skipped"));
    }

    public override Task<OperationResult<bool>> AssertAsync(string predicate,
                                                             Dictionary<string, object>? parameters,
                                                             CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<bool>.SuccessResult(true, "No-op: assert skipped"));
    }

    public override Task<OperationResult<List<EntityDescriptor>>> DiscoverAsync(string? scope,
                                                                                 CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<List<EntityDescriptor>>.SuccessResult(
            new List<EntityDescriptor>(), "No-op: discover skipped"));
    }
}
