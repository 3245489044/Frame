
#nullable enable

namespace Framework.Tree
{

    public interface ITreeNode
    {
        string Id { get; }

        ITreeNode? Parent { get; set; }

        IReadOnlyList<ITreeNode> Children { get; }

        void AddChild(ITreeNode child);

        bool RemoveChild(ITreeNode child);
    }


    public interface IBehavior : IDisposable
    {
        void Execute(object? context);
    }


    public class ActionBehavior : IBehavior
    {
        private readonly Action _action;

        public ActionBehavior(Action action) => _action = action;

        public void Execute(object? context) => _action?.Invoke();

        public void Dispose() { }
    }


    public class CompositeBehavior : IBehavior
    {
        private readonly List<IBehavior> _behaviors = new();

        public void Add(IBehavior behavior)
        {
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));
            _behaviors.Add(behavior);
        }

        public bool Remove(IBehavior behavior) => _behaviors.Remove(behavior);

        public void Execute(object? context)
        {
            foreach (var b in _behaviors)
                b.Execute(context);
        }

        public void Dispose()
        {
            foreach (var b in _behaviors)
            {
                try { b?.Dispose(); } catch { }
            }
            _behaviors.Clear();
        }
    }


    public interface ILifecycleManager
    {
        void UpdateNode(TreeNode node, object? context);
    }


    public class DefaultLifecycleManager : ILifecycleManager
    {
        public void UpdateNode(TreeNode node, object? context)
        {
            if (node.State != NodeState.Created) return;
            node.Update(context);

            int i = 0;
            while (i < node.Children.Count)
            {
                var child = node.Children[i];
                if (child is TreeNode treeNode)
                {
                    UpdateNode(treeNode, context);
                    if (treeNode.Parent == node)
                        i++;
                }
                else
                {
                    i++;
                }
            }
        }
    }


    public interface INodeComponent
    {
        void OnAttach(TreeNode node) { }

        void OnDetach(TreeNode node) { }

        void OnCreate(object? context) { }

        void OnDestroy(object? context) { }

        void OnUpdate(object? context) { }
    }


    public enum NodeState
    {
        Initial,
        Created,
        Destroyed
    }


    public abstract class TreeNode : ITreeNode
    {
        public string Id { get; set; } = string.Empty;

        public ITreeNode? Parent { get; set; }

        public TreeNode? TreeNodeParent => Parent as TreeNode;

        private readonly List<ITreeNode> _children = new();

        public IReadOnlyList<ITreeNode> Children => _children;

        public IReadOnlyList<TreeNode> TreeNodeChildren =>
            _children.OfType<TreeNode>().ToList().AsReadOnly();

        public NodeState State { get; protected set; } = NodeState.Initial;

        public bool CanDynamicCreate { get; set; } = true;

        private IBehavior? _externalBehavior;

        public IBehavior? ExternalBehavior
        {
            get => _externalBehavior;
            set
            {
                var old = _externalBehavior;
                _externalBehavior = value;
                old?.Dispose();
            }
        }

        private readonly Dictionary<Type, INodeComponent> _components = new();


        public void AddChild(ITreeNode child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (child.Parent != null)
            {
                if (!child.Parent.RemoveChild(child))
                    throw new InvalidOperationException(
                        $"Failed to remove child (Id: {child.Id ?? "<null>"}) from its current parent (Id: {child.Parent.Id ?? "<null>"})");
            }
            _children.Add(child);
            child.Parent = this;
        }

        public bool RemoveChild(ITreeNode child)
        {
            if (child != null && _children.Remove(child))
            {
                child.Parent = null;
                return true;
            }
            return false;
        }


        public void AddComponent(INodeComponent component)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            var type = component.GetType();
            if (_components.TryGetValue(type, out var old))
            {
                old.OnDetach(this);
                _components.Remove(type);
            }
            _components[type] = component;
            component.OnAttach(this);
        }

        public bool RemoveComponent<T>() where T : INodeComponent
        {
            var type = typeof(T);
            if (_components.TryGetValue(type, out var comp))
            {
                comp.OnDetach(this);
                _components.Remove(type);
                return true;
            }
            return false;
        }

        public T? GetComponent<T>() where T : INodeComponent
        {
            _components.TryGetValue(typeof(T), out var comp);
            return (T?)comp;
        }

        public bool TryGetComponent<T>(out T? component) where T : INodeComponent
        {
            if (_components.TryGetValue(typeof(T), out var comp) && comp is T t)
            {
                component = t;
                return true;
            }
            component = default;
            return false;
        }


        public TreeNode? FindNode(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (Id == id) return this;
            foreach (var child in _children)
            {
                if (child is TreeNode treeNode)
                {
                    var result = treeNode.FindNode(id);
                    if (result != null) return result;
                }
            }
            return null;
        }

        public List<TreeNode> FindNodes(Predicate<TreeNode> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            var results = new List<TreeNode>();
            FindNodesInternal(predicate, results);
            return results;
        }

        private void FindNodesInternal(Predicate<TreeNode> predicate, List<TreeNode> results)
        {
            if (predicate(this)) results.Add(this);
            foreach (var child in _children)
            {
                if (child is TreeNode treeNode)
                    treeNode.FindNodesInternal(predicate, results);
            }
        }


        public virtual void Initialize(object? context) { }

        public virtual void Update(object? context)
        {
            if (State != NodeState.Created) return;

            _externalBehavior?.Execute(context);
            foreach (var comp in _components.Values.ToList())
                comp.OnUpdate(context);
        }

        public void Create(object? context)
        {
            if (State != NodeState.Initial) return;
            OnCreate(context);
            foreach (var child in _children)
            {
                if (child is TreeNode treeNode)
                    treeNode.Create(context);
            }
        }

        protected virtual void OnCreate(object? context)
        {
            State = NodeState.Created;
            foreach (var comp in _components.Values.ToList())
                comp.OnCreate(context);
        }

        public virtual void Destroy(object? context)
        {
            if (State == NodeState.Destroyed) return;

            _externalBehavior?.Dispose();
            _externalBehavior = null;

            DestroyAllChildren(context);

            foreach (var comp in _components.Values.ToList())
            {
                comp.OnDestroy(context);
            }
            _components.Clear();

            State = NodeState.Destroyed;

            Parent?.RemoveChild(this);
        }

        private void DestroyAllChildren(object? context)
        {
            var snapshot = _children.ToList();
            foreach (var child in snapshot)
            {
                if (child is TreeNode treeNode)
                    treeNode.Destroy(context);
            }
        }
    }

}
