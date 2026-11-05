
using System.Collections.Generic;

namespace Modules.ComponentData.Reactor
{
    internal sealed class PriorityChain<TEntry>
    {
        private readonly List<(float Priority, TEntry Entry)> _items = new();
        private readonly object _lock = new();

        public void Add(float priority, TEntry entry)
        {
            lock (_lock)
            {
                int idx = 0;
                while (idx < _items.Count && _items[idx].Priority > priority)
                    idx++;
                _items.Insert(idx, (priority, entry));
            }
        }

        public bool Remove(Predicate<TEntry> match)
        {
            lock (_lock)
            {
                int removed = _items.RemoveAll(item => match(item.Entry));
                return removed > 0;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
            }
        }

        public int Count
        {
            get { lock (_lock) { return _items.Count; } }
        }

        public void ForEach(System.Action<TEntry> action)
        {
            List<(float, TEntry)> snapshot;
            lock (_lock)
            {
                snapshot = new List<(float, TEntry)>(_items);
            }
            foreach (var (_, entry) in snapshot)
            {
                try
                {
                    action(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PriorityChain] ForEach error: {ex.Message}");
                }
            }
        }
    }
}
