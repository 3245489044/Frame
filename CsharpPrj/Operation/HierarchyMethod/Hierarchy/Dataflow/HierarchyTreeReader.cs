using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.Container;
using Operation.HierarchyMethod.Nodes.Keys;
using Operation.HierarchyMethod.Nodes.Linkage;
using Modules.Node.Identity;
using Share.Conversation.Ports;

namespace Operation.HierarchyMethod.Hierarchy.Dataflow
{
    /// <summary>
    /// 三层树（history → task → node）只读物化条目 —— 可直接渲染的两级树（左「历史→任务」+ task 关联 node 键集）。
    /// </summary>
    /// <param name="HistoryId">历史容器 Id。</param>
    /// <param name="TaskKeys">该历史归拢的 task 键集（空 = 无归拢，不合成）。</param>
    /// <param name="TaskNodeKeys">taskKey → node 键集（可空；未装配引用守卫或该 task 无节点时为 null）。</param>
    public sealed record HierarchyTree(
        string HistoryId,
        IReadOnlyList<string> TaskKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? TaskNodeKeys = null);

    /// <summary>
    /// 三层树只读装配器（LLM-free）—— 把「容器→成员→子成员」物化为只读树：
    /// <c>IConversationLogStore.ListHistoryIdsAsync</c>（历史存在性）→ <c>ITransferFlow.EnumerateAsync</c>
    /// （归属枚举单源 = USharp 流 <c>[Flow] TransferContribute</c> 的 <c>enumerate_members</c> 步，
    /// <c>edge_type</c> ∈ {history_owns_task, cross_db_link}）→ 键前缀剥离。
    /// <para>
    /// 单一真源、只读、零合成：空库返回空表（不造 default 容器、不从任务池补行）；未装配传递端口 ⇒
    /// 回退图端口（迁移期双读），二者皆缺 ⇒ 对应层返回空集（不猜测）。
    /// 写边唯一 owner 仍是 DataStoreDriven.ReferenceGraphCoordinator，本装配器不触碰写路径。
    /// </para>
    /// </summary>
    public sealed class HierarchyTreeReader
    {
        private readonly IHistoryRootCatalog _catalog;
        private readonly IContainerRelationManager? _relations;
        private readonly IReferenceMaintenance? _grounding;
        private readonly ITransferFlow? _transfer;
        private readonly string _workflowId;

        /// <summary>根目录为必填（唯一根枚举入口）；未装配即构造期失败。</summary>
        public HierarchyTreeReader(
            IHistoryRootCatalog catalog,
            IContainerRelationManager? relations = null,
            IReferenceMaintenance? grounding = null,
            ITransferFlow? transfer = null,
            string? workflowId = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _relations = relations;
            _grounding = grounding;
            _transfer = transfer;
            _workflowId = KeyAddressing.NormalizeNamespace(workflowId);
        }


        /// <summary>装配三层树（只读真源）。三源皆空 ⇒ 返回空表（禁造 default 容器、禁任务池补行）。
        /// <para><paramref name="includeTaskNodeKeys"/> 缺省 <c>true</c>：task→node 键集经**批量**端口
        /// （一次图枚举，见 <see cref="IHierarchyReferenceManager.GetTaskNodeKeysManyAsync"/>）构建，无 N+1。</para></summary>
        public async Task<IReadOnlyList<HierarchyTree>> ReadAsync(
            CancellationToken ct = default, bool includeTaskNodeKeys = true)
        {
            var nodes = new List<HierarchyTree>();
            var roots = await _catalog.ListAsync(ct).ConfigureAwait(false);
            var ids = roots.Select(r => r.Key.Value).ToList();

            foreach (var hid in ids)
            {
                if (string.IsNullOrWhiteSpace(hid)) continue;
                // 归属枚举单源（USharp 流 [Flow] TransferContribute 的 enumerate_members 步，edge_type 参数化）；
                // 未装配传递端口 ⇒ 回退图端口（迁移期双读；图未装配 ⇒ 该历史下无任务行，不猜测）。
                var tasks = _transfer is not null
                    ? await _transfer.EnumerateAsync(hid, CrossDbReferenceTypes.HistoryOwnsTask, ct).ConfigureAwait(false)
                    : _relations is null
                        ? (IReadOnlyList<string>)Array.Empty<string>()
                        : await _relations.GetTaskKeysAsync(hid, ct).ConfigureAwait(false);
                // 修剪原则：只为「有消费方」的字段付费。TaskNodeKeys 无人读 ⇒ 不构建（否则 tasks × 图枚举 = N+1）。
                var taskNodeKeys = includeTaskNodeKeys
                    ? await BuildTaskNodeKeysAsync(tasks, ct).ConfigureAwait(false)
                    : null;
                nodes.Add(new HierarchyTree(hid, tasks, taskNodeKeys));
            }
            return nodes;
        }

        /// <summary>批量查询引用守卫（一次图枚举；键已由端口归一，本层不再自剥前缀）。</summary>
        private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> BuildTaskNodeKeysAsync(
            IReadOnlyList<string>? taskKeys, CancellationToken ct)
        {
            if (taskKeys is null || taskKeys.Count == 0 || (_grounding is null && _transfer is null))
                return null;

            var dict = new Dictionary<string, IReadOnlyList<string>>(taskKeys.Count, StringComparer.Ordinal);
            if (_grounding is not null)
            {
                var many = await _grounding
                    .GetTaskNodeKeysManyAsync(taskKeys, _workflowId, ct)
                    .ConfigureAwait(false);
                foreach (var pair in many)
                {
                    if (pair.Value.Count > 0) dict[pair.Key] = pair.Value;
                }

                return dict.Count > 0 ? dict : null;
            }

            // 传递端口（声明流）路径：保持逐 task；键形态交由节点端点文法归一（不自写切片）。
            foreach (var task in taskKeys)
            {
                if (string.IsNullOrWhiteSpace(task)) continue;
                var rawKeys = await _transfer!.EnumerateAsync(task, CrossDbReferenceTypes.CrossDbLink, ct).ConfigureAwait(false);
                var cleanKeys = new List<string>(rawKeys.Count);
                foreach (var k in rawKeys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (NodeEndpointIds.TryParseNodeIdentity(k, _workflowId, out var clean) && clean.Length > 0)
                    {
                        cleanKeys.Add(clean);
                    }
                }

                if (cleanKeys.Count > 0) dict[task] = cleanKeys;
            }

            return dict.Count > 0 ? dict : null;
        }
    }
}
