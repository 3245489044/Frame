namespace Share.AiScheduling.Regulation;

/// <summary>
/// 压力治理输入信号（通用，不绑定业务域）：每轮/每次评估的观测值。
/// EvalIsPass 表示本轮评估是否通过（复杂度压力在 token 耗尽且未通过时打满）。
/// </summary>
public sealed record RegulationSignal(
    int Round,
    int ResultTextLength,
    int NoProgressSteps,
    int TokenValue,
    int TokenMax,
    bool EvalIsPass);
