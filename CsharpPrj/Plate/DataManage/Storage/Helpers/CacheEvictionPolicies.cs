using System;
using System.Collections.Generic;

namespace DataManage.Storage.Helpers
{
    public interface ICacheEvictionPolicy<TKey>
    {
        void OnAdd(TKey key);
        void OnAccess(TKey key);
        bool TryEvict(out TKey key);
        void OnRemove(TKey key);
        void Clear();
    }

    public sealed class LruEvictionPolicy<TKey> : ICacheEvictionPolicy<TKey> where TKey : notnull
    {
        private readonly LinkedList<TKey> _order = new();
        private readonly Dictionary<TKey, LinkedListNode<TKey>> _nodeMap = new();
        private readonly object _sync = new();

        public void OnAdd(TKey key)
        {
            lock (_sync)
            {
                var node = _order.AddLast(key);
                _nodeMap[key] = node;
            }
        }

        public void OnAccess(TKey key)
        {
            lock (_sync)
            {
                if (_nodeMap.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddLast(node);
                }
            }
        }

        public bool TryEvict(out TKey key)
        {
            lock (_sync)
            {
                if (_order.Count > 0)
                {
                    var first = _order.First!;
                    key = first.Value;
                    _order.RemoveFirst();
                    _nodeMap.Remove(key);
                    return true;
                }
                key = default!;
                return false;
            }
        }

        public void OnRemove(TKey key)
        {
            lock (_sync)
            {
                if (_nodeMap.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _nodeMap.Remove(key);
                }
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _order.Clear();
                _nodeMap.Clear();
            }
        }
    }

    public sealed class FifoEvictionPolicy<TKey> : ICacheEvictionPolicy<TKey> where TKey : notnull
    {
        private readonly Queue<TKey> _queue = new();
        private readonly object _sync = new();

        public void OnAdd(TKey key)
        {
            lock (_sync) _queue.Enqueue(key);
        }

        public void OnAccess(TKey key) { }

        public bool TryEvict(out TKey key)
        {
            lock (_sync)
            {
                if (_queue.Count > 0)
                {
                    key = _queue.Dequeue();
                    return true;
                }
                key = default!;
                return false;
            }
        }

        public void OnRemove(TKey key) { }

        public void Clear()
        {
            lock (_sync) _queue.Clear();
        }
    }

    public sealed class NoEvictionPolicy<TKey> : ICacheEvictionPolicy<TKey> where TKey : notnull
    {
        public void OnAdd(TKey key) { }
        public void OnAccess(TKey key) { }
        public bool TryEvict(out TKey key) { key = default!; return false; }
        public void OnRemove(TKey key) { }
        public void Clear() { }
    }
}