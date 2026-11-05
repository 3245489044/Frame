using Modules.Parsing.Models;

namespace Modules.Parsing.Abstractions;

public delegate Task<OperationResult<object>> CommandDelegate(Modules.Parsing.Models.MetaCommand command, CancellationToken cancellationToken);

public interface ICommandMiddleware
{
    Task<OperationResult<object>> InvokeAsync(Modules.Parsing.Models.MetaCommand command, CommandDelegate next, CancellationToken cancellationToken);
}
