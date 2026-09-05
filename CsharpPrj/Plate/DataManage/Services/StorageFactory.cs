#nullable enable

using System;
using DataManage.Abstractions;
using DataManage.Data;
using DataManage.Data.Graph;
using DataManage.Storage;
using DataManage.Foundational.Adapters;
using DataManage.PathManager;
using Microsoft.Extensions.DependencyInjection;

namespace DataManage.Services
{
    public static class StorageConstants
    {
        public static class StorageTypes
        {
            public const string DataMethodSQLite = "DataMethodSQLite";
            public const string DataMethodLiteDB = "DataMethodLiteDB";
            public const string FrameManageSQLite = "FrameManageSQLite";
        }
    }

    public enum StorageEngine
    {
        SQLite,
        LiteDB,
        FileSystem
    }

    public static class StorageFactory
    {
        public static Abstractions.IKeyValueStore CreateKeyValueStore(StorageEngine engine, string? storagePath = null)
        {
            storagePath ??= GetDefaultStoragePath(engine);

            return engine switch
            {
                StorageEngine.SQLite => CreateSQLiteKeyValueStore(storagePath),
                StorageEngine.LiteDB => CreateLiteDBKeyValueStore(storagePath),
                StorageEngine.FileSystem => CreateFileKeyValueStore(storagePath),
                _ => throw new ArgumentOutOfRangeException(nameof(engine))
            };
        }

        public static IGraphStorage CreateGraphStorage(StorageEngine engine = StorageEngine.SQLite, string? storagePath = null)
        {
            var baseStorage = CreateKeyValueStore(engine, storagePath);
            var cache = Data.Graph.GraphStorageCacheFactory.Create();
            return Data.Graph.CachedGraphStorageFactory.Create(new GraphStorageWrapper(baseStorage), cache);
        }

        public static IGraphIndexManager CreateGraphIndexManager(Func<string, string, System.Threading.CancellationToken, System.Threading.Tasks.Task<System.Collections.Generic.List<UniversalWorkflowFramework.Core.Models.Resource>>>? getResourcesByTypeAsync = null)
        {
            return GraphIndexManagerFactory.Create(getResourcesByTypeAsync);
        }

        public static IGraphTransactionManager CreateGraphTransactionManager(IGraphStorage storage)
        {
            return GraphTransactionManagerFactory.Create(storage);
        }

        public static Abstractions.IGraphStorageCache CreateGraphStorageCache(TimeSpan? expiration = null)
        {
            return Data.Graph.GraphStorageCacheFactory.Create(expiration);
        }

        public static UnifiedStorageManager CreateUnifiedStorageManager(StorageEngine engine = StorageEngine.SQLite, string? storagePath = null)
        {
            storagePath ??= GetDefaultStoragePath(engine);
            Abstractions.IKeyValueStore kvStore = engine switch
            {
                StorageEngine.SQLite => CreateSQLiteKeyValueStore(storagePath),
                StorageEngine.LiteDB => CreateLiteDBKeyValueStore(storagePath),
                StorageEngine.FileSystem => CreateFileKeyValueStore(storagePath),
                _ => throw new ArgumentOutOfRangeException(nameof(engine))
            };
            var dataStorage = new DataManage.Foundational.Adapters.KeyValueStoreToDataStorageAdapter(kvStore);
            var workflowStorage = new DataManage.Foundational.Adapters.KeyValueStoreToWorkflowStorageAdapter<string>(kvStore);
            var graphStorage = CreateGraphStorage(engine, storagePath);

            return new UnifiedStorageManager(dataStorage, workflowStorage, graphStorage);
        }

        public static UnifiedStorageManager CreateDefault()
        {
            return CreateUnifiedStorageManager();
        }

        public static UnifiedStorageManager CreateFrameManage()
        {
            return CreateUnifiedStorageManager(StorageEngine.SQLite);
        }

        private static Storage.SQLiteKeyValueStore CreateSQLiteKeyValueStore(string storagePath)
        {
            var store = new Storage.SQLiteKeyValueStore("SQLiteStore");
            store.SetConnectionString(storagePath);
            return store;
        }

        private static Storage.LiteDBKeyValueStore CreateLiteDBKeyValueStore(string storagePath)
        {
            var store = new Storage.LiteDBKeyValueStore("LiteDBStore");
            store.SetConnectionString(storagePath);
            return store;
        }

        private static Storage.FileKeyValueStore CreateFileKeyValueStore(string storagePath)
        {
            var store = new Storage.FileKeyValueStore("FileStore");
            store.SetBaseDirectory(storagePath);
            return store;
        }

        private static string GetDefaultStoragePath(StorageEngine engine)
        {
            return StoragePathManagerSingleton.Instance.GetDefaultStoragePath(engine.ToString());
        }
    }

    internal class GraphStorageWrapper : Abstractions.IGraphStorage
    {
        private readonly Abstractions.IKeyValueStore _store;

        public GraphStorageWrapper(Abstractions.IKeyValueStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public System.Threading.Tasks.Task InitializeAsync()
        {
            return _store.InitializeAsync(System.Threading.CancellationToken.None);
        }

        public System.Threading.Tasks.Task StoreResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            System.Collections.Generic.Dictionary<string, object> metadata,
            string? symbol = null)
        {
            var data = new System.Collections.Generic.Dictionary<string, object>(metadata);
            data["ResourceId"] = $"{resourceId.ResourceType}:{resourceId.Id}";
            data["Symbol"] = symbol ?? string.Empty;
            return _store.SaveAsync("Resources", data, System.Threading.CancellationToken.None);
        }

