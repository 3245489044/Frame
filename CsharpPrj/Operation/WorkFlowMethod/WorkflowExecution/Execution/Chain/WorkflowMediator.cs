
#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using System.Linq;
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.DefaultImplementations;
using Share.LogicComposition.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Orchestration.Content
{
    public class WorkflowMediator : IWorkflowMediator
    {
        private readonly IChainComposer<NodeConfig> _chainComposer;
        private readonly IWorkflowDeserializer _deserializer;

        public WorkflowMediator()
            : this(new WorkflowDeserializer(), new DefaultChainComposer<NodeConfig>())
        {
        }

        public WorkflowMediator(
            IWorkflowDeserializer deserializer,
            IChainComposer<NodeConfig> chainComposer)
        {
            // 必填端口（T）：反序列化器/链组合器由装配点显式提供；无参构造是组件自装配的唯一入口。
            _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
            _chainComposer = chainComposer ?? throw new ArgumentNullException(nameof(chainComposer));
        }

        private static void LinkChainInternal(Chain<string, NodeConfig> chain)
        {
            if (chain?.AllNodes == null || chain.AllNodes.Count <= 1) return;
            for (int i = 0; i < chain.AllNodes.Count - 1; i++)
            {
                var current = chain.AllNodes[i];
                var next = chain.AllNodes[i + 1];
                if (current.basicConfig != null)
                    current.NextNodeId = next.basicConfig?.Callkey;
            }
        }

        private static void LinkNodesInternal(NodeConfig source, NodeConfig target)
        {
            if (source?.basicConfig != null && target?.basicConfig != null)
                source.NextNodeId = target.basicConfig.Callkey;
        }

        public Chain<string, NodeConfig> BuildFromConfig(ChainConfig config)
        {
            if (config?.Chains == null || config.Chains.Count == 0)
                return new Chain<string, NodeConfig> { AllNodes = new List<NodeConfig>() };

            var allNodes = new List<NodeConfig>();
            var seenCallkeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in config.Chains)
            {
                if (pair.ChainInfo?.AllNodes == null) continue;
                foreach (var node in pair.ChainInfo.AllNodes)
                {
                    var callkey = node.basicConfig?.Callkey;
                    if (callkey != null && !seenCallkeys.Add(callkey))
                        continue;
                    allNodes.Add(node);
                }
            }

            var result = new Chain<string, NodeConfig>
            {
                AllNodes = allNodes,
                StartNode = allNodes.FirstOrDefault(),
                EndNode = allNodes.LastOrDefault()
            };

            if (allNodes.Count > 1)
                LinkChainInternal(result);

            return result;
        }

        public Chain<string, NodeConfig> ConnectWorkflows(Chain<string, NodeConfig> source, Chain<string, NodeConfig> target, ConnectRequest request)
        {
            if (source == null) return target ?? new Chain<string, NodeConfig> { AllNodes = new List<NodeConfig>() };
            if (target == null) return source;

            var nodeSequences = new[] { source, target }
                .Where(c => c?.AllNodes != null)
                .Select(c => (IEnumerable<NodeConfig>)c.AllNodes);

            var composeResult = _chainComposer.Compose(
                nodeSequences,
                request?.MergeOptions ?? new CompositionOptions
                {
                    DuplicateHandling = DuplicateHandling.Skip,
                    ValidateBeforeMerge = true,
                    TrackConflicts = true
                });

            var mergedNodes = composeResult.IsSuccess && composeResult.Result != null
                ? composeResult.Result.ToList()
                : source.AllNodes.Concat(target.AllNodes ?? Enumerable.Empty<NodeConfig>()).ToList();

            var merged = new Chain<string, NodeConfig>
            {
                AllNodes = mergedNodes,
                StartNode = mergedNodes.FirstOrDefault(),
                EndNode = mergedNodes.LastOrDefault()
            };

            if (!string.IsNullOrEmpty(request?.SourceEndNodeCallkey)
                && !string.IsNullOrEmpty(request?.TargetStartNodeCallkey))
            {
                var sourceEnd = merged.AllNodes.FirstOrDefault(n =>
                    n.basicConfig?.Callkey == request.SourceEndNodeCallkey);
                var targetStart = merged.AllNodes.FirstOrDefault(n =>
                    n.basicConfig?.Callkey == request.TargetStartNodeCallkey);

                if (sourceEnd != null && targetStart != null)
                    LinkNodesInternal(sourceEnd, targetStart);
            }

            return merged;
        }

    }
}
