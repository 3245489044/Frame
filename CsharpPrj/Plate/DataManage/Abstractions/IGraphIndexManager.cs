#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataManage.Abstractions
{
    public interface IGraphIndexManager
    {
        Task CreateIndexAsync(string resourceType, string propertyName, CancellationToken cancellationToken = default);

        Task DropIndexAsync(string resourceType, string propertyName, CancellationToken cancellationToken = default);

        Task RebuildIndexAsync(string resourceType, CancellationToken cancellationToken = default);

        Task<bool> HasIndexAsync(string resourceType, string propertyName, CancellationToken cancellationToken = default);

        Task<List<string>> GetIndexedPropertiesAsync(string resourceType, CancellationToken cancellationToken = default);

        Task<List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier>> LookupByIndexAsync(
            string propertyName,
            object value,
            CancellationToken cancellationToken = default);

        Task<Dictionary<string, long>> GetIndexStatisticsAsync(CancellationToken cancellationToken = default);
    }
}