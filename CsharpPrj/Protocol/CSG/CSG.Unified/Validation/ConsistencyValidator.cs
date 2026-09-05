using CSG.Unified;

namespace CSG.Unified.Validation;

public class ConsistencyValidator
{
    public ValidationResult Validate(
        CanonicalForm configForm,
        CanonicalForm stateMachineForm,
        CanonicalForm graphForm)
    {
        var result = new ValidationResult();

        var configNodeIds = new HashSet<string>(configForm.Nodes.Select(n => n.Id));
        var smNodeIds = new HashSet<string>(stateMachineForm.Nodes.Select(n => n.Id));
        var graphNodeIds = new HashSet<string>(graphForm.Nodes.Select(n => n.Id));

        foreach (var nodeId in configNodeIds)
        {
            if (!smNodeIds.Contains(nodeId))
                result.AddError(CsgErrorCode.NodeCoverageViolation, "NodeCoverage", $"配置节点 '{nodeId}' 在状态机中不存在", nodeId);
            if (!graphNodeIds.Contains(nodeId))
                result.AddError(CsgErrorCode.NodeCoverageViolation, "NodeCoverage", $"配置节点 '{nodeId}' 在图中不存在", nodeId);
        }

        var configEdgeKeys = new HashSet<(string SourceId, string TargetId)>(
            configForm.Edges.Select(e => (e.SourceId, e.TargetId)));
        var smEdgeKeys = new HashSet<(string SourceId, string TargetId)>(
            stateMachineForm.Edges.Select(e => (e.SourceId, e.TargetId)));
        var graphEdgeKeys = new HashSet<(string SourceId, string TargetId)>(
            graphForm.Edges.Select(e => (e.SourceId, e.TargetId)));

        foreach (var edge in configForm.Edges)
        {
            var key = (edge.SourceId, edge.TargetId);
            if (!smEdgeKeys.Contains(key))
                result.AddWarning("EdgeCoverage", $"配置边 '{edge.Id}' ({key}) 在状态机中不存在", edge.SourceId, edge.TargetId);
            if (!graphEdgeKeys.Contains(key))
                result.AddWarning("EdgeCoverage", $"配置边 '{edge.Id}' ({key}) 在图中不存在", edge.SourceId, edge.TargetId);
        }

        foreach (var node in stateMachineForm.Nodes)
        {
            if (!graphNodeIds.Contains(node.Id))
                result.AddError(CsgErrorCode.ReachabilityViolation, "Reachability", $"状态机状态 '{node.Id}' 在图中不可达", node.Id);
        }

        var configNodeTypes = configForm.Nodes.ToDictionary(n => n.Id, n => n.Type);
        var smNodeTypes = stateMachineForm.Nodes.ToDictionary(n => n.Id, n => n.Type);
        var graphNodeTypes = graphForm.Nodes.ToDictionary(n => n.Id, n => n.Type);

        foreach (var nodeId in configNodeIds.Intersect(smNodeIds).Intersect(graphNodeIds))
        {
            if (configNodeTypes.TryGetValue(nodeId, out var cType) &&
                smNodeTypes.TryGetValue(nodeId, out var sType) &&
                graphNodeTypes.TryGetValue(nodeId, out var gType))
            {
                if (cType != sType || cType != gType)
                    result.AddWarning("TypeConsistency",
                        $"节点 '{nodeId}' 类型不一致: C={cType}, S={sType}, G={gType}", nodeId);
            }
        }

        return result;
    }
}
