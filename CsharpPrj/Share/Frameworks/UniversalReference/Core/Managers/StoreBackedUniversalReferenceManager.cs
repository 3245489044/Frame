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

        public override async Task CleanupNonExistentResourcesAsync()
        {
            await Task.CompletedTask;
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
                var type = Enum.TryParse<ReferenceType>(r.RefType, ignoreCase: true, out var t) ? t : ReferenceType.Other;
                result.Add((r.Source, r.Target, type, null, null, r.Strength, r.IsIncoming));
            }
            return result;
        }
    }
}
