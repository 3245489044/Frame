
namespace Container.Abstractions;

public interface IExecutionEngine : IDisposable
{
    string Name { get; }

    ContainerStatus Status { get; }

    Task<EngineExecutionResult> RunAsync(
        EngineRequest request,
        CancellationToken cancellationToken = default);

    event EventHandler<EngineStateChangedEventArgs>? StateChanged;
}
