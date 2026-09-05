
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.GraphProcessing.Algorithms
{
    /// <summary>环检测器（泛型，不依赖具体图模型）。</summary>
    /// <typeparam name="TKey">节点键类型。</typeparam>
    public interface ICycleDetector<TKey> where TKey : notnull
    {
        /// <summary>检测是否存在环。</summary>
        bool HasCycle(IReadOnlyCollection<TKey> nodes, Func<TKey, IEnumerable<TKey>> getOutgoing);

        /// <summary>返回全部环路径（DFS 着色回边提取，含自环）。</summary>
        IReadOnlyList<IReadOnlyList<TKey>> FindCycles(IReadOnlyCollection<TKey> nodes, Func<TKey, IEnumerable<TKey>> getOutgoing);
    }

    /// <summary>默认栈式 DFS 环检测实现（移植自 UniversalReference.CircularDependencyDetector，泛化 + 去重）。</summary>
    public class DefaultCycleDetector<TKey> : ICycleDetector<TKey> where TKey : notnull
    {
        // 着色：0=白(未访问)、1=灰(在递归栈)、2=黑(已回溯)
        private const int White = 0;
        private const int Gray = 1;
        private const int Black = 2;

        public bool HasCycle(IReadOnlyCollection<TKey> nodes, Func<TKey, IEnumerable<TKey>> getOutgoing)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (getOutgoing == null) throw new ArgumentNullException(nameof(getOutgoing));
            return FindCycles(nodes, getOutgoing).Count > 0;
        }

        public IReadOnlyList<IReadOnlyList<TKey>> FindCycles(IReadOnlyCollection<TKey> nodes, Func<TKey, IEnumerable<TKey>> getOutgoing)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (getOutgoing == null) throw new ArgumentNullException(nameof(getOutgoing));

            var color = new Dictionary<TKey, int>();
            var stack = new List<TKey>();
            var seen = new HashSet<string>();
            var result = new List<IReadOnlyList<TKey>>();

            foreach (var node in nodes)
            {
                if (!color.ContainsKey(node))
                    Dfs(node, color, stack, getOutgoing, result, seen);
            }

            return result;
        }

        private void Dfs(
            TKey current,
            Dictionary<TKey, int> color,
            List<TKey> stack,
            Func<TKey, IEnumerable<TKey>> getOutgoing,
            List<IReadOnlyList<TKey>> result,
            HashSet<string> seen)
        {
            color[current] = Gray;
            stack.Add(current);

            var outgoing = getOutgoing(current);
            if (outgoing != null)
            {
                foreach (var next in outgoing)
                {
                    if (EqualityComparer<TKey>.Default.Equals(current, next))
                    {
                        AddCycle(new List<TKey> { next }, result, seen); // 自环
                        continue;
                    }

                    if (!color.TryGetValue(next, out var nextColor))
                    {
                        Dfs(next, color, stack, getOutgoing, result, seen);
                    }
                    else if (nextColor == Gray)
                    {
                        var idx = stack.IndexOf(next);
                        if (idx >= 0)
                        {
                            var cycle = new List<TKey>();
                            for (int i = idx; i < stack.Count; i++)
                                cycle.Add(stack[i]);
                            AddCycle(cycle, result, seen);
                        }
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            color[current] = Black;
        }

        private void AddCycle(List<TKey> cycle, List<IReadOnlyList<TKey>> result, HashSet<string> seen)
        {
            if (cycle.Count < 2) return;

            // 规范化：旋转至字典序最小首元素，作为去重签名
            var minIdx = 0;
            for (int i = 1; i < cycle.Count; i++)
            {
                if (Comparer<TKey>.Default.Compare(cycle[i], cycle[minIdx]) < 0)
                    minIdx = i;
            }

            var signatureParts = new List<string>(cycle.Count);
            for (int i = 0; i < cycle.Count; i++)
                signatureParts.Add(cycle[(minIdx + i) % cycle.Count]?.ToString() ?? string.Empty);

            if (seen.Add(string.Join(">", signatureParts)))
                result.Add(cycle.AsReadOnly());
        }
    }
}
