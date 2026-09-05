
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class TreeSearch<TNode> : ITreeSearch<TNode>
    {
        private readonly Func<TNode, IEnumerable<TNode>> _getChildren;

        public TreeSearch(Func<TNode, IEnumerable<TNode>> getChildren)
        {
            _getChildren = getChildren ?? throw new ArgumentNullException(nameof(getChildren));
        }

        public TNode? FindByName(TNode root, string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            return FindBy(root, node =>
            {
                var nameProp = node?.GetType().GetProperty("Name");
                return nameProp != null && nameProp.GetValue(node)?.ToString() == name;
            });
        }

        public TNode? FindByType<TTarget>(TNode root) where TTarget : TNode
        {
            return FindBy(root, node => node is TTarget);
        }

        public TNode? FindBy(TNode root, Func<TNode, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(predicate);

            if (predicate(root))
                return root;

            foreach (var child in _getChildren(root))
            {
                var result = FindBy(child, predicate);
                if (result != null)
                    return result;
            }

            return default;
        }

        public List<TNode> FindAllByName(TNode root, string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            return FindAllBy(root, node =>
            {
                var nameProp = node?.GetType().GetProperty("Name");
                return nameProp != null && nameProp.GetValue(node)?.ToString() == name;
            });
        }

        public List<TNode> FindAllByType<TTarget>(TNode root) where TTarget : TNode
        {
            return FindAllBy(root, node => node is TTarget);
        }

        public List<TNode> FindAllBy(TNode root, Func<TNode, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(predicate);

            var results = new List<TNode>();

            if (predicate(root))
                results.Add(root);

            foreach (var child in _getChildren(root))
            {
                results.AddRange(FindAllBy(child, predicate));
            }

            return results;
        }
    }
}
