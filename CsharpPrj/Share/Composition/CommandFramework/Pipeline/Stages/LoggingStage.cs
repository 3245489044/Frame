
using System.Diagnostics;
using CommandFramework.Abstractions;
using CommandFramework.Models;
using Microsoft.Extensions.Logging;
using BP = Modules.Pipeline;

namespace CommandFramework.Pipeline.Stages;

public class LoggingStage<TContext> : PipelineStageBase<TContext>
    where TContext : BP.IPipelineContext
{
    private readonly Action<string> _logger;
    private readonly ILogger? _loggerInterface;
    private readonly bool _logContextDetails;

    public LoggingStage(Action<string>? logger = null, string name = "Logging", bool logContextDetails = true)
        : base(name, "Log pipeline stage execution")
    {
        _logger = logger ?? Console.WriteLine;
        _loggerInterface = null;
        _logContextDetails = logContextDetails;
    }

    public LoggingStage(ILogger logger, string name = "Logging", bool logContextDetails = true)
        : base(name, "Log pipeline stage execution")
    {
        _loggerInterface = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger = msg => logger.LogInformation("{Message}", msg);
        _logContextDetails = logContextDetails;
    }

    protected override async Task ExecuteCoreAsync(TContext context, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        var startInfo = BuildContextSummary(context, "START");
        Log(LogLevel.Information, $"{startInfo}");

        try
        {
            await Task.CompletedTask;
            sw.Stop();

            var endInfo = BuildContextSummary(context, "END");
            Log(LogLevel.Information, $"{endInfo} | Duration: {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log(LogLevel.Error, $"FAILED in {sw.ElapsedMilliseconds}ms: {ex.Message}");
            throw;
        }
    }

    private string BuildContextSummary(TContext context, string phase)
    {
        if (!_logContextDetails)
            return $"Phase={phase}";

        var parts = new List<string> { $"Phase={phase}" };

        if (context is CommandContext cmdCtx)
        {
            if (!string.IsNullOrWhiteSpace(cmdCtx.CurrentCommand))
                parts.Add($"Cmd='{Truncate(cmdCtx.CurrentCommand, 50)}'");

            if (!string.IsNullOrWhiteSpace(cmdCtx.Mode))
                parts.Add($"Mode={cmdCtx.Mode}");

            if (cmdCtx.CurrentCommandLineNumber > 0)
                parts.Add($"Line={cmdCtx.CurrentCommandLineNumber}");

            if (cmdCtx.HasError)
            {
                var errorMsg = cmdCtx.GetErrorMessage();
                parts.Add($"HasError=true{(errorMsg != null ? $" Error='{Truncate(errorMsg, 80)}'" : "")}");
            }
            else
            {
                parts.Add("HasError=false");
            }
        }

        return string.Join(" | ", parts);
    }


    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";

    private void Log(LogLevel level, string message)
    {
        if (_loggerInterface != null)
        {
            _loggerInterface.Log(level, "[{StageName}] {Message}", Name, message);
        }
        else
        {
            var prefix = level switch
            {
                LogLevel.Error or LogLevel.Critical => "[ERR]",
                LogLevel.Warning => "[WARN]",
                _ => "[INFO]"
            };
            _logger($"{prefix} [{Name}] {message}");
        }
    }
}


