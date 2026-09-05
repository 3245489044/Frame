
#nullable enable
using System.Collections.Generic;

namespace WorkflowExecution.Abstractions
{
    public interface INodeRepository<TNode>
    {
        void AddOrUpdateNode(string key, TNode node);

        TNode? GetNode(string key);

        bool RemoveNode(string key);

        bool ContainsNode(string key);

        IEnumerable<string> GetAllNodeKeys();

        IEnumerable<TNode> GetAllNodes();

        int Count { get; }
    }
}
