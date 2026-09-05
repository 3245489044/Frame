using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.Core.Interfaces
{
    public interface IUniversalReferenceManager
    {
        Task InitializeAsync();

        Task AddResourceAsync(Core.Models.ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null);

        Task UpdateResourceAsync(Core.Models.ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null);

        Task<Core.Models.Resource?> GetResourceAsync(Core.Models.ResourceIdentifier resourceId);

        Task<List<Core.Models.Resource>> GetAllResourcesAsync();

        Task RemoveResourceAsync(Core.Models.ResourceIdentifier resourceId);

        Task AddReferenceAsync(Core.Models.ResourceIdentifier sourceResourceId, Core.Models.ResourceIdentifier targetResourceId, ReferenceType referenceType, string? location = null, string? description = null, double strength = 1.0);

        Task RemoveReferenceAsync(Core.Models.ResourceIdentifier sourceResourceId, Core.Models.ResourceIdentifier targetResourceId, ReferenceType referenceType);

        Task<List<(Core.Models.ResourceIdentifier Source, Core.Models.ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength, bool IsIncoming)>> GetReferencesAsync(Core.Models.ResourceIdentifier resourceId, bool includeIncoming = true);

        Task<List<Core.Models.ResourceIdentifier>> GetDependenciesAsync(Core.Models.ResourceIdentifier resourceId, int maxDepth = 5);

        Task<List<Core.Models.ResourceIdentifier>> GetDependentsAsync(Core.Models.ResourceIdentifier resourceId, int maxDepth = 5);

        Task<List<List<Core.Models.ResourceIdentifier>>> FindPathsAsync(Core.Models.ResourceIdentifier startResourceId, Core.Models.ResourceIdentifier endResourceId, int maxDepth = 10);

        Task<List<Core.Models.ResourceIdentifier>> FindShortestPathAsync(Core.Models.ResourceIdentifier startResourceId, Core.Models.ResourceIdentifier endResourceId);

        Task<List<Core.Models.Resource>> GetResourcesByTypeAsync(string resourceType);

        Task CleanupNonExistentResourcesAsync();

        bool ContainsResource(Core.Models.ResourceIdentifier resourceId);
    }
}
