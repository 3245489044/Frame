
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ConditionalStateMachine = Modules.StateMachine.ConditionalStateMachine<Share.StateManagement.ExecutionStatus>;
using StateMachineStore = Modules.StateMachine.StateMachineStore<Share.StateManagement.ExecutionStatus>;

namespace Share.StateManagement
{

    public enum ExecutionStatus
    {
        NotStarted,
        Running,
        Paused,
        Completed,
        Faulted,
        Terminated,
        Cancelled
    }


    public interface IExecutionStateMachine<TState> : Modules.StateMachine.IConditionalStateMachine<TState>
        where TState : struct, Enum
    {
    }


    public interface IExecutionStateStore<TState> : Modules.StateMachine.IStateMachineStore<TState>
        where TState : struct, Enum
    {
    }


    public class NodeExecutionResult
    {
        public bool Success { get; set; }
        public object? Result { get; set; }
        public Exception? Error { get; set; }
        public IDictionary<string, object>? OutputParameters { get; set; }
    }

    public interface INodeExecutor
    {
        Task<NodeExecutionResult> ExecuteAsync(string nodeKey, IDictionary<string, object> parameters, CancellationToken cancellationToken);
    }

    public interface INodeExecutionCoordinator
    {
        bool TryRegisterNode(string executionId, string nodeKey, out bool isDuplicate);

        Task<bool> ExecuteNodeAsync(
            string executionId,
            string nodeKey,
            Func<Task> executeFunc,
            CancellationToken cancellationToken = default);

        Task<NodeExecutionResult> ExecuteNodeWithParamsAsync(
            string executionId,
            string nodeKey,
            INodeExecutor executor,
            IDictionary<string, object> parameters,
            CancellationToken cancellationToken = default);

        Task WaitForNodeCompletionAsync(
            string executionId,
            string nodeKey,
            CancellationToken cancellationToken = default);

        void MarkNodeCompleted(string executionId, string nodeKey);

        void MarkNodeFailed(string executionId, string nodeKey, Exception error);

        bool IsNodeCompleted(string executionId, string nodeKey);

        string? GetCurrentNode(string executionId);

        void SetCurrentNode(string executionId, string nodeKey);

        IEnumerable<string> GetActiveExecutionIds();

        void CancelAll(string executionId);

        int MaxConcurrency { get; set; }
    }


    public interface IPauseManager : Modules.StateMachine.IPauseManager
    {
    }
}
