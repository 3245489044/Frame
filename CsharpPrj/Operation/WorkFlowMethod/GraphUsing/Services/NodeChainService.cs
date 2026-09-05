#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using GraphFoundation;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using Modules.GraphProcessing.Orchestration;
using WorkFlowMethod.Infrastructure.Abstractions;
using WorkFlowMethod.Orchestration;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Orchestration.IdManagement;
using WorkFlowMethod.Orchestration.Content;
using WorkFlowMethod.Orchestration.Context.Runtime;
#endregion

namespace WorkFlowMethod.GraphFramework.Services
{
    public class NodeChainService : INodeChainService
    {
        private const string DefaultQuerySteps =
            "\"WhereNoOutputs\",\"WhereSymbolMatchfigure\", \"SelectManyInputs\"";

        private readonly IWorkflowDeserializer _deserializer;
        private readonly INodeIdentityGenerator _identityGen;
        private readonly IWorkflowRuntimeContextAccessor _contextAccessor;

        public NodeChainService(
            IWorkflowDeserializer deserializer = null,
            INodeIdentityGenerator identityGen = null,
            IWorkflowRuntimeContextAccessor contextAccessor = null)
        {
            _deserializer = deserializer ?? new WorkflowDeserializer();
            _identityGen = identityGen ?? new DefaultNodeIdentityGenerator();
            _contextAccessor = contextAccessor ?? new WorkflowRuntimeContextAccessor();
        }

        private WorkflowRuntimeContext EnsureContext(string workflowId = null)
        {
            return _contextAccessor.GetOrCreateContext(workflowId);
        }

        public Chain<string, TNode> CreateChain<TNode>(
            string json, int chainCount = 1, string workflowId = null)
            where TNode : NodeConfig
        {
            global::GraphFramework.Helpers.Guard.AgainstNullOrEmpty(json, nameof(json));
            global::GraphFramework.Helpers.Guard.AgainstNonPositive(chainCount, nameof(chainCount));
            var ctx = EnsureContext(workflowId);
            var typed = CreateChainedNodes<TNode>(json, chainCount, ctx)
                ?? throw new InvalidOperationException("创建 Chain<string, NodeConfig> 失败");

            var nodeConfigChain = new Chain<string, NodeConfig>
            {
                StartNode = typed.StartNode,
                EndNode = typed.EndNode,
                AllNodes = typed.AllNodes?.Cast<NodeConfig>().ToList() ?? new List<NodeConfig>()
            };
            ctx.RegisterChain(workflowId ?? "default", nodeConfigChain);

            return typed;
        }

        public Chain<string, TNode> CreateChainedNodes<TNode>(
            string json, int chainCount,
            WorkflowRuntimeContext context = null, string nodeCallkeyPattern = null)
            where TNode : NodeConfig
        {
            context ??= EnsureContext(null);
            var baseNodes = _deserializer.Deserialize<TNode>(json);
            if (baseNodes?.AllNodes == null || baseNodes.AllNodes.Count == 0 || chainCount <= 0)
                return new() { AllNodes = new List<TNode>() };

            var graph = new CoreGraph<string, TNode, object>();
            var orchestration = new Modules.GraphProcessing.Orchestration.OrchestrationService<string, TNode, object>(
                graph,
                keyExtractor: n => n.basicConfig?.Callkey,
                setPosition: (n, pos) => { },
                cloner: n => DeepCloneNode(n),
                keyConverter: i => i.ToString());

            var template = (TNode)baseNodes.AllNodes[0];
            var result = orchestration.CreateMultipleChains(template, chainCount, baseNodes.AllNodes.Count);

            var allNodes = new List<TNode>();
            foreach (var chainResult in result.CreatedChains)
            {
                foreach (var key in chainResult.NodeKeys)
                {
                    var node = graph.GetNodeData(key);
                    if (node != null)
                    {
                        context.RegisterNode(node);
                        allNodes.Add(node);
                    }
                }
            }

            return new Chain<string, TNode>
            {
                StartNode = allNodes.FirstOrDefault(),
                EndNode = allNodes.LastOrDefault(),
                AllNodes = allNodes
            };
        }

        private static TNode DeepCloneNode<TNode>(TNode source) where TNode : NodeConfig
        {
            var json = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<TNode>(json);
        }
    }
}
