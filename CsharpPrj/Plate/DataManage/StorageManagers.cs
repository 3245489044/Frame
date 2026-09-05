#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Foundational.Storage.Abstractions;

namespace FrameManage.DataStorage
{
    public interface IGenericStorageManager
    {
        string GetConfigurationPath(string fileName);
        string GetSQLitePath(string fileName);
        bool ValidatePath(string path);
        void EnsureDirectory(string path);
        Task<IEnumerable<T>> LoadAllAsync<T>(string containerName) where T : class, new();
        Task<T?> LoadAsync<T>(string containerName, string key) where T : class, new();
        Task SaveAsync<T>(string containerName, string key, T data) where T : class;
        Task<bool> DeleteAsync(string containerName, string key);
        Task<bool> ExistsAsync(string containerName, string key);
    }

    public class GenericStorageManager : IGenericStorageManager
    {
        private readonly IDataStorage _dataStorage;
        private readonly string _basePath;

        public GenericStorageManager(IDataStorage dataStorage, string applicationName)
        {
            _dataStorage = dataStorage;
            _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName);
        }

        public string GetConfigurationPath(string fileName)
        {
            var configPath = Path.Combine(_basePath, "Configuration");
            EnsureDirectory(configPath);
            return Path.Combine(configPath, fileName);
        }

        public string GetSQLitePath(string fileName)
        {
            var dbPath = Path.Combine(_basePath, "Database");
            EnsureDirectory(dbPath);
            return Path.Combine(dbPath, fileName);
        }

