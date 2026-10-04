
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Navigation;

namespace GraphFramework.Bridge
{
    public class ChainInfo<TKey>
        where TKey : notnull
    {
        public IReadOnlyList<TKey> NodeKeys { get; }

        public IReadOnlyList<string> EdgeIds { get; }

        public TKey StartKey => NodeKeys.Count > 0 ? NodeKeys[0] : throw new InvalidOperationException("空链");

        public TKey EndKey => NodeKeys.Count > 0 ? NodeKeys[^1] : throw new InvalidOperationException("空链");

        public int Length => NodeKeys.Count;

        public ChainInfo(IReadOnlyList<TKey> nodeKeys, IReadOnlyList<string> edgeIds)
        {
            NodeKeys = PortGuard.Required(nodeKeys, nameof(nodeKeys));
            EdgeIds = PortGuard.Required(edgeIds, nameof(edgeIds));
        }
    }

    public static class GraphFoundationBridge<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
        where TEdgeData : class
    {
        public static void ChainToGraph(
            ChainInfo<TKey> chain,
            IGraph<TKey, TNodeData, TEdgeData> graph,
            Func<TKey, TNodeData?> nodeDataFactory,
            Func<TKey, TKey, TEdgeData?>? edgeDataFactory = null)
        {
            chain = PortGuard.Required(chain, nameof(chain));
            graph = PortGuard.Required(graph, nameof(graph));
            nodeDataFactory = PortGuard.Required(nodeDataFactory, nameof(nodeDataFactory));

            foreach (var nodeKey in chain.NodeKeys)
            {
                if (!graph.ContainsNode(nodeKey))
                {
                    var nodeData = nodeDataFactory(nodeKey);
                    graph.AddNode(nodeKey, nodeData!);
                }
            }

            for (int i = 0; i < chain.NodeKeys.Count - 1; i++)
            {
                var source = chain.NodeKeys[i];
                var target = chain.NodeKeys[i + 1];
                var edgeId = $"{source}->{target}";

                if (!graph.ContainsEdge(edgeId))
                {
                    var edgeData = edgeDataFactory?.Invoke(source, target);
                    graph.AddEdge(edgeId, source, target, true, edgeData);
                }
            }
        }

        public static List<ChainInfo<TKey>> GraphToChain(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            int maxPaths = 1000,
            int maxDepth = 50)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            if (maxPaths <= 0) throw new ArgumentOutOfRangeException(nameof(maxPaths), "最大路径数必须大于0");
            if (maxDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxDepth), "最大路径深度必须大于0");

            var chains = new List<ChainInfo<TKey>>();
            var entryNodes = GetEntryNodeKeys(graph);
            var exitNodes = GetExitNodeKeys(graph);

            foreach (var entry in entryNodes)
            {
                foreach (var exit in exitNodes)
                {
                    var paths = FindAllPaths(graph, entry, exit, maxPaths, maxDepth);
                    foreach (var path in paths)
                    {
                        var edgeIds = new List<string>();
                        for (int i = 0; i < path.Count - 1; i++)
                        {
                            var source = path[i];
                            var target = path[i + 1];
                            var outgoing = graph.GetOutgoingEdges(source);
                            var edge = outgoing.FirstOrDefault(e =>
                                EqualityComparer<TKey>.Default.Equals(e.Target, target));
                            if (!string.IsNullOrEmpty(edge.EdgeId))
                                edgeIds.Add(edge.EdgeId);
                        }
                        chains.Add(new ChainInfo<TKey>(path, edgeIds.AsReadOnly()));
                    }
                }
            }

