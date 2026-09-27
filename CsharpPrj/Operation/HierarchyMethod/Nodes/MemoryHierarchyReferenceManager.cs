using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes.Linkage;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 三层层级引用端口的内存实现（最小实现，零持久化）：history→task→node 归属边记录于内存，
    /// 写侧即记录、读侧按记录返回。供声明式编排入口在无宿主设施条件下驱动编排动作；不承担断言语义。
    /// <para>与持久实现 <c>ReferenceGrounding</c> 并列：本类为内存最小落地
    /// （原 Agent 侧 TaskPool 的 DeclaredHierarchyReferenceManager 下沉，SingleImplementation / TH-12）。</para>
    /// <para>另承载**测试面簿记流入口**（<see cref="INodeAttributionFlow"/> / <see cref="ITaskProvisionFlow"/>）：
    /// 声明式编排入口 <c>TaskOrchestrator.RunTask</c> 的端口面需要簿记流入口，而生产回退支已删
    /// （B2 续；见《节点到任务到历史-重复冗余错误与欠账分析》）。本类直接改内存状态，**不经**生产写边方法，
    /// 故不构成第二条生产写路径（B9 随测试面端口迁移一并收口）。</para>
    /// </summary>
    public sealed class MemoryHierarchyReferenceManager : IHierarchyReferenceManager, INodeAttributionFlow, ITaskProvisionFlow
    {
        private readonly List<string> _histories = new();
        private readonly Dictionary<string, List<string>> _historyTasks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _taskNodes = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Histories => _histories;

        public bool HasHistoryTask(string historyId, string taskKey)
            => _historyTasks.TryGetValue(historyId, out var tasks) && tasks.Contains(taskKey);

        public bool HasTaskNode(string taskKey, string nodeKey)
            => _taskNodes.TryGetValue(taskKey, out var keys) && keys.Contains(nodeKey);

        public Task EnsureHistoryAsync(string historyId, CancellationToken ct = default)
        {
            if (!_histories.Contains(historyId)) _histories.Add(historyId);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default)
        {
            _historyTasks.Remove(historyId);
            return Task.FromResult(_histories.Remove(historyId));
        }

        public Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        {
            Link(_historyTasks, historyId, taskKey);
            return Task.CompletedTask;
        }

        public Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        {
            if (_historyTasks.TryGetValue(historyId, out var tasks)) tasks.Remove(taskKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetHistoryTaskKeysAsync(string historyId, CancellationToken ct = default)
            => Task.FromResult(Keys(_historyTasks, historyId));

        public Task LinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        {
            Link(_taskNodes, taskKey, nodeKey);
            return Task.CompletedTask;
        }

        public Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        {
            if (_taskNodes.TryGetValue(taskKey, out var keys)) keys.Remove(nodeKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default)
            => Task.FromResult(Keys(_taskNodes, taskKey));

        public Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        /// <summary>会话子图读面：内存端口不承载会话挂载（会话边由引用守卫写），恒空集（不伪造）。</summary>
        public Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        /// <summary>会话子图投影：同上（无挂载 → 空投影，不伪造时序）。</summary>
        public Task<IReadOnlyList<NodeProjectionItem>> ProjectSessionNodesAsync(string sessionName, string workflowId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeProjectionItem>>(Array.Empty<NodeProjectionItem>());

        // ---- 测试面簿记流入口（INodeAttributionFlow / ITaskProvisionFlow）：直接改内存状态 ----

        /// <summary>task→node 归属边（<c>cross_db_link</c> 内存口径）：直接落内存，不调生产写边方法。</summary>
        public Task AttachAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskKey) || string.IsNullOrWhiteSpace(nodeKey)) return Task.CompletedTask;
            Link(_taskNodes, taskKey, nodeKey);
            return Task.CompletedTask;
        }

        /// <summary>session_owns：内存端口不承载会话挂载（会话边由引用守卫写）⇒ 恒 no-op（不伪造）。</summary>
        public Task SessionAttachAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default)
            => Task.CompletedTask;

        /// <summary>任务供给簿记流（内存口径）：容器 + 归拢直接落内存，不调生产写边方法。</summary>
        public Task<bool> ProvisionAsync(string? historyId, string? historyName, string taskKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskKey)) return Task.FromResult(true);
            var hid = string.IsNullOrWhiteSpace(historyId) ? taskKey! : historyId!;
            if (!_histories.Contains(hid)) _histories.Add(hid);
            Link(_historyTasks, hid, taskKey!);
            return Task.FromResult(true);
        }

        private static void Link(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var values)) map[key] = values = new List<string>();
            if (!values.Contains(value)) values.Add(value);
        }

        private static IReadOnlyList<string> Keys(Dictionary<string, List<string>> map, string key)
            => map.TryGetValue(key, out var values) ? values.ToArray() : Array.Empty<string>();
    }
}
