#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess
{

    public class ChainInfoWorkflowStorageManager
    {
        private readonly ConcurrentDictionary<string, Chain<string, NodeConfig>> _storage = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _timestamps = new(StringComparer.OrdinalIgnoreCase);

        public void SaveChainInfo(string workflowId, Chain<string, NodeConfig> chainInfo)
        {
            if (string.IsNullOrEmpty(workflowId))
                throw new ArgumentException("工作流 ID 不能为空", nameof(workflowId));

            _storage[workflowId] = chainInfo ?? throw new ArgumentNullException(nameof(chainInfo));
            _timestamps[workflowId] = DateTime.UtcNow;
        }

        public Chain<string, NodeConfig> GetChainInfo(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId))
                throw new ArgumentException("工作流 ID 不能为空", nameof(workflowId));

            _storage.TryGetValue(workflowId, out var chainInfo);
            return chainInfo;
        }

        public bool DeleteChainInfo(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId))
                return false;

            var removed = _storage.TryRemove(workflowId, out _);
            _timestamps.TryRemove(workflowId, out _);

            return removed;
        }

        public bool ContainsChainInfo(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId))
                return false;

            return _storage.ContainsKey(workflowId);
        }

        public List<string> GetAllWorkflowIds()
        {
            return _storage.Keys.ToList();
        }

        public DateTime? GetTimestamp(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId))
                return null;

            if (_timestamps.TryGetValue(workflowId, out var timestamp))
                return timestamp;

            return null;
        }

        public void Clear()
        {
            _storage.Clear();
            _timestamps.Clear();
        }

        public int Count => _storage.Count;
    }

    public class WebCallManager
    {
        private readonly ConcurrentDictionary<string, WebCallConfig> _webCallConfigs = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterWebCall(string callKey, WebCallConfig config)
        {
            if (string.IsNullOrEmpty(callKey))
                throw new ArgumentException("调用键不能为空", nameof(callKey));

            _webCallConfigs[callKey] = config ?? throw new ArgumentNullException(nameof(config));
        }

        public WebCallConfig GetWebCallConfig(string callKey)
        {
            _webCallConfigs.TryGetValue(callKey, out var config);
            return config;
        }

        public bool ContainsWebCall(string callKey)
        {
            return _webCallConfigs.ContainsKey(callKey);
        }

        public bool RemoveWebCall(string callKey)
        {
            return _webCallConfigs.TryRemove(callKey, out _);
        }

        public IEnumerable<string> GetAllCallKeys() => _webCallConfigs.Keys;

        public void Clear()
        {
            _webCallConfigs.Clear();
        }
    }

    public class WebCallConfig
    {
        public string Url { get; set; }
        public string Method { get; set; } = "GET";
        public Dictionary<string, string> Headers { get; set; } = new();
        public string BodyTemplate { get; set; }
        public int TimeoutMs { get; set; } = 30000;
        public int RetryCount { get; set; } = 3;
        public bool ValidateCertificate { get; set; } = false;
    }
}
