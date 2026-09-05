using CSG.Unified;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.CSG.Adapters;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFramework.Snapshots;
using WorkFlowMethod.WorkFlowPipeline;
using GraphFramework.Models;

namespace WorkFlowMethod.CSG;

public class CsgIntegrationService : IDisposable
{
    private readonly CsgFacade _csg;
    private bool _disposed;

    public CsgFacade Facade => _csg;

    public CsgIntegrationService()
    {
        _csg = new CsgFacade(options =>
        {
            options.EnableCache = true;
            options.UseDefaultRules = true;
        });

        RegisterAdapters();
    }

    private void RegisterAdapters()
    {
        _csg.RegisterAdapter<PipelineContext, IConfiguration>(
            ctx => new PipelineContextConfigAdapter(ctx));

        _csg.RegisterAdapter<PipelineContext, IStateMachine>(
            ctx => new PipelineContextStateMachineAdapter(ctx));

        _csg.RegisterAdapter<GraphSnapshot<string, NodeConfig, object>, IGraph>(
            snap => new GraphSnapshotGraphAdapter(snap));
    }

    public CanonicalForm ChainToCanonical(Chain<string, NodeConfig> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var form = new CanonicalForm();

        foreach (var node in chain.AllNodes)
        {
            var nodeId = node.basicConfig?.Callkey ?? node.Id ?? node.Symbol ?? Guid.NewGuid().ToString("N");
            form.Nodes.Add(new CanonicalNode
            {
                Id = nodeId,
                Label = node.Label ?? node.Symbol ?? nodeId,
                Type = CanonicalNodeType.Default,
                Properties = new Dictionary<string, object?>
                {
                    ["symbol"] = node.Symbol,
                    ["callKey"] = node.basicConfig?.Callkey,
                    ["nextNodeId"] = node.NextNodeId
                }
            });
        }

        var edgeIndex = 0;
        foreach (var node in chain.AllNodes)
        {
            var sourceId = node.basicConfig?.Callkey ?? node.Id ?? node.Symbol ?? Guid.NewGuid().ToString("N");
            if (!string.IsNullOrEmpty(node.NextNodeId))
            {
                edgeIndex++;
                form.Edges.Add(new CanonicalEdge
                {
                    Id = $"chain_edge_{edgeIndex}",
                    SourceId = sourceId,
                    TargetId = node.NextNodeId,
                    Label = "next",
                    Type = CanonicalEdgeType.Default
                });
            }
        }

        form.Metadata["csgVersion"] = "1.0.0";
        form.Metadata["source"] = "WorkFlowMethod.ChainInfo";
        form.Metadata["nodeCount"] = chain.AllNodes.Count.ToString();
        form.Metadata["edgeCount"] = edgeIndex.ToString();
        form.Metadata["created"] = DateTime.UtcNow.ToString("O");

        return form;
    }

