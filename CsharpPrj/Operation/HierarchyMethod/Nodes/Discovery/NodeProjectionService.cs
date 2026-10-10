using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;

namespace Operation.HierarchyMethod.Nodes.Discovery;


public sealed class NodeProjectionService
{
    private readonly ReferenceGrounding _grounding;
    private readonly INodeStore _nodes;

    public NodeProjectionService(ReferenceGrounding grounding, INodeStore nodes)
    {
        _grounding = grounding;
        _nodes = nodes;
    }

    public async Task<ProjectionResult> ProjectAsync(string taskKey, string workflowId, int limit, CancellationToken ct)
    {
        var result = new ProjectionResult { TaskKey = taskKey, WorkflowId = workflowId };
        var projected = await _grounding.ProjectTaskNodesAsync(taskKey, workflowId, ct).ConfigureAwait(false);
        if (projected.Count == 0) return result;

        var order = 0;
        foreach (var item in projected)
        {
            if (limit > 0 && result.Candidates.Count >= limit) break;
            var node = await NodeRecordResolver.ResolveAsync(_nodes, workflowId, item.NodeKey, ct).ConfigureAwait(false);
            var size = node is null ? 0 : Plate.DslMethod.Usharp.UsharpValueJson.ToPayloadText(node.Payload).Length;
            result.Candidates.Add(new CandidateItem
            {
                NodeKey = item.NodeKey,
                Order = ++order,
                NodeType = node?.NodeType ?? string.Empty,
                Status = node?.Status ?? string.Empty,
                UpdatedAt = node?.UpdatedAt ?? string.Empty,
                SizeBytes = size,
                Summary = NodeSummaryBuilder.Build(node),
                EstTokens = TokenEstimator.Estimate(size),
            });
        }
        result.TotalEstTokens = result.Candidates.Sum(c => c.EstTokens);
        return result;
    }
}
