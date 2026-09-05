namespace Modules.Parsing.Models;

public class CommandMetrics
{
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions : 0;
    public double AverageDurationMs { get; set; }
    public long TotalDurationMs { get; set; }
    public Dictionary<string, VerbMetrics> VerbMetrics { get; set; } = new();
    public List<ErrorRecord> RecentErrors { get; set; } = new();
    public DateTime StartTime { get; set; }
    public TimeSpan Uptime { get; set; }
}

public class VerbMetrics
{
    public int Executions { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }
    public double AverageDurationMs { get; set; }
    public Dictionary<string, int> ActionCounts { get; set; } = new();
    public Dictionary<ErrorCode, int> ErrorCounts { get; set; } = new();
}

public class ErrorRecord
{
    public string Verb { get; set; } = string.Empty;
    public ErrorCode ErrorCode { get; set; }
    public DateTime Timestamp { get; set; }
}