            return chains;
        }

        public static List<IReadOnlyList<TKey>> ExtractComponents(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            graph = PortGuard.Required(graph, nameof(graph));

            var components = new List<IReadOnlyList<TKey>>();
            var visited = new HashSet<TKey>();

            foreach (var key in graph.GetAllNodeKeys())
            {
                if (!visited.Contains(key))
                {
                    var component = new List<TKey>();
                    var stack = new Stack<TKey>();
                    stack.Push(key);

                    while (stack.Count > 0)
                    {
                        var node = stack.Pop();
                        if (visited.Add(node))
                        {
                            component.Add(node);
                            foreach (var neighbor in graph.GetSuccessorKeys(node))
                            {
                                if (!visited.Contains(neighbor))
                                    stack.Push(neighbor);
                            }
                        }
                    }

                    components.Add(component.AsReadOnly());
                }
            }

            return components;
        }

        public static void InsertNodesAfter(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            TKey targetNodeKey,
            IReadOnlyList<TKey> newNodes,
            Func<TKey, TNodeData?> nodeDataFactory,
            Func<TKey, TKey, TEdgeData?>? edgeDataFactory = null)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            if (targetNodeKey == null) throw new ArgumentNullException(nameof(targetNodeKey));
            newNodes = PortGuard.Required(newNodes, nameof(newNodes));
            nodeDataFactory = PortGuard.Required(nodeDataFactory, nameof(nodeDataFactory));

            var outgoingEdges = graph.GetOutgoingEdges(targetNodeKey).ToList();

            foreach (var edge in outgoingEdges)
            {
                graph.RemoveEdge(edge.EdgeId);
            }

            TKey previous = targetNodeKey;
            foreach (var newNode in newNodes)
            {
                if (!graph.ContainsNode(newNode))
                {
                    var nodeData = nodeDataFactory(newNode);
                    graph.AddNode(newNode, nodeData!);
                }

                var edgeId = $"{previous}->{newNode}";
                if (!graph.ContainsEdge(edgeId))
                {
                    var edgeData = edgeDataFactory?.Invoke(previous, newNode);
                    graph.AddEdge(edgeId, previous, newNode, true, edgeData);
                }

                previous = newNode;
            }

            foreach (var edge in outgoingEdges)
            {
                var newEdgeId = $"{previous}->{edge.Target}";
                if (!graph.ContainsEdge(newEdgeId))
                {
                    graph.AddEdge(newEdgeId, previous, edge.Target, edge.IsDirected, edge.Data, edge.Weight);
                }
            }
        }

        public static void InsertNodesBefore(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            TKey targetNodeKey,
            IReadOnlyList<TKey> newNodes,
            Func<TKey, TNodeData?> nodeDataFactory,
            Func<TKey, TKey, TEdgeData?>? edgeDataFactory = null)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            if (targetNodeKey == null) throw new ArgumentNullException(nameof(targetNodeKey));
            newNodes = PortGuard.Required(newNodes, nameof(newNodes));
            nodeDataFactory = PortGuard.Required(nodeDataFactory, nameof(nodeDataFactory));

            var incomingEdges = graph.GetIncomingEdges(targetNodeKey).ToList();

            foreach (var edge in incomingEdges)
            {
                graph.RemoveEdge(edge.EdgeId);
            }

            TKey firstNewNode = newNodes[0];
            TKey previous = firstNewNode;

            foreach (var edge in incomingEdges)
            {
                var newEdgeId = $"{edge.Source}->{previous}";
                if (!graph.ContainsEdge(newEdgeId))
                {
                    graph.AddEdge(newEdgeId, edge.Source, previous, edge.IsDirected, edge.Data, edge.Weight);
                }
            }

            for (int i = 1; i < newNodes.Count; i++)
            {
                var newNode = newNodes[i];
                if (!graph.ContainsNode(newNode))
                {
                    var nodeData = nodeDataFactory(newNode);
                    graph.AddNode(newNode, nodeData!);
                }

                var edgeId = $"{previous}->{newNode}";
                if (!graph.ContainsEdge(edgeId))
                {
                    var edgeData = edgeDataFactory?.Invoke(previous, newNode);
                    graph.AddEdge(edgeId, previous, newNode, true, edgeData);
                }

                previous = newNode;
            }

            var defaultData = incomingEdges.Count > 0 ? incomingEdges[0].Data : null;
            var defaultWeight = incomingEdges.Count > 0 ? incomingEdges[0].Weight : 1.0;
            var finalEdgeId = $"{previous}->{targetNodeKey}";
            if (!graph.ContainsEdge(finalEdgeId))
            {
                graph.AddEdge(finalEdgeId, previous, targetNodeKey, true, defaultData, defaultWeight);
            }
        }

        public static void DeleteNode(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            TKey nodeKey)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));

            if (!graph.ContainsNode(nodeKey))
                return;

            var predecessors = graph.GetIncomingEdges(nodeKey).ToList();
            var successors = graph.GetOutgoingEdges(nodeKey).ToList();

            foreach (var pred in predecessors)
            {
                foreach (var succ in successors)
                {
                    var newEdgeId = $"{pred.Source}->{succ.Target}";
                    if (!graph.ContainsEdge(newEdgeId))
                    {
                        graph.AddEdge(newEdgeId, pred.Source, succ.Target, 
                            pred.IsDirected || succ.IsDirected, 
                            pred.Data ?? succ.Data, 
                            (pred.Weight + succ.Weight) / 2.0);
                    }
                }
            }

            graph.RemoveNode(nodeKey);
        }

        public static void ReplaceNode(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            TKey oldNodeKey,
            TKey newNodeKey,
            Func<TKey, TNodeData?> nodeDataFactory)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            if (oldNodeKey == null) throw new ArgumentNullException(nameof(oldNodeKey));
            if (newNodeKey == null) throw new ArgumentNullException(nameof(newNodeKey));
            nodeDataFactory = PortGuard.Required(nodeDataFactory, nameof(nodeDataFactory));

            if (!graph.ContainsNode(oldNodeKey))
                return;

            if (EqualityComparer<TKey>.Default.Equals(oldNodeKey, newNodeKey))
            {
                var nodeData = nodeDataFactory(newNodeKey);
                graph.UpdateNode(newNodeKey, nodeData!);
                return;
            }

            var incomingEdges = graph.GetIncomingEdges(oldNodeKey).ToList();
            var outgoingEdges = graph.GetOutgoingEdges(oldNodeKey).ToList();

            if (!graph.ContainsNode(newNodeKey))
            {
                var nodeData = nodeDataFactory(newNodeKey);
                graph.AddNode(newNodeKey, nodeData!);
            }

            foreach (var edge in incomingEdges)
            {
                var newEdgeId = $"{edge.Source}->{newNodeKey}";
                if (!graph.ContainsEdge(newEdgeId))
                {
                    graph.AddEdge(newEdgeId, edge.Source, newNodeKey, edge.IsDirected, edge.Data, edge.Weight);
                }
            }

            foreach (var edge in outgoingEdges)
            {
                var newEdgeId = $"{newNodeKey}->{edge.Target}";
                if (!graph.ContainsEdge(newEdgeId))
                {
                    graph.AddEdge(newEdgeId, newNodeKey, edge.Target, edge.IsDirected, edge.Data, edge.Weight);
                }
            }

            graph.RemoveNode(oldNodeKey);
        }

        public static void RedirectEdge(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            string edgeId,
            TKey newTarget)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            edgeId = PortGuard.Required(edgeId, nameof(edgeId));
            if (newTarget == null) throw new ArgumentNullException(nameof(newTarget));

            if (!graph.ContainsEdge(edgeId))
                return;

            var source = graph.GetEdgeSource(edgeId);
            var isDirected = graph.GetEdgeIsDirected(edgeId);
            var weight = graph.GetEdgeWeight(edgeId);
            var data = graph.GetEdgeData(edgeId);

            graph.RemoveEdge(edgeId);
            graph.AddEdge(edgeId, source, newTarget, isDirected, data, weight);
        }

        public static IReadOnlyList<TKey> GetNextNodeKeys(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey nodeKey)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));

            return graph.GetOutgoingEdges(nodeKey)
                .Select(e => e.Target)
                .Distinct()
                .ToList()
                .AsReadOnly();
        }

        public static IReadOnlyList<TKey> GetPreviousNodeKeys(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey nodeKey)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            if (nodeKey == null) throw new ArgumentNullException(nameof(nodeKey));

            return graph.GetIncomingEdges(nodeKey)
                .Select(e => e.Source)
                .Distinct()
                .ToList()
                .AsReadOnly();
        }

        #region 内部辅助方法

        private static IReadOnlyList<TKey> GetEntryNodeKeys(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var allKeys = graph.GetAllNodeKeys();
            var hasIncoming = new HashSet<TKey>();

            foreach (var key in allKeys)
            {
                var incoming = graph.GetIncomingEdges(key);
                foreach (var edge in incoming)
                {
                    hasIncoming.Add(key);
                }
            }

            return allKeys.Where(k => !hasIncoming.Contains(k)).ToList().AsReadOnly();
        }

        private static IReadOnlyList<TKey> GetExitNodeKeys(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var allKeys = graph.GetAllNodeKeys();
            var hasOutgoing = new HashSet<TKey>();

            foreach (var key in allKeys)
            {
                var outgoing = graph.GetOutgoingEdges(key);
                if (outgoing.Count > 0)
                    hasOutgoing.Add(key);
            }

            return allKeys.Where(k => !hasOutgoing.Contains(k)).ToList().AsReadOnly();
        }

        private static List<List<TKey>> FindAllPaths(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey start,
            TKey end,
            int maxPaths = 1000,
            int maxDepth = 50)
        {
            var paths = new List<List<TKey>>();
            var visited = new HashSet<TKey>();
            var currentPath = new List<TKey>();
            DfsFindPaths(graph, start, end, visited, currentPath, paths, maxPaths, maxDepth);
            return paths;
        }

        private static void DfsFindPaths(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey current,
            TKey target,
            HashSet<TKey> visited,
            List<TKey> currentPath,
            List<List<TKey>> paths,
            int maxPaths = 1000,
            int maxDepth = 50)
        {
            if (paths.Count >= maxPaths)
                return;

            if (currentPath.Count >= maxDepth)
                return;

            visited.Add(current);
            currentPath.Add(current);

            if (EqualityComparer<TKey>.Default.Equals(current, target))
            {
                paths.Add(new List<TKey>(currentPath));
            }
            else
            {
                foreach (var neighbor in graph.GetSuccessorKeys(current))
                {
                    if (!visited.Contains(neighbor))
                        DfsFindPaths(graph, neighbor, target, visited, currentPath, paths, maxPaths, maxDepth);
                }
            }

            currentPath.RemoveAt(currentPath.Count - 1);
            visited.Remove(current);
        }

        #endregion
    }
}
