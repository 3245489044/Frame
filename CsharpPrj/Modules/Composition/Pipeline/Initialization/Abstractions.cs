
using TConfig;

namespace Modules.Pipeline.Initialization;


public interface IInitializationOrchestrator<TContext>
{
    Task<TContext> CreateAndConfigureAsync(
        string configPath,
        CancellationToken cancellationToken = default);

    Task<bool> ExecuteScriptAsync(
        TContext context,
        string commandFile,
        CancellationToken cancellationToken = default);

    IStageCoordinator? StageCoordinator { get; }
}


public interface IStageConfigProvider
{
    IReadOnlyList<StageDefinition> GetStages();

    IReadOnlyList<string> GetPrerequisiteStageIds();

    string? GetScriptExecutionStageId();
}


public interface IDependencyInjector<TContext>
{
    Task InjectDependenciesAsync(
        TContext context,
        IStageCoordinator? coordinator,
        IServiceProvider? services,
        CancellationToken cancellationToken);
}


public interface IScriptExecutor<TContext>
{
    Task<bool> ExecuteScriptAsync(
        TContext context,
        string commandFile,
        CancellationToken cancellationToken);
}


public record StageDefinition(
    string StageId,
    string Description,
    int TimeoutMs = 30000);


public enum StageState
{
    Pending,
    Running,
    Completed,
    Failed,
    TimedOut
}
