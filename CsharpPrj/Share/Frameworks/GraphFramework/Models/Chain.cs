
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphFramework.Models
{
    public class Chain<TKey, TNode>
        where TKey : notnull
        where TNode : class
    {
        public TKey? ChainId { get; set; }

        public TNode? StartNode { get; set; }

        public TNode? EndNode { get; set; }

        public List<TNode> AllNodes { get; set; } = new();

        public string? Label { get; set; }

        public Dictionary<string, object?> Metadata { get; set; } = new();

        public int NodeCount => AllNodes.Count;

        public bool IsEmpty => AllNodes.Count == 0;

        public Chain() { }

        public Chain(TKey? chainId, IEnumerable<TNode>? nodes = null, string? label = null)
        {
            ChainId = chainId;
            Label = label;
            if (nodes != null)
            {
                AllNodes = nodes.ToList();
                StartNode = AllNodes.FirstOrDefault();
                EndNode = AllNodes.LastOrDefault();
            }
        }

        public List<TKey> GetAllNodeKeys(Func<TNode, TKey> keyExtractor) =>
            AllNodes.Select(keyExtractor).ToList();

        public TKey GetNodeKey(TNode node, Func<TNode, TKey> keyExtractor) =>
            keyExtractor(node);

        public TKey? GetStartNodeKey(Func<TNode, TKey> keyExtractor) =>
            StartNode != null ? keyExtractor(StartNode) : default;

        public TKey? GetEndNodeKey(Func<TNode, TKey> keyExtractor) =>
            EndNode != null ? keyExtractor(EndNode) : default;

        public void AppendNode(TNode node)
        {
            AllNodes.Add(node);
            EndNode = node;
            if (StartNode == null) StartNode = node;
        }

        public void PrependNode(TNode node)
        {
            AllNodes.Insert(0, node);
            StartNode = node;
            if (EndNode == null) EndNode = node;
        }

        public void InsertNode(int index, TNode node)
        {
            if (index < 0 || index > AllNodes.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            AllNodes.Insert(index, node);
            if (index == 0) StartNode = node;
            if (index == AllNodes.Count - 1) EndNode = node;
        }

        public bool RemoveNode(TNode node)
        {
            var removed = AllNodes.Remove(node);
            if (removed)
            {
                StartNode = AllNodes.FirstOrDefault();
                EndNode = AllNodes.LastOrDefault();
            }
            return removed;
        }

        public Chain<TKey, TNode> Clone() =>
            new(ChainId, AllNodes.ToList(), Label)
            {
                StartNode = StartNode,
                EndNode = EndNode,
                Metadata = new Dictionary<string, object?>(Metadata)
            };

        public override string? ToString() =>
            $"Chain<{typeof(TNode).Name}>[{ChainId}]: {NodeCount} nodes";
    }

    public class ChainInfo<TKey, TNode> : Chain<TKey, TNode>
        where TKey : notnull
        where TNode : class
    {
        public IReadOnlyList<TNode> Nodes => AllNodes.AsReadOnly();

        public ChainInfo() { }

        public ChainInfo(TKey? chainId, IEnumerable<TNode>? nodes = null, string? label = null)
            : base(chainId, nodes, label) { }

        public static ChainInfo<TKey, TNode> FromChain(Chain<TKey, TNode> chain)
        {
            var info = new ChainInfo<TKey, TNode>(chain.ChainId, chain.AllNodes, chain.Label)
            {
                StartNode = chain.StartNode,
                EndNode = chain.EndNode,
                Metadata = new Dictionary<string, object?>(chain.Metadata)
            };
            return info;
        }
    }
}
