
using Modules.StateMachine;

namespace Share.StateManagement
{
    public static class ExecutionExtensions
    {

        public static bool TryStart(this IConditionalStateMachine<ExecutionStatus> machine)
        {
            return machine.TryTransitionFrom(ExecutionStatus.NotStarted, ExecutionStatus.Running);
        }

        public static bool TryPause(this IConditionalStateMachine<ExecutionStatus> machine)
        {
            return machine.TryTransitionFrom(ExecutionStatus.Running, ExecutionStatus.Paused);
        }

        public static bool TryResume(this IConditionalStateMachine<ExecutionStatus> machine)
        {
            return machine.TryTransitionFrom(ExecutionStatus.Paused, ExecutionStatus.Running);
        }

        public static bool TryComplete(this IConditionalStateMachine<ExecutionStatus> machine)
        {
            return machine.TryTransitionTo(ExecutionStatus.Completed,
                s => s != ExecutionStatus.Completed &&
                     s != ExecutionStatus.Faulted &&
                     s != ExecutionStatus.Terminated);
        }

        public static bool TryTerminate(this IConditionalStateMachine<ExecutionStatus> machine)
        {
            return machine.TryTransitionTo(ExecutionStatus.Terminated,
                s => s != ExecutionStatus.Terminated && s != ExecutionStatus.Completed);
        }

        public static bool TryFault(this IConditionalStateMachine<ExecutionStatus> machine)
        {
            return machine.TryTransitionTo(ExecutionStatus.Faulted,
                s => s != ExecutionStatus.Completed && s != ExecutionStatus.Terminated);
        }

        public static bool TryCancel(this IConditionalStateMachine<ExecutionStatus> machine)
        {
            return machine.TryTransitionTo(ExecutionStatus.Cancelled,
                s => s != ExecutionStatus.Completed
                  && s != ExecutionStatus.Faulted
                  && s != ExecutionStatus.Terminated
                  && s != ExecutionStatus.Cancelled);
        }


        public static int PauseAll(this IExecutionStateStore<ExecutionStatus> store)
        {
            return PauseAllWithCleanup(store, null);
        }

        public static int ResumeAll(this IExecutionStateStore<ExecutionStatus> store)
        {
            int count = 0;
            foreach (var id in store.GetAllIds())
            {
                var state = store.Get(id);
                if (state != null && state.CurrentState == ExecutionStatus.Paused && state.TryResume())
                    count++;
            }
            return count;
        }

        public static int TerminateAll(this IExecutionStateStore<ExecutionStatus> store)
        {
            return TerminateAllWithCleanup(store, null);
        }

        public static int TerminateAllWithCleanup(
            this IExecutionStateStore<ExecutionStatus> store,
            Action<string>? cleanupAction = null)
        {
            int count = 0;
            foreach (var id in store.GetAllIds())
            {
                var state = store.Get(id);
                if (state != null && state.TryTerminate())
                {
                    cleanupAction?.Invoke(id);
                    count++;
                }
            }
            return count;
        }

        public static int PauseAllWithCleanup(
            this IExecutionStateStore<ExecutionStatus> store,
            Action<string>? cleanupAction = null)
        {
            int count = 0;
            foreach (var id in store.GetActiveIds())
            {
                var state = store.Get(id);
                if (state != null && state.TryPause())
                {
                    cleanupAction?.Invoke(id);
                    count++;
                }
            }
            return count;
        }


        public static void SetParameters(
            this IExecutionParameterStore store,
            string executionId,
            IDictionary<string, object?> parameters)
        {
            PortGuard.Required(store, nameof(store));
            PortGuard.Required(parameters, nameof(parameters));

            foreach (var kvp in parameters)
            {
                store.SetParameter(executionId, kvp.Key, kvp.Value);
            }
        }

        public static T? GetParameterOrDefault<T>(
            this IExecutionParameterStore store,
            string executionId,
            string key,
            T? defaultValue = default)
        {
            PortGuard.Required(store, nameof(store));

            var value = store.GetParameter(executionId, key);
            if (value == null) return defaultValue;

            try
            {
                return (T)value;
            }
            catch
            {
                return defaultValue;
            }
        }


        public static bool AreAllNodesCompleted(
            this INodeExecutionCoordinator coordinator,
            string executionId)
        {
            PortGuard.Required(coordinator, nameof(coordinator));

            return !coordinator.GetActiveExecutionIds().Contains(executionId);
        }
    }
}
