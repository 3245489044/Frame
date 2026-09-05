
#nullable enable
using System.Collections.Concurrent;
using TConfig.Abstractions;
using UniversalMiddleware.Abstractions;

namespace UniversalMiddleware.Rendering.Lifecycle;

public class SessionManager
{
    private readonly ConcurrentDictionary<string, IRenderSession> _sessions = new(StringComparer.Ordinal);

    public int ActiveSessionCount => _sessions.Count;

    public IEnumerable<string> ActiveSessionIds => _sessions.Keys;

    public void Register(IRenderSession session)
    {
        _sessions.TryAdd(session.SessionId, session);
    }

    public void Unregister(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    public IRenderSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public void CleanupCompleted()
    {
        var completedIds = _sessions
            .Where(kvp => kvp.Value.IsCompleted)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in completedIds)
        {
            _sessions.TryRemove(id, out _);
        }
    }

    public void ClearAll()
    {
        _sessions.Clear();
    }
}
