
#nullable enable

namespace GraphFoundation.Chain
{
    public interface IChainNodeHandler<TNode, TChain>
        where TNode : class
        where TChain : class
    {
        IEnumerable<TNode> GetNodes(TChain chain);

        string GetKey(TNode node);

        TNode? GetNode(TChain chain, string key);

        TNode CreateNode(string key, object data);

        void SetNode(TChain chain, TNode node);

        void RemoveNode(TChain chain, string key);
    }
}
