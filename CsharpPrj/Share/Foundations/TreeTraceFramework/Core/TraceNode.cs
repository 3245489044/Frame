
using Share.TreeTraceFramework.Abstractions;

namespace Share.TreeTraceFramework.Core
{
    public class TraceNode : ITraceNode<TraceNode>
    {
        private readonly List<object?> _inputs = new();
        private readonly List<object?> _outputs = new();
        private readonly List<string> _labels = new();
        private readonly List<string> _records = new();
        private readonly List<TraceNode> _children = new();
        private readonly object _lock = new();
        private readonly WeakReference<ITraceTree<TraceNode>> _owner;

        public string NodeId { get; }

        public string Name { get; }

        public IReadOnlyList<string> Labels
        {
            get { lock (_lock) return _labels.ToList().AsReadOnly(); }
        }

        public IReadOnlyList<object?> Inputs
        {
            get { lock (_lock) return _inputs.ToList().AsReadOnly(); }
        }

        public IReadOnlyList<object?> Outputs
        {
            get { lock (_lock) return _outputs.ToList().AsReadOnly(); }
        }

        public IReadOnlyList<TraceNode> Children
        {
            get { lock (_lock) return _children.ToList().AsReadOnly(); }
        }

        public TraceNode? Parent { get; private set; }

        public IReadOnlyList<string> Records
        {
            get { lock (_lock) return _records.ToList().AsReadOnly(); }
        }

        public DateTime CreatedAt { get; }

        public int Depth { get; private set; }

        public TraceNode(string name, ITraceTree<TraceNode> owner, string? nodeId = null)
        {
            Name = PortGuard.Required(name, nameof(name));
            NodeId = nodeId ?? Guid.NewGuid().ToString("N");
            _owner = new WeakReference<ITraceTree<TraceNode>>(PortGuard.Required(owner, nameof(owner)));
            CreatedAt = DateTime.UtcNow;
            Depth = 0;
        }


        public TraceNode AddChild(string name, object? input = null, object? output = null, IEnumerable<string>? labels = null)
        {
            if (!_owner.TryGetTarget(out var owner))
                throw new InvalidOperationException("所属追踪树已被释放");

            var child = new TraceNode(name, owner);

            lock (_lock)
            {
                child.Parent = this;
                child.Depth = this.Depth + 1;
                if (input != null) child._inputs.Add(input);
                if (output != null) child._outputs.Add(output);
                if (labels != null) child._labels.AddRange(labels);
                _children.Add(child);
            }

            if (owner is Core.TraceTree tt)
            {
                tt.RegisterNode(child);
            }

            return child;
        }

        public void AddInput(object? input)
        {
            lock (_lock) _inputs.Add(input);
        }

        public void AddOutput(object? output)
        {
            lock (_lock) _outputs.Add(output);
        }

        public void AddLabel(string label)
        {
            lock (_lock) _labels.Add(label);
        }

        public void AddRecord(string record)
        {
            lock (_lock) _records.Add(record);
        }

        public bool Detach()
        {
            if (Parent is TraceNode parent)
            {
                return parent.RemoveChild(NodeId);
            }
            return false;
        }

        public bool RemoveChild(string nodeId)
        {
            lock (_lock)
            {
                var child = _children.FirstOrDefault(c => c.NodeId == nodeId);
                if (child != null)
                {
                    _children.Remove(child);
                    child.Parent = null;

                    if (_owner.TryGetTarget(out var owner) && owner is Core.TraceTree tt)
                    {
                        tt.UnregisterNode(nodeId);
                    }

                    return true;
                }
                return false;
            }
        }

        internal void ApplyConfig(AddNodeConfig config)
        {
            if (config == null) return;

            lock (_lock)
            {
                foreach (var input in config.Inputs) _inputs.Add(input);
                foreach (var output in config.Outputs) _outputs.Add(output);
                foreach (var label in config.Labels) _labels.Add(label);
                foreach (var record in config.Records) _records.Add(record);
            }
        }

        public override string ToString()
        {
            return $"[{NodeId[..8]}] {Name} (Depth: {Depth}, Children: {_children.Count})";
        }
    }
}
