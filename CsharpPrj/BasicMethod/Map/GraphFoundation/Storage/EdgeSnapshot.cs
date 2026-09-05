
#nullable enable

namespace GraphFoundation.Storage
{
    public readonly struct EdgeSnapshot<TKey, TEdgeData>
        where TKey : notnull
    {
        public string EdgeId { get; }

        public TKey Source { get; }

        public TKey Target { get; }

        public bool IsDirected { get; }

        public double Weight { get; }

        public TEdgeData? Data { get; }

        public EdgeSnapshot(string edgeId, TKey source, TKey target,
                            bool isDirected, double weight, TEdgeData? data)
        {
            EdgeId = edgeId;
            Source = source;
            Target = target;
            IsDirected = isDirected;
            Weight = weight;
            Data = data;
        }

        public void Deconstruct(out TKey source, out TKey target,
                                out string edgeId, out bool isDirected,
                                out double weight, out TEdgeData? data)
        {
            source = Source;
            target = Target;
            edgeId = EdgeId;
            isDirected = IsDirected;
            weight = Weight;
            data = Data;
        }
    }
}
