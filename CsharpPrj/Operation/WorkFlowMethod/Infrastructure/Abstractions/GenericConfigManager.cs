using System;
using System.Collections.Generic;
using System.Linq;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace WorkFlowMethod.Infrastructure.Abstractions
{
    public abstract class GenericWorkflowConfig<TWorkflow> where TWorkflow : class
    {
        public string ConfigKey { get; }
        public string WorkflowId { get; protected set; }
        protected GenericWorkflowStorageManager<TWorkflow> StorageManager { get; }

        protected GenericWorkflowConfig(string configKey, GenericWorkflowStorageManager<TWorkflow> storageManager, string workflowId = null)
        {
            ConfigKey = configKey ?? throw new ArgumentNullException(nameof(configKey));
            StorageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
            WorkflowId = workflowId;
            Initialize();
        }

        private void Initialize()
        {
            if (!string.IsNullOrEmpty(WorkflowId))
            {
                var workflow = StorageManager.GetWorkflow(WorkflowId);
                if (workflow != null)
                {
                    UpdateFromWorkflow(workflow);
                    return;
                }
            }
            ResetToDefaults();
        }

        public void Refresh()
        {
            Initialize();
        }

        protected abstract void UpdateFromWorkflow(TWorkflow workflow);
        protected abstract void ResetToDefaults();
        protected abstract bool IsConfigValidForWorkflow(TWorkflow workflow);
    }

    public class GenericConfigManager<TConfig, TWorkflow>
        where TConfig : GenericWorkflowConfig<TWorkflow>
        where TWorkflow : class
    {
        private readonly Dictionary<string, TConfig> _configs = new();
        private readonly Func<string, GenericWorkflowStorageManager<TWorkflow>, TWorkflow, TConfig> _factory;
        private readonly GenericWorkflowStorageManager<TWorkflow> _storageManager;

        public GenericConfigManager(
            Func<string, GenericWorkflowStorageManager<TWorkflow>, TWorkflow, TConfig> factory,
            GenericWorkflowStorageManager<TWorkflow> storageManager)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        }

        public IReadOnlyDictionary<string, TConfig> ConfigCache =>
            _configs as IReadOnlyDictionary<string, TConfig>;

        public TConfig GetOrCreateConfig(string key, TWorkflow workflow = null)
        {
            if (_configs.TryGetValue(key, out var existing))
                return existing;

            var config = _factory(key, _storageManager, workflow);
            _configs[key] = config;
            return config;
        }

        public List<TConfig> GetAllConfigs() => _configs.Values.ToList();

        public bool TryGetConfig(string key, out TConfig config) =>
            _configs.TryGetValue(key, out config);

        public void RemoveConfig(string key) => _configs.Remove(key);

        public void ClearAllConfigs() => _configs.Clear();

        public int Count => _configs.Count;

        public void Dispose()
        {
            _configs.Clear();
        }
    }
}
