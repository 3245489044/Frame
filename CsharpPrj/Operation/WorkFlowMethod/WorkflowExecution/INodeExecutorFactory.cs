#region USINGS
using System;
using System.Collections.Generic;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Orchestration.Context.Execution
{
    public interface INodeConfigProvider
    {
        string CallKey { get; }
    }

    public interface INodeExecutorFactory
    {
        INodeExecutor CreateExecutor(NodeConfig node);
        void RegisterExecutor(string nodeType, Func<INodeExecutor> factory);
    }

    public interface INodeCallkeyGenerator
    {
        string GenerateCallkey(NodeConfig node);
    }

    public class DefaultNodeExecutorFactory : INodeExecutorFactory
    {
        private readonly Dictionary<string, Func<INodeExecutor>> _executorFactories = new();
        private readonly INodeExecutor _defaultExecutor = new DefaultNodeExecutor();

        public INodeExecutor CreateExecutor(NodeConfig node)
        {
            if (node == null) return _defaultExecutor;
            var nodeType = node.Symbol ?? "default";
            return _executorFactories.TryGetValue(nodeType, out var factory) ? factory() : _defaultExecutor;
        }

        public void RegisterExecutor(string nodeType, Func<INodeExecutor> factory)
        {
            _executorFactories[nodeType] = factory ?? throw new ArgumentNullException(nameof(factory));
        }
    }
}
