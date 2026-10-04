
using SkeletonFlow;

namespace Common.Diagnostics;


public static class LogEntryBuilder
{
    public static void Info(string module, string message, string category = "General")
        => LogRepository.Write("INFO", category, module, message);

    public static void Warn(string module, string message, string category = "General")
        => LogRepository.Write("WARN", category, module, message);

    public static void Error(string module, string message, string category = "General")
        => LogRepository.Write("ERROR", category, module, message);

    public static void Debug(string module, string message, string category = "General")
        => LogRepository.Write("DEBUG", category, module, message);

}


public class LoggingSkeletonWrapper : ISkeleton<LogPipelineContext, LogPipelineContext>
{
    private readonly ISkeleton<LogPipelineContext, LogPipelineContext> _inner;

    public string Name => $"LoggingWrapper({_inner.Name})";
    public string Description => $"Logging wrapper for {_inner.Name}";

    public LoggingSkeletonWrapper(ISkeleton<LogPipelineContext, LogPipelineContext> inner)
    {
        _inner = PortGuard.Required(inner, nameof(inner));
    }

    public LogPipelineContext Execute(LogPipelineContext input, SkeletonContext ctx)
    {
        return ExecuteInternalAsync(ctx, () => Task.FromResult(_inner.Execute(input, ctx)))
            .GetAwaiter().GetResult();
    }

    public Task<LogPipelineContext> ExecuteAsync(LogPipelineContext input, SkeletonContext ctx)
    {
        return ExecuteInternalAsync(ctx, () => _inner.ExecuteAsync(input, ctx));
    }

    private async Task<LogPipelineContext> ExecuteInternalAsync(
        SkeletonContext ctx,
        Func<Task<LogPipelineContext>> executeFunc)
    {
        var stats = CreateStats();

        try
        {
            LogStart();
            var result = await executeFunc();
            LogSuccess(stats);
            return result;
        }
        catch (Exception ex)
        {
            LogFailure(stats, ex);
            throw;
        }
        finally
        {
            SaveStats(ctx, stats);
        }
    }

    private SkeletonExecutionStats CreateStats()
    {
        return new SkeletonExecutionStats
        {
            SkeletonName = _inner.Name,
            StartTime = DateTime.Now
        };
    }

    private void LogStart()
    {
        LogRepository.Write("INFO", "Batch_Routing", _inner.Name, $"[{_inner.Name}] 开始执行");
    }

    private void LogSuccess(SkeletonExecutionStats stats)
    {
        stats.IsSuccess = true;
        stats.EndTime = DateTime.Now;
        stats.Summary = $"执行成功，耗时 {stats.Duration.TotalMilliseconds:F0}ms";

        LogRepository.Write("INFO", "Batch_Routing", _inner.Name,
            $"[{_inner.Name}] 执行成功 ({stats.Duration.TotalMilliseconds:F0}ms)");
    }

    private void LogFailure(SkeletonExecutionStats stats, Exception ex)
    {
        stats.IsSuccess = false;
        stats.EndTime = DateTime.Now;
        stats.ErrorMessage = ex.Message;

        LogRepository.Write("ERROR", "Batch_Routing", _inner.Name,
            $"[{_inner.Name}] 执行失败: {ex.Message}");
    }

    private void SaveStats(SkeletonContext ctx, SkeletonExecutionStats stats)
    {
        ctx.SetData(stats, $"Stats_{_inner.Name}");
    }
}


public class SkeletonExecutionStats
{
    public string SkeletonName { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Summary { get; set; }

    public TimeSpan Duration => EndTime - StartTime;
}
