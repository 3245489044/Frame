#region USINGS
using System.Collections.Concurrent;
using WorkFlowMethod.Infrastructure.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Runtime
{
    public class WorkflowRuntimeContextAccessor : IWorkflowRuntimeContextAccessor
    {
        private readonly ConcurrentDictionary<string, WorkflowRuntimeContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

        public WorkflowRuntimeContext GetOrCreateContext(string workflowId = null)
        {
            return _contexts.GetOrAdd(workflowId ?? "default", id =>
                new WorkflowRuntimeContext());
        }

        public WorkflowRuntimeContext CurrentContext { get; set; }
    }
}
