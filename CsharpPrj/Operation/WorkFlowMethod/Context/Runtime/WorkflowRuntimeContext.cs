#region USINGS
using System.Collections.Concurrent;
using System.Collections.Generic;
using GraphFramework.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using INodeExecutorFactory = WorkFlowMethod.Orchestration.Context.Execution.INodeExecutorFactory;
using DefaultNodeExecutorFactory = WorkFlowMethod.Orchestration.Context.Execution.DefaultNodeExecutorFactory;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Runtime
{
    public class WorkflowRuntimeContext
    {
        private readonly ConcurrentDictionary<string, NodeConfig> _nodeCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Chain<string, NodeConfig>> _chainCache = new(StringComparer.OrdinalIgnoreCase);

        private readonly INodeExecutorFactory _executorFactory;
        private readonly WorkflowConfiguration _configuration;

        public object AICallFrameRoot { get; set; }

        public WorkflowRuntimeContext(INodeExecutorFactory executorFactory = null, WorkflowConfiguration configuration = null)
        {
            _executorFactory = executorFactory ?? new DefaultNodeExecutorFactory();
            _configuration = configuration ?? new WorkflowConfiguration();
        }

        #region 节点管理

        public void RegisterNode(NodeConfig node)
        {
            if (node?.basicConfig?.Callkey != null)
                _nodeCache[node.basicConfig.Callkey] = node;
        }

        public void RegisterNodes(IEnumerable<NodeConfig> nodes)
        {
            foreach (var node in nodes) RegisterNode(node);
        }

        public NodeConfig GetNode(string callkey) =>
            _nodeCache.TryGetValue(callkey, out var node) ? node : null;

        public bool RemoveNode(string callkey) => _nodeCache.TryRemove(callkey, out _);

        #endregion

        #region 链管理

        public void RegisterChain(string workflowId, Chain<string, NodeConfig> chain)
        {
            _chainCache[workflowId] = chain;
            if (chain?.AllNodes != null)
                RegisterNodes(chain.AllNodes);
        }

        public Chain<string, NodeConfig> GetChain(string workflowId) =>
            _chainCache.TryGetValue(workflowId, out var chain) ? chain : null;

        public bool RemoveChain(string workflowId) => _chainCache.TryRemove(workflowId, out _);

        #endregion
    }
}