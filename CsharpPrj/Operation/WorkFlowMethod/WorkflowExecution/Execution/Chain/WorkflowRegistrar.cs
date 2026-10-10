
#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using System.Linq;
using Share.LogicComposition.DefaultImplementations;
using Share.LogicComposition.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Skeletons.NodeProcess;
#endregion

namespace WorkFlowMethod.Orchestration.Content
{
    public interface IWorkflowRegistrar
    {
        void RegisterWorkflow(string workflowId, Chain<string, NodeConfig> chain);
        Chain<string, NodeConfig> GetWorkflow(string workflowId);
        bool UnregisterWorkflow(string workflowId);

        /// <summary>已登记工作流 Id 集（声明面投影消费；未知 ⇒ 空集）。</summary>
        IReadOnlyList<string> GetWorkflowIds();

        /// <summary>某工作流的节点键集（声明序；未知工作流 ⇒ 空集）。</summary>
        IReadOnlyList<string> GetNodeKeys(string workflowId);
    }

    public class WorkflowRegistrar : IWorkflowRegistrar
    {
        private readonly ChainInfoWorkflowStorageManager _storageManager;
        private readonly DefaultChainComposer<NodeConfig>? _chainComposer;

        public WorkflowRegistrar(
            ChainInfoWorkflowStorageManager storageManager,
            DefaultChainComposer<NodeConfig>? chainComposer = null)
        {
            _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
            _chainComposer = chainComposer;
        }

        public void RegisterWorkflow(string workflowId, Chain<string, NodeConfig> chain)
        {
            if (_chainComposer != null && chain?.AllNodes != null)
            {
                var result = _chainComposer.Compose(
                    new[] { (IEnumerable<NodeConfig>)chain.AllNodes },
                    new CompositionOptions
                    {
                        DuplicateHandling = DuplicateHandling.Skip,
                        ValidateBeforeMerge = false,
                        TrackConflicts = false
                    });

                if (result.IsSuccess && result.Result != null)
                {
                    var nodeList = result.Result.ToList();
                    chain.AllNodes = nodeList;
                    chain.StartNode = nodeList.FirstOrDefault();
                    chain.EndNode = nodeList.LastOrDefault();
                }
            }

            var typedChain = new Chain<string, NodeConfig>
            {
                StartNode = chain.StartNode,
                EndNode = chain.EndNode,
                AllNodes = chain.AllNodes
            };
            _storageManager.SaveChainInfo(workflowId, typedChain);
        }

        public Chain<string, NodeConfig> GetWorkflow(string workflowId)
        {
            var result = _storageManager.GetChainInfo(workflowId);
            return result;
        }

        public bool UnregisterWorkflow(string workflowId) =>
            _storageManager.DeleteChainInfo(workflowId);

        /// <inheritdoc />
        public IReadOnlyList<string> GetWorkflowIds() => _storageManager.GetAllWorkflowIds();

        /// <inheritdoc />
        public IReadOnlyList<string> GetNodeKeys(string workflowId)
        {
            if (string.IsNullOrWhiteSpace(workflowId)) return Array.Empty<string>();
            var chain = _storageManager.GetChainInfo(workflowId);
            if (chain?.AllNodes is null) return Array.Empty<string>();

            var keys = new List<string>(chain.AllNodes.Count);
            foreach (var node in chain.AllNodes)
            {
                var key = node?.basicConfig?.Callkey;
                if (string.IsNullOrWhiteSpace(key)) key = node?.Symbol;
                if (string.IsNullOrWhiteSpace(key)) key = node?.Id;
                if (!string.IsNullOrWhiteSpace(key)) keys.Add(key!);
            }
            return keys;
        }

        public void RegisterWorkflows(IEnumerable<KeyValuePair<string, Chain<string, NodeConfig>>> workflows)
        {
            if (workflows == null) return;
            foreach (var kvp in workflows)
                RegisterWorkflow(kvp.Key, kvp.Value);
        }
    }
}
