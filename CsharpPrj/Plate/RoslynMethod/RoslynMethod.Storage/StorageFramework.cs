#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
#endregion
#nullable disable

namespace StorageFramework
{
    #region 缓存实现

    public class LRUCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly ConcurrentDictionary<TKey, LinkedListNode<CacheItem>> _cache;
        private readonly LinkedList<CacheItem> _accessOrder;
        private readonly ReaderWriterLockSlim _lock = new();
        private bool _disposed;

        private class CacheItem
        {
            public TKey Key { get; }
            public TValue Value { get; set; }
            public DateTime CreatedAt { get; }
            public DateTime LastAccessed { get; set; }

            public CacheItem(TKey key, TValue value)
            {
                Key = key;
                Value = value;
                CreatedAt = DateTime.UtcNow;
                LastAccessed = DateTime.UtcNow;
            }
        }

        public LRUCache(int capacity)
        {
            _capacity = capacity > 0 ? capacity : throw new ArgumentException("Capacity must be positive", nameof(capacity));
            _cache = new ConcurrentDictionary<TKey, LinkedListNode<CacheItem>>();
            _accessOrder = new LinkedList<CacheItem>();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LRUCache<TKey, TValue>));

            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    node.Value.LastAccessed = DateTime.UtcNow;
                    value = node.Value.Value;
                    _lock.EnterWriteLock();
                    try
                    {
                        MoveToFront(node);
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                    return true;
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }

            value = default;
            return false;
        }

        public void Add(TKey key, TValue value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LRUCache<TKey, TValue>));

            _lock.EnterWriteLock();
            try
            {
                if (_cache.TryGetValue(key, out var existingNode))
                {
                    existingNode.Value.Value = value;
                    existingNode.Value.LastAccessed = DateTime.UtcNow;
                    MoveToFront(existingNode);
                    return;
                }

                if (_cache.Count >= _capacity)
                {
                    RemoveLeastRecentlyUsed();
                }

                var item = new CacheItem(key, value);
                var node = new LinkedListNode<CacheItem>(item);
                _accessOrder.AddFirst(node);
                _cache[key] = node;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Remove(TKey key)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LRUCache<TKey, TValue>));

            _lock.EnterWriteLock();
            try
            {
                if (_cache.TryRemove(key, out var node))
                {
                    _accessOrder.Remove(node);
                    return true;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            return false;
        }

        public void Clear()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LRUCache<TKey, TValue>));

            _lock.EnterWriteLock();
            try
            {
                _cache.Clear();
                _accessOrder.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _cache.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        private void MoveToFront(LinkedListNode<CacheItem> node)
        {
            _accessOrder.Remove(node);
            _accessOrder.AddFirst(node);
        }

        private void RemoveLeastRecentlyUsed()
        {
            var lastNode = _accessOrder.Last;
            if (lastNode != null)
            {
                _cache.TryRemove(lastNode.Value.Key, out _);
                _accessOrder.RemoveLast();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _lock?.Dispose();
                _cache.Clear();
                _accessOrder.Clear();
            }
            GC.SuppressFinalize(this);
        }
    }

    #endregion

    #region 内存管理

    public static class MemoryManager
    {
        private static long _lastCheckTime;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

        public static bool HasSufficientMemory(long requiredBytes, object config = null)
        {
            if (requiredBytes <= 0) return true;

            try
            {
                var currentTime = Environment.TickCount64;
                if (currentTime - _lastCheckTime < CheckInterval.TotalMilliseconds)
                    return true;

                _lastCheckTime = currentTime;

                var process = System.Diagnostics.Process.GetCurrentProcess();
                var availableMemory = GetAvailableMemory();

                return availableMemory > requiredBytes * 1.5;
            }
            catch
            {
                return true;
            }
        }

        private static long GetAvailableMemory()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var totalMemory = process.WorkingSet64;
                var memoryLimit = GetMemoryLimit();

                return memoryLimit > 0 ? memoryLimit - totalMemory : long.MaxValue;
            }
            catch
            {
                return long.MaxValue;
            }
        }

        private static long GetMemoryLimit()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                return process.MaxWorkingSet.ToInt64();
            }
            catch
            {
                return long.MaxValue;
            }
        }

        public static long GetCurrentMemoryUsage()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                return process.WorkingSet64;
            }
            catch
            {
                return 0;
            }
        }

        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:0.##} {suffixes[suffixIndex]}";
        }
    }

    #endregion

    #region 自定义异常

    public class InsufficientMemoryException : Exception
    {
        public long RequiredBytes { get; }
        public long AvailableBytes { get; }

        public InsufficientMemoryException(string message, long requiredBytes, long availableBytes = 0)
            : base(message)
        {
            RequiredBytes = requiredBytes;
            AvailableBytes = availableBytes;
        }

        public InsufficientMemoryException(string message) : base(message)
        {
            RequiredBytes = 0;
            AvailableBytes = 0;
        }

        public InsufficientMemoryException(string message, Exception inner)
            : base(message, inner)
        {
            RequiredBytes = 0;
            AvailableBytes = 0;
        }
    }

    #endregion
}
