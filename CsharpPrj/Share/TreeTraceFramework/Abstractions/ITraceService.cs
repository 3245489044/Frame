
using Share.TreeTraceFramework.Models;

namespace Share.TreeTraceFramework.Abstractions
{
    public interface ITraceService<TNode>
        where TNode : ITraceNode<TNode>
    {
        ITraceTree<TNode> CreateTraceTree(string sessionId);

        ITraceTree<TNode>? GetTraceTree(string sessionId);

        ITraceTree<TNode> GetOrCreateTraceTree(string sessionId);

        bool DeleteTraceTree(string sessionId);

        TNode? AddNodeFromConfig(string sessionId, AddNodeConfig config);

        IEnumerable<TNode> QueryNodes(string sessionId, TraceQuery<TNode> query);

        void StoreTraceTree(ITraceTree<TNode> tree);

        ITraceTree<TNode>? RestoreTraceTree(string traceId);
    }

    public class AddNodeConfig
    {
        public string? ParentNodeId { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<object?> Inputs { get; set; } = new();

        public List<object?> Outputs { get; set; } = new();

        public List<string> Labels { get; set; } = new();

        public List<string> Records { get; set; } = new();
    }
}
