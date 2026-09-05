
#nullable enable

namespace Framework.Tree
{

    public class TreeIndex<TNode, TKey>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TNode> _store;
        private readonly bool _allowOverwrite;

        public TreeIndex(IEqualityComparer<TKey>? comparer = null, bool allowOverwrite = true)
        {
            _store = new Dictionary<TKey, TNode>(comparer ?? EqualityComparer<TKey>.Default);
            _allowOverwrite = allowOverwrite;
        }

        public int Count => _store.Count;

        public void Rebuild(TNode root, Func<TNode, IEnumerable<TNode>> getChildren,
            Func<TNode, TKey?> keyExtractor)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(getChildren);
            ArgumentNullException.ThrowIfNull(keyExtractor);

            _store.Clear();
            TraverseAndIndex(root, getChildren, keyExtractor);
        }

        public void RebuildPreserveExisting(TNode root,
            Func<TNode, IEnumerable<TNode>> getChildren,
            Func<TNode, TKey?> keyExtractor)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(getChildren);
            ArgumentNullException.ThrowIfNull(keyExtractor);

            TraverseAndIndex(root, getChildren, keyExtractor);
        }

        public IEnumerable<KeyValuePair<TKey, TNode>> GetAll()
        {
            return _store.ToArray();
        }

        public bool TryGet(TKey key, out TNode? node)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.TryGetValue(key, out node);
        }

        public bool Contains(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.ContainsKey(key);
        }

        public IEnumerable<TKey> GetKeys()
        {
            return _store.Keys.ToArray();
        }

        public void Clear()
        {
            _store.Clear();
        }

        public void RegisterNode(TNode node, TKey key)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(key);
            _store[key] = node;
        }

        public void UnregisterNode(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            _store.Remove(key);
        }

        private void TraverseAndIndex(TNode root,
            Func<TNode, IEnumerable<TNode>> getChildren,
            Func<TNode, TKey?> keyExtractor)
        {
            var key = keyExtractor(root);
            if (key != null)
            {
                _store[key] = root;
            }

            foreach (var child in getChildren(root))
            {
                if (child != null)
                {
                    TraverseAndIndex(child, getChildren, keyExtractor);
                }
            }
        }
    }


    public class TreeAdapter<TNode> : ITreeNode
    {
        private readonly TNode _source;
        private readonly Func<TNode, IEnumerable<TNode>> _getChildren;
        private readonly Func<TNode, string> _idExtractor;
        private readonly List<ITreeNode> _adaptedChildren;
        private readonly object _childrenLock = new();
        private bool _childrenInitialized;

        public TreeAdapter(TNode source, Func<TNode, IEnumerable<TNode>> getChildren,
            Func<TNode, string>? idExtractor = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _getChildren = getChildren ?? throw new ArgumentNullException(nameof(getChildren));
            _idExtractor = idExtractor ?? (n => n?.ToString() ?? "");
            _adaptedChildren = new List<ITreeNode>();
        }

        public string Id => _idExtractor(_source);

        public ITreeNode? Parent { get; set; }

        public IReadOnlyList<ITreeNode> Children
        {
            get
            {
                EnsureChildrenInitialized();
                return _adaptedChildren;
            }
        }

        public TNode Source => _source;

        public void AddChild(ITreeNode child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (child.Parent != null)
            {
                child.Parent.RemoveChild(child);
            }
            _adaptedChildren.Add(child);
            child.Parent = this;
        }

        public bool RemoveChild(ITreeNode child)
        {
            if (child != null && _adaptedChildren.Remove(child))
            {
                child.Parent = null;
                return true;
            }
            return false;
        }

        public void InvalidateChildren()
        {
            lock (_childrenLock)
            {
                _adaptedChildren.Clear();
                _childrenInitialized = false;
            }
        }

        private void EnsureChildrenInitialized()
        {
            if (_childrenInitialized) return;

            lock (_childrenLock)
            {
                if (_childrenInitialized) return;

                foreach (var child in _getChildren(_source))
                {
                    if (child != null)
                    {
                        var adapted = new TreeAdapter<TNode>(child, _getChildren, _idExtractor)
                        {
                            Parent = this
                        };
                        _adaptedChildren.Add(adapted);
                    }
                }
                _childrenInitialized = true;
            }
        }

        public override string ToString() => $"TreeAdapter<{typeof(TNode).Name}>[{Id}]";
    }


    public static class CreationKeyTracker
    {
        private static readonly HashSet<string> _createdKeys = new();
        private static readonly object _lock = new();

        public static bool IsFirstCreate(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_lock)
                return _createdKeys.Add(key);
        }

        public static bool IsFirstCreate(string key, bool persistent)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_lock)
            {
                if (_createdKeys.Contains(key))
                    return false;
                if (persistent)
                    _createdKeys.Add(key);
                return true;
            }
        }

        public static void Cleanup()
        {
            lock (_lock)
                _createdKeys.Clear();
        }

        public static void RemoveKey(string key)
        {
            if (!string.IsNullOrEmpty(key))
                lock (_lock)
                    _createdKeys.Remove(key);
        }

        public static int Count
        {
            get { lock (_lock) return _createdKeys.Count; }
        }
    }


    public static class DelegateUtility
    {
        public static TDelegate[] SafeAddAction<TDelegate>(TDelegate[]? existing, TDelegate newAction)
            where TDelegate : Delegate
        {
            if (newAction == null)
                throw new ArgumentNullException(nameof(newAction));

            var nonNullExisting = existing?.Where(d => d != null).ToArray() ?? Array.Empty<TDelegate>();

            if (nonNullExisting.Any(action => AreDelegatesEqual(action, newAction)))
                return nonNullExisting;

            var newList = new List<TDelegate>(nonNullExisting) { newAction };
            return newList.ToArray();
        }

        private static bool AreDelegatesEqual(Delegate? a, Delegate? b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;

            var invListA = a.GetInvocationList();
            var invListB = b.GetInvocationList();
            if (invListA.Length != invListB.Length) return false;

            for (int i = 0; i < invListA.Length; i++)
            {
                var da = invListA[i];
                var db = invListB[i];
                if (da.Target != db.Target || da.Method != db.Method || da.GetType() != db.GetType())
                    return false;
            }
            return true;
        }
    }
}
