
#nullable enable
using System.Collections.ObjectModel;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Container;

public class ContainerNode
{
    private readonly List<ContainerNode> _children = new();
    private readonly object _lock = new();

    public string Name { get; }

    public IConfigContainer Container { get; }

    public ContainerNode? Parent { get; private set; }

    public IReadOnlyList<ContainerNode> Children => _children.AsReadOnly();

    public int Depth { get; private set; }

    public string Path { get; private set; }

    public bool IsLeaf => _children.Count == 0;

    public bool IsRoot => Parent == null;

    public ContainerNode(string name, IConfigContainer container)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Container = container ?? throw new ArgumentNullException(nameof(container));
        Path = $"/{name}";
    }

    public ContainerNode AddChild(string name, IConfigContainer container)
    {
        lock (_lock)
        {
            var child = new ContainerNode(name, container)
            {
                Parent = this,
                Depth = Depth + 1,
                Path = $"{Path}/{name}"
            };
            _children.Add(child);
            return child;
        }
    }

    public bool RemoveChild(string name)
    {
        lock (_lock)
        {
            var child = _children.Find(n => n.Name == name);
            if (child != null)
            {
                child.Parent = null;
                return _children.Remove(child);
            }
            return false;
        }
    }

    public ContainerNode? FindNode(string name)
    {
        if (Name == name) return this;

        lock (_lock)
        {
            foreach (var child in _children)
            {
                var found = child.FindNode(name);
                if (found != null) return found;
            }
        }
        return null;
    }

    public ContainerNode? FindByPath(string path)
    {
        if (Path == path) return this;

        lock (_lock)
        {
            foreach (var child in _children)
            {
                var found = child.FindByPath(path);
                if (found != null) return found;
            }
        }
        return null;
    }

    public IEnumerable<ContainerNode> GetAncestors()
    {
        var ancestors = new List<ContainerNode>();
        var current = Parent;
        while (current != null)
        {
            ancestors.Add(current);
            current = current.Parent;
        }
        ancestors.Reverse();
        return ancestors;
    }

    public IEnumerable<ContainerNode> GetDescendants()
    {
        List<ContainerNode> snapshot;
        lock (_lock)
        {
            snapshot = new List<ContainerNode>(_children);
        }

        foreach (var child in snapshot)
        {
            yield return child;
            foreach (var descendant in child.GetDescendants())
            {
                yield return descendant;
            }
        }
    }

    public IEnumerable<ContainerNode> TraversePreOrder()
    {
        yield return this;

        List<ContainerNode> snapshot;
        lock (_lock)
        {
            snapshot = new List<ContainerNode>(_children);
        }

        foreach (var child in snapshot)
        {
            foreach (var node in child.TraversePreOrder())
            {
                yield return node;
            }
        }
    }

    public IEnumerable<ContainerNode> TraversePostOrder()
    {
        List<ContainerNode> snapshot;
        lock (_lock)
        {
            snapshot = new List<ContainerNode>(_children);
        }

        foreach (var child in snapshot)
        {
            foreach (var node in child.TraversePostOrder())
            {
                yield return node;
            }
        }
        yield return this;
    }

    public override string ToString() => $"[{Path}] {Container.ContainerName} (Depth={Depth})";
}
