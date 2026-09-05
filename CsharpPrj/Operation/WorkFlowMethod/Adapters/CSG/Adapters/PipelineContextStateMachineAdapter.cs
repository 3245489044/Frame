using CSG.Unified;
using WorkflowExecution.Pipeline;
using WorkFlowMethod.WorkFlowPipeline;

namespace WorkFlowMethod.CSG.Adapters;

public class PipelineContextStateMachineAdapter : IStateMachine
{
    private readonly PipelineContext _context;
    private readonly PipelineContextAccessor _accessor;
    private event Action<string, string, string>? _onStateChanged;

    public string CurrentState => _context.CurrentNodeKey ?? "idle";

    public PipelineContextStateMachineAdapter(PipelineContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _accessor = new PipelineContextAccessor(context);
    }

    public bool CanTransitionTo(string state)
    {
        if (string.IsNullOrEmpty(state))
            return false;

        if (string.Equals(_context.NextNodeKey, state, StringComparison.OrdinalIgnoreCase))
            return true;

        if (_context.GraphSnapshot != null)
        {
            return _accessor.Nodes.ContainsKey(state);
        }

        return false;
    }

    public bool TryTransitionTo(string state, object? trigger = null)
    {
        if (!CanTransitionTo(state))
            return false;

        var oldState = CurrentState;

        _context.CurrentNodeKey = state;
        _context.NextNodeKey = null;

        if (trigger != null)
        {
            _context.CurrentInput = trigger;
        }

        _onStateChanged?.Invoke(_context.ContextId, oldState, state);

        return true;
    }

    public event Action<string, string, string>? OnStateChanged
    {
        add => _onStateChanged += value;
        remove => _onStateChanged -= value;
    }

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();

        form.Nodes.Add(new CanonicalNode
        {
            Id = CurrentState,
            Label = CurrentState,
            Type = CanonicalNodeType.State,
            Properties = new Dictionary<string, object?>
            {
                ["isCurrent"] = true,
                ["input"] = _context.CurrentInput?.ToString(),
                ["output"] = _context.CurrentOutput?.ToString()
            }
        });

        if (!string.IsNullOrEmpty(_context.NextNodeKey))
        {
            form.Nodes.Add(new CanonicalNode
            {
                Id = _context.NextNodeKey,
                Label = _context.NextNodeKey,
                Type = CanonicalNodeType.State,
                Properties = new Dictionary<string, object?>
                {
                    ["isCurrent"] = false,
                    ["isNext"] = true
                }
            });

            form.Edges.Add(new CanonicalEdge
            {
                Id = $"transition_{CurrentState}_to_{_context.NextNodeKey}",
                SourceId = CurrentState,
                TargetId = _context.NextNodeKey,
                Label = "next",
                Type = CanonicalEdgeType.Transition,
                Condition = _context.CurrentInput?.ToString()
            });
        }

        if (_context.GraphSnapshot != null)
        {
            var allNodes = _accessor.NodeKeys;
            foreach (var nodeKey in allNodes)
            {
                if (form.Nodes.Any(n => n.Id == nodeKey))
                    continue;

                form.Nodes.Add(new CanonicalNode
                {
                    Id = nodeKey,
                    Label = nodeKey,
                    Type = CanonicalNodeType.State,
                    Properties = new Dictionary<string, object?>
                    {
                        ["isCurrent"] = string.Equals(nodeKey, CurrentState, StringComparison.OrdinalIgnoreCase),
                        ["isNext"] = string.Equals(nodeKey, _context.NextNodeKey, StringComparison.OrdinalIgnoreCase)
                    }
                });
            }

            foreach (var edge in _accessor.Edges)
            {
                if (form.Edges.Any(e => e.SourceId == edge.SourceNodeKey && e.TargetId == edge.TargetNodeKey))
                    continue;

                form.Edges.Add(new CanonicalEdge
                {
                    Id = $"transition_{edge.SourceNodeKey}_to_{edge.TargetNodeKey}",
                    SourceId = edge.SourceNodeKey,
                    TargetId = edge.TargetNodeKey,
                    Label = edge.EdgeId ?? "transition",
                    Type = CanonicalEdgeType.Transition
                });
            }
        }

        form.Metadata["csgVersion"] = "1.0.0";
        form.Metadata["source"] = "WorkFlowMethod.PipelineContext.StateMachine";
        form.Metadata["contextId"] = _context.ContextId;
        form.Metadata["currentState"] = CurrentState;
        form.Metadata["created"] = DateTime.UtcNow.ToString("O");

        return form;
    }
}

