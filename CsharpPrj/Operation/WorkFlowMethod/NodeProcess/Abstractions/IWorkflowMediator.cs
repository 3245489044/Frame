using GraphFramework.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace WorkFlowMethod.Skeletons.NodeProcess.Abstractions
{
    public interface IWorkflowMediator
    {
        Chain<string, NodeConfig> BuildFromConfig(ChainConfig config);
    }
}