    public ValidationResult ValidateWorkflow(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var errors = new List<string>();
        var warnings = new List<string>();

        var configAdapter = new PipelineContextConfigAdapter(context);
        var configForm = configAdapter.ToCanonical();

        var stateMachineAdapter = new PipelineContextStateMachineAdapter(context);
        var stateForm = stateMachineAdapter.ToCanonical();

        CanonicalForm? graphForm = null;
        var snapshot = context.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
        if (snapshot != null)
        {
            var graphAdapter = new GraphSnapshotGraphAdapter(snapshot);
            graphForm = graphAdapter.ToCanonical();
        }

        var configActionNodes = configForm.Nodes
            .Where(n => n.Type == CanonicalNodeType.Action)
            .Select(n => n.Id)
            .ToHashSet();

        var stateNodes = stateForm.Nodes
            .Select(n => n.Id)
            .ToHashSet();

        foreach (var actionNode in configActionNodes)
        {
            if (!stateNodes.Contains(actionNode))
            {
                warnings.Add($"C 投影中的 Action 节点 '{actionNode}' 在 S 投影中不存在");
            }
        }

        if (graphForm != null)
        {
            var graphNodeIds = graphForm.Nodes.Select(n => n.Id).ToHashSet();
            var currentState = stateMachineAdapter.CurrentState;

            if (!graphNodeIds.Contains(currentState) && currentState != "idle")
            {
                warnings.Add($"S 投影的当前状态 '{currentState}' 在 G 投影中不存在");
            }

            var stateTransitions = stateForm.Edges
                .Where(e => e.Type == CanonicalEdgeType.Transition)
                .Select(e => (e.SourceId, e.TargetId))
                .ToHashSet();

            foreach (var edge in graphForm.Edges)
            {
                if (!stateTransitions.Contains((edge.SourceId, edge.TargetId)))
                {
                    warnings.Add($"G 投影的边 '{edge.Id}' 在 S 投影中没有对应的 Transition");
                }
            }
        }

        if (configForm.Metadata.TryGetValue("contextId", out var configCtxId) &&
            stateForm.Metadata.TryGetValue("contextId", out var stateCtxId))
        {
            if (configCtxId?.ToString() != stateCtxId?.ToString())
            {
                errors.Add("C 投影和 S 投影的 ContextId 不一致");
            }
        }

        var result = new ValidationResult();
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                result.AddError(error);
        }
        foreach (var warning in warnings)
            result.Warnings.Add(warning);
        result.ConfigForm = configForm;
        result.StateForm = stateForm;
        result.GraphForm = graphForm;
        return result;
    }

    public string ExportToJson(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var form = new CanonicalForm();

        var configAdapter = new PipelineContextConfigAdapter(context);
        var configForm = configAdapter.ToCanonical();

        var stateMachineAdapter = new PipelineContextStateMachineAdapter(context);
        var stateForm = stateMachineAdapter.ToCanonical();

        var nodeIds = new HashSet<string>();
        foreach (var node in configForm.Nodes.Concat(stateForm.Nodes))
        {
            if (nodeIds.Add(node.Id))
            {
                form.Nodes.Add(node);
            }
        }

        var edgeKeys = new HashSet<string>();
        foreach (var edge in configForm.Edges.Concat(stateForm.Edges))
        {
            var key = $"{edge.SourceId}→{edge.TargetId}";
            if (edgeKeys.Add(key))
            {
                form.Edges.Add(edge);
            }
        }

        var exportSnapshot = context.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
        if (exportSnapshot != null)
        {
            var exportGraphAdapter = new GraphSnapshotGraphAdapter(exportSnapshot);
            var graphForm = exportGraphAdapter.ToCanonical();

            foreach (var node in graphForm.Nodes)
            {
                if (nodeIds.Add(node.Id))
                {
                    form.Nodes.Add(node);
                }
            }

            foreach (var edge in graphForm.Edges)
            {
                var key = $"{edge.SourceId}→{edge.TargetId}";
                if (edgeKeys.Add(key))
                {
                    form.Edges.Add(edge);
                }
            }
        }

        form.Metadata["csgVersion"] = "1.0.0";
        form.Metadata["source"] = "WorkFlowMethod.CsgIntegrationService";
        form.Metadata["contextId"] = context.ContextId;
        form.Metadata["exportType"] = "merged";
        form.Metadata["exportTime"] = DateTime.UtcNow.ToString("O");
        form.Metadata["nodeCount"] = form.Nodes.Count.ToString();
        form.Metadata["edgeCount"] = form.Edges.Count.ToString();

        return form.ToJson();
    }

    public object? ResolveUri(string uri, PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(context);

        if (!uri.StartsWith("csg://", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = uri[6..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        var resourceType = parts[0].ToLowerInvariant();
        var resourceId = parts[1];

        return resourceType switch
        {
            "node" => ResolveNode(resourceId, context),
            "edge" => ResolveEdge(resourceId, context),
            "context" => context.ContextId == resourceId ? context : null,
            "snapshot" => context.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>() is { } snap && snap.SnapshotId == resourceId ? snap : null,
            _ => null
        };
    }

    private object? ResolveNode(string nodeId, PipelineContext context)
    {
        if (string.Equals(context.CurrentNodeKey, nodeId, StringComparison.OrdinalIgnoreCase))
            return new { NodeId = nodeId, Role = "current", Input = context.CurrentInput, Output = context.CurrentOutput };

        if (string.Equals(context.NextNodeKey, nodeId, StringComparison.OrdinalIgnoreCase))
            return new { NodeId = nodeId, Role = "next" };

        var resolveSnapshot = context.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
        if (resolveSnapshot?.Nodes.ContainsKey(nodeId) == true)
            return new { NodeId = nodeId, Role = "graph", Definition = new PipelineContextAccessor(context).GetNodeData(nodeId) };

        return null;
    }

    private object? ResolveEdge(string edgeId, PipelineContext context)
    {
        var resolveEdgeSnapshot = context.GetGraphSnapshot<GraphSnapshot<string, NodeConfig, object>>();
        if (resolveEdgeSnapshot != null)
        {
            var edge = new PipelineContextAccessor(context).Edges
                .FirstOrDefault(e => e.EdgeId == edgeId);
            if (edge != null)
                return edge;
        }

        return null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _csg?.Dispose();
            _disposed = true;
        }
    }
}

public class ValidationResult : WorkFlowMethod.Infrastructure.ValidationResult
{
    public List<string> Warnings { get; set; } = new();

    public CanonicalForm? ConfigForm { get; set; }

    public CanonicalForm? StateForm { get; set; }

    public CanonicalForm? GraphForm { get; set; }
}

