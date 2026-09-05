#nullable enable
using System.Runtime.CompilerServices;

namespace Modules.Common
{
    public class DedupFlags<TKey> where TKey : class
    {
        private readonly ConditionalWeakTable<TKey, object> _flags = new();
        private static readonly object _flag = new();
        private readonly object _lock = new();

        public bool TrySet(TKey key)
        {
            lock (_lock)
            {
                if (_flags.TryGetValue(key, out _))
                    return false;

                _flags.Add(key, _flag);
                return true;
            }
        }

        public List<TKey> TrySetRange(IEnumerable<TKey> keys)
        {
            var result = new List<TKey>();
            lock (_lock)
            {
                foreach (var key in keys)
                {
                    if (!_flags.TryGetValue(key, out _))
                    {
                        _flags.Add(key, _flag);
                        result.Add(key);
                    }
                }
            }
            return result;
        }

        public bool IsSet(TKey key)
        {
            lock (_lock)
            {
                return _flags.TryGetValue(key, out _);
            }
        }

        public List<TKey> IsSetRange(IEnumerable<TKey> keys)
        {
            var result = new List<TKey>();
            lock (_lock)
            {
                foreach (var key in keys)
                {
                    if (_flags.TryGetValue(key, out _))
                        result.Add(key);
                }
            }
            return result;
        }

        public void Reset(TKey key)
        {
            lock (_lock)
            {
                _flags.Remove(key);
            }
        }

        public void ResetRange(IEnumerable<TKey> keys)
        {
            lock (_lock)
            {
                foreach (var key in keys)
                {
                    _flags.Remove(key);
                }
            }
        }

        public void ResetAll() => Clear();

        public void Clear()
        {
            lock (_lock)
            {
                _flags.Clear();
            }
        }
    }
}
