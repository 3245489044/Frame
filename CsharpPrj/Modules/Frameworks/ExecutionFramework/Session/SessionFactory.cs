namespace ExecutionFramework.Session;

using Abstractions;
using TConfig;

public interface ISessionFactory
{
    IExecutionSession CreateSession(ExecutionPlan plan);

    IExecutionSession CreateSession(ExecutionPlan plan, string sessionId);
}

public class DefaultSessionFactory : ISessionFactory
{
    protected int _sessionCounter;

    public IExecutionSession CreateSession(ExecutionPlan plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        var sessionId = GenerateSessionId(plan);
        return new ExecutionSession(plan.Id, sessionId);
    }

    public IExecutionSession CreateSession(ExecutionPlan plan, string sessionId)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrEmpty(sessionId)) throw new ArgumentException("会话 ID 不能为空", nameof(sessionId));

        return new ExecutionSession(plan.Id, sessionId);
    }

    private string GenerateSessionId(ExecutionPlan plan)
    {
        var counter = Interlocked.Increment(ref _sessionCounter);
        return $"session-{plan.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}-{counter}";
    }
}
