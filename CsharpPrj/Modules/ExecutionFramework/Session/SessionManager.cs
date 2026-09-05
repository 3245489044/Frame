using System.Collections.Concurrent;

namespace ExecutionFramework.Engine;

using Abstractions;
using TConfig;
using Session;

public interface ISessionManager
{
    IExecutionSession CreateSession(ExecutionPlan plan);

    IExecutionSession CreateSession(ExecutionPlan plan, string sessionId);

    IExecutionSession? GetSession(string sessionId);

    bool RemoveSession(string sessionId);

    IEnumerable<IExecutionSession> GetActiveSessions();

    IEnumerable<IExecutionSession> GetAllSessions();
}

public class DefaultSessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, IExecutionSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISessionFactory _sessionFactory;

    public DefaultSessionManager(ISessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public IExecutionSession CreateSession(ExecutionPlan plan)
    {
        var session = _sessionFactory.CreateSession(plan);
        _sessions[session.SessionId] = session;
        return session;
    }

    public IExecutionSession CreateSession(ExecutionPlan plan, string sessionId)
    {
        var session = _sessionFactory.CreateSession(plan, sessionId);
        _sessions[session.SessionId] = session;
        return session;
    }

    public IExecutionSession? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public bool RemoveSession(string sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }

    public IEnumerable<IExecutionSession> GetActiveSessions()
    {
        return _sessions.Values.Where(s =>
            s.Status == ExecutionStatus.Running ||
            s.Status == ExecutionStatus.Paused);
    }

    public IEnumerable<IExecutionSession> GetAllSessions()
    {
        return _sessions.Values;
    }
}
