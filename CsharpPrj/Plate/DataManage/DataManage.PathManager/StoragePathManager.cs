using System;
using System.Collections.Generic;
using System.IO;

namespace DataManage.PathManager
{
    public interface IStoragePathManager
    {
        string BasePath { get; }

        void ConfigureCustomPath(string storageType, string customPath);
        string GetSQLitePath(string databaseName, string storageType = "SQLite");
        string GetLiteDBPath(string databaseName, string storageType = "LiteDB");
        string GetFileStoragePath(string containerName, string storageType = "File");
        string GetConfigurationPath(string configFileName);
        string GetLogPath(string logFileName);
        bool ValidatePath(string path);
        bool CleanStorageType(string storageType);
        string GetDefaultStoragePath(string engineType);
    }

    public class StoragePathManager : IStoragePathManager
    {
        private readonly string _basePath;
        private readonly Dictionary<string, string> _customPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string BasePath => _basePath;

        public StoragePathManager(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                throw new ArgumentNullException(nameof(basePath), "基础路径不能为空");

            _basePath = Path.GetFullPath(basePath);
            EnsureDirectoryExists(_basePath);
        }

        public void ConfigureCustomPath(string storageType, string customPath)
        {
            if (string.IsNullOrWhiteSpace(storageType))
                throw new ArgumentNullException(nameof(storageType));

            if (string.IsNullOrWhiteSpace(customPath))
                throw new ArgumentNullException(nameof(customPath));

            var fullPath = Path.IsPathRooted(customPath)
                ? customPath
                : Path.Combine(_basePath, customPath);

            EnsureDirectoryExists(fullPath);
            _customPaths[storageType] = fullPath;
        }

        public string GetSQLitePath(string databaseName, string storageType = "SQLite")
        {
            if (string.IsNullOrWhiteSpace(databaseName))
                throw new ArgumentNullException(nameof(databaseName));

            var baseDir = GetBaseDirectoryForType(storageType);
            return Path.Combine(baseDir, $"{databaseName}{StoragePathConstants.SQLiteExtension}");
        }

        public string GetLiteDBPath(string databaseName, string storageType = "LiteDB")
        {
            if (string.IsNullOrWhiteSpace(databaseName))
                throw new ArgumentNullException(nameof(databaseName));

            var baseDir = GetBaseDirectoryForType(storageType);
            return Path.Combine(baseDir, $"{databaseName}{StoragePathConstants.LiteDBExtension}");
        }

        public string GetFileStoragePath(string containerName, string storageType = "File")
        {
            if (string.IsNullOrWhiteSpace(containerName))
                throw new ArgumentNullException(nameof(containerName));

            var baseDir = GetBaseDirectoryForType(storageType);
            var containerDir = Path.Combine(baseDir, containerName);
            EnsureDirectoryExists(containerDir);
            return containerDir;
        }

        public string GetConfigurationPath(string configFileName)
        {
            if (string.IsNullOrWhiteSpace(configFileName))
                throw new ArgumentNullException(nameof(configFileName));

            var configDir = Path.Combine(_basePath, StoragePathConstants.ConfigurationDirectory);
            EnsureDirectoryExists(configDir);
            return Path.Combine(configDir, configFileName);
        }

        public string GetLogPath(string logFileName)
        {
            if (string.IsNullOrWhiteSpace(logFileName))
                throw new ArgumentNullException(nameof(logFileName));

            var logDir = Path.Combine(_basePath, StoragePathConstants.LogDirectory);
            EnsureDirectoryExists(logDir);
            return Path.Combine(logDir, logFileName);
        }

        public string GetDefaultStoragePath(string engineType)
        {
            var baseDataPath = Path.Combine(_basePath, StoragePathConstants.DataDirectory);
            EnsureDirectoryExists(baseDataPath);

            return engineType.ToLowerInvariant() switch
            {
                "sqlite" => Path.Combine(baseDataPath, $"default{StoragePathConstants.SQLiteExtension}"),
                "litedb" => Path.Combine(baseDataPath, $"default{StoragePathConstants.LiteDBExtension}"),
                "filesystem" => baseDataPath,
                _ => baseDataPath
            };
        }

        private string GetBaseDirectoryForType(string storageType)
        {
            if (_customPaths.TryGetValue(storageType, out var customPath))
                return customPath;

            var defaultPath = Path.Combine(_basePath, storageType);
            EnsureDirectoryExists(defaultPath);
            return defaultPath;
        }

        private static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
        }

        public bool ValidatePath(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                return Directory.Exists(fullPath) || File.Exists(fullPath);
            }
            catch
            {
                return false;
            }
        }

        public bool CleanStorageType(string storageType)
        {
            try
            {
                var baseDir = GetBaseDirectoryForType(storageType);
                if (Directory.Exists(baseDir))
                {
                    Directory.Delete(baseDir, true);
                    Directory.CreateDirectory(baseDir);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public static class StoragePathManagerSingleton
    {
        private static StoragePathManager? _instance;
        private static readonly object _lock = new object();

        public static StoragePathManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage");
                            _instance = new StoragePathManager(defaultPath);
                        }
                    }
                }
                return _instance;
            }
            set
            {
                lock (_lock)
                {
                    _instance = value;
                }
            }
        }

        public static void Initialize(string basePath)
        {
            Instance = new StoragePathManager(basePath);
        }
    }
}
