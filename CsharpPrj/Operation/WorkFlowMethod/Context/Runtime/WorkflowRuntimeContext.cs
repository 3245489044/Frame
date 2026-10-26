#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GraphFramework.Models;
using Modules.KeySpace.Abstractions;
using WorkFlowMethod.KeySpace.Adapters;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using INodeExecutorFactory = WorkFlowMethod.Orchestration.Context.Execution.INodeExecutorFactory;
using DefaultNodeExecutorFactory = WorkFlowMethod.Orchestration.Context.Execution.DefaultNodeExecutorFactory;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Runtime
{
    public class WorkflowRuntimeContext
    {
        // A7：键比较器唯一口径 = IKeyEquivalence.Comparer（禁各存储层自建 StringComparer）。
        private readonly ConcurrentDictionary<string, NodeConfig> _nodeCache;
        private readonly ConcurrentDictionary<string, Chain<string, NodeConfig>> _chainCache;

        private readonly INodeExecutorFactory _executorFactory;
        private readonly WorkflowConfiguration _configuration;

        public object AICallFrameRoot { get; set; }

        public WorkflowRuntimeContext()
            : this(new DefaultNodeExecutorFactory(), new WorkflowConfiguration())
        {
        }

        public WorkflowRuntimeContext(
            INodeExecutorFactory executorFactory,
            WorkflowConfiguration configuration,
            IKeyEquivalence? equivalence = null)
        {
            // 必填端口（T）：节点执行器工厂/工作流配置由装配点显式提供；无参构造是组件自装配的唯一入口。
            _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            var comparer = (equivalence ?? WorkflowKeySpaceDefaults.Equivalence).Comparer;
            _nodeCache = new ConcurrentDictionary<string, NodeConfig>(comparer);
            _chainCache = new ConcurrentDictionary<string, Chain<string, NodeConfig>>(comparer);
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