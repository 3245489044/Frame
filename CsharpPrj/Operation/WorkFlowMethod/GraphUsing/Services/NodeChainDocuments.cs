
#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Node.Carriers;
using Modules.Node.Spec;
using GraphFramework.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.GraphFramework.Services
{
    /// <summary>
    /// 链 → 框架中立链文档（<b>唯一转换点</b>）：使宿主各校验入口共用框架
    /// <see cref="NodeSpecValidator.ValidateChains"/> 的链判据（治 P19：禁各写一套环/悬空算法）。
    /// </summary>
    public static class NodeChainDocuments
    {
        /// <summary>链连通性校验器（链判据不依赖载体：空载体注册表即可，未注册位置仅记告警）。</summary>
        public static NodeSpecValidator ChainValidator { get; } = new(new CarrierRegistry());

        public static NodeChainDocument FromChain(Chain<string, NodeConfig> chain, string? chainId = null)
        {
            var nodes = chain.AllNodes ?? new List<NodeConfig>();
            var startKey = chain.StartNode?.basicConfig?.Callkey;

            return new NodeChainDocument(
                chainId ?? startKey ?? "chain",
                nodes
                    .Where(node => node is not null)
                    .Select(node => FromNode(node, startKey))
                    .Where(node => node.NodeKey.Length > 0)
                    .ToArray());
        }

        private static NodeChainNode FromNode(NodeConfig node, string? startKey)
        {
            var key = node.basicConfig?.Callkey ?? string.Empty;
            var nextKey = node.nextNodeConfig?.NextNodeKey;
            var isExit = string.IsNullOrEmpty(nextKey);

            return new NodeChainNode(
                key,
                nextKey,
                NormalizeKeys(node.funcPipelineConfig?.ParameterKeys),
                NormalizeKeys(node.funcPipelineConfig?.ResultKeys),
                IsEntry: !string.IsNullOrEmpty(startKey) && string.Equals(key, startKey, StringComparison.Ordinal),
                IsExit: isExit);
        }

        private static IReadOnlyList<string> NormalizeKeys(string? raw) =>
            NodeResultExtractor.NormalizeResultKeys(raw).ToArray();
    }
}
