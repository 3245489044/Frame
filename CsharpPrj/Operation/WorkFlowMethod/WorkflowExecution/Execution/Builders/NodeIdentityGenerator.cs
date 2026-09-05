#region USINGS
using System;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Orchestration.IdManagement;
#endregion

namespace WorkFlowMethod.Orchestration.Content
{
    public interface INodeIdentityGenerator
    {
        string GenerateNodeId(NodeConfig node);
        string GenerateNodeCallkey(NodeConfig node);
    }

    public class DefaultNodeIdentityGenerator : INodeIdentityGenerator
    {
        public string GenerateNodeId(NodeConfig node) => IdServices.Generator.GenerateId();
        public string GenerateNodeCallkey(NodeConfig node) => $"{IdServices.Generator.GenerateId()}_{node.Symbol ?? "node"}";
    }
}
