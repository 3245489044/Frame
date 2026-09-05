#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
#endregion

namespace WorkFlowMethod.Orchestration.IdManagement
{
    public static class IdConstants
    {
        public const string DefaultPrefix = "NODE_";
        public const string DefaultSeparator = "_";
        public const string WorkflowPrefix = "WF_";
        public const string ChainPrefix = "CHAIN_";
        public const string ExecutionPrefix = "EXEC_";
        public const string DefaultCategory = "Default";
        public const int DefaultSequenceLength = 6;

        public const string DefaultNodeCallkeyPattern = "{symbol}_c{chainIndex}_n{nodeIndex}";
    }

    public interface IIdGenerator
    {
        string GenerateId(string prefix = null, string category = null);
        string GenerateIdForWorkflow();
        string GenerateIdForChain();
        string GenerateIdForExecution();
    }

    public class IdGenerator : IIdGenerator
    {
        private static readonly ConcurrentDictionary<string, long> _sequences = new();
        private static readonly Random _random = Random.Shared;

        public string GenerateId(string prefix = null, string category = null)
        {
            prefix ??= IdConstants.DefaultPrefix;
            category ??= IdConstants.DefaultCategory;
            var sequence = _sequences.AddOrUpdate(category, _ => 1, (_, current) => current + 1);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var randomSuffix = _random.Next(1000, 9999);
            return $"{prefix}{category}{IdConstants.DefaultSeparator}{timestamp}{IdConstants.DefaultSeparator}{sequence:D6}{randomSuffix}";
        }

        public string GenerateIdForWorkflow() => GenerateId(IdConstants.WorkflowPrefix);
        public string GenerateIdForChain() => GenerateId(IdConstants.ChainPrefix);
        public string GenerateIdForExecution() => GenerateId(IdConstants.ExecutionPrefix);
    }

    public interface INodeRegistry
    {
        void Register(string callkey, object node);
        bool Unregister(string callkey);
        object GetNode(string callkey);
        bool Contains(string callkey);
        IEnumerable<string> GetAllCallkeys();
        int Count { get; }

        bool TryRegister(string callkey, object node);

        event EventHandler<NodeOverwriteEventArgs>? OnNodeOverwrite;
    }

    public class NodeOverwriteEventArgs : EventArgs
    {
        public string Callkey { get; }
        public object OldNode { get; }
        public object NewNode { get; }

        public NodeOverwriteEventArgs(string callkey, object oldNode, object newNode)
        {
            Callkey = callkey;
            OldNode = oldNode;
            NewNode = newNode;
        }
    }

    public class NodeRegistry : INodeRegistry
    {
        private readonly ConcurrentDictionary<string, object> _nodes = new(StringComparer.OrdinalIgnoreCase);

        public bool EnableDuplicateWarning { get; set; } = true;

        public event EventHandler<NodeOverwriteEventArgs>? OnNodeOverwrite;

        public void Register(string callkey, object node)
        {
            if (string.IsNullOrEmpty(callkey)) throw new ArgumentNullException(nameof(callkey));
            if (node == null) throw new ArgumentNullException(nameof(node));

            if (_nodes.TryGetValue(callkey, out var oldNode))
            {
                if (EnableDuplicateWarning)
                {
                    WorkFlowMethod.Diagnostics.WorkFlowMethodLog.Log("NodeRegistry_Register_duplicate", new { callkey });
                }
                OnNodeOverwrite?.Invoke(this, new NodeOverwriteEventArgs(callkey, oldNode, node));
            }
            _nodes[callkey] = node;
        }

        public bool TryRegister(string callkey, object node)
        {
            if (string.IsNullOrEmpty(callkey)) throw new ArgumentNullException(nameof(callkey));
            if (node == null) throw new ArgumentNullException(nameof(node));

            return _nodes.TryAdd(callkey, node);
        }

        public bool Unregister(string callkey) => _nodes.TryRemove(callkey, out _);
        public object GetNode(string callkey) => _nodes.TryGetValue(callkey, out var node) ? node : null;
        public bool Contains(string callkey) => _nodes.ContainsKey(callkey);
        public IEnumerable<string> GetAllCallkeys() => _nodes.Keys.ToList();
        public int Count => _nodes.Count;
    }

    [Obsolete("请通过 DI 容器注册 IIdGenerator 和 INodeRegistry，而非直接使用静态入口")]
    public static class IdServices
    {
        private static readonly Lazy<IIdGenerator> _generator = new(() => new IdGenerator());
        private static readonly Lazy<INodeRegistry> _registry = new(() => new NodeRegistry());

        public static IIdGenerator Generator => _generator.Value;
        public static INodeRegistry Registry => _registry.Value;
    }
}
