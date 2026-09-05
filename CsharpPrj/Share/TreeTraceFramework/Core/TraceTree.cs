
using Framework.Tree;
using Share.TreeTraceFramework.Abstractions;
using Share.TreeTraceFramework.Models;

namespace Share.TreeTraceFramework.Core
{
    public class TraceTree : ITraceTree<TraceNode>
    {
        protected readonly TreeIndex<TraceNode, string> _nodeIndex = new();
        protected readonly TreeWalker<TraceNode> _walker;
        protected readonly TreePathFinder<TraceNode> _pathFinder;
        protected TraceNode? _root;
        protected readonly ReaderWriterLockSlim _lock = new();
        private bool _disposed;

        public string TraceId { get; }

        public TraceNode? Root
        {
            get
            {
                _lock.EnterReadLock();
                try { return _root; }
                finally { _lock.ExitReadLock(); }
            }
        }

        public int NodeCount => _nodeIndex.Count;

        public DateTime CreatedAt { get; }

        public ITraceQueryEngine<TraceNode> QueryEngine { get; set; }

        public TraceTree(string? traceId = null, ITraceQueryEngine<TraceNode>? queryEngine = null)
        {
            TraceId = traceId ?? Guid.NewGuid().ToString("N");
            CreatedAt = DateTime.UtcNow;
            QueryEngine = queryEngine ?? new TraceQueryEngine();

            _walker = new TreeWalker<TraceNode>(
                getChildren: n => n.Children);

            _pathFinder = new TreePathFinder<TraceNode>(
                getParent: n => n.Parent);
        }


        protected virtual TraceNode CreateNodeInstance(string name, string? nodeId = null)
        {
            return new TraceNode(name, this, nodeId);
        }

        public TraceNode CreateRoot(string name, object? input = null, IEnumerable<string>? labels = null)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_root != null)
                    throw new InvalidOperationException($"根节点已存在 (Name: {_root.Name})");

                _root = CreateNodeInstance(name);
                if (input != null) _root.AddInput(input);
                if (labels != null) foreach (var l in labels) _root.AddLabel(l);
                _nodeIndex.RegisterNode(_root, _root.NodeId);
                return _root;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _nodeIndex.Clear();
                _root = null;
            }
            finally { _lock.ExitWriteLock(); }
        }


        public TraceNode? FindNode(string nodeId)
        {
            if (_nodeIndex.TryGet(nodeId, out var node))
                return node;
            return null;
        }

        public IEnumerable<TraceNode> FindByName(string name)
        {
            _lock.EnterReadLock();
            try
            {
                if (_root == null) return Enumerable.Empty<TraceNode>();
                return _walker.FindAll(_root, n => n.Name == name);
            }
            finally { _lock.ExitReadLock(); }
        }

        public IEnumerable<TraceNode> FindByLabel(string label)
        {
            _lock.EnterReadLock();
            try
            {
                if (_root == null) return Enumerable.Empty<TraceNode>();
                return _walker.FindAll(_root, n => n.Labels.Contains(label));
            }
            finally { _lock.ExitReadLock(); }
        }

        public IEnumerable<TraceNode> Query(TraceQuery<TraceNode> query)
        {
            return QueryEngine.Execute(this, query);
        }

        public IEnumerable<TraceNode> GetAllNodes()
        {
            _lock.EnterReadLock();
            try
            {
                if (_root == null) return Enumerable.Empty<TraceNode>();
                return _walker.Flatten(_root);
            }
            finally { _lock.ExitReadLock(); }
        }


        public TraceNode? FindNearestAncestor(string nodeId, Func<TraceNode, bool> predicate)
        {
            _lock.EnterReadLock();
            try
            {
                var node = FindNode(nodeId);
                if (node == null) return null;
                return _pathFinder.FindNearestAncestor(node, predicate);
            }
            finally { _lock.ExitReadLock(); }
        }

        public TraceNode? FindLowestCommonAncestor(string nodeId1, string nodeId2)
        {
            _lock.EnterReadLock();
            try
            {
                var node1 = FindNode(nodeId1);
                var node2 = FindNode(nodeId2);
                if (node1 == null || node2 == null) return null;
                return _pathFinder.FindLowestCommonAncestor(node1, node2);
            }
            finally { _lock.ExitReadLock(); }
        }

        public int GetNodeDepth(string nodeId)
        {
            _lock.EnterReadLock();
            try
            {
                var node = FindNode(nodeId);
                if (node == null) return -1;
                return _pathFinder.GetDepth(node);
            }
            finally { _lock.ExitReadLock(); }
        }

        public bool IsDescendantOf(string nodeId, string ancestorNodeId)
        {
            _lock.EnterReadLock();
            try
            {
                var node = FindNode(nodeId);
                var ancestor = FindNode(ancestorNodeId);
                if (node == null || ancestor == null) return false;
                return _pathFinder.IsDescendantOf(node, ancestor);
            }
            finally { _lock.ExitReadLock(); }
        }


        internal void RegisterNode(TraceNode node)
        {
            _nodeIndex.RegisterNode(node, node.NodeId);
        }

        internal void UnregisterNode(string nodeId)
        {
            _nodeIndex.UnregisterNode(nodeId);
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lock.EnterWriteLock();
            try
            {
                _nodeIndex.Clear();
                _root = null;
            }
            finally { _lock.ExitWriteLock(); }

            _lock.Dispose();
        }
    }
}
