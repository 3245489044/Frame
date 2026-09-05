
#nullable enable

namespace GraphFramework.Models
{
    public interface IGraphEdge<TKey>
        where TKey : notnull
    {
        TKey EdgeId { get; }

        TKey SourceId { get; }

        TKey TargetId { get; }

        bool IsDirected { get; }

        string? Label { get; }

        double Weight { get; }
    }

    public class GraphEdge<TKey> : IGraphEdge<TKey>
        where TKey : notnull
    {
        public TKey EdgeId { get; set; }

        public TKey SourceId { get; set; }

        public TKey TargetId { get; set; }

        public bool IsDirected { get; set; } = true;

        public string? Label { get; set; }

        public double Weight { get; set; } = 1.0;

        protected GraphEdge() { }

        public GraphEdge(TKey edgeId, TKey sourceId, TKey targetId,
            bool isDirected = true, string? label = null, double weight = 1.0)
        {
            EdgeId = edgeId;
            SourceId = sourceId;
            TargetId = targetId;
            IsDirected = isDirected;
            Label = label;
            Weight = weight;
        }

        public override string? ToString() =>
            $"{EdgeId}: {SourceId} {(IsDirected ? "->" : "--")} {TargetId}";
    }
}
