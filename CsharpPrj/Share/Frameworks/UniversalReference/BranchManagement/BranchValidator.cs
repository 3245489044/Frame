using System.Collections.Generic;
using System.Linq;
using UniversalWorkflowFramework.BranchManagement.Models;
using UniversalWorkflowFramework.Core;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.BranchManagement.Validators
{
    public class BranchValidator
    {
        private readonly GraphValidator<string> _graphValidator = new();

        public ValidationResult ValidateBranchIntegrity(BranchGraph branchGraph)
        {
            var allNodes = branchGraph.Nodes.Select(n => n.NodeId);
            var edges = branchGraph.Edges.Select(e => (e.SourceNodeId, e.TargetNodeId));

            return _graphValidator.ValidateIntegrity(
                allNodes,
                edges,
                isStartNode: id => branchGraph.Nodes.Any(n => n.NodeId == id && n.Type == BranchNodeType.Start),
                isEndNode: id => branchGraph.Nodes.Any(n => n.NodeId == id && n.Type == BranchNodeType.End)
            );
        }

        public ValidationResult ValidateBranchFunctionality(BranchGraph branchGraph)
        {
            var startNode = branchGraph.Nodes.Find(n => n.Type == BranchNodeType.Start);
            if (startNode == null)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Message = "分支缺少开始节点"
                };
            }

            var endNodes = branchGraph.Nodes.Where(n => n.Type == BranchNodeType.End).Select(n => n.NodeId);

            var hasPath = _graphValidator.HasPathToAnyEnd(
                startNode.NodeId,
                endNodes,
                id => branchGraph.Edges.Where(e => e.SourceNodeId == id).Select(e => e.TargetNodeId)
            );

            return new ValidationResult
            {
                IsValid = hasPath,
                Message = hasPath ? "分支功能性验证通过" : "分支缺少从开始节点到结束节点的路径"
            };
        }
    }
}
