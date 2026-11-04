#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.EventDriven.Models;
using UniversalWorkflowFramework.Reference.Models;


namespace UniversalWorkflowFramework.EventDriven
{
    public class ObservableReferenceManager : IUniversalReferenceManager, IDisposable
    {
        private readonly IUniversalReferenceManager _innerManager;
        private bool _disposed;

        public event EventHandler<ReferenceChangeEventArgs>? OnResourceAdded;

        public event EventHandler<ReferenceChangeEventArgs>? OnResourceUpdated;

        public event EventHandler<ReferenceChangeEventArgs>? OnResourceRemoved;

        public event EventHandler<ReferenceChangeEventArgs>? OnReferenceCreated;

        public event EventHandler<ReferenceChangeEventArgs>? OnReferenceRemoved;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ObservableReferenceManager));
        }

        public ObservableReferenceManager(IUniversalReferenceManager innerManager)
        {
            _innerManager = innerManager ?? throw new ArgumentNullException(nameof(innerManager));
        }

        public Task InitializeAsync()
        {
            return _innerManager.InitializeAsync();
        }

        public async Task AddResourceAsync(ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null)
        {
            ThrowIfDisposed();
            await _innerManager.AddResourceAsync(resourceId, metadata, symbol);
            OnResourceAdded?.Invoke(this, new ReferenceChangeEventArgs(
                resourceId,
                Reference.Models.ReferenceChangeType.Added,
                null,
                metadata));
        }

        public async Task UpdateResourceAsync(ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null)
        {
            ThrowIfDisposed();
            await _innerManager.UpdateResourceAsync(resourceId, metadata, symbol);
            OnResourceUpdated?.Invoke(this, new ReferenceChangeEventArgs(
                resourceId,
                Reference.Models.ReferenceChangeType.Updated,
                null,
                metadata));
        }

        public Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId)
        {
            return _innerManager.GetResourceAsync(resourceId);
        }

        public Task<List<Resource>> GetAllResourcesAsync()
        {
            return _innerManager.GetAllResourcesAsync();
        }

        public async Task RemoveResourceAsync(ResourceIdentifier resourceId)
        {
            ThrowIfDisposed();
            await _innerManager.RemoveResourceAsync(resourceId);
            OnResourceRemoved?.Invoke(this, new ReferenceChangeEventArgs(
                resourceId,
                Reference.Models.ReferenceChangeType.Removed));
        }

        public async Task AddReferenceAsync(ResourceIdentifier sourceResourceId, ResourceIdentifier targetResourceId, ReferenceType referenceType, string? location = null, string? description = null, double strength = 1.0)
        {
            ThrowIfDisposed();
            await _innerManager.AddReferenceAsync(sourceResourceId, targetResourceId, referenceType, location, description, strength);
            OnReferenceCreated?.Invoke(this, new ReferenceChangeEventArgs(
                sourceResourceId, Reference.Models.ReferenceChangeType.Added,
                relationshipType: referenceType.ToString(), relatedResource: targetResourceId));
        }

        public async Task RemoveReferenceAsync(ResourceIdentifier sourceResourceId, ResourceIdentifier targetResourceId, ReferenceType referenceType)
        {
            ThrowIfDisposed();
            await _innerManager.RemoveReferenceAsync(sourceResourceId, targetResourceId, referenceType);
            OnReferenceRemoved?.Invoke(this, new ReferenceChangeEventArgs(
                sourceResourceId, Reference.Models.ReferenceChangeType.Removed,
                relationshipType: referenceType.ToString(), relatedResource: targetResourceId));
        }

        public Task<List<(ResourceIdentifier Source, ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength, bool IsIncoming)>> GetReferencesAsync(ResourceIdentifier resourceId, bool includeIncoming = true)
        {
            return _innerManager.GetReferencesAsync(resourceId, includeIncoming);
        }

        public Task<List<ResourceIdentifier>> GetDependenciesAsync(ResourceIdentifier resourceId, int maxDepth = 5)
        {
            return _innerManager.GetDependenciesAsync(resourceId, maxDepth);
        }

        public Task<List<ResourceIdentifier>> GetDependentsAsync(ResourceIdentifier resourceId, int maxDepth = 5)
        {
            return _innerManager.GetDependentsAsync(resourceId, maxDepth);
        }

        public Task<List<List<ResourceIdentifier>>> FindPathsAsync(ResourceIdentifier startResourceId, ResourceIdentifier endResourceId, int maxDepth = 10)
        {
            return _innerManager.FindPathsAsync(startResourceId, endResourceId, maxDepth);
        }

        public Task<List<ResourceIdentifier>> FindShortestPathAsync(ResourceIdentifier startResourceId, ResourceIdentifier endResourceId)
        {
            return _innerManager.FindShortestPathAsync(startResourceId, endResourceId);
        }

        public Task<List<Resource>> GetResourcesByTypeAsync(string resourceType)
        {
            return _innerManager.GetResourcesByTypeAsync(resourceType);
        }

        public Task CleanupNonExistentResourcesAsync()
        {
            return _innerManager.CleanupNonExistentResourcesAsync();
        }

        public bool ContainsResource(ResourceIdentifier resourceId)
        {
            return _innerManager.ContainsResource(resourceId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            OnResourceAdded = null;
            OnResourceUpdated = null;
            OnResourceRemoved = null;
            OnReferenceCreated = null;
            OnReferenceRemoved = null;

            if (_innerManager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
