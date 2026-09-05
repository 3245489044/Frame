
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CommandFramework.Execution
{
    public sealed class CancellationTokenSourceManager
    {
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _sources =
            new(StringComparer.OrdinalIgnoreCase);

        public CancellationTokenSource Register(string key, CancellationToken parentToken = default)
        {
            var cts = parentToken == CancellationToken.None
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            _sources[key] = cts;
            return cts;
        }

        public CancellationToken GetToken(string key)
            => _sources.TryGetValue(key, out var cts) ? cts.Token : CancellationToken.None;

        public bool Cancel(string key)
        {
            if (_sources.TryGetValue(key, out var cts))
            {
                cts.Cancel();
                return true;
            }
            return false;
        }

        public bool TryRemove(string key)
        {
            if (_sources.TryRemove(key, out var cts))
            {
                cts.Dispose();
                return true;
            }
            return false;
        }

        public void CancelAll()
        {
            foreach (var kvp in _sources)
            {
                try { kvp.Value.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        public void Clear()
        {
            foreach (var cts in _sources.Values)
            {
                try { cts.Dispose(); }
                catch (ObjectDisposedException) { }
            }
            _sources.Clear();
        }
    }
}
