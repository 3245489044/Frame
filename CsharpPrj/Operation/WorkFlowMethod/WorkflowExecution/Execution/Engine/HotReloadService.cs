#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using GraphFramework.Models;
using Share.StateManagement;
using WorkFlowMethod.Execution.Session;
using GraphFramework.Snapshots;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using ExecutionFramework.Abstractions;
using TConfig;
#endregion

namespace WorkFlowMethod.Execution.Engine
{
    public class HotReloadService
    {
        private readonly SessionFactory _sessionFactory;
        private readonly ExecutionManager _executionManager;

        public HotReloadService(SessionFactory sessionFactory, ExecutionManager executionManager)
        {
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            _executionManager = executionManager ?? throw new ArgumentNullException(nameof(executionManager));
        }

        public bool ShouldReload(ExecutionSession session, GraphSnapshot<string, NodeConfig, object> newSnapshot)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (newSnapshot == null) throw new ArgumentNullException(nameof(newSnapshot));

            if (session.Snapshot.SnapshotId == newSnapshot.SnapshotId)
                return false;

            var sessionInterface = (ExecutionFramework.Abstractions.IExecutionSession)session;
            return sessionInterface.Status == TConfig.ExecutionStatus.Running
                || sessionInterface.Status == TConfig.ExecutionStatus.Paused;
        }

        public ReloadResult ReloadSession(ExecutionSession oldSession, GraphSnapshot<string, NodeConfig, object> newSnapshot)
        {
            if (oldSession == null) throw new ArgumentNullException(nameof(oldSession));
            if (newSnapshot == null) throw new ArgumentNullException(nameof(newSnapshot));

            var result = new ReloadResult();

            var dataSnapshot = oldSession.Data.CreateSnapshot();
            var oldSessionInterface = (ExecutionFramework.Abstractions.IExecutionSession)oldSession;
            var oldStatus = oldSessionInterface.Status;

            var newSession = _sessionFactory.CreateSession(newSnapshot);

            newSession.Data.RestoreFromSnapshot(dataSnapshot);

            foreach (var oldNodeState in oldSession.GetAllNodeStates())
            {
                var newNodeState = newSession.GetOrCreateNodeState(oldNodeState.Key);
                switch (oldNodeState.Value.Status)
                {
                    case NodeStatus.Completed:
                        newNodeState.Complete();
                        break;
                    case NodeStatus.Failed:
                        newNodeState.Fail(oldNodeState.Value.ErrorMessage);
                        break;
                    case NodeStatus.Skipped:
                        newNodeState.Skip(oldNodeState.Value.ErrorMessage);
                        break;
                    case NodeStatus.Running:
                        newNodeState.Reset();
                        break;
                    case NodeStatus.Pending:
                        break;
                }
            }

            oldSessionInterface.MarkCancelled();

            _executionManager?.RemoveSession(oldSession.SessionId);

            result.OldSessionId = oldSession.SessionId;
            result.NewSession = newSession;
            result.Success = true;

            return result;
        }

        public List<ReloadResult> ReloadMultipleSessions(
            IEnumerable<ExecutionSession> sessions,
            GraphSnapshot<string, NodeConfig, object> newSnapshot)
        {
            if (sessions == null) throw new ArgumentNullException(nameof(sessions));
            if (newSnapshot == null) throw new ArgumentNullException(nameof(newSnapshot));

            var results = new List<ReloadResult>();
            foreach (var session in sessions)
            {
                if (ShouldReload(session, newSnapshot))
                {
                    results.Add(ReloadSession(session, newSnapshot));
                }
            }
            return results;
        }
    }

    public class ReloadResult
    {
        public bool Success { get; set; }

        public string OldSessionId { get; set; }

        public ExecutionSession NewSession { get; set; }

        public string ErrorMessage { get; set; }
    }
}
