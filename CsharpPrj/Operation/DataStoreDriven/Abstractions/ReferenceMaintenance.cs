using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;

namespace Operation.DataStoreDriven.Abstractions
{
    /// <summary>对账作用域：单节点 / 全图。</summary>
    public enum MaintenanceScope
    {
        /// <summary>单节点：仅对账指定节点（携带 workflowId + nodeKey）。</summary>
        Node,

        /// <summary>全图：对账全部业务节点。</summary>
        All,
    }

    /// <summary>图对账差异报告（A5）：差异计数（悬空端点数）+ 本次对账处理节点数。</summary>
    /// <param name="DanglingNodeResources">图库中无对应业务节点的悬空 node 资源 Id（仅报告，不自动删除）。</param>
    /// <param name="ReconciledNodes">本次对账处理的业务节点数。</param>
    public sealed record GraphMaintenanceDiff(
        IReadOnlyList<string> DanglingNodeResources,
        int ReconciledNodes)
    {
        /// <summary>差异计数（悬空端点数；0 = 图与 SQL 权威一致）。</summary>
        public int DanglingCount => DanglingNodeResources.Count;
    }

    /// <summary>
    /// 引用关系维护守卫（唯一守卫入口，对齐 SingleImplementation / TH-12）。
    /// <para>
    /// 通用图维护面下沉到 <see cref="IGraphGrounding"/>（节点镜像 / 权威边 / 声明 / 对账 / 重建 / 墓碑 / seed）；
    /// 本接口只保留**领域语义**成员（task / history / session）。
    /// </para>
    /// </summary>
    public interface IReferenceMaintenance : IGraphGrounding
    {
        /// <summary>镜像 task 端点（Id=taskKey，幂等）。</summary>
        Task EnsureTaskMirrorAsync(string taskKey, CancellationToken ct = default);

        /// <summary>移除 task 端点镜像（级联清边）。</summary>
        Task RemoveTaskMirrorAsync(string taskKey, CancellationToken ct = default);

        /// <summary>task 关联节点：注册 task 端点 + 与关联节点建 task→node 边（幂等）。</summary>
        Task ReconcileTaskAsync(string taskKey, IReadOnlyCollection<string> relatedNodeKeys,
            string workflowId = "", CancellationToken ct = default);

        /// <summary>运行时 task 归属边（cross_db_link）：task（ur_tasks）→ 节点（ur_nodes）。幂等加法。</summary>
        Task EnsureTaskNodeLinkAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>解除 task 与节点的关联边（M:N 撤销）。</summary>
        Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>该 task 关联的节点键集（task 作用域寻址；供执行期输入解析）。</summary>
        Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default);

        /// <summary>批量 task 节点键集（消 N+1；默认逐条，生产实现覆盖为单次枚举）。</summary>
        async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetTaskNodeKeysManyAsync(
            IReadOnlyCollection<string> taskKeys, string workflowId, CancellationToken ct = default)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var taskKey in taskKeys)
            {
                result[taskKey] = await GetTaskNodeKeysAsync(taskKey, workflowId, ct).ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>历史 class 容器根端点：幂等建 history.{historyId} 端点镜像。</summary>
        Task EnsureHistoryMirrorAsync(string historyId, CancellationToken ct = default);

        /// <summary>移除历史根端点镜像（级联清边）。</summary>
        Task RemoveHistoryMirrorAsync(string historyId, CancellationToken ct = default);

        /// <summary>历史归拢任务：history.{historyId} → task（taskKey）引用边（幂等加法）。</summary>
        Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default);

        /// <summary>解除历史归拢任务边（历史 → task 出边移除）。</summary>
        Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default);

        /// <summary>按历史查 history_owns_task 出边，返回该历史归拢的任务键集。</summary>
        Task<IReadOnlyList<string>> GetHistoryTasksAsync(string historyId, CancellationToken ct = default);

        /// <summary>批量成员集（一次持锁读多个历史出边，避免逐 history 门等待）。</summary>
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetHistoryTasksManyAsync(
            IReadOnlyCollection<string> historyIds, CancellationToken ct);

        /// <summary>【**已退役**（W1）】历史挂载节点曾写 <c>history_owns_node</c> 边；调用即抛。</summary>
        Task AttachHistoryNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>按历史查工作流节点键集（派生视图：该历史全部任务的 task→node 边并集）。</summary>
        Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default);

        /// <summary>会话归属边（session_owns）：把业务节点挂到会话根（幂等加法）。</summary>
        Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>会话子图节点键集：按 session_owns 出边返回会话挂载的业务节点键。</summary>
        Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default);
    }
}
