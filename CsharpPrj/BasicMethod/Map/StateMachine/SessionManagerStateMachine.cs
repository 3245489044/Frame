using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.StateMachine
{
    #region 会话管理器状态机

    public class SessionManagerStateMachine<TState>
        where TState : struct, Enum
    {
        private readonly ConcurrentDictionary<string, ExecutionStateMachineBase<TState>> _sessions;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens;
        private readonly PauseManager _pauseManager;
        private int _maxCapacity;
        private TimeSpan _retentionPeriod;

        public SessionManagerStateMachine(int maxCapacity = 1000, TimeSpan? retentionPeriod = null)
        {
            _sessions = new ConcurrentDictionary<string, ExecutionStateMachineBase<TState>>(StringComparer.OrdinalIgnoreCase);
            _cancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
            _pauseManager = new PauseManager();
            _maxCapacity = maxCapacity;
            _retentionPeriod = retentionPeriod ?? TimeSpan.FromMinutes(5);
        }

        #region 属性

        public int Count => _sessions.Count;

        public int MaxCapacity
        {
            get => _maxCapacity;
            set => _maxCapacity = value;
        }

        public TimeSpan StateRetentionPeriod
        {
            get => _retentionPeriod;
            set => _retentionPeriod = value;
        }

        public PauseManager PauseManager => _pauseManager;

        public bool IsGloballyPaused => _pauseManager.IsGloballyPaused;

        #endregion

        #region 会话管理

        public ExecutionStateMachineBase<TState> CreateSession(
            string sessionId,
            TState initialStatus,
            TState runningState,
            TState pausedState)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session ID cannot be empty", nameof(sessionId));

            CleanupIfNeeded();

            var session = new ExecutionStateMachineBase<TState>(sessionId, initialStatus, runningState, pausedState);
            _sessions.TryAdd(sessionId, session);
            _cancellationTokens.GetOrAdd(sessionId, _ => new CancellationTokenSource());
            return session;
        }

        public ExecutionStateMachineBase<TState>? GetSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return null;

            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }

        public ExecutionStateMachineBase<TState> GetOrCreateSession(
            string sessionId,
            TState initialStatus,
            TState runningState,
            TState pausedState)
        {
            var existing = GetSession(sessionId);
            if (existing != null)
                return existing;

            return CreateSession(sessionId, initialStatus, runningState, pausedState);
        }

        public bool RemoveSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            CancelSession(sessionId);
            return _sessions.TryRemove(sessionId, out _);
        }

        public void CancelSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            if (_cancellationTokens.TryRemove(sessionId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        public void CancelAllSessions()
        {
            foreach (var sessionId in GetAllSessionIds())
            {
                CancelSession(sessionId);
            }
        }

        public CancellationToken? GetCancellationToken(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return null;

            return _cancellationTokens.TryGetValue(sessionId, out var cts)
                ? cts.Token
                : null;
        }

        public IEnumerable<string> GetActiveSessionIds()
        {
            return _sessions
                .Where(kv => kv.Value.IsActive)
                .Select(kv => kv.Key);
        }

        public IEnumerable<string> GetAllSessionIds() => _sessions.Keys.ToArray();

        public int CleanupFinished()
        {
            var cutoff = DateTime.UtcNow - _retentionPeriod;
            var finishedIds = _sessions
                .Where(kv =>
                {
                    var session = kv.Value;
                    return session.IsFinished
                        && session.EndTime.HasValue
                        && session.EndTime.Value < cutoff;
                })
                .Select(kv => kv.Key)
                .ToList();

            foreach (var id in finishedIds)
            {
                CancelSession(id);
                _sessions.TryRemove(id, out _);
            }

            return finishedIds.Count;
        }

        #endregion

        #region 暂停/恢复

        public void RequestGlobalPause() => _pauseManager.RequestGlobalPause();

        public void ResumeGlobalPause() => _pauseManager.ResumeGlobalPause();

        public void RequestLocalPause(string scopeId) => _pauseManager.RequestLocalPause(scopeId);

        public void ResumeLocalPause(string scopeId) => _pauseManager.ResumeLocalPause(scopeId);

        public Task WaitForResumeAsync(string? scopeId, CancellationToken cancellationToken, int pollIntervalMs = 100)
            => _pauseManager.WaitForResumeAsync(scopeId, cancellationToken, pollIntervalMs);

        #endregion

        #region 查询

        public bool HasSession(string sessionId) => _sessions.ContainsKey(sessionId);

        public bool IsSessionActive(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var session) && session.IsActive;
        }

        public bool IsSessionFinished(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var session) && session.IsFinished;
        }

        #endregion

        #region 内部方法

        private void CleanupIfNeeded()
        {
            if (_sessions.Count >= _maxCapacity)
            {
                CleanupFinished();
            }
        }

        #endregion
    }

    #endregion
}
