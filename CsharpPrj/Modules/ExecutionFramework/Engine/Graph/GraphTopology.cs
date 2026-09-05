using Rge.Unified.Models;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Engine;

public static class GraphTopology
{
    public static List<RgeEntity> Sort(ExecutionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var inDegree = BuildInDegree(graph);
        var adjacency = BuildAdjacency(graph, inDegree);
        var (queue, enqueued) = BuildInitialReadyQueue(graph, inDegree);

        var result = new List<RgeEntity>(graph.Entities.Count);
        while (queue.Count > 0)
        {
            var entity = queue.Dequeue();
            result.Add(entity);

            if (adjacency.TryGetValue(entity.Uid, out var successors))
            {
                foreach (var successorUid in successors)
                {
                    var remaining = inDegree.GetValueOrDefault(successorUid) - 1;
                    inDegree[successorUid] = remaining;
                    if (remaining == 0
                        && !enqueued.Contains(successorUid)
                        && graph.Entities.TryGetValue(successorUid, out var successor))
                    {
                        queue.Enqueue(successor);
                        enqueued.Add(successor.Uid);
                    }
                }
            }
        }

        return result;
    }

    public static List<List<RgeEntity>> SortByLayers(ExecutionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var inDegree = BuildInDegree(graph);
        var adjacency = BuildAdjacency(graph, inDegree);
        var (queue, ready) = BuildInitialReadyQueue(graph, inDegree);

        var layers = new List<List<RgeEntity>>();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0)
        {
            var layer = new List<RgeEntity>();
            var layerCount = queue.Count;
            var newlyReady = new List<string>();

            for (int i = 0; i < layerCount; i++)
            {
                var entity = queue.Dequeue();
                layer.Add(entity);
                processed.Add(entity.Uid);

                if (adjacency.TryGetValue(entity.Uid, out var successors))
                {
                    foreach (var successorUid in successors)
                    {
                        if (processed.Contains(successorUid) || ready.Contains(successorUid))
                            continue;

                        var remaining = inDegree.GetValueOrDefault(successorUid) - 1;
                        inDegree[successorUid] = remaining;
                        if (remaining == 0)
                            newlyReady.Add(successorUid);
                    }
                }
            }

            layers.Add(layer);

            foreach (var uid in newlyReady)
            {
                if (!ready.Contains(uid) && graph.Entities.TryGetValue(uid, out var next))
                {
                    queue.Enqueue(next);
                    ready.Add(uid);
                }
            }
        }

        return layers;
    }

    public static List<RgeEntity> GetCyclicEntities(ExecutionGraph graph, IReadOnlyCollection<RgeEntity> sorted)
    {
        var sortedUids = new HashSet<string>(sorted.Select(e => e.Uid), StringComparer.OrdinalIgnoreCase);
        return graph.Entities.Values
            .Where(e => !sortedUids.Contains(e.Uid))
            .ToList();
    }

    private static Dictionary<string, int> BuildInDegree(ExecutionGraph graph)
    {
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var uid in graph.Entities.Keys)
            inDegree[uid] = 0;
        return inDegree;
    }

    private static Dictionary<string, List<string>> BuildAdjacency(
        ExecutionGraph graph, Dictionary<string, int> inDegree)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            if (!graph.Entities.ContainsKey(edge.From) || !graph.Entities.ContainsKey(edge.To))
                continue;
            if (!adjacency.TryGetValue(edge.From, out var list))
            {
                list = new List<string>();
                adjacency[edge.From] = list;
            }
            list.Add(edge.To);
            inDegree[edge.To] = inDegree.GetValueOrDefault(edge.To) + 1;
        }
        return adjacency;
    }

    private static (Queue<RgeEntity> Queue, HashSet<string> Ready) BuildInitialReadyQueue(
        ExecutionGraph graph, IReadOnlyDictionary<string, int> inDegree)
    {
        var queue = new Queue<RgeEntity>();
        var ready = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var explicitEntryUid = graph.EntryEntity;
        if (!string.IsNullOrWhiteSpace(explicitEntryUid)
            && graph.Entities.TryGetValue(explicitEntryUid, out var explicitEntry))
        {
            queue.Enqueue(explicitEntry);
            ready.Add(explicitEntry.Uid);
        }

        foreach (var entity in graph.Entities.Values)
        {
            if (!ready.Contains(entity.Uid) && inDegree.GetValueOrDefault(entity.Uid) == 0)
            {
                queue.Enqueue(entity);
                ready.Add(entity.Uid);
            }
        }

        return (queue, ready);
    }
}