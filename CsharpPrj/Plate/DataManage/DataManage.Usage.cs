#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DataManage.Usage
{
    public class DataStorageManager
    {
        private readonly string _storagePath;
        private readonly bool _enableLogging;
        private global::DataManage.UnifiedStorageManager? _unifiedManager;
        private global::DataManage.Abstractions.IGraphStorage? _graphStorage;

        public DataStorageManager(string storagePath, bool enableLogging = false)
        {
            _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
            _enableLogging = enableLogging;
        }

        #region 1. 存储引擎创建

        public async Task<global::DataManage.Abstractions.IKeyValueStore> CreateKeyValueStore(
            global::DataManage.Services.StorageEngine engine, 
            string? customPath = null)
        {
            var effectivePath = customPath ?? _storagePath;
            var store = global::DataManage.Services.StorageFactory.CreateKeyValueStore(engine, effectivePath);
            await store.InitializeAsync();
            Log($"存储引擎 {engine} 创建成功");
            return store;
        }

        public async Task<global::DataManage.Abstractions.IKeyValueStore> CreateSQLiteStore(string? customPath = null)
        {
            return await CreateKeyValueStore(global::DataManage.Services.StorageEngine.SQLite, customPath);
        }

        public async Task<global::DataManage.Abstractions.IKeyValueStore> CreateLiteDBStore(string? customPath = null)
        {
            return await CreateKeyValueStore(global::DataManage.Services.StorageEngine.LiteDB, customPath);
        }

        public async Task<global::DataManage.Abstractions.IKeyValueStore> CreateFileSystemStore(string? customPath = null)
        {
            return await CreateKeyValueStore(global::DataManage.Services.StorageEngine.FileSystem, customPath);
        }

        #endregion

        #region 2. 统一存储管理器

        public async Task<global::DataManage.UnifiedStorageManager> GetOrCreateUnifiedManager(
            global::DataManage.Services.StorageEngine? engine = null)
        {
            if (_unifiedManager != null)
                return _unifiedManager;

            _unifiedManager = engine.HasValue
                ? global::DataManage.UnifiedStorageManagerFactory.CreateWithEngine(engine.Value, _storagePath)
                : global::DataManage.UnifiedStorageManagerFactory.CreateDefault();

            await _unifiedManager.InitializeAllAsync();
            Log("统一存储管理器创建成功");
            return _unifiedManager;
        }

        public async Task<T?> LoadAsync<T>(string key)
        {
            var manager = await GetOrCreateUnifiedManager();
            return await manager.LoadAsync<T>(key);
        }

        public async Task SaveAsync<T>(string key, T data)
        {
            var manager = await GetOrCreateUnifiedManager();
            await manager.SaveAsync(key, data);
            Log($"数据保存成功: {key}");
        }

        public async Task<bool> ExistsAsync(string key)
        {
            var manager = await GetOrCreateUnifiedManager();
            return await manager.ExistsAsync(key);
        }

        public async Task<bool> DeleteAsync(string key)
        {
            var manager = await GetOrCreateUnifiedManager();
            var result = await manager.DeleteAsync(key);
            Log($"数据删除: {key}, 结果: {result}");
            return result;
        }

        public async Task<IEnumerable<string>> GetKeysAsync()
        {
            var manager = await GetOrCreateUnifiedManager();
            return await manager.GetKeysAsync();
        }

        #endregion

        #region 3. 工作流存储

        public async Task SaveWorkflowAsync(string workflowId, string workflowData)
        {
            var manager = await GetOrCreateUnifiedManager();
            await manager.SaveWorkflowAsync(workflowId, workflowData);
            Log($"工作流保存成功: {workflowId}");
        }

        public async Task<string?> LoadWorkflowAsync(string workflowId)
        {
            var manager = await GetOrCreateUnifiedManager();
            return await manager.LoadWorkflowAsync(workflowId);
        }

        public async Task<bool> WorkflowExistsAsync(string workflowId)
        {
            var manager = await GetOrCreateUnifiedManager();
            return await manager.WorkflowExistsAsync(workflowId);
        }

        public async Task DeleteWorkflowAsync(string workflowId)
        {
            var manager = await GetOrCreateUnifiedManager();
            await manager.DeleteWorkflowAsync(workflowId);
            Log($"工作流删除成功: {workflowId}");
        }

        #endregion

        #region 4. 图存储操作

        public async Task<global::DataManage.Abstractions.IGraphStorage> GetOrCreateGraphStorage(
            global::DataManage.Services.StorageEngine engine = global::DataManage.Services.StorageEngine.SQLite)
        {
            if (_graphStorage != null)
                return _graphStorage;

            _graphStorage = global::DataManage.Services.StorageFactory.CreateGraphStorage(engine, _storagePath);
            await _graphStorage.InitializeAsync();
            Log("图存储创建成功");
            return _graphStorage;
        }

        public async Task StoreResourceAsync(
            ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            string? externalId = null)
        {
            var storage = await GetOrCreateGraphStorage();
            await storage.StoreResourceAsync(resourceId, metadata, externalId);
            Log($"资源存储成功: {resourceId.ResourceType}/{resourceId.Id}");
        }

        public async Task<UniversalWorkflowFramework.Core.Models.Resource?> GetResourceAsync(ResourceIdentifier resourceId)
        {
            var storage = await GetOrCreateGraphStorage();
            return await storage.GetResourceAsync(resourceId);
        }

        public async Task<bool> ResourceExistsAsync(ResourceIdentifier resourceId, CancellationToken cancellationToken = default)
        {
            var storage = await GetOrCreateGraphStorage();
            return await storage.ExistsAsync(resourceId, cancellationToken);
        }

        public async Task UpdateResourceAsync(
            ResourceIdentifier resourceId,
            Dictionary<string, object> metadata,
            CancellationToken cancellationToken = default)
        {
            var storage = await GetOrCreateGraphStorage();
            await storage.UpdateResourceAsync(resourceId, metadata, cancellationToken);
            Log($"资源更新成功: {resourceId.ResourceType}/{resourceId.Id}");
        }

        public async Task DeleteResourceAsync(ResourceIdentifier resourceId)
        {
            var storage = await GetOrCreateGraphStorage();
            await storage.DeleteResourceAsync(resourceId);
            Log($"资源删除成功: {resourceId.ResourceType}/{resourceId.Id}");
        }

        public async Task<List<UniversalWorkflowFramework.Core.Models.Resource>> GetResourcesByTypeAsync(string resourceType)
        {
            var storage = await GetOrCreateGraphStorage();
            return await storage.GetResourcesByTypeAsync(resourceType);
        }

        public async Task<List<UniversalWorkflowFramework.Core.Models.Resource>> SearchResourcesAsync(
            string keyword,
            List<string>? searchFields = null,
            int maxResults = 100,
            CancellationToken cancellationToken = default)
        {
            var storage = await GetOrCreateGraphStorage();
            return await storage.SearchResourcesAsync(keyword, searchFields, maxResults, cancellationToken);
        }

        public async Task<long> CountResourcesAsync(
            string resourceType,
            Dictionary<string, object>? filters = null,
            CancellationToken cancellationToken = default)
        {
            var storage = await GetOrCreateGraphStorage();
            return await storage.CountResourcesAsync(resourceType, filters, cancellationToken);
        }

        public async Task<Dictionary<string, long>> CountByTypeGroupAsync(CancellationToken cancellationToken = default)
        {
            var storage = await GetOrCreateGraphStorage();
            return await storage.CountByTypeGroupAsync(cancellationToken);
        }

        public async Task BulkStoreResourcesAsync(
            List<(ResourceIdentifier ResourceId, Dictionary<string, object> Metadata, string? Symbol)> resources,
            CancellationToken cancellationToken = default)
        {
            var storage = await GetOrCreateGraphStorage();
            await storage.BulkStoreResourcesAsync(resources, cancellationToken);
            Log($"批量存储完成");
        }

        public async Task BulkDeleteResourcesAsync(
            List<ResourceIdentifier> resourceIds,
            CancellationToken cancellationToken = default)
        {
            var storage = await GetOrCreateGraphStorage();
            await storage.BulkDeleteResourcesAsync(resourceIds, cancellationToken);
            Log($"批量删除完成");
        }

        #endregion

        #region 5. 事务管理

        public async Task ExecuteGraphTransactionAsync(
            Func<global::DataManage.Abstractions.IGraphTransaction, Task> action,
            CancellationToken cancellationToken = default)
        {
            var storage = await GetOrCreateGraphStorage();
            var transactionManager = global::DataManage.Services.StorageFactory.CreateGraphTransactionManager(storage);

            await using var transaction = await transactionManager.BeginTransactionAsync(cancellationToken);
            try
            {
                await action(transaction);
                await transaction.CommitAsync(cancellationToken);
                Log("事务提交成功");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                Log($"事务回滚: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 6. 缓存机制

        public async Task<global::DataManage.Abstractions.IGraphStorage> CreateCachedGraphStorage(
            TimeSpan? cacheDuration = null,
            global::DataManage.Services.StorageEngine engine = global::DataManage.Services.StorageEngine.SQLite)
        {
            var effectiveDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
            var cache = global::DataManage.Services.StorageFactory.CreateGraphStorageCache(effectiveDuration);
            var baseStorage = global::DataManage.Services.StorageFactory.CreateGraphStorage(engine, _storagePath);
            var cachedStorage = global::DataManage.Data.Graph.CachedGraphStorageFactory.Create(baseStorage, cache, effectiveDuration);

            await cachedStorage.InitializeAsync();
            Log($"带缓存的图存储创建成功，缓存时长: {effectiveDuration}");
            return cachedStorage;
        }

        public static global::DataManage.Abstractions.Models.CacheStatistics? GetCacheStatistics(global::DataManage.Abstractions.IGraphStorage storage)
        {
            if (storage is global::DataManage.Data.Graph.CachedGraphStorage cached)
            {
                return cached.GetCacheStatistics();
            }
            return null;
        }

        #endregion

        #region 7. 依赖注入配置

        public static void ConfigureServices(
            IServiceCollection services,
            string storagePath,
            global::DataManage.Services.StorageEngine defaultEngine = global::DataManage.Services.StorageEngine.SQLite)
        {
            global::DataManage.Services.StorageServiceRegistry.AddDefaultDataStorage(services);
            global::DataManage.Services.StorageServiceRegistry.AddDataManageStorage(services, 
                global::DataManage.Services.StorageConstants.StorageTypes.DataMethodSQLite);
            global::DataManage.Services.StorageServiceRegistry.AddDataMethodLiteDBStorage(services);
            global::DataManage.Services.StorageServiceRegistry.AddDataMethodSQLiteStorage(services, storagePath);

            services.AddSingleton<DataStorageManager>(sp => new DataStorageManager(storagePath));
        }

        #endregion

        #region 8. 辅助方法

        private void Log(string message)
        {
            if (_enableLogging)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
        }

        #endregion
    }

    public static class StorageKeyConventions
    {
        public static string UserKey(string userId) => $"user:{userId}";
        public static string WorkflowKey(string workflowId) => $"workflow:{workflowId}";
        public static string ConfigKey(string section) => $"config:{section}";
        public static string CacheKey(string type, string id) => $"cache:{type}:{id}";
        public static string ResourceKey(string resourceType, string id) => $"{resourceType}:{id}";
    }

    public static class DataStorageExtensions
    {
        public static async Task<T?> SafeLoadAsync<T>(this DataStorageManager manager, string key)
        {
            try
            {
                return await manager.LoadAsync<T>(key);
            }
            catch (global::DataManage.Exceptions.StorageException)
            {
                return default;
            }
        }

        public static async Task<bool> SafeSaveAsync<T>(this DataStorageManager manager, string key, T data)
        {
            try
            {
                await manager.SaveAsync(key, data);
                return true;
            }
            catch (global::DataManage.Exceptions.StorageException)
            {
                return false;
            }
        }
    }
}
