using Share.TaskStatusPool.Loops;

namespace Share.TaskStatusPool.Routing;

/// <summary>
/// 决策词词汇登记 —— 决策词 ↔ <see cref="LoopDecision"/> 的唯一词表（R9 决策词单一权威，无 Operation/Agent 依赖）。
/// Agent.Operation 的 <c>DecisionWordClassifier</c> 与状态驱动框架的决策记录投影均委托本类，禁止第三套词表
/// （SingleImplementation / TH-12）。词表：continue→Continue；stop/complete→Success；error→Error（非成功非继续，
/// 由调用方经 <see cref="IsError"/> 显式判定）；未知/空词→Unknown（不静默判成功）。
/// 归一时 Trim + ToLowerInvariant（大小写不敏感）。
/// </summary>
public static class DecisionWords
{
    public const string ContinueWord = "continue";
    public const string StopWord = "stop";

    /// <summary>遗留外部契约词汇（旧 agent_loop_entry 决策域），映射为 Success 保持兼容。</summary>
    public const string CompleteWord = "complete";

    public const string ErrorWord = "error";

    /// <summary>词分类（归一后）。</summary>
    public enum Kind
    {
        Unknown,
        Continue,
        Success,
        Error,
    }

    /// <summary>统一分类（唯一权威）。空白/未识别 → Unknown；识别成功/继续/错误分别归类。</summary>
    public static Kind Classify(string? word)
    {
        if (string.IsNullOrWhiteSpace(word)) return Kind.Unknown;
        return word.Trim().ToLowerInvariant() switch
        {
            ContinueWord => Kind.Continue,
            StopWord or CompleteWord => Kind.Success,
            ErrorWord => Kind.Error,
            _ => Kind.Unknown,
        };
    }

    /// <summary>决策词 → LoopDecision；未知词/空/error → null（error 为非成功非继续，由调用方经 <see cref="IsError"/> 显式判定）。</summary>
    public static LoopDecision? Map(string? word) => Classify(word) switch
    {
        Kind.Continue => LoopDecision.Continue,
        Kind.Success => LoopDecision.Success,
        _ => null,
    };

    /// <summary>是否"任务完成"（stop/complete → Success）。</summary>
    public static bool IsSuccess(string? word) => Classify(word) == Kind.Success;

    /// <summary>是否"继续下一轮"（continue → Continue）。</summary>
    public static bool IsContinue(string? word) => Classify(word) == Kind.Continue;

    /// <summary>是否"执行失败"（error 词）。</summary>
    public static bool IsError(string? word) => Classify(word) == Kind.Error;

    /// <summary>是否未知/无法识别的决策词（既非成功、非继续、也非显式 error）。</summary>
    public static bool IsUnknown(string? word) => Classify(word) == Kind.Unknown;
}
