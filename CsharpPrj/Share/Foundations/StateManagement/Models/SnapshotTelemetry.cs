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
    string? UserInput = null,
    // T2（阶段 3 · 路由 intent 源富化）——快照级决策输入追加本轮意图/产出轴（契约扩展，可空缺省=未知不臆断）：
    // ToolExpected = 该轮是否"应做工具/落库 op"（intent 轴）；Produced = 是否实际施加了产出（produced 轴）。
    // 由 Agent 层每轮边界以 BuildContext(telemetryOverride) 覆写喂入，供语义导演（SemanticVerdictDirector）富化决策，
    // 不再因无源而恒 DirectorDegraded=true（对应原障碍 B）。
    bool? ToolExpected = null,
    bool? Produced = null)
{
    public static SnapshotTelemetry Empty { get; } = new(0, 0, 0, 0, false, 0, null, null);
}
