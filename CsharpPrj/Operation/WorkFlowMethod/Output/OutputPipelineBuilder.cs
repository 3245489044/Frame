
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.EventFramework.Services;
using TConfig.ContextFrame.Abstractions;
using Modules.Pipeline;
using WorkFlowMethod.Output.Stages;

namespace WorkFlowMethod.Output;

public class OutputPipelineBuilder
{
    private readonly List<IPipelineStage> _stages = new();
    private bool _useSnapshot;

    public OutputPipelineBuilder AddStage(IPipelineStage stage)
    {
        _stages.Add(stage);
        return this;
    }

    public OutputPipelineBuilder UseSnapshot()
    {
        _useSnapshot = true;
        return this;
    }

    public Pipeline Build()
    {
        var pipeline = new Pipeline();
        var stages = new List<IPipelineStage>();

        if (_useSnapshot)
        {
            stages.Add(new SnapshotStage(null!));
        }

        stages.AddRange(_stages);

        if (_useSnapshot)
        {
            stages.Add(new RollbackStage(null!));
        }

        pipeline.RegisterStages(stages.ToArray());
        return pipeline;
    }

    public Pipeline BuildFromServiceProvider(IServiceProvider serviceProvider)
    {
        var pipeline = new Pipeline();
        var stages = new List<IPipelineStage>();

        if (_useSnapshot)
        {
            var storageEngine = serviceProvider.GetRequiredService<IStorageEngine>();
            stages.Add(new SnapshotStage(storageEngine));
        }

        var writer = serviceProvider.GetRequiredService<EnhancedValueWriter>();
        stages.Add(new OutputWriteStage(writer));

        if (_useSnapshot)
        {
            var storageEngine = serviceProvider.GetRequiredService<IStorageEngine>();
            stages.Add(new RollbackStage(storageEngine));
        }

        pipeline.RegisterStages(stages.ToArray());
        return pipeline;
    }

    public static OutputPipelineBuilder CreateDefault()
    {
        return new OutputPipelineBuilder();
    }

    public static OutputPipelineBuilder CreateWithSnapshot()
    {
        return new OutputPipelineBuilder().UseSnapshot();
    }
}

public static class OutputPipelineServiceExtensions
{
    public static IServiceCollection AddOutputPipeline(this IServiceCollection services)
    {
        services.TryAddSingleton<Pipeline>(sp =>
            OutputPipelineBuilder.CreateDefault().BuildFromServiceProvider(sp));

        services.TryAddSingleton<OutputPipelineRunner>();

        return services;
    }

    public static IServiceCollection AddOutputPipelineWithSnapshot(this IServiceCollection services)
    {
        services.TryAddSingleton<Pipeline>(sp =>
            OutputPipelineBuilder.CreateWithSnapshot().BuildFromServiceProvider(sp));

        services.TryAddSingleton<OutputPipelineRunner>();

        return services;
    }
}
