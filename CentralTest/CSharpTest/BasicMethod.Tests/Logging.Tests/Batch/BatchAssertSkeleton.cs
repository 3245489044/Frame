
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Batch
{
    public static class BatchAssertSkeleton
    {
        #region 批次注册断言

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> BatchRegistered(string batchId)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:BatchRegistered({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    var metadata = BatchManager.Instance.GetBatch(batchId);
                    Assert.IsNotNull(metadata, $"批次 {batchId} 应已注册");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> BatchNotRegistered(string batchId)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:BatchNotRegistered({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    var metadata = BatchManager.Instance.GetBatch(batchId);
                    Assert.IsNull(metadata, $"批次 {batchId} 应未注册");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> CurrentBatchEquals(string expectedBatchId)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:CurrentBatchEquals({expectedBatchId})",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.AreEqual(expectedBatchId, BatchManager.Instance.CurrentBatchId,
                        $"当前批次应为 {expectedBatchId}");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> BatchCountEquals(int expected)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:BatchCountEquals({expected})",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.AreEqual(expected, BatchManager.Instance.AllBatchIds.Count(),
                        $"批次数量应为 {expected}");
                    return ctx;
                });
        }

        #endregion

        #region 日志断言

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> LogCountEquals(string batchId, int expected)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:LogCountEquals({batchId},{expected})",
                syncWithCtx: (ctx, _) =>
                {
                    var actual = ctx.Storage != null
                        ? Common.Diagnostics.BatchRouter.Instance.GetLogCount(batchId, ctx.Storage)
                        : Common.Diagnostics.BatchRouter.Instance.GetLogCount(batchId);
                    Assert.AreEqual(expected, actual,
                        $"批次 {batchId} 的日志数量应为 {expected}，实际为 {actual}");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> LogCountGreaterThanZero(string batchId)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:LogCountGreaterThanZero({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    var actual = ctx.Storage != null
                        ? Common.Diagnostics.BatchRouter.Instance.GetLogCount(batchId, ctx.Storage)
                        : Common.Diagnostics.BatchRouter.Instance.GetLogCount(batchId);
                    Assert.IsTrue(actual > 0,
                        $"批次 {batchId} 的日志数量应大于 0，实际为 {actual}");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> QueryResultCount(string batchId, int expected)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:QueryResultCount({batchId},{expected})",
                syncWithCtx: (ctx, _) =>
                {
                    var results = ctx.Storage != null
                        ? Common.Diagnostics.BatchRouter.Instance.GetBatchLogs(batchId, ctx.Storage)
                        : Common.Diagnostics.BatchRouter.Instance.GetBatchLogs(batchId);
                    Assert.AreEqual(expected, results?.Count() ?? 0,
                        $"批次 {batchId} 的查询结果数量应为 {expected}");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> StorageIsEmpty(string batchId)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:StorageIsEmpty({batchId})",
                syncWithCtx: (ctx, _) =>
                {
                    var count = ctx.Storage != null
                        ? Common.Diagnostics.BatchRouter.Instance.GetLogCount(batchId, ctx.Storage)
                        : Common.Diagnostics.BatchRouter.Instance.GetLogCount(batchId);
                    Assert.AreEqual(0, count,
                        $"批次 {batchId} 的隔离存储应为空，实际有 {count} 条日志");
                    return ctx;
                });
        }

        #endregion

        #region 元数据断言

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> MetadataFieldEquals(
            string batchId, string fieldName, object? expected)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:MetadataFieldEquals({batchId},{fieldName})",
                syncWithCtx: (ctx, _) =>
                {
                    var metadata = BatchManager.Instance.GetBatch(batchId);
                    Assert.IsNotNull(metadata, $"批次 {batchId} 的元数据不应为 null");

                    if (fieldName is "LogCount" or "ErrorCount" or "WarnCount")
                    {
                        var logs = ctx.Storage != null
                            ? Common.Diagnostics.BatchRouter.Instance.GetBatchLogs(batchId, ctx.Storage)
                            : Common.Diagnostics.BatchRouter.Instance.GetBatchLogs(batchId);
                        BatchManager.Instance.UpdateStats(batchId, logs);
                    }

                    object? actual = fieldName switch
                    {
                        "Name" => metadata!.Name,
                        "Source" => metadata!.Source,
                        "Status" => metadata!.Status.ToString(),
                        "LogCount" => metadata!.LogCount,
                        "ErrorCount" => metadata!.ErrorCount,
                        "WarnCount" => metadata!.WarnCount,
                        _ => throw new ArgumentException($"未知的元数据字段: {fieldName}")
                    };

                    Assert.AreEqual(expected, actual,
                        $"批次 {batchId} 的元数据字段 {fieldName} 应为 {expected}，实际为 {actual}");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> StatusEquals(string batchId, BatchStatus expected)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:StatusEquals({batchId},{expected})",
                syncWithCtx: (ctx, _) =>
                {
                    var metadata = BatchManager.Instance.GetBatch(batchId);
                    Assert.IsNotNull(metadata, $"批次 {batchId} 的元数据不应为 null");
                    Assert.AreEqual(expected, metadata!.Status,
                        $"批次 {batchId} 的状态应为 {expected}，实际为 {metadata.Status}");
                    return ctx;
                });
        }

        #endregion

        #region 对比断言

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> CompareResultCorrect(
            string batchA, string batchB, int expectedAdded, int expectedRemoved)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:CompareResultCorrect({batchA},{batchB})",
                syncWithCtx: (ctx, _) =>
                {
                    var logsA = Common.Diagnostics.BatchRouter.Instance.GetBatchLogs(batchA) ?? new List<LogEntry>();
                    var logsB = Common.Diagnostics.BatchRouter.Instance.GetBatchLogs(batchB) ?? new List<LogEntry>();
                    var result = Common.Diagnostics.LogBatchChange.CompareBatches(logsA, logsB);
                    Assert.AreEqual(expectedAdded, result.Added,
                        $"批次 {batchB} 相对于 {batchA} 应新增 {expectedAdded} 条日志");
                    Assert.AreEqual(expectedRemoved, result.Removed,
                        $"批次 {batchB} 相对于 {batchA} 应删除 {expectedRemoved} 条日志");
                    return ctx;
                });
        }

        #endregion

        #region 异常断言

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> ThrowsException<TException>(
            string name, Action action) where TException : Exception
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:Throws({name})",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.ThrowsException<TException>(action);
                    return ctx;
                });
        }

        #endregion

        #region 选择器断言

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> SelectorCurrentBatchEquals(string? expectedBatchId)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:SelectorCurrentBatchEquals({expectedBatchId})",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.AreEqual(expectedBatchId, BatchSelector.GetCurrentBatch(),
                        $"选择器当前批次应为 {expectedBatchId}");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> SelectorIsViewingHistory(bool expected)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:SelectorIsViewingHistory({expected})",
                syncWithCtx: (ctx, _) =>
                {
                    Assert.AreEqual(expected, BatchSelector.IsViewingHistory(),
                        $"选择器 IsViewingHistory 应为 {expected}");
                    return ctx;
                });
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> SelectorListCountEquals(int expected)
        {
            return new DelegateSkeleton<BatchFixtureContext, BatchFixtureContext>(
                $"Assert:SelectorListCountEquals({expected})",
                syncWithCtx: (ctx, _) =>
                {
                    var items = BatchSelector.GetBatchDisplayList();
                    Assert.AreEqual(expected, items.Count,
                        $"选择器列表数量应为 {expected}，实际为 {items.Count}");
                    return ctx;
                });
        }

        #endregion

        #region 通用断言

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> Check(string name, Func<bool> predicate)
        {
            return Shared.AssertSkeleton.Check<BatchFixtureContext>(name, predicate);
        }

        public static ISkeleton<BatchFixtureContext, BatchFixtureContext> Check(string name, Action assertion)
        {
            return Shared.AssertSkeleton.Check<BatchFixtureContext>(name, assertion);
        }

        #endregion
    }
}