        public bool ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            
            try
            {
                var fullPath = Path.GetFullPath(path);
                return Path.IsPathRooted(fullPath);
            }
            catch
            {
                return false;
            }
        }

        public void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public async Task<IEnumerable<T>> LoadAllAsync<T>(string containerName) where T : class, new()
        {
            var keys = await _dataStorage.GetKeysAsync();
            var containerKeys = keys.Where(k => k.StartsWith($"{containerName}_")).ToList();
            var result = new List<T>();

            foreach (var key in containerKeys)
            {
                var itemKey = key.Substring(containerName.Length + 1);
                var data = await LoadAsync<T>(containerName, itemKey);
                if (data != null)
                {
                    result.Add(data);
                }
            }

            return result;
        }

        public async Task<T?> LoadAsync<T>(string containerName, string key) where T : class, new()
        {
            var storageKey = $"{containerName}_{key}";
            try
            {
                return await _dataStorage.LoadAsync<T>(storageKey);
            }
            catch
            {
                return default;
            }
        }

        public async Task SaveAsync<T>(string containerName, string key, T data) where T : class
        {
            var storageKey = $"{containerName}_{key}";
            await _dataStorage.SaveAsync(storageKey, data);
        }

        public async Task<bool> DeleteAsync(string containerName, string key)
        {
            var storageKey = $"{containerName}_{key}";
            return await _dataStorage.DeleteAsync(storageKey);
        }

        public async Task<bool> ExistsAsync(string containerName, string key)
        {
            var storageKey = $"{containerName}_{key}";
            return await _dataStorage.ExistsAsync(storageKey);
        }
    }

    public class GenericDataService<T> where T : class, IIdentifiable, new()
    {
        private readonly IGenericStorageManager _storageManager;
        private readonly ILogger? _logger;
        private readonly string _containerName;

        public GenericDataService(IGenericStorageManager storageManager, string containerName, ILogger? logger = null)
        {
            _storageManager = storageManager;
            _containerName = containerName;
            _logger = logger;
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            try
            {
                return await _storageManager.LoadAllAsync<T>(_containerName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"获取所有数据失败");
                return Enumerable.Empty<T>();
            }
        }

        public async Task<T?> GetByIdAsync(string id)
        {
            try
            {
                return await _storageManager.LoadAsync<T>(_containerName, id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"获取数据失败: {id}");
                return null;
            }
        }

        public async Task<bool> CreateAsync(T item)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    item.Id = Guid.NewGuid().ToString();
                }
                
                await _storageManager.SaveAsync(_containerName, item.Id, item);
                _logger?.LogInformation($"创建数据成功: {item.Id}");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"创建数据失败");
                return false;
            }
        }

        public async Task<bool> UpdateAsync(T item)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                    return false;

                var existing = await GetByIdAsync(item.Id);
                if (existing == null)
                    return false;

                await _storageManager.SaveAsync(_containerName, item.Id, item);
                _logger?.LogInformation($"更新数据成功: {item.Id}");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"更新数据失败: {item.Id}");
                return false;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var deleted = await _storageManager.DeleteAsync(_containerName, id);
                if (deleted)
                {
                    _logger?.LogInformation($"删除数据成功: {id}");
                }
                return deleted;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"删除数据失败: {id}");
                return false;
            }
        }

        public async Task<bool> ExistsAsync(string id)
        {
            try
            {
                return await _storageManager.ExistsAsync(_containerName, id);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"检查数据存在失败: {id}");
                return false;
            }
        }
    }

    public interface IIdentifiable
    {
        string Id { get; set; }
    }

    public class ResourceDefinition : IIdentifiable
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public Dictionary<string, object> Properties { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public ResourceMetadata Metadata { get; set; } = new();
    }

    public class ResourceMetadata
    {
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0";
        public Dictionary<string, object> CustomProperties { get; set; } = new();
    }

    public class ResourceOperationResult
    {
        public bool Success { get; set; }
        public string? ResourceId { get; set; }
        public string? ErrorMessage { get; set; }

        public static ResourceOperationResult CreateSuccess(string resourceId) => new()
        {
            Success = true,
            ResourceId = resourceId
        };

        public static ResourceOperationResult CreateFailure(string errorMessage) => new()
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }

    public class ResourceManagementService
    {
        private readonly GenericDataService<ResourceDefinition> _dataService;
        private readonly ILogger _logger;

        public ResourceManagementService(IGenericStorageManager storageManager, ILogger logger)
        {
            _dataService = new GenericDataService<ResourceDefinition>(storageManager, "Resource", logger);
            _logger = logger;
        }

        public async Task<ResourceOperationResult> CreateResourceAsync(ResourceDefinition resource)
        {
            try
            {
                resource.CreatedAt = DateTime.UtcNow;
                resource.UpdatedAt = DateTime.UtcNow;

                var success = await _dataService.CreateAsync(resource);
                if (success)
                {
                    _logger.LogInformation("创建资源成功: {ResourceId}", resource.Id);
                    return ResourceOperationResult.CreateSuccess(resource.Id);
                }
                return ResourceOperationResult.CreateFailure("创建资源失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建资源失败: {ResourceId}", resource.Id);
                return ResourceOperationResult.CreateFailure($"创建资源失败: {ex.Message}");
            }
        }

        public async Task<ResourceDefinition?> GetResourceAsync(string resourceId)
        {
            try
            {
                return await _dataService.GetByIdAsync(resourceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取资源失败: {ResourceId}", resourceId);
                return null;
            }
        }

        public async Task<IEnumerable<ResourceDefinition>> GetAllResourcesAsync()
        {
            try
            {
                var resources = await _dataService.GetAllAsync();
                _logger.LogInformation($"获取到 {resources.Count()} 个资源");
                return resources;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取资源列表失败");
                return Enumerable.Empty<ResourceDefinition>();
            }
        }

        public async Task<ResourceOperationResult> UpdateResourceAsync(ResourceDefinition resource)
        {
            try
            {
                resource.UpdatedAt = DateTime.UtcNow;

                var success = await _dataService.UpdateAsync(resource);
                if (success)
                {
                    _logger.LogInformation("更新资源成功: {ResourceId}", resource.Id);
                    return ResourceOperationResult.CreateSuccess(resource.Id);
                }
                return ResourceOperationResult.CreateFailure($"资源不存在: {resource.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新资源失败: {ResourceId}", resource.Id);
                return ResourceOperationResult.CreateFailure($"更新资源失败: {ex.Message}");
            }
        }

        public async Task<ResourceOperationResult> DeleteResourceAsync(string resourceId)
        {
            try
            {
                var deleted = await _dataService.DeleteAsync(resourceId);
                if (deleted)
                {
                    _logger.LogInformation("删除资源成功: {ResourceId}", resourceId);
                    return ResourceOperationResult.CreateSuccess(resourceId);
                }
                _logger.LogWarning("资源不存在: {ResourceId}", resourceId);
                return ResourceOperationResult.CreateFailure($"资源不存在: {resourceId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除资源失败: {ResourceId}", resourceId);
                return ResourceOperationResult.CreateFailure($"删除资源失败: {ex.Message}");
            }
        }

        public async Task<bool> ResourceExistsAsync(string resourceId)
        {
            try
            {
                return await _dataService.ExistsAsync(resourceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查资源存在失败: {ResourceId}", resourceId);
                return false;
            }
        }

        public async Task<IEnumerable<ResourceDefinition>> SearchResourcesAsync(string searchTerm)
        {
            var allResources = await GetAllResourcesAsync();
            return allResources.Where(r =>
                r.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                r.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                r.Type.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    public static class StorageHelper
    {
        public static long GetDataSize(object data)
        {
            if (data == null) return 0;
            try
            {
                string json = JsonSerializer.Serialize(data);
                return System.Text.Encoding.UTF8.GetByteCount(json);
            }
            catch
            {
                return 0;
            }
        }
    }
}