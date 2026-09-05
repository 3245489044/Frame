#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataManage.Abstractions
{
    public interface IGraphStorage
    {
        Task InitializeAsync();

        Task StoreResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            string? symbol = null);

        Task<UniversalWorkflowFramework.Core.Models.Resource?> GetResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId);

        Task DeleteResourceAsync(UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId);

        Task<List<UniversalWorkflowFramework.Core.Models.Resource>> GetResourcesByTypeAsync(string resourceType);

        Task<List<UniversalWorkflowFramework.Core.Models.Resource>> QueryResourcesAsync(
            string resourceType,
            Dictionary<string, object>? filters = null,
            int? limit = null,
            int? offset = null,
            CancellationToken cancellationToken = default);

        Task<List<UniversalWorkflowFramework.Core.Models.Resource>> SearchResourcesAsync(
            string keyword,
            List<string>? searchFields = null,
            int maxResults = 100,
            CancellationToken cancellationToken = default);

        Task<long> CountResourcesAsync(
            string resourceType,
            Dictionary<string, object>? filters = null,
            CancellationToken cancellationToken = default);

        Task<Dictionary<string, long>> CountByTypeGroupAsync(
            CancellationToken cancellationToken = default);

        Task BulkStoreResourcesAsync(
            List<(UniversalWorkflowFramework.Core.Models.ResourceIdentifier ResourceId,
                   Dictionary<string, object> Metadata,
                   string? Symbol)> resources,
            CancellationToken cancellationToken = default);

        Task BulkDeleteResourcesAsync(
            List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier> resourceIds,
            CancellationToken cancellationToken = default);

        Task<bool> ExistsAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            CancellationToken cancellationToken = default);

        Task UpdateResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            CancellationToken cancellationToken = default);
    }
}