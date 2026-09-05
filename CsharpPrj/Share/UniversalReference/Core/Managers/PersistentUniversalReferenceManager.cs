#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Plate.JsonMethod.Serialization;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.Core.Managers
{
    public class PersistentUniversalReferenceManager : ReferenceManagerBase
    {
        private readonly string _dataDirectory;
        private static readonly string ResourcesFileName = "resources.json";
        private static readonly string ReferencesFileName = "references.json";
        private bool _initialized = false;

        // 持久化文件保持历史落盘契约（PascalCase 属性名 / 缩进 / 保留 null / 默认转义），
        // 故不套用 JsonSerializerFactory 的 camelCase 上下文；仅借用平台层
        // DynamicObjectConverter 修复 Dictionary<string,object> 元数据回读丢失值类型的问题。
        private static readonly JsonSerializerOptions PersistenceOptions = CreatePersistenceOptions();

        private static JsonSerializerOptions CreatePersistenceOptions()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new DynamicObjectConverter());
            return options;
        }

        public PersistentUniversalReferenceManager(string dataDirectory)
        {
            _dataDirectory = dataDirectory;
        }

        public override async Task InitializeAsync()
        {
            Directory.CreateDirectory(_dataDirectory);
            await LoadFromDiskAsync();
            _initialized = true;
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                Directory.CreateDirectory(_dataDirectory);
                LoadFromDiskSync();
                _initialized = true;
            }
        }

        public override async Task AddResourceAsync(Core.Models.ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null)
        {
            EnsureInitialized();
            await base.AddResourceAsync(resourceId, metadata, symbol);
            await SaveToDiskAsync();
        }

        public override async Task UpdateResourceAsync(Core.Models.ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null)
        {
            EnsureInitialized();
            await base.UpdateResourceAsync(resourceId, metadata, symbol);
            await SaveToDiskAsync();
        }

        public override async Task RemoveResourceAsync(Core.Models.ResourceIdentifier resourceId)
        {
            EnsureInitialized();
            await base.RemoveResourceAsync(resourceId);
            await SaveToDiskAsync();
        }

        public override async Task AddReferenceAsync(Core.Models.ResourceIdentifier sourceResourceId, Core.Models.ResourceIdentifier targetResourceId, ReferenceType referenceType, string? location = null, string? description = null, double strength = 1.0)
        {
            EnsureInitialized();
            await base.AddReferenceAsync(sourceResourceId, targetResourceId, referenceType, location, description, strength);
            await SaveToDiskAsync();
        }

        public override async Task RemoveReferenceAsync(Core.Models.ResourceIdentifier sourceResourceId, Core.Models.ResourceIdentifier targetResourceId, ReferenceType referenceType)
        {
            EnsureInitialized();
            await base.RemoveReferenceAsync(sourceResourceId, targetResourceId, referenceType);
            await SaveToDiskAsync();
        }

        public override async Task CleanupNonExistentResourcesAsync()
        {
            EnsureInitialized();
            await base.CleanupNonExistentResourcesAsync();
            await SaveToDiskAsync();
        }

        private async Task SaveToDiskAsync()
        {
            EnsureInitialized();

            List<ResourceSnapshot> resourcesData;
            List<ReferenceRecord> referencesSnapshot;

            lock (_lock)
            {
                resourcesData = new List<ResourceSnapshot>(_resources.Count);
                foreach (var kvp in _resources)
                {
                    resourcesData.Add(new ResourceSnapshot
                    {
                        Key = kvp.Key,
                        ResourceType = kvp.Value.Identifier.ResourceType,
                        Id = kvp.Value.Identifier.Id,
                        Name = kvp.Value.Identifier.Name,
                        Symbol = kvp.Value.Symbol,
                        Metadata = kvp.Value.Metadata
                    });
                }

                referencesSnapshot = new List<ReferenceRecord>(_references);
            }

            var resourcesPath = Path.Combine(_dataDirectory, ResourcesFileName);
            var referencesPath = Path.Combine(_dataDirectory, ReferencesFileName);

            await JsonSerializationService.Instance
                .SerializeToFileAsync(resourcesPath, resourcesData, PersistenceOptions).ConfigureAwait(false);
            await JsonSerializationService.Instance
                .SerializeToFileAsync(referencesPath, referencesSnapshot, PersistenceOptions).ConfigureAwait(false);
        }

        private async Task LoadFromDiskAsync()
        {
            var resourcesPath = Path.Combine(_dataDirectory, ResourcesFileName);
            var referencesPath = Path.Combine(_dataDirectory, ReferencesFileName);

            string? resourcesJson = null;
            string? referencesJson = null;

            if (File.Exists(resourcesPath))
                resourcesJson = await File.ReadAllTextAsync(resourcesPath).ConfigureAwait(false);

            if (File.Exists(referencesPath))
                referencesJson = await File.ReadAllTextAsync(referencesPath).ConfigureAwait(false);

            ApplyLoadedFiles(resourcesJson, referencesJson);
        }

        private void LoadFromDiskSync()
        {
            var resourcesPath = Path.Combine(_dataDirectory, ResourcesFileName);
            var referencesPath = Path.Combine(_dataDirectory, ReferencesFileName);

            string? resourcesJson = null;
            string? referencesJson = null;

            if (File.Exists(resourcesPath))
                resourcesJson = File.ReadAllText(resourcesPath);

            if (File.Exists(referencesPath))
                referencesJson = File.ReadAllText(referencesPath);

            ApplyLoadedFiles(resourcesJson, referencesJson);
        }

        private void ApplyLoadedFiles(string? resourcesJson, string? referencesJson)
        {
            lock (_lock)
            {
                DeserializeAndLoad(resourcesJson, referencesJson);
            }
        }

        private void DeserializeAndLoad(string? resourcesJson, string? referencesJson)
        {
            _resources.Clear();
            _references.Clear();

            if (resourcesJson != null)
            {
                var resourcesData = JsonSerializationService.Instance
                    .Deserialize<List<ResourceSnapshot>>(resourcesJson, PersistenceOptions);
                if (resourcesData != null)
                {
                    foreach (var snap in resourcesData)
                    {
                        var resource = new Resource
                        {
                            Identifier = new Core.Models.ResourceIdentifier
                            {
                                ResourceType = snap.ResourceType,
                                Id = snap.Id,
                                Name = snap.Name
                            },
                            Metadata = snap.Metadata ?? new Dictionary<string, object>(),
                            Symbol = snap.Symbol
                        };
                        _resources[snap.Key] = resource;
                    }
                }
            }

            if (referencesJson != null)
            {
                var refs = JsonSerializationService.Instance
                    .Deserialize<List<ReferenceRecord>>(referencesJson, PersistenceOptions);
                if (refs != null)
                {
                    _references.AddRange(refs);
                }
            }
        }

        private class ResourceSnapshot
        {
            public string Key { get; set; } = string.Empty;
            public string? ResourceType { get; set; }
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Symbol { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }
    }
}
