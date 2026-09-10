
namespace CommandFramework.Models;

public class StageResult
{
    public string StageName { get; set; } = "";

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public TimeSpan Duration { get; set; }
}

public class CommandExecutionReport
{
    public string? Command { get; set; }

    public int LineNumber { get; set; }

    public bool IsNonEvent { get; set; }

    public List<StageResult> Stages { get; set; } = new();

    public TimeSpan TotalDuration { get; set; }

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public StageResult? GetStage(string stageName)
    {
        return Stages.Find(s => s.StageName == stageName);
    }

    public StageResult? GetFirstFailure()
    {
        return Stages.Find(s => !s.IsSuccess);
    }

    public string ToSummaryString()
    {
        var status = IsSuccess ? "✓ 成功" : "✗ 失败";
        var summary = $"[{status}] {Command ?? "(空命令)"} (行 {LineNumber})";
        if (!IsSuccess && ErrorMessage != null)
        {
            summary += $"\n  错误: {ErrorMessage}";
        }
        return summary;
    }
}
