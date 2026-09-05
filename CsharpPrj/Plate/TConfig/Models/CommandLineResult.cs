namespace TConfig;

/// <summary>
/// 命令批次内的单命令执行记录（集中配置于 TConfig，消除各模块本地重复副本）。
/// 语义来源：原 BackEndDriven.Models.CommandLineResult（收敛，RedundancyElimination）。
/// </summary>
public class CommandLineResult
{
    public int LineNumber { get; set; }

    public string? CommandText { get; set; }

    public bool Success { get; set; }

    public string? Message { get; set; }

    public TimeSpan? Duration { get; set; }
}
