
#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Chain;
using Modules.GraphProcessing.Reference;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Skeletons.NodeProcess.ReferenceRegistration;
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

        /// <summary>字段访问器（清单本身来自 <see cref="NodeReferenceFields.Declared"/>，禁第二份字段表）。</summary>
        private sealed record ReferenceBinding(
            string Name,
            Func<NodeConfig, string?> Get,
            Action<NodeConfig, string?> Set);

        private static readonly IReadOnlyList<ReferenceBinding> Bindings = BuildBindings();

        private static IReadOnlyList<ReferenceBinding> BuildBindings()
        {
            var bindings = new List<ReferenceBinding>(NodeReferenceFields.Declared.Count);
            foreach (var field in NodeReferenceFields.Declared)
            {
                bindings.Add(field.Name switch
                {
                    NodeReferenceFields.AddConfigAddKey => new ReferenceBinding(
                        field.Name,
                        node => node.addNodeConfig?.AddConfigAddKey,
                        (node, value) =>
                        {
                            if (node.addNodeConfig is not null) node.addNodeConfig.AddConfigAddKey = value;
                        }),
                    NodeReferenceFields.ConnectChainInfoKey => new ReferenceBinding(
                        field.Name,
                        node => node.nextNodeConfig?.ConnectChainInfoKey,
                        (node, value) =>
                        {
                            if (node.nextNodeConfig is not null) node.nextNodeConfig.ConnectChainInfoKey = value;
                        }),
                    NodeReferenceFields.NextNodeKey => new ReferenceBinding(
                        field.Name,
                        node => node.nextNodeConfig?.NextNodeKey,
                        (node, value) =>
                        {
                            if (node.nextNodeConfig is not null) node.nextNodeConfig.NextNodeKey = value;
                        }),
                    NodeReferenceFields.ParameterKeys => new ReferenceBinding(
                        field.Name,
                        node => node.funcPipelineConfig?.ParameterKeys,
                        (node, value) =>
                        {
                            if (node.funcPipelineConfig is not null) node.funcPipelineConfig.ParameterKeys = value;
                        }),
                    NodeReferenceFields.ResultKeys => new ReferenceBinding(
                        field.Name,
                        node => node.funcPipelineConfig?.ResultKeys,
                        (node, value) =>
                        {
                            if (node.funcPipelineConfig is not null) node.funcPipelineConfig.ResultKeys = value;
                        }),
                    _ => throw new InvalidOperationException(
                        $"引用字段 '{field.Name}' 已在 NodeReferenceFields.Declared 声明，但缺少访问器绑定"),
                });
            }

            return bindings;
        }

        public List<(string referenceValue, string referenceType)> ExtractReferences(
            string nodeKey, NodeConfig node)
        {
            var references = new List<(string, string)>();
            if (node == null) return references;

            foreach (var binding in Bindings)
            {
                var value = binding.Get(node);
                if (value != null)
                {
                    references.Add((value, binding.Name));
                }
            }

            return references;
        }

        public void UpdateReference(NodeConfig node, string oldReference, string newReference)
        {
            if (node == null) return;

            foreach (var binding in Bindings)
            {
                if (binding.Get(node) == oldReference)
                {
                    binding.Set(node, newReference);
                }
            }
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
