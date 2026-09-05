
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

        public void RegisterWorkflows(IEnumerable<KeyValuePair<string, Chain<string, NodeConfig>>> workflows)
        {
            if (workflows == null) return;
            foreach (var kvp in workflows)
                RegisterWorkflow(kvp.Key, kvp.Value);
        }
    }
}
