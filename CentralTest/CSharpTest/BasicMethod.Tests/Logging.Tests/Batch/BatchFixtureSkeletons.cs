
using Common.Diagnostics;
using Logging.Tests.Shared;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Batch
{
    public static class BatchFixtureSkeletons
    {
        #region 基础操作

        public static ISkeleton<Unit, BatchFixtureContext> ClearAll()
        {
            return LoggingCleanupSkeleton.ClearAllToBatchContext();
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> RegisterBatch(
            string batchId, string name, string source = "test")
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"BatchFixture:Register({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    BatchManager.Instance.RegisterBatch(batchId, name, source);

                    ctx.CurrentBatchId = batchId;
                    ctx.RegisteredBatches.Add(new BatchMetadata
                    {
                        BatchId = batchId,
                        Name = name,
                        Source = source,
                        Status = BatchStatus.Running,
                        StartTime = DateTime.Now
                    });
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> RegisterBatchWithMetadata(
            string batchId, BatchMetadata metadata)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"BatchFixture:RegisterWithMeta({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    BatchManager.Instance.RegisterBatch(batchId, metadata.Name, metadata.Source);
                    ctx.CurrentBatchId = batchId;
                    ctx.RegisteredBatches.Add(metadata);
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> SetCurrentBatch(string batchId)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"BatchFixture:SetCurrent({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    BatchManager.Instance.SetCurrentBatch(batchId);
                    ctx.CurrentBatchId = batchId;
                    return ctx;
                });
        }

        #endregion

        #region 日志写入

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> WriteLogs(
            int count, string level = "INFO", string module = "test")
        {
            return LoggingWriteSkeleton.WriteLogs<BatchFixtureContext>(
                count, level, module,
                onLogCreated: (ctx, entry) => ctx.GeneratedLogs.Add(entry));
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> WriteLogsToBatch(
            string batchId, int count, string level = "INFO", string module = "test")
        {
            return LoggingWriteSkeleton.WriteLogs<BatchFixtureContext>(
                count, level, module,
                onLogCreated: (ctx, entry) =>
                {
                    entry.Extensions["BatchId"] = batchId;
                    ctx.GeneratedLogs.Add(entry);
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> FlushToStorage()
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                "BatchFixture:FlushToStorage",
                syncWithCtx: (ctx, _) =>
                {
                    if (ctx.CurrentBatchId != null && ctx.Storage != null)
                    {
                        Common.Diagnostics.BatchRouter.Instance.FlushToIsolatedStorage(ctx.CurrentBatchId, ctx.Storage);
                    }
                    LogRepository.Reset();
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> FlushToStorage(string batchId)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"BatchFixture:FlushToStorage({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    if (ctx.Storage != null)
                    {
                        Common.Diagnostics.BatchRouter.Instance.FlushToIsolatedStorage(batchId, ctx.Storage);
                    }
                    LogRepository.Reset();
                    return ctx;
                });
        }

        #endregion

        #region 完整流程

        public static ISkeleton<Unit, BatchFixtureContext> SetupFullFlow(
            string batchId, int logCount, string level = "INFO", string module = "test")
        {
            return ClearAll()
                .Then(RegisterBatch(batchId, $"TestBatch-{batchId}", module))
                .Then(WriteLogs(logCount, level, module))
                .Then(FlushToStorage());
        }

        public static ISkeleton<Unit, BatchFixtureContext> SetupMultiBatchFlow(
            (string batchId, int logCount, string level) batchA,
            (string batchId, int logCount, string level) batchB)
        {
            return ClearAll()
                .Then(RegisterBatch(batchA.batchId, $"TestBatch-{batchA.batchId}", "test"))
                .Then(WriteLogs(batchA.logCount, batchA.level))
                .Then(FlushToStorage())
                .Then(ClearLogRepository())
                .Then(RegisterBatch(batchB.batchId, $"TestBatch-{batchB.batchId}", "test"))
                .Then(WriteLogs(batchB.logCount, batchB.level))
                .Then(FlushToStorage());
        }

        private static ISkeleton<BatchFixtureContext, BatchFixtureContext> ClearLogRepository()
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                "BatchFixture:ClearLogRepo",
                syncWithCtx: (ctx, _) =>
                {
                    LogRepository.Reset();
                    return ctx;
                });
        }

        #endregion

        #region 隔离存储

        public static ISkeleton<Unit, BatchFixtureContext> CreateTempDb()
        {
            return new DelegateSkeleton<Unit, BatchFixtureContext>(
                "BatchFixture:CreateTempDb",
                syncWithCtx: (_, _) =>
                {
                    var tempPath = Path.GetTempFileName();
                    var storage = new SqliteIsolatedStorage(tempPath);
                    storage.Initialize();
                    return new BatchFixtureContext
                    {
                        TempDbPath = tempPath,
                        Storage = storage
                    };
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> CleanupTempDb()
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                "BatchFixture:CleanupTempDb",
                syncWithCtx: (ctx, _) =>
                {
                    if (ctx.TempDbPath != null && File.Exists(ctx.TempDbPath))
                    {
                        try { File.Delete(ctx.TempDbPath); } catch { }
                        ctx.TempDbPath = null;
                    }
                    ctx.Storage = null;
                    return ctx;
                });
        }

        #endregion
    }
}
