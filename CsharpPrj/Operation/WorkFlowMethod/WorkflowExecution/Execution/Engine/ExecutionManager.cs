#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Share.StateManagement;
using WorkFlowMethod.Execution.Session;
using WorkflowExecution.Abstractions;
using WorkflowExecution.Engine;
#endregion

namespace WorkFlowMethod.Execution.Engine
{
    public class ExecutionManager
    {
        private readonly ConcurrentDictionary<string, ExecutionSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

        public ExecutionManager()
        {
        }

        public int ActiveSessionCount => _sessions.Count;

        public ICollection<string> SessionIds => _sessions.Keys;

        public void RegisterSession(ExecutionSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            _sessions[session.SessionId] = session;
        }

        public ExecutionSession? GetSession(string sessionId)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        public bool TryGetSession(string sessionId, out ExecutionSession? session)
        {
            return _sessions.TryGetValue(sessionId, out session);
        }

        public bool RemoveSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                try { (session as IDisposable)?.Dispose(); }
                catch (Exception ex)
                {
                    WorkFlowMethodLog.Log("ExecutionManager_DisposeError", new
                    {
                        sessionId,
                        error = ex.Message
                    });
                }
                return true;
            }
            return false;
        }

        public bool CancelSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                try
                {
                    var execSession = (ExecutionFramework.Abstractions.IExecutionSession)session;
                    execSession.MarkCancelled();
                    return true;
                }
                catch (Exception ex)
                {
                    WorkFlowMethodLog.Log("ExecutionManager_CancelError", new
                    {
                        sessionId,
                        error = ex.Message
                    });
                }
            }
            return false;
        }

        public List<ExecutionSession> GetSessionsBySnapshot(string snapshotId)
        {
            return _sessions.Values
                .Where(s => s.Snapshot.SnapshotId == snapshotId)
                .ToList();
        }

        public List<ExecutionSession> GetAllSessions()
        {
            return _sessions.Values.ToList();
        }
    }
}
