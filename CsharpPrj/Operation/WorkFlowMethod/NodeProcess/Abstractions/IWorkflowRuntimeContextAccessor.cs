using WorkFlowMethod.Orchestration.Context.Runtime;

namespace WorkFlowMethod.Skeletons.NodeProcess.Abstractions
{
    public interface IWorkflowRuntimeContextAccessor
    {
        WorkflowRuntimeContext GetOrCreateContext(string workflowId = null);

        WorkflowRuntimeContext CurrentContext { get; set; }
    }
}
