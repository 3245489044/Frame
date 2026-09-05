
using WorkFlowMethod.Orchestration.Context.Models;

namespace WorkFlowMethod.FunctionExecutionFramework.Models
{
    public class ExecutionRequest
    {
        public string StartNodeKey { get; set; } = string.Empty;

        public Dictionary<string, object> Parameters { get; set; } = new();

        public Dictionary<string, Func<WorkflowContext, object>>? FunctionOverrides { get; set; }

        public Dictionary<string, Func<WorkflowContext, Task<object>>>? AsyncFunctionOverrides { get; set; }

        public Dictionary<string, object> ContextData { get; set; } = new();

        public int? ExecutionTimeoutMs { get; set; }

        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        public string? WorkflowId { get; set; }

        public static ExecutionRequest CreateDefault(string startNodeKey)
        {
            return new ExecutionRequest
            {
                StartNodeKey = startNodeKey
            };
        }

        public static ExecutionRequest CreateWithParameters(string startNodeKey, Dictionary<string, object> parameters)
        {
            return new ExecutionRequest
            {
                StartNodeKey = startNodeKey,
                Parameters = parameters
            };
        }

        public WorkflowContext ToWorkflowContext()
        {
            return new WorkflowContext(
                parameters: Parameters,
                data: ContextData,
                nodeKey: StartNodeKey,
                workflowId: WorkflowId
            )
            {
                FunctionOverrides = FunctionOverrides,
                AsyncFunctionOverrides = AsyncFunctionOverrides,
                CancellationToken = CancellationToken
            };
        }
    }
}
