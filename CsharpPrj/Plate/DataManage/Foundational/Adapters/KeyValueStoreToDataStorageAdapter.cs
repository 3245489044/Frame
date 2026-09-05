#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataManage.Abstractions;

namespace DataManage.Foundational.Adapters
{
    public class KeyValueStoreToDataStorageAdapter : Abstractions.IDataStorage
    {
        private readonly Abstractions.IKeyValueStore _keyValueStore;
        private readonly string _containerName;
        private readonly JsonSerializerOptions _jsonOptions;

        public KeyValueStoreToDataStorageAdapter(Abstractions.IKeyValueStore keyValueStore, string containerName = "DataStorage")
        {
            _keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
            _containerName = containerName ?? throw new ArgumentNullException(nameof(containerName));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        }

        public async Task InitializeAsync()
        {
            await _keyValueStore.InitializeAsync();
        }

        public async Task SaveAsync<T>(string key, T data)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var dict = ObjectToDictionary(data);
            dict["_key"] = key;
            dict["_updatedAt"] = DateTime.UtcNow.ToString("O");

            await _keyValueStore.SaveAsync(_containerName, dict);
        }

        public async Task<T?> LoadAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var allData = await _keyValueStore.LoadAllAsync(_containerName);
            var row = allData.FirstOrDefault(d => d["_key"]?.ToString() == key);
            return row != null ? DictionaryToObject<T>(row) : default;
        }

        public async Task<bool> DeleteAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            var result = await _keyValueStore.DeleteAsync(_containerName, "_key", key);
            return result > 0;
        }

        public async Task<bool> ExistsAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            return await _keyValueStore.ExistsAsync(_containerName, "_key", key);
        }

        public async Task<IEnumerable<string>> GetKeysAsync()
        {
            var allData = await _keyValueStore.LoadAllAsync(_containerName);
            return allData
                .Select(d => d["_key"]?.ToString())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
        }

        public async Task ClearAsync()
        {
            var allData = await _keyValueStore.LoadAllAsync(_containerName);
            foreach (var row in allData)
            {
                var key = row["_key"]?.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    await _keyValueStore.DeleteAsync(_containerName, "_key", key);
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

        private Dictionary<string, object> ObjectToDictionary<T>(T obj)
        {
            if (obj == null)
                return new Dictionary<string, object>();

            var type = typeof(T);
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            {
                return new Dictionary<string, object> { { "_value", obj! } };
            }

            var json = JsonSerializer.Serialize(obj, _jsonOptions);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
        }

        private T DictionaryToObject<T>(IDictionary<string, object> dict)
        {
            var cleanedDict = new Dictionary<string, object>(dict);
            cleanedDict.Remove("_key");
            cleanedDict.Remove("_updatedAt");

            var type = typeof(T);
            if ((type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) && cleanedDict.TryGetValue("_value", out var value))
            {
                return (T)Convert.ChangeType(value, type);
            }

            var json = JsonSerializer.Serialize(cleanedDict, _jsonOptions);
            return JsonSerializer.Deserialize<T>(json)!;
        }
    }

    public class KeyValueStoreToWorkflowStorageAdapter<T> : Abstractions.IWorkflowStorage<T>
    {
        private readonly Abstractions.IKeyValueStore _keyValueStore;
        private readonly string _containerName;
        private readonly JsonSerializerOptions _jsonOptions;

        public KeyValueStoreToWorkflowStorageAdapter(Abstractions.IKeyValueStore keyValueStore, string containerName = "Workflows")
        {
            _keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
            _containerName = containerName ?? throw new ArgumentNullException(nameof(containerName));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        }

        public async Task InitializeAsync()
        {
            await _keyValueStore.InitializeAsync();
        }

        public async Task StoreAsync(string workflowId, T workflow)
        {
            var dict = new Dictionary<string, object>
            {
                ["_key"] = workflowId,
                ["_workflow"] = SerializeWorkflow(workflow),
                ["_createdAt"] = DateTime.UtcNow.ToString("O"),
                ["_updatedAt"] = DateTime.UtcNow.ToString("O")
            };
            await _keyValueStore.SaveAsync(_containerName, dict);
        }

        public async Task<T?> RetrieveAsync(string workflowId)
        {
            var allData = await _keyValueStore.LoadAllAsync(_containerName);
            var row = allData.FirstOrDefault(d => d["_key"]?.ToString() == workflowId);
            if (row == null || !row.TryGetValue("_workflow", out var workflowJson))
                return default;

            return DeserializeWorkflow(workflowJson.ToString() ?? string.Empty);
        }

        public async Task DeleteAsync(string workflowId)
        {
            await _keyValueStore.DeleteAsync(_containerName, "_key", workflowId);
        }

        public async Task<bool> ExistsAsync(string workflowId)
        {
            return await _keyValueStore.ExistsAsync(_containerName, "_key", workflowId);
        }

        public async Task<List<string>> GetAllWorkflowIdsAsync()
        {
            var allData = await _keyValueStore.LoadAllAsync(_containerName);
            return allData
                .Select(d => d["_key"]?.ToString())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
        }

        public async Task ClearAllWorkflowsAsync()
        {
            var allData = await _keyValueStore.LoadAllAsync(_containerName);
            foreach (var row in allData)
            {
                var key = row["_key"]?.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    await _keyValueStore.DeleteAsync(_containerName, "_key", key);
                }
            }
        }

        private string SerializeWorkflow(T workflow)
        {
            return JsonSerializer.Serialize(workflow, _jsonOptions);
        }

        private T? DeserializeWorkflow(string workflowJson)
        {
            return JsonSerializer.Deserialize<T>(workflowJson, _jsonOptions);
        }
    }
}
