
using System.Collections.Concurrent;
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Models;

namespace Share.TreeTraceFramework.Core
{
    public class TraceService : ITraceService<TraceNode>
    {
        private readonly ConcurrentDictionary<string, ITraceTree<TraceNode>> _traceTrees = new();
        private readonly Func<string, ITraceTree<TraceNode>> _traceTreeFactory;

        public static TraceService Default { get; } = new TraceService();

        public TraceService(Func<string, ITraceTree<TraceNode>>? traceTreeFactory = null)
        {
            _traceTreeFactory = traceTreeFactory ?? (sessionId => new TraceTree());
        }


        public ITraceTree<TraceNode> CreateTraceTree(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentNullException(nameof(sessionId));

            var tree = _traceTreeFactory(sessionId);
            _traceTrees[sessionId] = tree;
            return tree;
        }

        public ITraceTree<TraceNode>? GetTraceTree(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            _traceTrees.TryGetValue(sessionId, out var tree);
            return tree;
        }

        public ITraceTree<TraceNode> GetOrCreateTraceTree(string sessionId)
        {
            return GetTraceTree(sessionId) ?? CreateTraceTree(sessionId);
        }

        public bool DeleteTraceTree(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            if (_traceTrees.TryRemove(sessionId, out var tree))
            {
                tree.Dispose();
                return true;
            }
            return false;
        }


        public TraceNode? AddNodeFromConfig(string sessionId, AddNodeConfig config)
        {
            config = PortGuard.Required(config, nameof(config));

            var tree = GetOrCreateTraceTree(sessionId);

            if (!string.IsNullOrEmpty(config.ParentNodeId))
            {
                var parent = tree.FindNode(config.ParentNodeId);
                if (parent != null)
                {
                    var child = parent.AddChild(config.Name);
                    child.ApplyConfig(config);
                    return child;
                }
                return null;
            }

            if (tree.Root == null)
            {
                var root = tree.CreateRoot(config.Name);
                root.ApplyConfig(config);
                return root;
            }

            var childNode = tree.Root.AddChild(config.Name);
            childNode.ApplyConfig(config);
            return childNode;
        }


        public IEnumerable<TraceNode> QueryNodes(string sessionId, TraceQuery<TraceNode> query)
        {
            var tree = GetTraceTree(sessionId);
            if (tree == null)
                return Enumerable.Empty<TraceNode>();

            return tree.Query(query);
        }


        public void StoreTraceTree(ITraceTree<TraceNode> tree)
        {
            tree = PortGuard.Required(tree, nameof(tree));

            var existingEntry = _traceTrees.FirstOrDefault(kvp => kvp.Value.TraceId == tree.TraceId);
            if (!existingEntry.Equals(default(KeyValuePair<string, ITraceTree<TraceNode>>)))
            {
                _traceTrees[existingEntry.Key] = tree;
            }
        }

        public ITraceTree<TraceNode>? RestoreTraceTree(string traceId)
        {
            if (string.IsNullOrEmpty(traceId))
                return null;

            return _traceTrees.Values.FirstOrDefault(t => t.TraceId == traceId);
        }

        public IEnumerable<ITraceTree<TraceNode>> GetAllTraceTrees()
        {
            return _traceTrees.Values.ToList();
        }

        public void Clear()
        {
            foreach (var t in _traceTrees.Values)
                t.Dispose();
            _traceTrees.Clear();
        }
    }
}
