using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 三层层级引用端口的内存实现（最小实现，零持久化）：history→task→node 归属边记录于内存，
    /// 写侧即记录、读侧按记录返回。供声明式编排入口在无宿主设施条件下驱动编排动作；不承担断言语义。
    /// <para>与持久实现 <c>ReferenceGrounding</c> 并列：本类为内存最小落地
    /// （原 Agent 侧 TaskPool 的 DeclaredHierarchyReferenceManager 下沉，SingleImplementation / TH-12）。</para>
    /// </summary>
    public sealed class MemoryHierarchyReferenceManager : IContainerMemberPort
    {
        private readonly List<string> _histories = new();
        private readonly Dictionary<string, List<string>> _historyTasks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _taskNodes = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Histories => _histories;

        public bool HasHistoryTask(string historyId, string taskKey)
            => _historyTasks.TryGetValue(historyId, out var tasks) && tasks.Contains(taskKey);

        public bool HasTaskNode(string taskKey, string nodeKey)
            => _taskNodes.TryGetValue(taskKey, out var keys) && keys.Contains(nodeKey);

        public Task EnsureContainerAsync(string historyId, CancellationToken ct = default)
        {
            if (!_histories.Contains(historyId)) _histories.Add(historyId);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteContainerAsync(string historyId, CancellationToken ct = default)
        {
            _historyTasks.Remove(historyId);
            return Task.FromResult(_histories.Remove(historyId));
        }

        public Task AttachMemberAsync(string historyId, string taskKey, CancellationToken ct = default)
        {
            Link(_historyTasks, historyId, taskKey);
            return Task.CompletedTask;
        }

        public Task DetachMemberAsync(string historyId, string taskKey, CancellationToken ct = default)
        {
            if (_historyTasks.TryGetValue(historyId, out var tasks)) tasks.Remove(taskKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetMemberKeysAsync(string historyId, CancellationToken ct = default)
            => Task.FromResult(Keys(_historyTasks, historyId));

        public Task LinkMemberNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        {
            Link(_taskNodes, taskKey, nodeKey);
            return Task.CompletedTask;
        }

        public Task UnlinkMemberNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        {
            if (_taskNodes.TryGetValue(taskKey, out var keys)) keys.Remove(nodeKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetMemberNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default)
            => Task.FromResult(Keys(_taskNodes, taskKey));

        public Task<IReadOnlyList<string>> GetContainerNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        /// <summary>会话子图读面：内存端口不承载会话挂载（会话边由引用守卫写），恒空集（不伪造）。</summary>
        public Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        /// <summary>会话子图投影：同上（无挂载 → 空投影，不伪造时序）。</summary>
        public Task<IReadOnlyList<NodeProjectionItem>> ProjectSessionNodesAsync(string sessionName, string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeProjectionItem>>(Array.Empty<NodeProjectionItem>());

        /// <summary>task 作用域候选投影：内存端口只按挂载顺序出索引（不伪造图拓扑）。</summary>
        public Task<IReadOnlyList<NodeProjectionItem>> ProjectMemberNodesAsync(string taskKey, string workflowId, CancellationToken ct = default)
        {
            var keys = Keys(_taskNodes, taskKey);
            IReadOnlyList<NodeProjectionItem> items = keys
                .Select((k, i) => new NodeProjectionItem(k, workflowId, i + 1))
                .ToList();
            return Task.FromResult(items);
        }

        // ---- 测试面簿记：LinkMemberNodeAsync（IContainerMemberPort）直接改内存状态 ----

        private static void Link(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var values)) map[key] = values = new List<string>();
            if (!values.Contains(value)) values.Add(value);
        }

        private static IReadOnlyList<string> Keys(Dictionary<string, List<string>> map, string key)
            => map.TryGetValue(key, out var values) ? values.ToArray() : Array.Empty<string>();
    }
}
