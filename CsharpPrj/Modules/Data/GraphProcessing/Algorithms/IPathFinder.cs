#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.GraphProcessing.Algorithms
{
    /// <summary>路径查找器（泛型，不依赖具体图模型）。</summary>
    /// <typeparam name="TKey">节点键类型。</typeparam>
    public interface IPathFinder<TKey> where TKey : notnull
    {
        /// <summary>判断 source 到 target 是否存在路径。</summary>
        bool HasPath(TKey source, TKey target, Func<TKey, IEnumerable<TKey>> getOutgoingEdges);

        /// <summary>返回 source 到 target 的全部路径（DFS 回溯，maxDepth 限制深度）。</summary>
        IReadOnlyList<IReadOnlyList<TKey>> FindAllPaths(TKey source, TKey target, Func<TKey, IEnumerable<TKey>> getOutgoingEdges, int maxDepth = 10);

        /// <summary>判断从 start 出发能否到达任一终点。</summary>
        bool HasPathToAnyEnd(TKey start, IEnumerable<TKey> endNodes, Func<TKey, IEnumerable<TKey>> getOutgoingEdges);

        /// <summary>返回从 start 出发可达的全部节点（含自身）。</summary>
        IReadOnlySet<TKey> GetReachableNodes(TKey start, Func<TKey, IEnumerable<TKey>> getOutgoingEdges);
    }

    /// <summary>默认路径查找实现（移植自 UniversalReference.GraphValidator，修正 DFS visited 回溯缺陷）。</summary>
    public class DefaultPathFinder<TKey> : IPathFinder<TKey> where TKey : notnull
    {
        public bool HasPath(TKey source, TKey target, Func<TKey, IEnumerable<TKey>> getOutgoingEdges)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (getOutgoingEdges == null) throw new ArgumentNullException(nameof(getOutgoingEdges));

            var visited = new HashSet<TKey>();
            return HasPathRecursive(source, target, getOutgoingEdges, visited);
        }

        public IReadOnlyList<IReadOnlyList<TKey>> FindAllPaths(
            TKey source,
            TKey target,
            Func<TKey, IEnumerable<TKey>> getOutgoingEdges,
            int maxDepth = 10)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (getOutgoingEdges == null) throw new ArgumentNullException(nameof(getOutgoingEdges));

            var paths = new List<IReadOnlyList<TKey>>();
            var visited = new HashSet<TKey>();
            var path = new List<TKey> { source };
            FindAllPathsRecursive(source, target, getOutgoingEdges, maxDepth, path, visited, paths);
            return paths;
        }

        public bool HasPathToAnyEnd(TKey start, IEnumerable<TKey> endNodes, Func<TKey, IEnumerable<TKey>> getOutgoingEdges)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (endNodes == null) throw new ArgumentNullException(nameof(endNodes));
            if (getOutgoingEdges == null) throw new ArgumentNullException(nameof(getOutgoingEdges));

            var endSet = new HashSet<TKey>(endNodes);
            var visited = new HashSet<TKey>();
            return HasPathToAnyEndRecursive(start, endSet, getOutgoingEdges, visited);
        }

        public IReadOnlySet<TKey> GetReachableNodes(TKey start, Func<TKey, IEnumerable<TKey>> getOutgoingEdges)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (getOutgoingEdges == null) throw new ArgumentNullException(nameof(getOutgoingEdges));

            var reachable = new HashSet<TKey>();
            var visited = new HashSet<TKey>();
            GetReachableNodesRecursive(start, getOutgoingEdges, visited, reachable);
            return reachable;
        }

        private bool HasPathRecursive(TKey current, TKey target, Func<TKey, IEnumerable<TKey>> getOutgoingEdges, HashSet<TKey> visited)
        {
            if (EqualityComparer<TKey>.Default.Equals(current, target))
                return true;

            if (!visited.Add(current))
                return false;

            var outgoing = getOutgoingEdges(current);
            if (outgoing != null)
            {
                foreach (var next in outgoing)
                {
                    if (HasPathRecursive(next, target, getOutgoingEdges, visited))
                        return true;
                }
            }

            return false;
        }

        private void FindAllPathsRecursive(
            TKey current,
            TKey target,
            Func<TKey, IEnumerable<TKey>> getOutgoingEdges,
            int depth,
            List<TKey> path,
            HashSet<TKey> visited,
            List<IReadOnlyList<TKey>> paths)
        {
            if (depth <= 0) return;

            if (EqualityComparer<TKey>.Default.Equals(current, target))
            {
                paths.Add(path.ToList().AsReadOnly());
                return;
            }

            if (!visited.Add(current))
                return;

            try
            {
                var outgoing = getOutgoingEdges(current);
                if (outgoing != null)
                {
                    foreach (var next in outgoing)
                    {
                        path.Add(next);
                        FindAllPathsRecursive(next, target, getOutgoingEdges, depth - 1, path, visited, paths);
                        path.RemoveAt(path.Count - 1);
                    }
                }
            }
            finally
            {
                // 回溯释放当前节点，允许后续路径复用（修正原实现 visited 不回退缺陷）
                visited.Remove(current);
            }
        }

        private bool HasPathToAnyEndRecursive(
            TKey current,
            HashSet<TKey> endSet,
            Func<TKey, IEnumerable<TKey>> getOutgoingEdges,
            HashSet<TKey> visited)
        {
            if (endSet.Contains(current))
                return true;

            if (!visited.Add(current))
                return false;

            var outgoing = getOutgoingEdges(current);
            if (outgoing != null)
            {
                foreach (var next in outgoing)
                {
                    if (HasPathToAnyEndRecursive(next, endSet, getOutgoingEdges, visited))
                        return true;
                }
            }

            return false;
        }

        private void GetReachableNodesRecursive(TKey current, Func<TKey, IEnumerable<TKey>> getOutgoingEdges, HashSet<TKey> visited, HashSet<TKey> reachable)
        {
            if (!visited.Add(current))
                return;

            reachable.Add(current);
            var outgoing = getOutgoingEdges(current);
            if (outgoing != null)
            {
                foreach (var next in outgoing)
                {
                    GetReachableNodesRecursive(next, getOutgoingEdges, visited, reachable);
                }
            }
        }
    }
}
