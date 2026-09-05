#region USINGS
using System;
using System.Collections.Generic;
using GraphFramework.Models;
using GraphFramework.Snapshots;
using WorkFlowMethod.Execution.Session;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkflowExecution.Abstractions;
using ShareSessionFactory = ExecutionFramework.Session.DefaultSessionFactory;
#endregion

namespace WorkFlowMethod.Execution.Engine
{
    public class SessionFactory
    {
        private readonly ShareSessionFactory _shareFactory;
        private int _sessionCounter;

        public SessionFactory()
        {
            _shareFactory = new ShareSessionFactory();
        }

        public ShareSessionFactory ShareFactory => _shareFactory;

        public ExecutionSession CreateSession(GraphSnapshot<string, NodeConfig, object> snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var sessionId = GenerateSessionId(snapshot);
            var session = new ExecutionSession(sessionId, snapshot);

            foreach (var kvp in snapshot.Nodes)
            {
                session.GetOrCreateNodeState(kvp.Key);
            }

            return session;
        }

        private string GenerateSessionId(GraphSnapshot<string, NodeConfig, object> snapshot)
        {
            var counter = System.Threading.Interlocked.Increment(ref _sessionCounter);
            return $"session-{snapshot.SnapshotId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{counter}";
        }
    }
}
