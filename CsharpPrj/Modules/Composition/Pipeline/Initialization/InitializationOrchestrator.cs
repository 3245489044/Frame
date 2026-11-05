
using Microsoft.Extensions.DependencyInjection;
using TConfig;

namespace Modules.Pipeline.Initialization;

public class InitializationOrchestrator<TContext> : IInitializationOrchestrator<TContext>
{
    protected readonly IServiceProvider? ServiceProvider;

    protected readonly IStageConfigProvider StageConfigProvider;

    protected readonly IDependencyInjector<TContext>? DependencyInjector;

    protected readonly IScriptExecutor<TContext>? ScriptExecutor;

    public IStageCoordinator? StageCoordinator { get; protected set; }



    public InitializationOrchestrator(
        IServiceProvider? serviceProvider = null,
        IStageConfigProvider? stageConfigProvider = null,
        IDependencyInjector<TContext>? dependencyInjector = null,
        IScriptExecutor<TContext>? scriptExecutor = null)
    {
        ServiceProvider = serviceProvider;
        StageConfigProvider = stageConfigProvider ?? new EmptyStageConfigProvider();
        DependencyInjector = dependencyInjector;
        ScriptExecutor = scriptExecutor;
    }



    public virtual async Task<TContext> CreateAndConfigureAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configPath);

        StageCoordinator = await CreateStageCoordinatorAsync(cancellationToken);

        await RegisterStagesAsync(cancellationToken);

        var context = await CreateContextAsync(configPath, cancellationToken);

        await InjectDependenciesAsync(context, cancellationToken);

        await StartStageCoordinatorAsync(cancellationToken);

        return context;
    }

    public virtual async Task<bool> ExecuteScriptAsync(
        TContext context,
        string commandFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandFile);

        await WaitForPrerequisiteStagesAsync(cancellationToken);

        var success = await ExecuteScriptInternalAsync(context, commandFile, cancellationToken);

        await CompleteScriptExecutionStageAsync(success, cancellationToken);

        return success;
    }



    protected virtual Task<IStageCoordinator> CreateStageCoordinatorAsync(
        CancellationToken cancellationToken)
    {
        var coordinator = ServiceProvider?.GetService<IStageCoordinator>();
        if (coordinator != null)
            return Task.FromResult(coordinator);

        throw new InvalidOperationException(
            "IStageCoordinator 未在 DI 容器中注册，请提供自定义实现或注册 IStageCoordinator。");
    }

    protected virtual Task RegisterStagesAsync(
        CancellationToken cancellationToken)
    {
        var coordinator = StageCoordinator;
        if (coordinator == null)
            return Task.CompletedTask;

        var stages = StageConfigProvider.GetStages();
        foreach (var stage in stages)
        {
            coordinator.RegisterStage(stage.StageId, stage.Description, stage.TimeoutMs);
        }

        return Task.CompletedTask;
    }

    protected virtual Task<TContext> CreateContextAsync(
        string configPath,
        CancellationToken cancellationToken)
    {
        if (ServiceProvider != null)
        {
            var context = ServiceProvider.GetRequiredService<TContext>();
            return Task.FromResult(context);
        }

        throw new InvalidOperationException(
            "请提供自定义的上下文创建逻辑或注册 IServiceProvider。");
    }

    protected virtual Task InjectDependenciesAsync(
        TContext context,
        CancellationToken cancellationToken)
    {
        if (DependencyInjector == null)
            return Task.CompletedTask;

        return DependencyInjector.InjectDependenciesAsync(
            context, StageCoordinator, ServiceProvider, cancellationToken);
    }

    protected virtual Task StartStageCoordinatorAsync(
        CancellationToken cancellationToken)
    {
        var coordinator = StageCoordinator;
        if (coordinator == null)
            return Task.CompletedTask;

        coordinator.Start();
        return Task.CompletedTask;
    }

    protected virtual async Task WaitForPrerequisiteStagesAsync(
        CancellationToken cancellationToken)
    {
        var coordinator = StageCoordinator;
        if (coordinator == null)
            return;

        var stageIds = StageConfigProvider.GetPrerequisiteStageIds();
        if (stageIds.Count == 0)
            return;

        await PipelineHelper.WaitForStagesAsync(
            coordinator,
            stageIds,
            cancellationToken);
    }

    protected virtual Task<bool> ExecuteScriptInternalAsync(
        TContext context,
        string commandFile,
        CancellationToken cancellationToken)
    {
        if (ScriptExecutor == null)
        {
            throw new InvalidOperationException(
                "IScriptExecutor 未设置，请提供脚本执行策略。");
        }

        return ScriptExecutor.ExecuteScriptAsync(context, commandFile, cancellationToken);
    }

    protected virtual Task CompleteScriptExecutionStageAsync(
        bool success,
        CancellationToken cancellationToken)
    {
        var coordinator = StageCoordinator;
        var scriptStageId = StageConfigProvider.GetScriptExecutionStageId();

        if (coordinator != null && scriptStageId != null)
        {
            coordinator.CompleteStage(scriptStageId, success);
        }

        return Task.CompletedTask;
    }



    protected class EmptyStageConfigProvider : IStageConfigProvider
    {
        public IReadOnlyList<StageDefinition> GetStages() => Array.Empty<StageDefinition>();
        public IReadOnlyList<string> GetPrerequisiteStageIds() => Array.Empty<string>();
        public string? GetScriptExecutionStageId() => null;
    }
}
