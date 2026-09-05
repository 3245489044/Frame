#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.GraphProcessing.Algorithms
{
    /// <summary>最短路径查找器（BFS，保证无权图最优解）。</summary>
    /// <typeparam name="TKey">节点键类型。</typeparam>
    public interface IShortestPathFinder<TKey> where TKey : notnull
    {
        /// <summary>返回 start 到 end 的最短路径节点序列（BFS）；不可达返回空序列。</summary>
        IReadOnlyList<TKey> FindShortestPath(TKey start, TKey end, Func<TKey, IEnumerable<TKey>> getOutgoingEdges);
    }

    /// <summary>默认 BFS 最短路径实现（移植自 UniversalReference.ReferenceManagerBase.FindShortestPathAsync）。</summary>
    public class DefaultShortestPathFinder<TKey> : IShortestPathFinder<TKey> where TKey : notnull
    {
        public IReadOnlyList<TKey> FindShortestPath(TKey start, TKey end, Func<TKey, IEnumerable<TKey>> getOutgoingEdges)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (end == null) throw new ArgumentNullException(nameof(end));
            if (getOutgoingEdges == null) throw new ArgumentNullException(nameof(getOutgoingEdges));

            if (EqualityComparer<TKey>.Default.Equals(start, end))
                return new List<TKey> { start }.AsReadOnly();

            var visited = new HashSet<TKey> { start };
            var queue = new Queue<(TKey Node, List<TKey> Path)>();
            queue.Enqueue((start, new List<TKey> { start }));

            while (queue.Count > 0)
            {
                var (current, path) = queue.Dequeue();

                var outgoing = getOutgoingEdges(current);
                if (outgoing == null) continue;

                foreach (var next in outgoing)
                {
                    if (EqualityComparer<TKey>.Default.Equals(next, end))
                    {
                        var result = new List<TKey>(path) { next };
                        return result.AsReadOnly();
                    }

                    if (visited.Contains(next)) continue;
                    visited.Add(next);

                    queue.Enqueue((next, new List<TKey>(path) { next }));
                }
            }

            return Array.Empty<TKey>();
        }
    }
}
