#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.Core
{
    public interface IResourceRegistrationService
    {
        Task RegisterResourceAsync(ResourceIdentifier resource, Dictionary<string, object> metadata, string name);

        Task RegisterResourceWithParentAsync(
            ResourceIdentifier resource,
            Dictionary<string, object> metadata,
            string name,
            ResourceIdentifier parentResource,
            ReferenceType referenceType,
            string description = "",
            string? location = null,
            double strength = 1.0
        );

        Task AddReferenceAsync(
            ResourceIdentifier source,
            ResourceIdentifier target,
            ReferenceType referenceType,
            string description = "",
            string? location = null,
            double strength = 1.0
        );
    }

    public interface IResourceUnregistrationService
    {
        Task<ResourceUnregistrationResult> UnregisterResourceAsync(ResourceIdentifier resource);

        Task<ResourceUnregistrationResult> UnregisterResourceWithChildrenAsync(ResourceIdentifier resource);

        Task<ResourceUnregistrationResult> UnregisterResourceHierarchyAsync(ResourceIdentifier resource, string[] childTypes);
    }

    public interface IModuleProjectRegistrationService
    {
        Task RegisterProjectAsync(string address, string projectName, string projectType, string projectPath);
    }

    public interface IModuleProjectUnregistrationService
    {
        Task<ModuleProjectUnregistrationResult> UnregisterProjectAsync(string address, string projectName);

        Task<ModuleProjectUnregistrationResult> UnregisterProjectByAddressAsync(string address);
    }

    public interface IModuleProjectService : IModuleProjectRegistrationService, IModuleProjectUnregistrationService
    {
    }
}