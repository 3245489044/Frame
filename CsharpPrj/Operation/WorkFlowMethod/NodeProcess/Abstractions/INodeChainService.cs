using GraphFramework.Models;
using WorkFlowMethod.Orchestration;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Orchestration.Context.Runtime;

namespace WorkFlowMethod.Skeletons.NodeProcess.Abstractions
{
    public interface INodeChainService
    {
        Chain<string, TNode> CreateChain<TNode>(
            string json, int chainCount = 1, string workflowId = null)
            where TNode : NodeConfig;

        Chain<string, TNode> CreateChainedNodes<TNode>(
            string json, int chainCount,
            WorkflowRuntimeContext context = null, string nodeCallkeyPattern = null)
            where TNode : NodeConfig;
    }
}
