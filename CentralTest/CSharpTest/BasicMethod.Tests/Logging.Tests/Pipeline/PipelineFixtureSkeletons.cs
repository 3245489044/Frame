
using Common.Diagnostics;
using Logging.Tests.Shared;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Pipeline
{
    public static class PipelineFixtureSkeletons
    {
        public static ISkeleton<Unit, PipelineFixtureContext> ClearAll()
        {
            return LoggingCleanupSkeleton.ClearAllToPipelineContext();
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> SetEnvironmentVariables(
            string batchId, string batchName, string source, string? description = null)
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "PipelineFixture:SetEnvVars",
                syncWithCtx: (ctx, _) =>
                {
                    Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_ID", batchId);
                    Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_NAME", batchName);
                    Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE", source);
                    if (description != null)
                        Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_DESC", description);

                    ctx.BatchId = batchId;
                    ctx.BatchName = batchName;
                    ctx.Source = source;
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> ClearEnvironmentVariables()
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "PipelineFixture:ClearEnvVars",
                syncWithCtx: (ctx, _) =>
                {
                    Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_ID", null);
                    Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_NAME", null);
                    Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE", null);
                    Environment.SetEnvironmentVariable("FILEDRIVEN_BATCH_DESC", null);
                    Environment.SetEnvironmentVariable("FILEDRIVEN_PROJECT_ROOT", null);
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> SetProjectRoot(string projectRoot)
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "PipelineFixture:SetProjectRoot",
                syncWithCtx: (ctx, _) =>
                {
                    Environment.SetEnvironmentVariable("FILEDRIVEN_PROJECT_ROOT", projectRoot);
                    ctx.ProjectRoot = projectRoot;
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> RegisterBatch(
            string batchId, string name, string source = "Launcher")
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                $"PipelineFixture:RegisterBatch({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    var metadata = new BatchMetadata
                    {
                        Name = name,
                        Source = source,
                        Status = BatchStatus.Running,
                        StartTime = DateTime.Now
                    };
                    BatchManager.Instance.RegisterBatch(batchId, metadata.Name, metadata.Source);

                    ctx.BatchId = batchId;
                    ctx.BatchName = name;
                    ctx.Source = source;
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> WriteLogs(
            int count, string level = "INFO", string module = "Test")
        {
            return LoggingWriteSkeleton.WriteLogs<PipelineFixtureContext>(
                count, level, module,
                onLogCreated: (ctx, entry) => ctx.GeneratedLogs.Add(entry));
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> ExecutePipeline(
            string projectRoot)
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "PipelineFixture:ExecutePipeline",
                syncWithCtx: (ctx, _) =>
                {
                    var result = LogPipeline.ExecuteWithExceptionHandling(
                        batchId: ctx.BatchId ?? "test_pipeline",
                        batchName: ctx.BatchName ?? "Test Pipeline",
                        source: ctx.Source ?? "PipelineTest",
                        projectRoot: projectRoot
                    );
                    ctx.PipelineResult = result;
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> ExecuteBasicPipeline(
            string projectRoot)
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "PipelineFixture:ExecuteBasicPipeline",
                syncWithCtx: (ctx, _) =>
                {
                    var result = LogPipeline.Execute(
                        batchId: ctx.BatchId ?? "test_pipeline",
                        batchName: ctx.BatchName ?? "Test Pipeline",
                        source: ctx.Source ?? "PipelineTest",
                        projectRoot: projectRoot
                    );
                    ctx.PipelineResult = result;
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> SimulateStorageFailure()
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "PipelineFixture:SimulateStorageFailure",
                syncWithCtx: (ctx, _) =>
                {
                    ctx.StorageFailureMode = true;
                    return ctx;
                });
        }
    }
}
