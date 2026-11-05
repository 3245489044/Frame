
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using ContractFramework.Codes;
using ContractFramework.Diagnostics;
using Modules.DiagnosticContext;
using UniversalWorkflowFramework.BatchIntegration;

namespace Common.Diagnostics
{
    /// <summary>批次日志受控码（配置读取失败等；单一实现 = CodeSet）。</summary>
    public static class BatchLoggingCodes
    {
        /// <summary>批次配置读取失败（已收敛为可见诊断，禁静默吞）。</summary>
        public const string ConfigReadFailed = "batch_config_read_failed";

        /// <summary>受控码集。</summary>
        public static readonly CodeSet Codes = new("batch-logging", ConfigReadFailed);
    }

    public static class BatchLoggingBootstrapper
    {
        /// <summary>启动期诊断（配置读取失败等；出口 = DiagnosticBag）。</summary>
        public static DiagnosticBag Diagnostics { get; } = new(BatchLoggingCodes.Codes);

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
                batchId = "Run_" + LoggingSetup.Clock.UtcNow.ToLocalTime()
                    .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                batchName ??= batchId;
                logAction?.Invoke("Executor_InitializeBatchLogging_auto_generated",
                    new { batchId });
            }

            batchName ??= batchId;

            BatchStatisticsBackfill.EnsureSubscribed();
            // 装配点：把批次打标适配器装到日志工厂端口（BasicMethod 只定义端口，不反向依赖 Batch）。
            LoggingSetup.EntryFactory = BatchEntryFactory.Instance;
            // 5 参重载：batchDesc 落到 description，workflowId 保持为空（§4.8）。
            BatchManager.Instance.RegisterBatch(batchId, batchName, batchSource, workflowId: null, description: batchDesc);

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
            catch (Exception ex)
            {
                // E-06：不再静默吞；保留缺省值但记录原因（受控码 + 异常类型）。
                Diagnostics.Add(BatchLoggingCodes.ConfigReadFailed, nameof(GetDefaultBatchSourceFromConfig), ex.GetType().Name);
            }
            return "Executor";
        }
    }
}
