
#nullable enable

namespace GraphFoundation.Chain
{
    public class Chain<T> : IReadOnlyList<T>
    {
        private readonly List<T> _nodes = new();
        private readonly object _lock = new();

        public string Name { get; }

        public int Index { get; }

        public int Count
        {
            get { lock (_lock) return _nodes.Count; }
        }

        public T this[int index]
        {
            get { lock (_lock) return _nodes[index]; }
        }

        public Chain(string name, int index = 0)
        {
            Name = name;
            Index = index;
        }

        public Chain(string name, IEnumerable<T> nodes, int index = 0)
        {
            Name = name;
            Index = index;
            _nodes = new List<T>(nodes);
        }

        public void Add(T node)
        {
            lock (_lock) _nodes.Add(node);
        }

        public void Insert(int index, T node)
        {
            lock (_lock)
            {
                if (index < 0 || index > _nodes.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                _nodes.Insert(index, node);
            }
        }

        public bool Remove(T node)
        {
            lock (_lock) return _nodes.Remove(node);
        }

        public void RemoveAt(int index)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _nodes.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                _nodes.RemoveAt(index);
            }
        }

        public void Move(int fromIndex, int toIndex)
        {
            lock (_lock)
            {
                if (fromIndex < 0 || fromIndex >= _nodes.Count)
                    throw new ArgumentOutOfRangeException(nameof(fromIndex));
                if (toIndex < 0 || toIndex >= _nodes.Count)
                    throw new ArgumentOutOfRangeException(nameof(toIndex));

                var node = _nodes[fromIndex];
                _nodes.RemoveAt(fromIndex);
                _nodes.Insert(toIndex, node);
            }
        }

        public void Clear()
        {
            lock (_lock) _nodes.Clear();
        }

        public bool Contains(T node)
        {
            lock (_lock) return _nodes.Contains(node);
        }

        public int IndexOf(T node)
        {
            lock (_lock) return _nodes.IndexOf(node);
        }

        public IReadOnlyList<T> ToReadOnlyList()
        {
            lock (_lock) return _nodes.ToList().AsReadOnly();
        }

        public IEnumerator<T> GetEnumerator()
        {
            List<T> snapshot;
            lock (_lock) snapshot = new List<T>(_nodes);
            return snapshot.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public Chain<T> ShallowClone(string? newName = null)
        {
            lock (_lock)
            {
                return new Chain<T>(newName ?? Name, new List<T>(_nodes), Index);
            }
        }

        public override string ToString() => $"Chain({Name}, Count={Count})";
    }

    public class ChainCollection<T>
    {
        private readonly List<Chain<T>> _chains = new();
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) return _chains.Count; }
        }

        public Chain<T> this[int index]
        {
            get { lock (_lock) return _chains[index]; }
        }

        public Chain<T> Add(string name)
        {
            var chain = new Chain<T>(name, _chains.Count);
            lock (_lock) _chains.Add(chain);
            return chain;
        }

        public bool Remove(Chain<T> chain)
        {
            lock (_lock) return _chains.Remove(chain);
        }

        public Chain<T>? FindByName(string name)
        {
            lock (_lock) return _chains.FirstOrDefault(c => c.Name == name);
        }

        public void Clear()
        {
            lock (_lock) _chains.Clear();
        }

        public IReadOnlyList<Chain<T>> ToReadOnlyList()
        {
            lock (_lock) return _chains.ToList().AsReadOnly();
        }
    }
}
