
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace Logging.Tests.Pipeline
{
    public static class PipelineAssertSkeleton
    {
        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> PipelineSucceeded()
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "Assert:PipelineSucceeded",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.IsNotNull(ctx.PipelineResult, "PipelineResult 不应为 null");
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> PipelineFailed()
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "Assert:PipelineFailed",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.IsNull(ctx.PipelineResult, "PipelineResult 应为 null");
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> StoredLogCountEquals(int expected)
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                $"Assert:StoredLogCountEquals({expected})",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.IsNotNull(ctx.PipelineResult, "PipelineResult 不应为 null");
                    var storage = BatchRouter.Instance.GetIsolatedStorage();
                    Assert.IsNotNull(storage, "IsolatedStorage 不应为 null");
                    var filter = new LogQueryFilter { BatchId = ctx.PipelineResult!.BatchId };
                    var actualCount = storage.Query(filter).Count();
                    Assert.AreEqual(expected, actualCount,
                        $"SQLite 中存储的日志数应为 {expected}，实际为 {actualCount}");
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> TotalCountEquals(int expected)
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                $"Assert:TotalCountEquals({expected})",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.IsNotNull(ctx.PipelineResult, "PipelineResult 不应为 null");
                    Assert.AreEqual(expected, ctx.PipelineResult!.CollectedLogCount,
                        $"收集的日志总数应为 {expected}，实际为 {ctx.PipelineResult.CollectedLogCount}");
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> StorageHasLogs()
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "Assert:StorageHasLogs",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.IsNotNull(ctx.PipelineResult, "PipelineResult 不应为 null");
                    var storage = BatchRouter.Instance.GetIsolatedStorage();
                    Assert.IsNotNull(storage, "IsolatedStorage 不应为 null");
                    var filter = new LogQueryFilter { BatchId = ctx.PipelineResult!.BatchId };
                    var count = storage.Query(filter).Count();
                    Assert.IsTrue(count > 0, "SQLite 中应有日志");
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> StorageHasNoLogs()
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                "Assert:StorageHasNoLogs",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.IsNotNull(ctx.PipelineResult, "PipelineResult 不应为 null");
                    var storage = BatchRouter.Instance.GetIsolatedStorage();
                    Assert.IsNotNull(storage, "IsolatedStorage 不应为 null");
                    var filter = new LogQueryFilter { BatchId = ctx.PipelineResult!.BatchId };
                    var count = storage.Query(filter).Count();
                    Assert.AreEqual(0, count, "SQLite 中应无日志");
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> StorageContainsField(string fieldName)
        {
            return new DelegateSkeleton<PipelineFixtureContext, PipelineFixtureContext>(
                $"Assert:StorageContainsField({fieldName})",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.IsNotNull(ctx.PipelineResult, "PipelineResult 不应为 null");
                    var storage = BatchRouter.Instance.GetIsolatedStorage();
                    Assert.IsNotNull(storage, "IsolatedStorage 不应为 null");
                    var filter = new LogQueryFilter { BatchId = ctx.PipelineResult!.BatchId };
                    var logs = storage.Query(filter).ToList();
                    Assert.IsTrue(logs.Count > 0, "SQLite 中应有日志");
                    Assert.IsTrue(logs.Any(l => l.Extensions.ContainsKey(fieldName) ||
                        l.GetType().GetProperty(fieldName) != null),
                        $"日志应包含字段: {fieldName}");
                    return ctx;
                });
        }

        public static ISkeleton<PipelineFixtureContext, PipelineFixtureContext> Check(string name, Action<PipelineFixtureContext> assertion)
        {
            return Shared.AssertSkeleton.Check<PipelineFixtureContext>(name, assertion);
        }
    }
}
