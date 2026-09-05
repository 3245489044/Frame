#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions.Models;

namespace DataManage.Abstractions
{
    public interface IGraphStorageCache
    {
        Task<UniversalWorkflowFramework.Core.Models.Resource?> GetAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default);

        Task SetAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            UniversalWorkflowFramework.Core.Models.Resource resource,
            CancellationToken cancellationToken = default);

        Task InvalidateAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default);

        Task InvalidateByTypeAsync(
            string resourceType,
            CancellationToken cancellationToken = default);

        Task InvalidateAllAsync(CancellationToken cancellationToken = default);

        Task<bool> ExistsAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default);

        CacheStatistics GetStatistics();
    }

    public interface ICacheEvictionPolicy
    {
        List<string> SelectKeysForEviction(
            Dictionary<string, (UniversalWorkflowFramework.Core.Models.Resource Resource, DateTime ExpiresAt)> cache,
            int count);
    }
}