
namespace Common.PlaceholderFramework.TreeTraversal;

public enum TraversalMode
{
    BreadthFirst,

    DepthFirst
}

public static class TreeWalker
{

    public static void ForEach<T>(T root, Action<T> action) where T : ITreeNode<T>
        => ForEach(new[] { root }, action, TraversalMode.BreadthFirst);

    public static void ForEach<T>(T root, Action<T> action, TraversalMode mode) where T : ITreeNode<T>
        => ForEach(new[] { root }, action, mode);

    public static void ForEach<T>(IEnumerable<T> roots, Action<T> action,
        TraversalMode mode = TraversalMode.BreadthFirst) where T : ITreeNode<T>
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (action == null) throw new ArgumentNullException(nameof(action));

        if (mode == TraversalMode.BreadthFirst)
            ForEachBreadthFirst(roots, action);
        else
            ForEachDepthFirst(roots, action);
    }

    private static void ForEachBreadthFirst<T>(IEnumerable<T> roots, Action<T> action) where T : ITreeNode<T>
    {
        var queue = new Queue<T>(roots);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            action(node);
            foreach (var child in node.GetChildren())
            {
                queue.Enqueue(child);
            }
        }
    }

    private static void ForEachDepthFirst<T>(IEnumerable<T> roots, Action<T> action) where T : ITreeNode<T>
    {
        var stack = new Stack<T>(roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            action(node);
            var children = node.GetChildren().ToList();
            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }


    public static T? Find<T>(IEnumerable<T> roots, Func<T, bool> predicate) where T : ITreeNode<T>
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        var stack = new Stack<T>(roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (predicate(node))
                return node;

            foreach (var child in node.GetChildren())
            {
                stack.Push(child);
            }
        }
        return default;
    }

    public static List<T> FindAll<T>(IEnumerable<T> roots, Func<T, bool> predicate) where T : ITreeNode<T>
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        var results = new List<T>();
        ForEach(roots, node =>
        {
            if (predicate(node))
                results.Add(node);
        });
        return results;
    }


    public static List<TResult> Collect<T, TResult>(IEnumerable<T> roots, Func<T, TResult?> selector)
        where T : ITreeNode<T>
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        var results = new List<TResult>();
        ForEach(roots, node =>
        {
            var value = selector(node);
            if (value != null)
                results.Add(value);
        });
        return results;
    }


    public static TOut Transform<TIn, TOut>(TIn root, Func<TIn, TOut> mapper,
        Func<TOut, IEnumerable<TOut>> childSelector,
        Action<TOut, IEnumerable<TOut>> childSetter)
        where TIn : ITreeNode<TIn>
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (mapper == null) throw new ArgumentNullException(nameof(mapper));

        var mapped = mapper(root);
        var children = root.GetChildren()
            .Select(child => Transform(child, mapper, childSelector, childSetter))
            .ToList();
        childSetter(mapped, children);
        return mapped;
    }


    public static int Count<T>(T root) where T : ITreeNode<T>
    {
        var count = 0;
        ForEach(root, _ => count++);
        return count;
    }

    public static int MaxDepth<T>(T root) where T : ITreeNode<T>
    {
        if (root == null) return 0;

        var maxChildDepth = 0;
        foreach (var child in root.GetChildren())
        {
            maxChildDepth = Math.Max(maxChildDepth, MaxDepth(child));
        }
        return maxChildDepth + 1;
    }


    public static List<T> FindPath<T>(IEnumerable<T> roots, Func<T, bool> predicate) where T : ITreeNode<T>
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        foreach (var root in roots)
        {
            var path = FindPathInternal(root, predicate, new List<T>());
            if (path != null)
                return path;
        }
        return new List<T>();
    }

    private static List<T>? FindPathInternal<T>(T node, Func<T, bool> predicate, List<T> currentPath)
        where T : ITreeNode<T>
    {
        currentPath.Add(node);

        if (predicate(node))
            return new List<T>(currentPath);

        foreach (var child in node.GetChildren())
        {
            var result = FindPathInternal(child, predicate, currentPath);
            if (result != null)
                return result;
        }

        currentPath.RemoveAt(currentPath.Count - 1);
        return null;
    }
}
