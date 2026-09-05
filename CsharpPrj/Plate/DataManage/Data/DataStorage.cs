#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataManage.Abstractions;

namespace DataManage.Data
{
    public abstract class DataStorageBase : IDataStorage
    {
        protected readonly string _containerName;
        protected readonly JsonSerializerOptions _jsonOptions;

        protected DataStorageBase(string containerName)
        {
            _containerName = containerName ?? throw new ArgumentNullException(nameof(containerName));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        }

        public abstract Task InitializeAsync();

        protected abstract Task SaveToStoreAsync(string container, IDictionary<string, object> data);
        protected abstract Task<IEnumerable<IDictionary<string, object>>> LoadAllFromStoreAsync(string container);
        protected abstract Task<int> DeleteFromStoreAsync(string container, string keyField, object keyValue);
        protected abstract Task<bool> ExistsInStoreAsync(string container, string keyField, object keyValue);

        public async Task SaveAsync<T>(string key, T data)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var dict = ObjectToDictionary(data);
            dict["_key"] = key;
            dict["_updatedAt"] = DateTime.UtcNow.ToString("O");

            await SaveToStoreAsync(_containerName, dict);
        }

        public async Task<T?> LoadAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var allData = await LoadAllFromStoreAsync(_containerName);
            var row = allData.FirstOrDefault(d => d["_key"]?.ToString() == key);
            return row != null ? DictionaryToObject<T>(row) : default;
        }

        public async Task<bool> DeleteAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var result = await DeleteFromStoreAsync(_containerName, "_key", key);
            return result > 0;
        }

        public async Task<bool> ExistsAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            return await ExistsInStoreAsync(_containerName, "_key", key);
        }

        public async Task<IEnumerable<string>> GetKeysAsync()
        {
            var allData = await LoadAllFromStoreAsync(_containerName);
            return allData
                .Select(d => d["_key"]?.ToString())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
        }

        public async Task ClearAsync()
        {
            var allData = await LoadAllFromStoreAsync(_containerName);
            foreach (var row in allData)
            {
                var key = row["_key"]?.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    await DeleteFromStoreAsync(_containerName, "_key", key);
                }
            }
        }

        public async Task StoreWorkflowDefaultParametersAsync(string workflowId, Dictionary<string, object> parameters)
        {
            var key = $"workflow_params_{workflowId}";
            await SaveAsync(key, parameters);
        }

        public async Task<Dictionary<string, object>> RetrieveWorkflowDefaultParametersAsync(string workflowId)
        {
            var key = $"workflow_params_{workflowId}";
            return await LoadAsync<Dictionary<string, object>>(key) ?? new Dictionary<string, object>();
        }

        public async Task DeleteWorkflowDefaultParametersAsync(string workflowId)
        {
            var key = $"workflow_params_{workflowId}";
            await DeleteAsync(key);
        }

        public async Task<bool> ExistsWorkflowDefaultParametersAsync(string workflowId)
        {
            var key = $"workflow_params_{workflowId}";
            return await ExistsAsync(key);
        }

        protected Dictionary<string, object> ObjectToDictionary<T>(T obj)
        {
            if (obj == null)
                return new Dictionary<string, object>();

            var json = JsonSerializer.Serialize(obj, _jsonOptions);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
        }

        protected T DictionaryToObject<T>(IDictionary<string, object> dict)
        {
            var cleanedDict = new Dictionary<string, object>(dict);
            cleanedDict.Remove("_key");
            cleanedDict.Remove("_updatedAt");

            var json = JsonSerializer.Serialize(cleanedDict, _jsonOptions);
            return JsonSerializer.Deserialize<T>(json)!;
        }
    }
}