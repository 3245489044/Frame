
using SkeletonFlow;
using SkeletonFlow.App;
using UniversalWorkflowFramework.BatchIntegration;
using CentralPipe = Modules.CentralPipeline;

namespace Common.Diagnostics;

public static class LogPipelineAdapter
{
    public static CentralPipe.CentralPipeline<LogPipelineContext> CreateDefaultPipeline(
        string? name = null)
    {
        var central = new CentralPipe.CentralPipeline<LogPipelineContext>(
            name ?? "LogPipeline-Default");

        central.Register(CentralPipe.PipelineStage.Initialization, new LogInitSkeleton());
        central.Register(CentralPipe.PipelineStage.PostProcessing,
            new LogTryExecuteSkeleton(new LogFlushSkeleton(), new LogEmergencyDumpSkeleton()));
        central.Register(CentralPipe.PipelineStage.Finalization, new LogSummarySkeleton());

        return central;
    }

    public static CentralPipe.CentralPipeline<LogPipelineContext> CreateMinimalPipeline(
        string? name = null)
    {
        var central = new CentralPipe.CentralPipeline<LogPipelineContext>(
            name ?? "LogPipeline-Minimal");

        central.Register(CentralPipe.PipelineStage.Initialization, new LogInitSkeleton());
        central.Register(CentralPipe.PipelineStage.PostProcessing,
            new LogTryExecuteSkeleton(new LogFlushSkeleton(), new LogEmergencyDumpSkeleton()));

        return central;
    }

    public static Flow<LogPipelineContext, LogPipelineContext> CreateWithBusinessSkeleton(
        ISkeleton<LogPipelineContext, LogPipelineContext> businessSkeleton,
        string? name = null)
    {
        var central = new CentralPipe.CentralPipeline<LogPipelineContext>(
            name ?? "LogPipeline-WithBusiness");

        var wrappedBusiness = new LoggingSkeletonWrapper(businessSkeleton);

        central.Register(CentralPipe.PipelineStage.Initialization, new LogInitSkeleton());
        central.Register(CentralPipe.PipelineStage.Execution, wrappedBusiness);
        central.Register(CentralPipe.PipelineStage.PostProcessing,
            new LogTryExecuteSkeleton(new LogFlushSkeleton(), new LogEmergencyDumpSkeleton()));
        central.Register(CentralPipe.PipelineStage.Finalization, new LogSummarySkeleton());

        central.Build();
        return Flow.FromSkeleton(central);
    }

    public static Flow<LogPipelineContext, LogPipelineContext> CreateWithExceptionHandling(
        ISkeleton<LogPipelineContext, LogPipelineContext> businessSkeleton,
        string? name = null)
    {
        var central = new CentralPipe.CentralPipeline<LogPipelineContext>(
            name ?? "LogPipeline-WithExceptionHandling");

        var wrappedBusiness = new LoggingSkeletonWrapper(businessSkeleton);

        var tryBusiness = new LogTryExecuteSkeleton(wrappedBusiness, new LogEmergencyDumpSkeleton());

        central.Register(CentralPipe.PipelineStage.Initialization, new LogInitSkeleton());
        central.Register(CentralPipe.PipelineStage.Execution, tryBusiness);
        central.Register(CentralPipe.PipelineStage.PostProcessing,
            new LogTryExecuteSkeleton(new LogFlushSkeleton(), new LogEmergencyDumpSkeleton()));
        central.Register(CentralPipe.PipelineStage.Finalization, new LogSummarySkeleton());

        central.Build();
        return Flow.FromSkeleton(central);
    }

    public static LogPipelineContext ExecuteWithExceptionHandling(
        string batchId,
        string batchName,
        string source = "Launcher",
        string? projectRoot = null)
    {
        var central = CreateDefaultPipeline();
        central.Build();

        var context = new LogPipelineContext
        {
            BatchId = batchId,
            BatchName = batchName,
            Source = source,
            ProjectRoot = projectRoot ?? Environment.CurrentDirectory
        };

        var result = central.Execute(context, context);

        try
        {
            BatchManager.Instance.CompleteBatch(batchId);
        }
        catch (Exception ex)
        {
            LogRepository.Write("ERROR", "Batch_Routing", "LogPipeline",
                $"Failed to mark batch '{batchId}' as completed: {ex.Message}");
        }

        return result;
    }
}
