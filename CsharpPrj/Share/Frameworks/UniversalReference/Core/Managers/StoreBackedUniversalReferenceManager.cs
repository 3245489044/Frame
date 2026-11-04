#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Reference.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Core.Managers
{
    /// <summary>
    /// 以 <see cref="IResourceReferenceStore"/> 端口（L1 存储原语层）为后端的 <see cref="IUniversalReferenceManager"/> 实现。
    /// B0 激活：让框架管理器查询真实业务存储（DataStoreDriven 的 SQLite 后端经端口注入），
    /// 而非内存/JSON 镜像；<see cref="ReferenceGraphEngine"/> 经此实现读到真实引用图。
    /// 写操作委托端口；图遍历（GetDependencies/FindPaths 等）保留基类内存实现——B2–B5 业务域降级后改走图引擎入口。
    /// </summary>
    public class StoreBackedUniversalReferenceManager : ReferenceManagerBase
    {
        private readonly IResourceReferenceStore _store;

        public StoreBackedUniversalReferenceManager(IResourceReferenceStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public override Task InitializeAsync()
            => _store.InitializeAsync();

        public override async Task AddResourceAsync(ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null)
            => await _store.AddResourceAsync(resourceId, metadata, symbol).ConfigureAwait(false);

        public override async Task UpdateResourceAsync(ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null)
            => await _store.UpdateResourceAsync(resourceId, metadata, symbol).ConfigureAwait(false);

        public override async Task RemoveResourceAsync(ResourceIdentifier resourceId)
            => await _store.RemoveResourceAsync(resourceId).ConfigureAwait(false);

        public override async Task AddReferenceAsync(ResourceIdentifier sourceResourceId, ResourceIdentifier targetResourceId, ReferenceType referenceType, string? location = null, string? description = null, double strength = 1.0)
            => await _store.AddReferenceAsync(sourceResourceId, targetResourceId, referenceType.ToString(), strength).ConfigureAwait(false);

        public override async Task RemoveReferenceAsync(ResourceIdentifier sourceResourceId, ResourceIdentifier targetResourceId, ReferenceType referenceType)
            => await _store.RemoveReferenceAsync(sourceResourceId, targetResourceId, referenceType.ToString()).ConfigureAwait(false);

        /// <summary>清理悬空引用：删除目标资源不存在的出边（端点存在性以存储为准；语义对齐 <c>ReferenceManagerBase</c> 基类实现）。</summary>
        public override async Task CleanupNonExistentResourcesAsync()
        {
            var resources = await _store.GetAllResourcesAsync().ConfigureAwait(false);
            var existing = new HashSet<string>(
                resources.Select(static r => r.Identifier.GetUniqueKey()), StringComparer.Ordinal);
            foreach (var resource in resources)
            {
                var outgoing = await _store.GetReferencesAsync(resource.Identifier, includeIncoming: false).ConfigureAwait(false);
                foreach (var reference in outgoing)
                {
                    if (existing.Contains(reference.Target.GetUniqueKey())) continue;
                    await _store.RemoveReferenceAsync(reference.Source, reference.Target, reference.RefType).ConfigureAwait(false);
                }
            }
        }

        // 图引擎（ReferenceGraphEngine.BuildGraphAsync）唯一调用的两个查询方法——委托端口读真实数据。
        public override async Task<List<Resource>> GetAllResourcesAsync()
            => (await _store.GetAllResourcesAsync().ConfigureAwait(false)).ToList();

        public override async Task<List<(ResourceIdentifier Source, ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength, bool IsIncoming)>> GetReferencesAsync(ResourceIdentifier resourceId, bool includeIncoming = true)
        {
            var refs = await _store.GetReferencesAsync(resourceId, includeIncoming).ConfigureAwait(false);
            var result = new List<(ResourceIdentifier Source, ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength, bool IsIncoming)>(refs.Count);
            foreach (var r in refs)
            {
                var type = Reference.Models.ReferenceEdgeTypes.ToReferenceType(r.RefType);
                result.Add((r.Source, r.Target, type, null, null, r.Strength, r.IsIncoming));
            }
            return result;
        }
        public override async Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId)
            => await _store.GetResourceAsync(resourceId).ConfigureAwait(false);

        public override async Task<List<Resource>> GetResourcesByTypeAsync(string resourceType)
        {
            var all = await _store.GetAllResourcesAsync().ConfigureAwait(false);
            return string.IsNullOrEmpty(resourceType)
                ? all.ToList()
                : all.Where(r => r.Identifier.ResourceType == resourceType).ToList();
        }

        public override bool ContainsResource(ResourceIdentifier resourceId)
            => _store.GetResourceAsync(resourceId).GetAwaiter().GetResult() is not null;

        // 依赖/反向依赖：以存储中的边为准做限深遍历（基类内存表对 StoreBacked 恒为空）。
        public override async Task<List<ResourceIdentifier>> GetDependenciesAsync(ResourceIdentifier resourceId, int maxDepth = 5)
            => await TraverseAsync(resourceId, maxDepth, downstream: true).ConfigureAwait(false);

        public override async Task<List<ResourceIdentifier>> GetDependentsAsync(ResourceIdentifier resourceId, int maxDepth = 5)
            => await TraverseAsync(resourceId, maxDepth, downstream: false).ConfigureAwait(false);

        public override async Task<List<List<ResourceIdentifier>>> FindPathsAsync(ResourceIdentifier startResourceId, ResourceIdentifier endResourceId, int maxDepth = 10)
        {
            var edges = await LoadOutgoingAsync().ConfigureAwait(false);
            var start = startResourceId.GetUniqueKey();
            var end = endResourceId.GetUniqueKey();
            var paths = new List<List<string>>();
            var path = new List<string>();
            var onPath = new HashSet<string>(StringComparer.Ordinal);
            FindAllPaths(start, end, maxDepth, edges, path, onPath, paths);
            return paths.Select(p => p.Select(KeyToResourceIdentifier).ToList()).ToList();
        }

        public override async Task<List<ResourceIdentifier>> FindShortestPathAsync(ResourceIdentifier startResourceId, ResourceIdentifier endResourceId)
        {
            var edges = await LoadOutgoingAsync().ConfigureAwait(false);
            var start = startResourceId.GetUniqueKey();
            var end = endResourceId.GetUniqueKey();
            if (start == end) return new List<ResourceIdentifier> { startResourceId };

            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!edges.TryGetValue(current, out var nexts)) continue;
                foreach (var next in nexts)
                {
                    if (previous.ContainsKey(next)) continue;
                    previous[next] = current;
                    if (next == end)
                    {
                        var keys = new List<string>();
                        var node = end;
                        while (node != start)
                        {
                            keys.Add(node);
                            node = previous[node];
                        }
                        keys.Add(start);
                        keys.Reverse();
                        return keys.Select(KeyToResourceIdentifier).ToList();
                    }
                    queue.Enqueue(next);
                }
            }
            return new List<ResourceIdentifier>();
        }

        private async Task<List<ResourceIdentifier>> TraverseAsync(ResourceIdentifier resourceId, int maxDepth, bool downstream)
        {
            var edges = await LoadOutgoingAsync().ConfigureAwait(false);
            var result = new List<ResourceIdentifier>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { resourceId.GetUniqueKey() };
            Walk(resourceId, maxDepth);
            return result;

            void Walk(ResourceIdentifier current, int depth)
            {
                if (depth <= 0) return;
                var key = current.GetUniqueKey();
                if (downstream)
                {
                    if (!edges.TryGetValue(key, out var nexts)) return;
                    foreach (var next in nexts)
                    {
                        if (!visited.Add(next)) continue;
                        result.Add(KeyToResourceIdentifier(next));
                        Walk(KeyToResourceIdentifier(next), depth - 1);
                    }
                }
                else
                {
                    var incoming = edges.Where(kv => kv.Value.Contains(key)).Select(kv => kv.Key);
                    foreach (var source in incoming)
                    {
                        if (!visited.Add(source)) continue;
                        result.Add(KeyToResourceIdentifier(source));
                        Walk(KeyToResourceIdentifier(source), depth - 1);
                    }
                }
            }
        }

        /// <summary>加载“源键 → 目标键集合”的邻接表（去重，按存储首次出现顺序）。</summary>
        private async Task<Dictionary<string, List<string>>> LoadOutgoingAsync()
        {
            var resources = await _store.GetAllResourcesAsync().ConfigureAwait(false);
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var resource in resources)
            {
                var outgoing = await _store.GetReferencesAsync(resource.Identifier, includeIncoming: false).ConfigureAwait(false);
                foreach (var reference in outgoing)
                {
                    var source = reference.Source.GetUniqueKey();
                    var target = reference.Target.GetUniqueKey();
                    if (!result.TryGetValue(source, out var targets))
                    {
                        targets = new List<string>();
                        result[source] = targets;
                    }
                    if (!targets.Contains(target)) targets.Add(target);
                }
            }
            return result;
        }

        private static void FindAllPaths(
            string current, string end, int remaining,
            Dictionary<string, List<string>> edges,
            List<string> path, HashSet<string> onPath, List<List<string>> results)
        {
            if (remaining <= 0) return;
            onPath.Add(current);
            path.Add(current);

            if (current == end)
            {
                results.Add(new List<string>(path));
            }
            else if (edges.TryGetValue(current, out var nexts))
            {
                foreach (var next in nexts)
                {
                    if (!onPath.Contains(next)) FindAllPaths(next, end, remaining - 1, edges, path, onPath, results);
                }
            }

            path.RemoveAt(path.Count - 1);
            onPath.Remove(current);
        }
    }
}
