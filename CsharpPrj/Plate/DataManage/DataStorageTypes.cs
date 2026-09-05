#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FrameManage.DataStorage
{
    public interface IConfigurationLoader
    {
        Task<T?> LoadConfigurationAsync<T>(string configPath);
        Task SaveConfigurationAsync<T>(string configPath, T configuration);
        Task<IEnumerable<T>> LoadConfigurationsFromDirectoryAsync<T>(string directoryPath);
    }

    public interface IDataStorage
    {
        Task InitializeAsync();
        Task SaveAsync<T>(string key, T data);
        Task<T?> LoadAsync<T>(string key);
        Task<bool> DeleteAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task<IEnumerable<string>> GetKeysAsync();
        Task ClearAsync();

        Task StoreWorkflowDefaultParametersAsync(string workflowId, Dictionary<string, object> parameters);
        Task<Dictionary<string, object>> RetrieveWorkflowDefaultParametersAsync(string workflowId);
        Task DeleteWorkflowDefaultParametersAsync(string workflowId);
        Task<bool> ExistsWorkflowDefaultParametersAsync(string workflowId);
    }

    public interface IGraphStorage
    {
        Task InitializeAsync();
        Task SaveGraphAsync(string graphId, string graphData);
        Task<string?> LoadGraphAsync(string graphId);
        Task<bool> DeleteGraphAsync(string graphId);
        Task<bool> ExistsGraphAsync(string graphId);
        Task<IEnumerable<string>> GetAllGraphIdsAsync();
        Task ClearAllGraphsAsync();
    }

    public interface IWorkflowStorage
    {
        Task InitializeAsync();
        Task SaveWorkflowAsync(string workflowId, string workflowJson);
        Task<string?> LoadWorkflowAsync(string workflowId);
        Task<bool> DeleteWorkflowAsync(string workflowId);
        Task<bool> ExistsWorkflowAsync(string workflowId);
        Task<IEnumerable<string>> GetAllWorkflowIdsAsync();
        Task ClearAllWorkflowsAsync();
    }

    public class PathManager
    {
        private static readonly Lazy<PathManager> _instance = new Lazy<PathManager>(() => new PathManager());

        public static PathManager Instance => _instance.Value;

        private readonly string _applicationDataPath;
        private readonly string _storagePath;
        private readonly string _databasePath;
        private readonly string _configurationPath;

        private PathManager()
        {
            _applicationDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FrameManage"
            );

            _storagePath = System.IO.Path.Combine(_applicationDataPath, "Storage");
            _databasePath = System.IO.Path.Combine(_storagePath, "DataBase");
            _configurationPath = System.IO.Path.Combine(_storagePath, "Configuration");

            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            if (!System.IO.Directory.Exists(_applicationDataPath))
                System.IO.Directory.CreateDirectory(_applicationDataPath);

            if (!System.IO.Directory.Exists(_storagePath))
                System.IO.Directory.CreateDirectory(_storagePath);

            if (!System.IO.Directory.Exists(_databasePath))
                System.IO.Directory.CreateDirectory(_databasePath);

            if (!System.IO.Directory.Exists(_configurationPath))
                System.IO.Directory.CreateDirectory(_configurationPath);
        }

        public string GetApplicationDataDirectory() => _applicationDataPath;

        public string GetStorageDirectory() => _storagePath;

        public string GetDatabaseDirectory() => _databasePath;

        public string GetConfigurationDirectory() => _configurationPath;

        public string GetDatabasePath(string databaseFileName) =>
            System.IO.Path.Combine(_databasePath, databaseFileName);

        public string GetConfigurationPath(string fileName) =>
            System.IO.Path.Combine(_configurationPath, fileName);

        public bool ValidatePath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                var fullPath = System.IO.Path.GetFullPath(path);
                return System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(fullPath));
            }
            catch
            {
                return false;
            }
        }

        public void EnsureDirectory(string path)
        {
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
        }
    }

    public class StorageException : Exception
    {
        public StorageException(string message) : base(message) { }

        public StorageException(string message, Exception innerException) : base(message, innerException) { }

        public static StorageException NotFound(string key) =>
            new StorageException($"Storage item not found: {key}");

        public static StorageException InvalidKey() =>
            new StorageException("Invalid storage key");

        public static StorageException ConnectionFailed(string message) =>
            new StorageException($"Storage connection failed: {message}");
    }
}
