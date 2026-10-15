using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.Nodes.Linkage;
using Share.Conversation;
using Share.Conversation.Ids;

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
    /// <c>IHistoryStore.ListHistoryIdsAsync</c>（历史存在性）→ <c>ITransferFlow.EnumerateAsync</c>
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
        private readonly IHistoryStore? _historyStore;
        private readonly IHistoryRelationManager? _relations;
        private readonly ReferenceGrounding? _grounding;
        private readonly ITransferFlow? _transfer;

        /// <param name="historyStore">历史存储（会话/历史存在性唯一真源，ur_histories）；null = 空表。</param>
        /// <param name="relations">历史关系管理端口（history→task 归拢边）；null = 无 task 行（不猜测）。</param>
        /// <param name="grounding">引用自动落地守卫（task→node 归属边）；null = 无 node 映射。</param>
        /// <param name="transfer">归属枚举 USharp 流（唯一成员枚举口）；null = 回退图端口（迁移期双读）。</param>
        public HierarchyTreeReader(
            IHistoryStore? historyStore,
            IHistoryRelationManager? relations = null,
            ReferenceGrounding? grounding = null,
            ITransferFlow? transfer = null)
        {
            _historyStore = historyStore;
            _relations = relations;
            _grounding = grounding;
            _transfer = transfer;
        }

        /// <summary>装配三层树（只读真源）。三源皆空 ⇒ 返回空表（禁造 default 容器、禁任务池补行）。</summary>
        public async Task<IReadOnlyList<HierarchyTree>> ReadAsync(CancellationToken ct = default)
        {
            var nodes = new List<HierarchyTree>();
            // 历史存储未装配 ⇒ 空（未就绪可见化由渲染面承担，不回退到任何「猜测式」读法）。
            if (_historyStore is null) return nodes;

            var ids = await _historyStore.ListHistoryIdsAsync(ct).ConfigureAwait(false);
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
                var taskNodeKeys = await BuildTaskNodeKeysAsync(tasks, ct).ConfigureAwait(false);
                nodes.Add(new HierarchyTree(hid, tasks, taskNodeKeys));
            }
            return nodes;
        }

        /// <summary>对每个任务查询引用守卫，返回 taskKey → nodeKeys 映射（端口内完成 wf. 前缀剥离；无节点返回空表）。</summary>
        private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> BuildTaskNodeKeysAsync(
            IReadOnlyList<string>? taskKeys, CancellationToken ct)
        {
            if (taskKeys is null || taskKeys.Count == 0 || (_grounding is null && _transfer is null))
                return null;
            var dict = new Dictionary<string, IReadOnlyList<string>>(taskKeys.Count, StringComparer.Ordinal);
            foreach (var task in taskKeys)
            {
                if (string.IsNullOrWhiteSpace(task)) continue;
                // 空 workflowId 匹配所有 cross_db_link 边；返回的键含 wf. 前缀，需剥离。
                // 枚举单源（USharp 流 enumerate_members，edge_type=cross_db_link）；未装配传递端口 ⇒ 回退图端口。
                var rawKeys = _transfer is not null
                    ? await _transfer.EnumerateAsync(task, CrossDbReferenceTypes.CrossDbLink, ct).ConfigureAwait(false)
                    : await _grounding!.GetTaskNodeKeysAsync(task, string.Empty, ct).ConfigureAwait(false);
                var cleanKeys = new List<string>(rawKeys.Count);
                foreach (var k in rawKeys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    cleanKeys.Add(NodeKey.StripPrefix(k));
                }
                if (cleanKeys.Count > 0)
                    dict[task] = cleanKeys;
            }
            return dict.Count > 0 ? dict : null;
        }
    }
}
