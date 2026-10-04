
using SkeletonFlow;
using SkeletonFlow.App;
using UniversalWorkflowFramework.BatchIntegration;
using BP = Modules.Pipeline;

namespace Common.Diagnostics;


public class LogPipelineException
{
    public Exception Exception { get; }

    public DateTime OccurredAt { get; }

    public string? StepName { get; set; }

    public LogPipelineException(Exception ex)
    {
        Exception = ex;
        OccurredAt = DateTime.Now;
    }

    public override string ToString() =>
        $"[{OccurredAt:HH:mm:ss.fff}] {Exception.GetType().Name}: {Exception.Message}";
}


public abstract class SyncSkeletonBase : SkeletonBase<LogPipelineContext, LogPipelineContext>
{
    protected SyncSkeletonBase(string name, string description) : base(name, description) { }

    protected sealed override Task<LogPipelineContext> ExecuteCoreAsync(LogPipelineContext input, SkeletonContext ctx)
    {
        return Task.FromResult(ExecuteCore(input, ctx));
    }
}


public class LogInitSkeleton : SyncSkeletonBase
{
    private static readonly ILogger _logger = new SimpleLogger("LogInitSkeleton");

    public LogInitSkeleton() : base("LogInit", "Initialize batch logging and register batch") { }

    protected override LogPipelineContext ExecuteCore(LogPipelineContext input, SkeletonContext ctx)
    {
        if (string.IsNullOrWhiteSpace(input.BatchId))
            throw new ArgumentException("BatchId cannot be empty", nameof(input.BatchId));

        BatchManager.Instance.RegisterBatch(
            input.BatchId,
            input.BatchName,
            input.Source,
            input.WorkflowId,
            input.Description
        );

        if (input.EnabledCategories != LogCategory.None)
        {
            LoggingSetup.Configure(input.EnabledCategories);
            LogRepository.Write("INFO", "Launcher_Bootstrap", _logger.ModuleName, 
                $"Log categories configured: {input.EnabledCategories}");
        }


        ctx.SetData(input, "LogPipelineContext");

        LogRepository.Write("INFO", "Launcher_Bootstrap", _logger.ModuleName, 
            $"Batch logging initialized: {input.BatchId} ({input.BatchName})");

        return input;
    }
}


public class LogFlushSkeleton : SyncSkeletonBase
{
    private static readonly ILogger _logger = new SimpleLogger("LogFlushSkeleton");

    public LogFlushSkeleton() : base("LogFlush", "Flush logs to SQLite storage") { }

    protected override LogPipelineContext ExecuteCore(LogPipelineContext input, SkeletonContext ctx)
    {
        if (string.IsNullOrWhiteSpace(input.BatchId))
            throw new ArgumentException("BatchId cannot be empty", nameof(input.BatchId));

        var count = BatchRouter.Instance.FlushToIsolatedStorage(input.BatchId);
        LogRepository.Write("INFO", "Launcher_Lifecycle", _logger.ModuleName, 
            $"已刷入 {count} 条日志到 SQLite（批次: {input.BatchId}）");

        return input;
    }
}


public class LogEmergencyDumpSkeleton : SyncSkeletonBase
{
    private static readonly ILogger _logger = new SimpleLogger("LogEmergencyDump");

    public LogEmergencyDumpSkeleton() : base("LogEmergencyDump", "Emergency dump logs from memory to SQLite") { }

    protected override LogPipelineContext ExecuteCore(LogPipelineContext input, SkeletonContext ctx)
    {
        try
        {
            EmergencyDumpHelper.EmergencyDumpToSqlite(input.BatchId);
            LogRepository.Write("INFO", "Batch_Routing", _logger.ModuleName, 
                $"紧急转存完成（批次: {input.BatchId}）");
        }
        catch (Exception ex)
        {
            LogRepository.Write("ERROR", "Batch_Routing", _logger.ModuleName, $"紧急转存失败: {ex.Message}");
        }

        return input;
    }
}


public class LogSummarySkeleton : SyncSkeletonBase
{
    private static readonly ILogger _logger = new SimpleLogger("LogSummary");

    public LogSummarySkeleton() : base("LogSummary", "Output log summary after pipeline execution") { }

