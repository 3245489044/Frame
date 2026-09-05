
#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Chain;
using Modules.GraphProcessing.Reference;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.GraphFramework.Deduplication
{
    public class ConfigRelationHandler
    {
        private readonly IChainNodeHandler<NodeConfig, Chain<string, NodeConfig>> _nodeHandler;

        public ConfigRelationHandler(
            IChainNodeHandler<NodeConfig, Chain<string, NodeConfig>> nodeHandler)
        {
            _nodeHandler = nodeHandler ?? throw new ArgumentNullException(nameof(nodeHandler));
        }

        public List<(string referenceValue, string referenceType)> ExtractReferences(
            string nodeKey, NodeConfig node)
        {
            var references = new List<(string, string)>();

            if (node == null) return references;

            if (node.nextNodeConfig?.ConnectChainInfoKey != null)
                references.Add((node.nextNodeConfig.ConnectChainInfoKey, "ConnectChainInfoKey"));

            if (node.nextNodeConfig?.NextNodeKey != null)
                references.Add((node.nextNodeConfig.NextNodeKey, "NextNodeKey"));

            if (node.addNodeConfig?.AddConfigAddKey != null)
                references.Add((node.addNodeConfig.AddConfigAddKey, "AddConfigAddKey"));

            if (node.funcPipelineConfig?.ParameterKeys != null)
                references.Add((node.funcPipelineConfig.ParameterKeys, "ParameterKeys"));

            if (node.funcPipelineConfig?.ResultKeys != null)
                references.Add((node.funcPipelineConfig.ResultKeys, "ResultKeys"));

            return references;
        }

        public void UpdateReference(NodeConfig node, string oldReference, string newReference)
        {
            if (node == null) return;

            if (node.nextNodeConfig?.ConnectChainInfoKey == oldReference)
                node.nextNodeConfig.ConnectChainInfoKey = newReference;

            if (node.nextNodeConfig?.NextNodeKey == oldReference)
                node.nextNodeConfig.NextNodeKey = newReference;

            if (node.addNodeConfig?.AddConfigAddKey == oldReference)
                node.addNodeConfig.AddConfigAddKey = newReference;

            if (node.funcPipelineConfig?.ParameterKeys == oldReference)
                node.funcPipelineConfig.ParameterKeys = newReference;

            if (node.funcPipelineConfig?.ResultKeys == oldReference)
                node.funcPipelineConfig.ResultKeys = newReference;
        }

        public void UpdateReferences(NodeConfig node, Dictionary<string, string> renameMap)
        {
            if (node == null || renameMap == null) return;

            foreach (var kvp in renameMap)
            {
                UpdateReference(node, kvp.Key, kvp.Value);
            }
        }
    }
}
