namespace Share.StateManagement;

/// <summary>
/// 快照级决策输入遥测（观测快照，与状态写入解耦 —— 决策输入不持有可变状态引用）。
/// 承载根循环状态投影（Round/Budget/Token/Halt）+ 评估/错误分类的字符串形态（契约纯化）。
/// 归属：状态宿主观测契约（Share.StateManagement），由 AI 调度导演上下文（Share.AiScheduling）承载。
/// </summary>
public sealed record SnapshotTelemetry(
    int Round,
    int BudgetLevel,
    int TokenValue,
    int TokenMax,
    bool Halt,
    int ContinuousFailures,
    string? LastEval,
    string? LastErrorClass,
    string? UserInput = null)
{
    public static SnapshotTelemetry Empty { get; } = new(0, 0, 0, 0, false, 0, null, null);
}
