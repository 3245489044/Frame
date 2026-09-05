#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GraphFramework.Models;
using GraphFramework.Snapshots;
using ExecutionFramework.Abstractions;
using TConfig;
using ExecutionFramework.Session;
using ShareExecutionSession = ExecutionFramework.Session.ExecutionSession;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.Execution.Session
{
    public class ExecutionSession : IExecutionSession, IDisposable
    {
        private readonly ConcurrentDictionary<string, NodeExecutionState> _nodeStates = new();
        private bool _disposed;
        private readonly ShareExecutionSession _shareSession;

        public string SessionId { get; }

        public GraphSnapshot<string, NodeConfig, object> Snapshot { get; }

        public DateTime CreatedAt { get; }

        public DataContext Data { get; }

        public ConcurrentDictionary<string, object> Properties { get; }

        public ShareExecutionSession ShareSession => _shareSession;

        public ExecutionSession(string sessionId, GraphSnapshot<string, NodeConfig, object> snapshot)
        {
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            CreatedAt = DateTime.UtcNow;
            Data = new DataContext(sessionId);
            Properties = new ConcurrentDictionary<string, object>();

            _shareSession = new ShareExecutionSession(sessionId);
        }

        public NodeExecutionState GetOrCreateNodeState(string nodeKey)
        {
            return _nodeStates.GetOrAdd(nodeKey, key => new NodeExecutionState(key));
        }

        public NodeExecutionState GetNodeState(string nodeKey)
        {
            _nodeStates.TryGetValue(nodeKey, out var state);
            return state;
        }

        public void SetNodeState(NodeExecutionState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            _nodeStates[state.NodeKey] = state;
        }

        public void MarkNodeCompleted(string nodeKey) => GetOrCreateNodeState(nodeKey).Complete();

        public void MarkNodeFailed(string nodeKey, string errorMessage) => GetOrCreateNodeState(nodeKey).Fail(errorMessage);

        public void MarkNodeSkipped(string nodeKey, string reason = null) => GetOrCreateNodeState(nodeKey).Skip(reason);

        public IReadOnlyDictionary<string, NodeExecutionState> GetAllNodeStates()
        {
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, NodeExecutionState>(
                new Dictionary<string, NodeExecutionState>(_nodeStates));
        }

        public int CompletedCount => _nodeStates.Values.Count(s => s.Status == NodeStatus.Completed);

        public int FailedCount => _nodeStates.Values.Count(s => s.Status == NodeStatus.Failed);

        public int TotalNodeCount => _nodeStates.Count;


        string IExecutionSession.WorkflowId => SessionId;

        DateTime IExecutionSession.StartTime => _shareSession.StartTime;

        DateTime? IExecutionSession.EndTime => _shareSession.EndTime;

        ExecutionStatus IExecutionSession.Status
            => ((IExecutionSession)_shareSession).Status;

        IDictionary<string, object> IExecutionSession.Metadata
            => Properties as IDictionary<string, object>
               ?? new ConcurrentDictionary<string, object>();

        void IExecutionSession.MarkRunning() => _shareSession.MarkRunning();

        void IExecutionSession.MarkCompleted() => _shareSession.MarkCompleted();

        void IExecutionSession.MarkFailed(string? errorMessage, Exception? exception)
            => _shareSession.MarkFailed(errorMessage, exception);

        void IExecutionSession.MarkCancelled() => _shareSession.MarkCancelled();

        void IExecutionSession.MarkPaused() => _shareSession.MarkPaused();

        void IExecutionSession.MarkResumed() => _shareSession.MarkResumed();

        string? IExecutionSession.GetErrorMessage() => _shareSession.GetErrorMessage();

        Exception? IExecutionSession.GetException() => _shareSession.GetException();

        TimeSpan IExecutionSession.GetExecutionTime() => _shareSession.GetExecutionTime();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _nodeStates.Clear(); } catch { }
            try { Data.Clear(); } catch { }
            try { Properties.Clear(); } catch { }
        }

        public override string ToString()
        {
            return $"ExecutionSession [Id={SessionId}, Status={_shareSession.Status}, Nodes={TotalNodeCount}]";
        }
    }
}
