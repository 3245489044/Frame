using System;

namespace Share.Conversation.Ids;

/// <summary>
/// 事实归属键形态（判据 §1.1）：<see cref="Round"/> = 轮（含 run 级退化形态）、
/// <see cref="Step"/> = 步骤、<see cref="Call"/> = 调用。层级由**段存在性**决定，不引入新随机源（不变式 I1）。
/// </summary>
public enum FactIdKind
{
    /// <summary>轮次键（<c>SessionKey#R&lt;n&gt;</c>；<c>RoundNo&lt;=0</c> 时为 run 级裸会话键）。</summary>
    Round,

    /// <summary>步骤键（<c>RoundKey/StepName#Attempt</c>）。</summary>
    Step,

    /// <summary>调用键（<c>StepKey/Iteration/CallId</c>）。</summary>
    Call,
}

/// <summary>
/// 事实归属键（值对象，唯一标识标准 §1.1 的运行时载体）——回答「这条记录属于哪一轮 / 哪一步 / 哪次调用」。
/// <para>
/// 三轴边界（防「第六套 ID」）：本类型仅承载**事实归属轴**；「怎么跑」属 <c>ExecutionFlag</c> 轴、
/// 「哪个实体」属 <c>EntityRef</c> 轴，三轴可组合但**不合并为一个 ID**（方案 §1.7）。
/// </para>
/// <para>
/// 生成一律经 <see cref="FactIds"/>（唯一生成器）；解析 / 格式化经 <see cref="FactIdGrammar"/>（唯一字面量来源）。
/// 相等性由 record struct 值语义保证（可安全作字典键与集合元素）。
/// </para>
/// </summary>
/// <param name="SessionKey">会话键（= runId = TaskKey = HistoryId）。</param>
/// <param name="RoundNo">轮次序号（会话累计轮，从 1 起；<c>&lt;=0</c> 表示 run 级键）。</param>
/// <param name="StepName">工作流步骤名（轮键为 null）。</param>
/// <param name="Attempt">同一步骤重试代次（从 1 起；null 视作 1）。</param>
/// <param name="Iteration">执行面 LLM 回合号（从 1 起；仅调用键非 null）。</param>
/// <param name="CallId">外部工具调用标识（原样保留，作末段）。</param>
public readonly record struct FactId(
    string SessionKey,
    int RoundNo,
    string? StepName = null,
    int? Attempt = null,
    int? Iteration = null,
    string? CallId = null) : IComparable<FactId>
{
    /// <summary>键形态（按段存在性判定）。</summary>
    public FactIdKind Kind => CallId is not null
        ? FactIdKind.Call
        : StepName is not null ? FactIdKind.Step : FactIdKind.Round;

    /// <summary>是否 run 级键（会话级，无轮号；线格式为裸会话键）。</summary>
    public bool IsRunKey => RoundNo <= 0;

    /// <summary>线格式串（唯一生成入口见 <see cref="FactIdGrammar.Format"/>）。</summary>
    public override string ToString() => FactIdGrammar.Format(this);

    /// <summary>解析（严格；失败返回 false，不猜测）。</summary>
    /// <param name="text">键串。</param>
    /// <param name="id">out：解析结果。</param>
    public static bool TryParse(string? text, out FactId id) => FactIdGrammar.TryParse(text, out id);

    /// <summary>解析（失败即抛 <see cref="FormatException"/>）。</summary>
    /// <param name="text">键串。</param>
    public static FactId Parse(string? text) => FactIdGrammar.Parse(text);

    /// <summary>键序比较（数值段感知；禁字符串字典序）。</summary>
    /// <param name="other">对方键。</param>
    public int CompareTo(FactId other) => FactIdGrammar.Compare(this, other);
}
