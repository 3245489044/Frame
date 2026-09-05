#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using GraphFramework.Bridge;
using GraphFramework.Models;
using GraphFramework.Pipeline;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkflowExecution.Pipeline;
#endregion

namespace WorkFlowMethod.GraphFramework.Pipeline
{
    public class GraphWorkflowConnector
    {
        private readonly IReadOnlyList<IGraphPipelineStep<string, NodeConfig, object>> _steps;

        public GraphWorkflowConnector(IEnumerable<IGraphPipelineStep<string, NodeConfig, object>> steps)
        {
            _steps = steps?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(steps));
        }

        public IReadOnlyList<Chain<string, NodeConfig>> Connect(
            IReadOnlyList<Chain<string, NodeConfig>> inputChains,
            IContext context)
        {
            var graph = ChainListToGraph(inputChains);

            var pipelineContext = new PipelineContext();
            foreach (var step in _steps)
            {
                graph = step.Execute(graph, pipelineContext);
            }

            return GraphToChainList(graph);
        }

        public IGraph<string, NodeConfig, object> ConnectToGraph(
            IReadOnlyList<Chain<string, NodeConfig>> inputChains,
            IContext context)
        {
            var graph = ChainListToGraph(inputChains);

            var pipelineContext = new PipelineContext();
            foreach (var step in _steps)
            {
                graph = step.Execute(graph, pipelineContext);
            }

            return graph;
        }

        private static IGraph<string, NodeConfig, object> ChainListToGraph(
            IReadOnlyList<Chain<string, NodeConfig>> inputChains)
        {
            var graph = new CoreGraph<string, NodeConfig, object>();

            foreach (var chain in inputChains)
            {
                if (chain?.AllNodes == null) continue;

                var nodeKeys = chain.AllNodes
                    .Select(n => n.basicConfig?.Callkey ?? n.Id)
                    .ToList();

                var chainInfo = new ChainInfo<string>(nodeKeys, new List<string>());
                GraphFoundationBridge<string, NodeConfig, object>.ChainToGraph(
                    chainInfo,
                    graph,
                    key =>
                    {
                        var node = chain.AllNodes.FirstOrDefault(n =>
                            (n.basicConfig?.Callkey ?? n.Id)?.Equals(key) == true);
                        return node;
                    });
            }

            return graph;
        }

        private static IReadOnlyList<Chain<string, NodeConfig>> GraphToChainList(
            IGraph<string, NodeConfig, object> graph)
        {
            var chainInfos = GraphFoundationBridge<string, NodeConfig, object>.GraphToChain(graph);
            var result = new List<Chain<string, NodeConfig>>();

            foreach (var chainInfo in chainInfos)
            {
                var nodes = new List<NodeConfig>();
                foreach (var key in chainInfo.NodeKeys)
                {
                    var nodeData = graph.GetNodeData(key);
                    if (nodeData != null)
                        nodes.Add(nodeData);
                }

                result.Add(new Chain<string, NodeConfig>
                {
                    AllNodes = nodes,
                    StartNode = nodes.FirstOrDefault(),
                    EndNode = nodes.LastOrDefault()
                });
            }

            return result.AsReadOnly();
        }
    }
}
