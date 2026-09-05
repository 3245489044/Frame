
#nullable enable

namespace Framework.Tree
{
    public class TreePathFinder<TNode>
    {
        private readonly Func<TNode, TNode?> _getParent;

        public TreePathFinder(Func<TNode, TNode?> getParent)
        {
            _getParent = getParent ?? throw new ArgumentNullException(nameof(getParent));
        }

        public TNode? FindNearestAncestor(TNode node, Func<TNode, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(predicate);

            var current = _getParent(node);
            while (current != null)
            {
                if (predicate(current))
                    return current;
                current = _getParent(current);
            }
            return default;
        }

        public List<TNode> FindAllAncestors(TNode node, Func<TNode, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(predicate);

            var results = new List<TNode>();
            var current = _getParent(node);
            while (current != null)
            {
                if (predicate(current))
                    results.Add(current);
                current = _getParent(current);
            }
            return results;
        }

        public List<TNode> GetPathToRoot(TNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            var path = new List<TNode>();
            var current = node;
            while (current != null)
            {
                path.Add(current);
                current = _getParent(current);
            }
            return path;
        }

        public int GetDepthDifference(TNode node, TNode ancestor)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(ancestor);

            int depth = 0;
            var current = _getParent(node);
            while (current != null)
            {
                if (EqualityComparer<TNode>.Default.Equals(current, ancestor))
                    return depth + 1;
                depth++;
                current = _getParent(current);
            }
            return -1;
        }

        public bool IsWithinDepth(TNode root, TNode target, int maxDepth)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(target);

            int depth = 0;
            var current = _getParent(target);
            while (current != null)
            {
                if (EqualityComparer<TNode>.Default.Equals(current, root))
                    return depth < maxDepth;
                depth++;
                if (depth >= maxDepth) return false;
                current = _getParent(current);
            }
            return false;
        }

        public bool IsDescendantOf(TNode node, TNode ancestor)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(ancestor);

            var current = _getParent(node);
            while (current != null)
            {
                if (EqualityComparer<TNode>.Default.Equals(current, ancestor))
                    return true;
                current = _getParent(current);
            }
            return false;
        }

        public int GetDepth(TNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            int depth = 0;
            var current = _getParent(node);
            while (current != null)
            {
                depth++;
                current = _getParent(current);
            }
            return depth;
        }

        public TNode GetRoot(TNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            var current = node;
            while (true)
            {
                var parent = _getParent(current);
                if (parent == null)
                    return current;
                current = parent;
            }
        }

        public TNode? FindLowestCommonAncestor(TNode node1, TNode node2)
        {
            ArgumentNullException.ThrowIfNull(node1);
            ArgumentNullException.ThrowIfNull(node2);

            var path1 = GetPathToRoot(node1);
            var path2 = GetPathToRoot(node2);

            int i = path1.Count - 1;
            int j = path2.Count - 1;

            TNode? lca = default;
            while (i >= 0 && j >= 0)
            {
                if (EqualityComparer<TNode>.Default.Equals(path1[i], path2[j]))
                    lca = path1[i];
                else
                    break;
                i--;
                j--;
            }

            return lca;
        }
    }
}
