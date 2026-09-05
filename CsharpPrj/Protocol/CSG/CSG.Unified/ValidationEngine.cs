using CSG.Unified;

namespace CSG.Unified;

public class ValidationEngine
{
    public ValidationResult Validate(CanonicalForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var result = new ValidationResult();

        if (form.State == FormState.Empty || form.State == FormState.Validated)
            return result;

        var duplicateIds = form.Nodes
            .GroupBy(n => n.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var id in duplicateIds)
            result.AddError(CsgErrorCode.DuplicateNodeId, "UniqueNodeId", $"重复的节点 ID: '{id}'", id);

        var duplicateEdgeIds = form.Edges
            .GroupBy(e => e.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var id in duplicateEdgeIds)
            result.AddError(CsgErrorCode.DuplicateEdgeId, "UniqueEdgeId", $"重复的边 ID: '{id}'");

        var nodeIds = new HashSet<string>(form.Nodes.Select(n => n.Id));
        foreach (var edge in form.Edges)
        {
            if (!nodeIds.Contains(edge.SourceId))
                result.AddError(CsgErrorCode.MissingSourceNode, "EdgeSourceExists",
                    $"边 '{edge.Id}' 引用的源节点 '{edge.SourceId}' 不存在",
                    null, edge.SourceId);

            if (!nodeIds.Contains(edge.TargetId))
                result.AddError(CsgErrorCode.MissingTargetNode, "EdgeTargetExists",
                    $"边 '{edge.Id}' 引用的目标节点 '{edge.TargetId}' 不存在",
                    edge.TargetId, null);
        }

        if (form.Nodes.Count == 0)
            result.AddError(CsgErrorCode.EmptyForm, "MinNodes", "规范形式中没有节点");

        var edgeNodeIds = new HashSet<string>();
        foreach (var edge in form.Edges)
        {
            edgeNodeIds.Add(edge.SourceId);
            edgeNodeIds.Add(edge.TargetId);
        }

        foreach (var node in form.Nodes)
        {
            if (!edgeNodeIds.Contains(node.Id))
                result.AddWarning("IsolatedNode", $"孤立节点: '{node.Id}'（没有任何边连接）", node.Id);
        }

        return result;
    }
}
