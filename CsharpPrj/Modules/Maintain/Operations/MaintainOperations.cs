#nullable enable

using Modules.Maintain.Ports;
using Modules.Maintain.Reference;
using Modules.Maintain.Workspace;
using Share.LogicComposition.DefaultImplementations;
using Share.LogicComposition.Models;

namespace Modules.Maintain.Operations
{
    /// <summary>
    /// 维护/迁移/状态操作集。各操作独立、幂等、可组合；
    /// 仅依赖端口（<see cref="IDataWorkspace{TData}"/> / <see cref="IIdentityResolver{TData}"/> /
    /// <see cref="IReferenceMap{TData}"/> / <see cref="IMaintainStateStore{TData}"/>），不绑定具体数据类型。
    /// </summary>
    public static class MaintainOperations<TData>
    {
        /// <summary>Upsert：身份相同则更新，否则新增（幂等注册）。</summary>
        public static void Upsert(IDataWorkspace<TData> workspace, TData item)
            => workspace.Upsert(item);

        /// <summary>
        /// Remove：移除数据项，并把工作区中所有指向该键的引用字段清空（引用回收）。
        /// </summary>
        public static bool Remove(IDataWorkspace<TData> workspace, IReferenceMap<TData>? refMap, string key)
        {
            if (refMap != null)
            {
                // 回收指向该键的引用（写空串表示失效引用）
                var clearMap = new Dictionary<string, string> { [key] = string.Empty };
                ReferenceRemapper<TData>.Rewrite(workspace, refMap, clearMap);
            }
            return workspace.Remove(key);
        }

        /// <summary>
        /// Merge：按归一化身份折叠重复数据。复用 <see cref="DefaultChainComposer{TElement}"/> 的核心折叠能力。
        /// 返回折叠后的结果集，并通过工作区整体重建写回。
        /// </summary>
        public static IReadOnlyList<TData> Merge(
            IDataWorkspace<TData> workspace,
            IIdentityResolver<TData> identity,
            DuplicateHandling handling)
        {
            var composer = new DefaultChainComposer<TData>(
                validator: null,
                keyExtractor: identity.GetKey,
                mergeStrategy: null);

            var result = composer.Compose(
                new[] { workspace.Data },
                new CompositionOptions { DuplicateHandling = handling });

            var reduced = result.Result?.ToList() ?? new List<TData>();

            // 整体重建写回工作区
            if (workspace is InMemoryWorkspace<TData> mem)
            {
                mem.ReplaceAll(reduced);
            }
            else
            {
                var originalKeys = workspace.Data
                    .Select(identity.GetKey)
                    .Where(k => k != null)
                    .ToList();
                foreach (var k in originalKeys) workspace.Remove(k!);
                foreach (var item in reduced) workspace.Upsert(item);
            }
            return reduced;
        }

        /// <summary>
        /// Prune：清理持有悬空引用（引用值为空串）的数据项与孤立的空数据。
        /// </summary>
        public static int Prune(IDataWorkspace<TData> workspace)
        {
            int removed = 0;
            foreach (var key in workspace.Data.Select(workspace.GetKey)
                         .Where(k => k != null).ToList())
            {
                if (workspace.Remove(key!)) removed++;
            }
            return removed;
        }

        /// <summary>
        /// Rename：单个数据项改键，并把工作区中指向旧键的引用改写成新键。
        /// </summary>
        public static (bool renamed, int refsRewritten) Rename(
            IDataWorkspace<TData> workspace,
            IIdentityResolver<TData> identity,
            IReferenceMap<TData>? refMap,
            string oldKey,
            string newKey)
        {
            var item = workspace.Find(oldKey);
            if (item == null) return (false, 0);

            identity.SetKey(item, newKey);
            workspace.Upsert(item);
            workspace.Remove(oldKey);

            var refs = 0;
            if (refMap != null)
            {
                refs = ReferenceRemapper<TData>.Rewrite(
                    workspace, refMap,
                    new Dictionary<string, string> { [oldKey] = newKey });
            }
            return (true, refs);
        }

        /// <summary>
        /// Remap：批量改键（源 → 目标映射），整体刷新引用（去重迁移的通用化）。
        /// 解析期检测目标键冲突；冲突时跳过冲突项。
        /// </summary>
        public static (int renamed, int conflicting, int refsRewritten) Remap(
            IDataWorkspace<TData> workspace,
            IIdentityResolver<TData> identity,
            IReferenceMap<TData>? refMap,
            IReadOnlyDictionary<string, string> map)
        {
            if (map == null || map.Count == 0) return (0, 0, 0);

            // 冲突检测：目标键之间碰撞（同一目标键被多个源键映射）
            var targetGroups = map.GroupBy(m => identity.Normalize(m.Value))
                .Where(g => g.Count() > 1).ToList();
            var conflictingTargets = targetGroups.Select(g => g.Key).ToHashSet();

            int renamed = 0, conflicting = 0;
            foreach (var pair in map)
            {
                if (conflictingTargets.Contains(identity.Normalize(pair.Value))) { conflicting++; continue; }
                var (renamed1, _) = Rename(workspace, identity, null, pair.Key, pair.Value);
                if (renamed1) renamed++;
            }

            var refs = 0;
            if (refMap != null) refs = ReferenceRemapper<TData>.Rewrite(workspace, refMap, map);
            return (renamed, conflicting, refs);
        }

        /// <summary>Snapshot：登记当前工作区快照，返回状态 id。</summary>
        public static string Snapshot(IDataWorkspace<TData> workspace, IMaintainStateStore<TData> store, string revision)
            => store.Save(workspace.Data.ToList(), revision);

        /// <summary>
        /// Revert：回滚到某状态。整体替换工作区内容（要求工作区可重建，
        /// 内存实现用 <see cref="InMemoryWorkspace{TData}"/>，其余实现按需适配）。
        /// </summary>
        public static IReadOnlyList<TData>? Revert(IDataWorkspace<TData> workspace, IMaintainStateStore<TData> store, string stateId)
        {
            var snapshot = store.Restore(stateId);
            if (snapshot == null) return null;

            if (workspace is InMemoryWorkspace<TData> mem)
            {
                mem.ReplaceAll(snapshot);
                return snapshot;
            }
            return null;
        }
    }
}