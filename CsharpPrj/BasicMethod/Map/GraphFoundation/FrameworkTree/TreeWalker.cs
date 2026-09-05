
#nullable enable

namespace Framework.Tree
{
    public enum TraversalStrategy
    {
        DepthFirst,
        BreadthFirst,
        PreOrder,
        PostOrder,
    }

    public class TraversalOptions
    {
        public TraversalStrategy Strategy { get; set; } = TraversalStrategy.DepthFirst;

        public int MaxDepth { get; set; } = 0;

        public bool IncludeRoot { get; set; } = true;
    }

    public class TreeWalker<TNode>
    {
        private readonly Func<TNode, IEnumerable<TNode>> _getChildren;

        public TreeWalker(Func<TNode, IEnumerable<TNode>> getChildren)
        {
            _getChildren = getChildren ?? throw new ArgumentNullException(nameof(getChildren));
        }

        public void Walk(TNode root, Action<TNode, int> action, TraversalOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(action);

            options ??= new TraversalOptions();

            switch (options.Strategy)
            {
                case TraversalStrategy.BreadthFirst:
                    WalkBFS(root, action, options);
                    break;
                case TraversalStrategy.PostOrder:
                    WalkPostOrder(root, action, options, 0);
                    break;
                case TraversalStrategy.DepthFirst:
                case TraversalStrategy.PreOrder:
                default:
                    WalkPreOrder(root, action, options, 0);
                    break;
            }
        }

        public async Task WalkAsync(TNode root, Func<TNode, int, Task> asyncAction, TraversalOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(asyncAction);

            options ??= new TraversalOptions();

            switch (options.Strategy)
            {
                case TraversalStrategy.BreadthFirst:
                    await WalkBFSAsync(root, asyncAction, options);
                    break;
                case TraversalStrategy.PostOrder:
                    await WalkPostOrderAsync(root, asyncAction, options, 0);
                    break;
                case TraversalStrategy.DepthFirst:
                case TraversalStrategy.PreOrder:
                default:
                    await WalkPreOrderAsync(root, asyncAction, options, 0);
                    break;
            }
        }


        public TNode? FindFirst(TNode root, Func<TNode, bool> predicate, TraversalOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(predicate);

            TNode? result = default;
            Walk(root, (node, _) =>
            {
                if (result == null && predicate(node))
                    result = node;
            }, options);
            return result;
        }

        public List<TNode> FindAll(TNode root, Func<TNode, bool> predicate, TraversalOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(predicate);

            var results = new List<TNode>();
            Walk(root, (node, _) =>
            {
                if (predicate(node))
                    results.Add(node);
            }, options);
            return results;
        }

        public List<TNode> GetPathTo(TNode root, TNode target)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(target);

            var path = new List<TNode>();
            if (FindPath(root, target, path))
                return path;
            return new List<TNode>();
        }

        public List<List<TNode>> GetAllLeafPaths(TNode root)
        {
            ArgumentNullException.ThrowIfNull(root);

            var paths = new List<List<TNode>>();
            var currentPath = new List<TNode>();
            FindAllLeafPaths(root, currentPath, paths);
            return paths;
        }

        public int GetDepth(TNode root)
        {
            ArgumentNullException.ThrowIfNull(root);

            int maxDepth = 0;
            Walk(root, (_, depth) =>
            {
                if (depth > maxDepth)
                    maxDepth = depth;
            }, new TraversalOptions { Strategy = TraversalStrategy.PreOrder });
            return maxDepth + 1;
        }

        public int Count(TNode root)
        {
            ArgumentNullException.ThrowIfNull(root);

            int count = 0;
            Walk(root, (_, _) => count++);
            return count;
        }

        public List<TNode> Flatten(TNode root)
        {
            ArgumentNullException.ThrowIfNull(root);

            var result = new List<TNode>();
            Walk(root, (node, _) => result.Add(node));
            return result;
        }


        private bool FindPath(TNode current, TNode target, List<TNode> path)
        {
            path.Add(current);
            if (EqualityComparer<TNode>.Default.Equals(current, target))
                return true;

            foreach (var child in _getChildren(current))
            {
                if (child != null && FindPath(child, target, path))
                    return true;
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }

        private void FindAllLeafPaths(TNode node, List<TNode> currentPath, List<List<TNode>> paths)
        {
            currentPath.Add(node);

            var children = _getChildren(node)?.Where(c => c != null).ToList();
            if (children == null || children.Count == 0)
            {
                paths.Add(new List<TNode>(currentPath));
            }
            else
            {
                foreach (var child in children)
                {
                    FindAllLeafPaths(child, currentPath, paths);
                }
            }

            currentPath.RemoveAt(currentPath.Count - 1);
        }

        private void WalkPreOrder(TNode node, Action<TNode, int> action, TraversalOptions options, int depth)
        {
            if (options.MaxDepth > 0 && depth > options.MaxDepth) return;
            if (depth > 0 || options.IncludeRoot)
                action(node, depth);

            foreach (var child in _getChildren(node))
            {
                if (child != null)
                    WalkPreOrder(child, action, options, depth + 1);
            }
        }

        private void WalkPostOrder(TNode node, Action<TNode, int> action, TraversalOptions options, int depth)
        {
            if (options.MaxDepth > 0 && depth > options.MaxDepth) return;

            foreach (var child in _getChildren(node))
            {
                if (child != null)
                    WalkPostOrder(child, action, options, depth + 1);
            }

            if (depth > 0 || options.IncludeRoot)
                action(node, depth);
        }

        private void WalkBFS(TNode root, Action<TNode, int> action, TraversalOptions options)
        {
            var queue = new Queue<(TNode Node, int Depth)>();
            if (options.IncludeRoot)
                queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (node, depth) = queue.Dequeue();
                if (options.MaxDepth > 0 && depth > options.MaxDepth) continue;

                action(node, depth);

                foreach (var child in _getChildren(node))
                {
                    if (child != null)
                        queue.Enqueue((child, depth + 1));
                }
            }
        }


        private async Task WalkPreOrderAsync(TNode node, Func<TNode, int, Task> action, TraversalOptions options, int depth)
        {
            if (options.MaxDepth > 0 && depth > options.MaxDepth) return;
            if (depth > 0 || options.IncludeRoot)
                await action(node, depth);

            foreach (var child in _getChildren(node))
            {
                if (child != null)
                    await WalkPreOrderAsync(child, action, options, depth + 1);
            }
        }

        private async Task WalkPostOrderAsync(TNode node, Func<TNode, int, Task> action, TraversalOptions options, int depth)
        {
            if (options.MaxDepth > 0 && depth > options.MaxDepth) return;

            foreach (var child in _getChildren(node))
            {
                if (child != null)
                    await WalkPostOrderAsync(child, action, options, depth + 1);
            }

            if (depth > 0 || options.IncludeRoot)
                await action(node, depth);
        }

        private async Task WalkBFSAsync(TNode root, Func<TNode, int, Task> action, TraversalOptions options)
        {
            var queue = new Queue<(TNode Node, int Depth)>();
            if (options.IncludeRoot)
                queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (node, depth) = queue.Dequeue();
                if (options.MaxDepth > 0 && depth > options.MaxDepth) continue;

                await action(node, depth);

                foreach (var child in _getChildren(node))
                {
                    if (child != null)
                        queue.Enqueue((child, depth + 1));
                }
            }
        }
    }
}


