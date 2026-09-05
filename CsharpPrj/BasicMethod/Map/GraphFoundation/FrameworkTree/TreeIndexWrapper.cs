#nullable enable

using Framework.Tree;

namespace FrameworkAbstractions.Tree;

public class TreeIndexWrapper<TNode, TKey>
    where TNode : class
{
    private readonly TreeIndex<TNode, TKey> _index;

    public TreeIndexWrapper(IEqualityComparer<TKey>? keyComparer = null)
    {
        _index = new TreeIndex<TNode, TKey>(keyComparer);
    }

    public void Rebuild(TNode root, Func<TNode, IEnumerable<TNode>> getChildren,
        Func<TNode, TKey?> keyExtractor) =>
        _index.Rebuild(root, getChildren, keyExtractor);

    public void RegisterNode(TNode node, TKey key) =>
        _index.RegisterNode(node, key);

    public void UnregisterNode(TKey key) =>
        _index.UnregisterNode(key);

    public TNode? Find(TKey key) =>
        _index.TryGet(key, out var node) ? node : null;

    public bool Contains(TKey key) =>
        _index.Contains(key);

    public IEnumerable<TKey> GetKeys() =>
        _index.GetKeys();

    public IEnumerable<TNode> GetAll() =>
        _index.GetAll().Select(kvp => kvp.Value);

    public void Clear() =>
        _index.Clear();

    public int Count => _index.Count;
}