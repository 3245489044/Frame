using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions;
using DataManage.Services;

namespace DataManage
{
    public class StoragePathManager
    {
        private readonly string _basePath;
        private readonly Dictionary<string, string> _customPaths = new();

        public StoragePathManager(string basePath)
        {
            _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }
        }

        public string GetStoragePath(string storeName)
        {
            if (_customPaths.TryGetValue(storeName, out var customPath))
            {
                return customPath;
            }
            return Path.Combine(_basePath, $"{storeName}.db");
        }

        public void ConfigureCustomPath(string storageType, string customPath)
        {
            _customPaths[storageType] = customPath;
        }
    }

    public class StorageProvider : IAsyncDisposable
    {
        private readonly StoragePathManager _pathManager;
        private readonly IKeyValueStore _store;

        public StoragePathManager PathManager => _pathManager;
        public IKeyValueStore Store => _store;

        public StorageProvider(string basePath, StorageEngine engine, string storeName = "default")
        {
            _pathManager = new StoragePathManager(basePath);
            _store = StorageFactory.CreateKeyValueStore(engine, _pathManager.GetStoragePath(storeName));
        }

        public StorageProvider(StoragePathManager pathManager, StorageEngine engine, string storeName = "default")
        {
            _pathManager = pathManager ?? throw new ArgumentNullException(nameof(pathManager));
            _store = StorageFactory.CreateKeyValueStore(engine, pathManager.GetStoragePath(storeName));
        }

        public static StorageProvider CreateDefault(string storeName = "default", StorageEngine? engine = null)
        {
            var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage");
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }
            return new StorageProvider(basePath, engine ?? StorageEngine.SQLite, storeName);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _store.InitializeAsync(cancellationToken);
        }

        public void ConfigureCustomPath(string storageType, string customPath)
        {
            _pathManager.ConfigureCustomPath(storageType, customPath);
        }

        public async Task SaveAsync(string container, IDictionary<string, object> data, CancellationToken cancellationToken = default)
        {
            await _store.SaveAsync(container, data, cancellationToken);
        }

        public async Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList, CancellationToken cancellationToken = default)
        {
            await _store.SaveBatchAsync(container, dataList, cancellationToken);
        }

        public async Task<IEnumerable<IDictionary<string, object>>> LoadAllAsync(string container, CancellationToken cancellationToken = default)
        {
            return await _store.LoadAllAsync(container, cancellationToken);
        }

        public async Task<int> DeleteAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            return await _store.DeleteAsync(container, keyField, keyValue, cancellationToken);
        }

        public async Task<bool> ExistsAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            return await _store.ExistsAsync(container, keyField, keyValue, cancellationToken);
        }

        public void Dispose()
        {
            _store.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await _store.DisposeAsync();
        }
    }

    public static class StorageProviderExtensions
    {
        public static StorageProvider CreateSQLite(string basePath, string storeName = "default")
        {
            return new StorageProvider(basePath, StorageEngine.SQLite, storeName);
        }

        public static StorageProvider CreateLiteDB(string basePath, string storeName = "default")
        {
            return new StorageProvider(basePath, StorageEngine.LiteDB, storeName);
        }

        public static StorageProvider CreateFile(string basePath, string storeName = "default")
        {
            return new StorageProvider(basePath, StorageEngine.FileSystem, storeName);
        }
    }
}