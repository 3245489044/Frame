
using BasicMethod.Block;

namespace Share.StateManagement
{
    public interface IStateOrchestrator
    {
        IStateStore Store { get; }

        void RegisterActor(IStateActor actor);

        IStateActor? GetActor(string name);

        Task<PipelineResult> ExecutePipelineAsync(ExecutionPipeline pipeline);

        Task InitializeAllActorsAsync();

        Task ShutdownAllActorsAsync();

        void Reset();
    }
}
