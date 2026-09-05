
using System.Threading;
using System.Threading.Tasks;

namespace TConfig
{
    public interface IEventExecutionService
    {
        Task<ExecutionResult> ExecuteEventAsync(string eventName, object? sender, object? args);

        Task<ExecutionResult> ExecuteFunctionAsync(string functionName, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default);

        void RegisterWorkflows(Dictionary<string, WorkflowDefinitionEntry> workflowDefinitions);
    }
}
