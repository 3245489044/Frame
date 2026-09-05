
using Common.Diagnostics;
using SkeletonFlow;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Shared
{
    public static class LoggingWriteSkeleton
    {
        public static ISkeleton<TContext, TContext> WriteLogs<TContext>(
            int count, string level = "INFO", string module = "test",
            Action<TContext, LogEntry>? onLogCreated = null)
            where TContext : class
        {
            return new DelegateSkeleton<TContext, TContext>(
                $"WriteLogs({count},{level})",
                syncWithCtx: (ctx, _) =>
                {
                    var batchId = BatchManager.Instance.CurrentBatchId;

                    for (int i = 0; i < count; i++)
                    {
                        var entry = new LogEntry
                        {
                            Timestamp = DateTime.Now.AddSeconds(-count + i),
                            Level = level,
                            Module = module,
                            Message = $"Test log #{i + 1} from {module}",
                            Category = LogCategory.System
                        };

                        if (!string.IsNullOrEmpty(batchId))
                        {
                            entry.Extensions["BatchId"] = batchId;
                        }

                        LogRepository.Write(entry);
                        onLogCreated?.Invoke(ctx, entry);
                    }
                    return ctx;
                });
        }

        public static ISkeleton<TContext, TContext> WriteLogsWithTemplate<TContext>(
            int count, string level, string module, string messageTemplate,
            Action<TContext, LogEntry>? onLogCreated = null)
            where TContext : class
        {
            return new DelegateSkeleton<TContext, TContext>(
                $"WriteLogs({count},{level})",
                syncWithCtx: (ctx, _) =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        var entry = new LogEntry
                        {
                            Timestamp = DateTime.Now.AddSeconds(-count + i),
                            Level = level,
                            Module = module,
                            Message = messageTemplate.Replace("{i}", (i + 1).ToString()),
                            Category = LogCategory.System
                        };
                        LogRepository.Write(entry);
                        onLogCreated?.Invoke(ctx, entry);
                    }
                    return ctx;
                });
        }
    }
}
