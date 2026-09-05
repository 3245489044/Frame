
using System;
using System.IO;
using System.Text.Json;
using Modules.DiagnosticContext;
using UniversalWorkflowFramework.BatchIntegration;

namespace Common.Diagnostics
{
    public static class BatchLoggingBootstrapper
    {
        private static string DefaultBatchSource => GetDefaultBatchSourceFromConfig();

        public static string Initialize(
            string? batchId = null,
            string? batchName = null,
            string? batchSource = null,
            string? batchDesc = null,
            Action<string, object?>? logAction = null)
        {
            batchId ??= Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_ID");
            batchName ??= Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_NAME");
            batchSource ??= Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_SOURCE") ?? DefaultBatchSource;
            batchDesc ??= Environment.GetEnvironmentVariable("FILEDRIVEN_BATCH_DESC");

            if (string.IsNullOrWhiteSpace(batchId))
            {
                batchId = $"Run_{DateTime.Now:yyyyMMdd_HHmmss}";
                batchName ??= batchId;
                logAction?.Invoke("Executor_InitializeBatchLogging_auto_generated",
                    new { batchId });
            }

            batchName ??= batchId;

            BatchStatisticsBackfill.EnsureSubscribed();
            BatchManager.Instance.RegisterBatch(batchId, batchName, batchSource, batchDesc);

            logAction?.Invoke("Executor_InitializeBatchLogging_ok",
                new { batchId, batchName });

            return batchId;
        }

        private static string GetDefaultBatchSourceFromConfig()
        {
            try
            {
                var source = DiagnosticsConfigReader.GetNestedString(typeof(BatchLoggingBootstrapper), "batch", "default_source");
                if (!string.IsNullOrEmpty(source))
                    return source;
            }
            catch
            {
            }
            return "Executor";
        }
    }
}
