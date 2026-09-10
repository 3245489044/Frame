
#nullable enable

namespace GraphFramework.Models
{
    public interface IGraphNode<TKey>
        where TKey : notnull
    {
        TKey Id { get; }

        string? Symbol { get; }

        string? Label { get; }

        string? Type { get; }
    }

    public class GraphNode<TKey> : IGraphNode<TKey>
        where TKey : notnull
    {
        public TKey Id { get; set; }

        public string? Symbol { get; set; }

        public string? Label { get; set; }

        public string? Type { get; set; }

        protected GraphNode() { }

        public GraphNode(TKey id, string? symbol = null, string? label = null, string? type = null)
        {
            Id = id;
            Symbol = symbol;
            Label = label;
            Type = type;
        }

        public override string? ToString() => $"{Label ?? Symbol ?? Id?.ToString()}";
    }
}