        public async System.Threading.Tasks.Task<UniversalWorkflowFramework.Core.Models.Resource?> GetResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            var allData = await _store.LoadAllAsync("Resources");
            var key = $"{resourceId.ResourceType}:{resourceId.Id}";
            var data = allData.FirstOrDefault(d => d["ResourceId"]?.ToString() == key);
            
            if (data == null)
                return null;
                
            var metadata = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var kvp in data)
            {
                if (kvp.Key != "Id" && kvp.Key != "ResourceId" && kvp.Key != "Symbol")
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
            
            return new UniversalWorkflowFramework.Core.Models.Resource
            {
                Identifier = resourceId,
                Metadata = metadata,
                Symbol = data.TryGetValue("Symbol", out var symbol) ? symbol?.ToString() : null
            };
        }

        public System.Threading.Tasks.Task DeleteResourceAsync(UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            return _store.DeleteAsync("Resources", "ResourceId", $"{resourceId.ResourceType}:{resourceId.Id}", System.Threading.CancellationToken.None);
        }

        public System.Threading.Tasks.Task<System.Collections.Generic.List<UniversalWorkflowFramework.Core.Models.Resource>> GetResourcesByTypeAsync(string resourceType)
        {
            return System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.List<UniversalWorkflowFramework.Core.Models.Resource>());
        }

        public System.Threading.Tasks.Task<System.Collections.Generic.List<UniversalWorkflowFramework.Core.Models.Resource>> QueryResourcesAsync(
            string resourceType,
            System.Collections.Generic.Dictionary<string, object>? filters = null,
            int? limit = null,
            int? offset = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.List<UniversalWorkflowFramework.Core.Models.Resource>());
        }

        public System.Threading.Tasks.Task<System.Collections.Generic.List<UniversalWorkflowFramework.Core.Models.Resource>> SearchResourcesAsync(
            string keyword,
            System.Collections.Generic.List<string>? searchFields = null,
            int maxResults = 100,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.List<UniversalWorkflowFramework.Core.Models.Resource>());
        }

        public async System.Threading.Tasks.Task<long> CountResourcesAsync(
            string resourceType,
            System.Collections.Generic.Dictionary<string, object>? filters = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var allData = await _store.LoadAllAsync("Resources");
            var count = allData.Count(d => 
            {
                var id = d["ResourceId"]?.ToString();
                return id != null && id.StartsWith(resourceType + ":");
            });
            return count;
        }

        public System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, long>> CountByTypeGroupAsync(
            System.Threading.CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.Dictionary<string, long>());
        }

        public async System.Threading.Tasks.Task BulkStoreResourcesAsync(
            System.Collections.Generic.List<(UniversalWorkflowFramework.Core.Models.ResourceIdentifier ResourceId,
                   System.Collections.Generic.Dictionary<string, object> Metadata,
                   string? Symbol)> resources,
            System.Threading.CancellationToken cancellationToken = default)
        {
            foreach (var (resourceId, metadata, symbol) in resources)
            {
                await StoreResourceAsync(resourceId, metadata, symbol);
            }
        }

        public System.Threading.Tasks.Task BulkDeleteResourcesAsync(
            System.Collections.Generic.List<UniversalWorkflowFramework.Core.Models.ResourceIdentifier> resourceIds,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task<bool> ExistsAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return _store.ExistsAsync("Resources", "ResourceId", $"{resourceId.ResourceType}:{resourceId.Id}", cancellationToken);
        }

        public System.Threading.Tasks.Task UpdateResourceAsync(
            UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            System.Collections.Generic.Dictionary<string, object> metadata,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return StoreResourceAsync(resourceId, metadata);
        }
    }

    public static class StorageServiceRegistry
    {
        public static IServiceCollection AddDataManageStorage(this IServiceCollection services, string storageType = "DataMethodSQLite", string? storagePath = null)
        {
            services.AddSingleton<IKeyValueStore>(_ => 
            {
                var engine = storageType switch
                {
                    "DataMethodSQLite" => StorageEngine.SQLite,
                    "DataMethodLiteDB" => StorageEngine.LiteDB,
                    _ => StorageEngine.SQLite
                };
                return StorageFactory.CreateKeyValueStore(engine, storagePath);
            });

            services.AddSingleton<IGraphStorage>(_ => StorageFactory.CreateGraphStorage());
            services.AddSingleton<IGraphIndexManager>(_ => StorageFactory.CreateGraphIndexManager());
            services.AddSingleton<IGraphTransactionManager>(sp => 
                StorageFactory.CreateGraphTransactionManager(sp.GetRequiredService<IGraphStorage>()));
            services.AddSingleton<IStorageLogger, ConsoleStorageLogger>();
            services.AddSingleton<IStorageExceptionHandler, StorageExceptionHandler>();

            return services;
        }

        public static IServiceCollection AddDataMethodSQLiteStorage(this IServiceCollection services, string? storagePath = null)
        {
            return services.AddDataManageStorage(StorageConstants.StorageTypes.DataMethodSQLite, storagePath);
        }

        public static IServiceCollection AddDataMethodLiteDBStorage(this IServiceCollection services, string? storagePath = null)
        {
            return services.AddDataManageStorage(StorageConstants.StorageTypes.DataMethodLiteDB, storagePath);
        }

        public static IServiceCollection AddDefaultDataStorage(this IServiceCollection services)
        {
            return services.AddDataMethodSQLiteStorage();
        }
    }
}