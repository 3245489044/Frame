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
        private readonly object _initLock = new();
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private volatile bool _initialized = false;

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
            await Task.Yield();
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (_initLock)
            {
                if (_initialized)
                    return;

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

            // 串行化落盘：并发 Save 若同时写同一文件会互相截断/交错（原实现无任何互斥）。
            await _saveGate.WaitAsync().ConfigureAwait(false);
            try
            {
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
                            Location = kvp.Value.Identifier.Location,
                            Id = kvp.Value.Identifier.Id,
                            Name = kvp.Value.Identifier.Name,
                            Symbol = kvp.Value.Symbol,
                            // 深拷贝：快照在锁外序列化，共享字典可能被 UpdateResourceAsync 并发改写。
                            Metadata = new Dictionary<string, object>(kvp.Value.Metadata)
                        });
                    }

                    referencesSnapshot = new List<ReferenceRecord>(_references);
                }

                await WriteAtomicallyAsync(Path.Combine(_dataDirectory, ResourcesFileName), resourcesData).ConfigureAwait(false);
                await WriteAtomicallyAsync(Path.Combine(_dataDirectory, ReferencesFileName), referencesSnapshot).ConfigureAwait(false);
            }
            finally
            {
                _saveGate.Release();
            }
        }

        /// <summary>先写临时文件再原子替换，避免进程中断/并发读看到半截 JSON。</summary>
        private static async Task WriteAtomicallyAsync(string path, object data)
        {
            var tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
            try
            {
                await JsonSerializationService.Instance
                    .SerializeToFileAsync(tempPath, data, PersistenceOptions).ConfigureAwait(false);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
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
                        var identifier = new Core.Models.ResourceIdentifier
                        {
                            ResourceType = snap.ResourceType,
                            Location = snap.Location,
                            Id = snap.Id,
                            Name = snap.Name
                        };
                        var resource = new Resource
                        {
                            Identifier = identifier,
                            Metadata = snap.Metadata ?? new Dictionary<string, object>(),
                            Symbol = snap.Symbol
                        };
                        // 以当前键算法重算（兼容旧文件里 2 段式 Key）：
                        _resources[identifier.GetUniqueKey()] = resource;
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
            public string? Location { get; set; }
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Symbol { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }
    }
}
