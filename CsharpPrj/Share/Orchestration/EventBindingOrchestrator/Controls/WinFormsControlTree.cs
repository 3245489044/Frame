
using System.Collections.Concurrent;
using System.Reflection;
using TConfig.Controls;

namespace Share.EventBindingOrchestrator.Adapters;

public class WinFormsControlTree : IControlTree
{
    private readonly Lazy<IControlNode?> _root;

    public WinFormsControlTree(object form)
    {
        _root = new Lazy<IControlNode?>(() => form != null ? new WinFormsControlNode(form) : null);
    }

    public IControlNode? Root => _root.Value;

    public IControlNode? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Root == null)
            return null;

        return FindCore(Root, name, CreateVisitedSet());
    }

    public IEnumerable<IControlNode> FindAll(Func<IControlNode, bool> predicate)
    {
        if (Root == null)
            yield break;

        var visited = CreateVisitedSet();
        var stack = new Stack<IControlNode>();
        stack.Push(Root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (!TryVisit(node, visited))
                continue;

            if (predicate(node))
                yield return node;

            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }
    }

    public void Walk(Action<IControlNode> action)
    {
        if (Root == null || action == null)
            return;

        var visited = CreateVisitedSet();
        WalkCore(Root, action, visited);
    }

    private static HashSet<object> CreateVisitedSet()
        => new(ReferenceEqualityComparer.Instance);

    private static bool TryVisit(IControlNode node, HashSet<object> visited)
        => node.NativeControl == null || visited.Add(node.NativeControl);

    private static IControlNode? FindCore(IControlNode node, string name, HashSet<object> visited)
    {
        if (!TryVisit(node, visited))
            return null;

        if (string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
            return node;

        foreach (var child in node.Children)
        {
            var found = FindCore(child, name, visited);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void WalkCore(IControlNode node, Action<IControlNode> action, HashSet<object> visited)
    {
        if (!TryVisit(node, visited))
            return;

        action(node);

        foreach (var child in node.Children)
        {
            WalkCore(child, action, visited);
        }
    }
}

internal class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
    public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