    protected override LogPipelineContext ExecuteCore(LogPipelineContext input, SkeletonContext ctx)
    {
        var batchId = input.BatchId;
        var logCount = BatchRouter.Instance.GetLogCount(batchId);

        LogRepository.Write("INFO", "Launcher_Lifecycle", _logger.ModuleName, 
            "===== 批次日志摘要 =====");
        LogRepository.Write("INFO", "Launcher_Lifecycle", _logger.ModuleName, 
            $"  批次: {batchId}");
        LogRepository.Write("INFO", "Launcher_Lifecycle", _logger.ModuleName, 
            $"  日志数: {logCount}");

        if (input.TotalCommands > 0)
        {
            LogRepository.Write("INFO", "Launcher_Lifecycle", _logger.ModuleName, 
                $"  命令统计: 总计 {input.TotalCommands}, 成功 {input.SuccessCount}, 失败 {input.FailCount}");
        }

        return input;
    }
}



public class LogTryExecuteSkeleton : SkeletonBase<LogPipelineContext, LogPipelineContext>
{
    private static readonly ILogger _logger = new SimpleLogger("LogTryExecute");
    private readonly ISkeleton<LogPipelineContext, LogPipelineContext> _main;
    private readonly ISkeleton<LogPipelineContext, LogPipelineContext> _fallback;

    public LogTryExecuteSkeleton(
        ISkeleton<LogPipelineContext, LogPipelineContext> main,
        ISkeleton<LogPipelineContext, LogPipelineContext> fallback)
        : base("LogTryExecute", "Try main path, fallback to emergency on exception")
    {
        _main = PortGuard.Required(main, nameof(main));
        _fallback = PortGuard.Required(fallback, nameof(fallback));
    }

    private LogPipelineContext HandleException(LogPipelineContext input, SkeletonContext ctx, Exception ex)
    {
        ctx.SetData(new LogPipelineException(ex) { StepName = _main.Name }, "LastException");

        LogRepository.Write("ERROR", "Batch_Routing", _logger.ModuleName, 
            $"Pipeline main path '{_main.Name}' failed: {ex.Message}, switching to emergency fallback");

        _fallback.Execute(input, ctx);

        return input;
    }

    protected override LogPipelineContext ExecuteCore(LogPipelineContext input, SkeletonContext ctx)
    {
        try
        {
            return _main.Execute(input, ctx);
        }
        catch (Exception ex)
        {
            return HandleException(input, ctx, ex);
        }
    }

    protected override async Task<LogPipelineContext> ExecuteCoreAsync(LogPipelineContext input, SkeletonContext ctx)
    {
        try
        {
            return await _main.ExecuteAsync(input, ctx);
        }
        catch (Exception ex)
        {
            return HandleException(input, ctx, ex);
        }
    }
}


public static class LogPipeline
{
    private static (
        LogInitSkeleton init,
        LogTryExecuteSkeleton tryFlush,
        LogSummarySkeleton summary,
        LogEmergencyDumpSkeleton emergencyDump) CreateBaseSkeletons()
    {
        var init = new LogInitSkeleton();
        var flush = new LogFlushSkeleton();
        var emergencyDump = new LogEmergencyDumpSkeleton();
        var tryFlush = new LogTryExecuteSkeleton(flush, emergencyDump);
        var summary = new LogSummarySkeleton();
        return (init, tryFlush, summary, emergencyDump);
    }

    public static ISkeleton<LogPipelineContext, LogPipelineContext> BuildDefaultPipeline()
    {
        var central = LogPipelineAdapter.CreateDefaultPipeline();
        central.Build();
        return central;
    }

    public static ISkeleton<LogPipelineContext, LogPipelineContext> BuildMinimalPipeline()
    {
        var central = LogPipelineAdapter.CreateMinimalPipeline();
        central.Build();
        return central;
    }

    public static Flow<LogPipelineContext, LogPipelineContext> CreateWithLoggingWrapper(
        ISkeleton<LogPipelineContext, LogPipelineContext> businessSkeleton)
    {
        return LogPipelineAdapter.CreateWithBusinessSkeleton(businessSkeleton);
    }

    public static Flow<LogPipelineContext, LogPipelineContext> CreateWithLoggingWrapperAndExceptionHandling(
        ISkeleton<LogPipelineContext, LogPipelineContext> businessSkeleton)
    {
        return LogPipelineAdapter.CreateWithExceptionHandling(businessSkeleton);
    }

    public static LogPipelineContext ExecuteWithExceptionHandling(
        string batchId,
        string batchName,
        string source = "Launcher",
        string? projectRoot = null)
    {
        return LogPipelineAdapter.ExecuteWithExceptionHandling(batchId, batchName, source, projectRoot);
    }
}
