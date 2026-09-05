
using System.Collections.Concurrent;

namespace Modules.Threading
{
    public static class ReentryGuard
    {
        private static readonly ConcurrentDictionary<object, int> _reentryDepth = new();

        public static IDisposable Enter(object key)
        {
            ArgumentNullException.ThrowIfNull(key);

            _reentryDepth.AddOrUpdate(key, 1, (_, count) => count + 1);
            return new ReentryScope(key);
        }

        public static bool IsReentering(object key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _reentryDepth.TryGetValue(key, out var depth) && depth > 0;
        }

        public static int GetDepth(object key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _reentryDepth.TryGetValue(key, out var depth) ? depth : 0;
        }

        public static void Reset(object key)
        {
            ArgumentNullException.ThrowIfNull(key);
            _reentryDepth.TryRemove(key, out _);
        }

        public static void ResetAll()
        {
            _reentryDepth.Clear();
        }

        public static IReadOnlyCollection<KeyValuePair<object, int>> GetActiveEntries()
            => _reentryDepth.Where(kvp => kvp.Value > 0).ToList().AsReadOnly();


        private sealed class ReentryScope : IDisposable
        {
            private readonly object _key;
            private bool _disposed;

            public ReentryScope(object key)
            {
                _key = key;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_reentryDepth.TryGetValue(_key, out var depth))
                {
                    if (depth <= 1)
                        _reentryDepth.TryRemove(_key, out _);
                    else
                        _reentryDepth.TryUpdate(_key, depth - 1, depth);
                }
            }
        }
    }
}
