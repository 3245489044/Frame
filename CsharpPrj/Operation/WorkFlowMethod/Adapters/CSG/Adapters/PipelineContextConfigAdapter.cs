using CSG.Unified;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.WorkFlowPipeline;

namespace WorkFlowMethod.CSG.Adapters;

public class PipelineContextConfigAdapter : IConfiguration
{
    private readonly PipelineContext _context;
    private readonly PipelineContextAccessor _accessor;

    public string ConfigType => "WorkflowPipeline";

    public string SchemaVersion => "1.0";

    public PipelineContextConfigAdapter(PipelineContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _accessor = new PipelineContextAccessor(context);
    }

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();

        foreach (var kvp in _context.SharedData)
        {
            var node = new CanonicalNode
            {
                Id = $"param_{kvp.Key}",
                Label = kvp.Key,
                Type = CanonicalNodeType.Data,
                Properties = new Dictionary<string, object?>
                {
                    ["value"] = kvp.Value?.ToString(),
                    ["valueType"] = kvp.Value?.GetType().Name ?? "unknown"
                }
            };
            form.Nodes.Add(node);
        }

        if (!string.IsNullOrEmpty(_context.CurrentNodeKey))
        {
            form.Nodes.Add(new CanonicalNode
            {
                Id = _context.CurrentNodeKey,
                Label = _context.CurrentNodeKey,
                Type = CanonicalNodeType.Action,
                Properties = new Dictionary<string, object?>
                {
                    ["role"] = "current",
                    ["input"] = _context.CurrentInput?.ToString(),
                    ["output"] = _context.CurrentOutput?.ToString()
                }
            });
        }

        if (!string.IsNullOrEmpty(_context.NextNodeKey))
        {
            form.Nodes.Add(new CanonicalNode
            {
                Id = _context.NextNodeKey,
                Label = _context.NextNodeKey,
                Type = CanonicalNodeType.Action,
                Properties = new Dictionary<string, object?>
                {
                    ["role"] = "next"
                }
            });
        }

        if (_context.GraphSnapshot != null)
        {
            var snapshotId = _accessor.SnapshotId;
            form.Nodes.Add(new CanonicalNode
            {
                Id = $"snapshot_{snapshotId}",
                Label = $"GraphSnapshot:{snapshotId}",
                Type = CanonicalNodeType.Data,
                Properties = new Dictionary<string, object?>
                {
                    ["graphId"] = _accessor.GraphId,
                    ["version"] = _accessor.Version.ToString()
                }
            });
        }

        if (!string.IsNullOrEmpty(_context.CurrentNodeKey) && !string.IsNullOrEmpty(_context.NextNodeKey))
        {
            form.Edges.Add(new CanonicalEdge
            {
                Id = $"flow_{_context.CurrentNodeKey}_to_{_context.NextNodeKey}",
                SourceId = _context.CurrentNodeKey,
                TargetId = _context.NextNodeKey,
                Label = "next",
                Type = CanonicalEdgeType.ControlFlow
            });
        }

        form.Metadata["csgVersion"] = "1.0.0";
        form.Metadata["source"] = "WorkFlowMethod.PipelineContext";
        form.Metadata["contextId"] = _context.ContextId;
        form.Metadata["created"] = DateTime.UtcNow.ToString("O");

        return form;
    }
}

