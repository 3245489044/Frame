
using TConfig;
using ExecutionFramework.Engine;
using WorkflowExecution.Pipeline;
using ShareStateStore = Share.StateManagement.IStateStore;

namespace Operation.StateBlock.Abstractions
{
    public interface IStateStore : ShareStateStore
    {
        ITrConfigRegistry? Registry { get; set; }

        IWorkflowRegistry? WorkflowRegistry { get; set; }

        FunctionExecutor? Executor { get; set; }

        PipelineExecutor? Pipeline { get; set; }

        IReadOnlyDictionary<string, object> ExtendedStates { get; }

        TState? Get<TState>(string key);

        void Set<TState>(string key, TState value);

        void ResetObjectState();
    }
}
