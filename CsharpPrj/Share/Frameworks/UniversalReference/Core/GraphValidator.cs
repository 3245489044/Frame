using System.Collections.Generic;
using System.Linq;
using Modules.GraphProcessing.Algorithms;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.Core
{
    /// <summary>
    /// 图校验器。路径遍历逻辑已下沉至 GraphProcessing.Algorithms.DefaultPathFinder，
    /// 本类保留 UR 签名（含完整性校验），消费方零改动。
    /// </summary>
    public class GraphValidator<TNodeId> where TNodeId : notnull
    {
        private readonly DefaultPathFinder<TNodeId> _pathFinder = new();

        public ValidationResult ValidateIntegrity(
            IEnumerable<TNodeId> allNodes,
            IEnumerable<(TNodeId Source, TNodeId Target)> edges,
            Func<TNodeId, bool> isStartNode,
            Func<TNodeId, bool> isEndNode)
        {
            var nodeList = allNodes.ToList();
            var edgeList = edges.ToList();

            var startNodes = nodeList.Where(isStartNode).ToList();
            if (startNodes.Count == 0)
                return new ValidationResult { IsValid = false, Message = "图中缺少开始节点" };

            if (startNodes.Count > 1)
                return new ValidationResult { IsValid = false, Message = "图中只能有一个开始节点" };

            var endNodes = nodeList.Where(isEndNode).ToList();
            if (endNodes.Count == 0)
                return new ValidationResult { IsValid = false, Message = "图中缺少结束节点" };

            var nodeIds = new HashSet<TNodeId>();
            foreach (var node in nodeList)
            {
                if (!nodeIds.Add(node))
                    return new ValidationResult { IsValid = false, Message = $"节点ID重复: {node}" };
            }

            var nodeSet = new HashSet<TNodeId>(nodeList);
            foreach (var (source, target) in edgeList)
            {
                if (!nodeSet.Contains(source))
                    return new ValidationResult { IsValid = false, Message = $"边的源节点不存在: {source}" };

                if (!nodeSet.Contains(target))
                    return new ValidationResult { IsValid = false, Message = $"边的目标节点不存在: {target}" };
            }

            return new ValidationResult { IsValid = true, Message = "图完整性验证通过" };
        }

        public bool HasPath(TNodeId source, TNodeId target,
            Func<TNodeId, IEnumerable<TNodeId>> getOutgoingEdges)
        {
            return _pathFinder.HasPath(source, target, getOutgoingEdges);
        }

        public List<List<TNodeId>> FindAllPaths(TNodeId source, TNodeId target,
            Func<TNodeId, IEnumerable<TNodeId>> getOutgoingEdges,
            int maxDepth = 10)
        {
            return _pathFinder.FindAllPaths(source, target, getOutgoingEdges, maxDepth)
                .Select(p => p.ToList())
                .ToList();
        }

        public bool HasPathToAnyEnd(TNodeId startNode, IEnumerable<TNodeId> endNodes,
            Func<TNodeId, IEnumerable<TNodeId>> getOutgoingEdges)
        {
            return _pathFinder.HasPathToAnyEnd(startNode, endNodes, getOutgoingEdges);
        }

        public HashSet<TNodeId> GetReachableNodes(TNodeId startNode,
            Func<TNodeId, IEnumerable<TNodeId>> getOutgoingEdges)
        {
            return _pathFinder.GetReachableNodes(startNode, getOutgoingEdges).ToHashSet();
        }
    }
}
