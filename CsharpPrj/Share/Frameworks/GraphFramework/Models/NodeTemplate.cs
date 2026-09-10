
#nullable enable

using System;
using System.Collections.Generic;

namespace GraphFramework.Models
{
    public class NodeTemplate<TKey, TNode>
        where TKey : notnull
        where TNode : class
    {
        public TKey? Id { get; set; }

        public string Symbol { get; set; } = string.Empty;

        public string? Label { get; set; }

        public TNode? NodeData { get; set; }

        public Dictionary<string, object?> Properties { get; set; } = new();

        public TKey GenerateKey(Func<string, TKey> keyFactory)
        {
            if (Id != null) return Id;
            return keyFactory(Guid.NewGuid().ToString("N"));
        }

        public TNode ToNodeData(Func<NodeTemplate<TKey, TNode>, TNode> factory)
        {
            return NodeData ?? factory(this);
        }
    }

    public class EdgeTemplate<TKey>
        where TKey : notnull
    {
        public TKey? EdgeId { get; set; }

        public TKey Source { get; set; } = default!;

        public TKey Target { get; set; } = default!;

        public object? Data { get; set; }

        public double Weight { get; set; } = 1.0;

        public bool IsDirected { get; set; } = true;

        public TKey GenerateEdgeId(Func<string, TKey> keyFactory) =>
            EdgeId ?? keyFactory($"edge_{Source}_{Target}_{Guid.NewGuid():N}");
    }
}
