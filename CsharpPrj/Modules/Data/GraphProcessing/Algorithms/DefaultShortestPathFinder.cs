#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.GraphProcessing.Algorithms
{
    /// <summary>
    /// 默认 BFS 最短路径实现（移植自 UniversalReference.ReferenceManagerBase.FindShortestPathAsync）。
    /// <para>
    /// 注意：原 <c>IShortestPathFinder&lt;TKey&gt;</c> 接口已移除（2026-09-10，M6）——它零接口消费者，
    /// 消费方始终绕过接口直用本具体类（ReferenceManagerBase / ReferenceGraphEngine），接口无存在价值。
    /// </para>
    /// <para>与 <c>RelationalHierarchyTopology.DescendantsAsync</c> 的 BFS 语义不同：本类任意图最短路、后者包含层次展开，不可互相替代。</para>
    /// </summary>
    public class DefaultShortestPathFinder<TKey> where TKey : notnull
    {
        /// <summary>返回 start 到 end 的最短路径节点序列（BFS，无权图最优解）；不可达返回空序列。</summary>
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