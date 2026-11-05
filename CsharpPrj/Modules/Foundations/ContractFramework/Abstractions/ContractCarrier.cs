namespace ContractFramework.Abstractions;

/// <summary>
/// 载体种类（受控词白名单）。当前仅 <see cref="StructuralFact"/>——判据载体必须为结构事实。
/// </summary>
public enum ContractCarrierKind
{
    /// <summary>结构事实：工具名 / 命令形态 / 参数 / 路径起点与深度 / 计数 / 已声明文本 / 内容指纹 / 已注册符号集。</summary>
    StructuralFact = 0,
}

/// <summary>
/// 判据载体（<b>结构事实</b>；权威: axioms <c>SEP.JudgmentCarrierStructural</c>）。
/// <para>允许：工具名 / 命令形态 / 参数 / 路径起点与深度 / 计数 / 已声明文本 / 内容指纹 / 已注册符号集。</para>
/// <para>禁止：动机 / 必要性 / 收益 / 副作用 / 风险 / "顺手" / "我已修复" / "应该没问题"——含语义词 ⇒ 载体不合格 ⇒ 裁决 <c>⊥</c>。</para>
/// </summary>
public sealed class ContractCarrier
{
    /// <summary>载体语义词标记（黑名单；大小写不敏感）。出现即判载体不合格。</summary>
    private static readonly string[] NarrativeMarkers =
    [
        "动机", "必要性", "收益", "副作用", "风险", "顺手", "已修复", "应该没问题",
        "motive", "rationale", "benefit", "side effect", "side-effect", "risk",
        "should be fine", "should be ok", "already fixed", "i fixed", "it works",
    ];

    private ContractCarrier(ContractCarrierKind kind, IReadOnlyList<string> facts, bool kindRecognized)
    {
        Kind = kind;
        Facts = facts;
        IsKindRecognized = kindRecognized;
    }

    /// <summary>载体种类（未识别种类恒 = <see cref="ContractCarrierKind.StructuralFact"/> 但 <see cref="IsKindRecognized"/>=false）。</summary>
    public ContractCarrierKind Kind { get; }

    /// <summary>结构事实清单（供人读 + 门禁可见化的文本；本身也是"已声明文本"结构事实）。</summary>
    public IReadOnlyList<string> Facts { get; }

    /// <summary>种类是否在受控词白名单内。</summary>
    public bool IsKindRecognized { get; }

    /// <summary>载体合格：种类已识别 ∧ 事实非空 ∧ 无陈述性语义词。</summary>
    public bool IsStructural =>
        IsKindRecognized
        && Facts.Count > 0
        && Facts.All(f => !string.IsNullOrWhiteSpace(f))
        && !Facts.Any(ContainsNarrative);

    /// <summary>非法载体占位（缺 <c>carrier</c> 时用，供 <c>⊥</c> 可见化）。</summary>
    public static ContractCarrier Invalid { get; } =
        new(ContractCarrierKind.StructuralFact, Array.Empty<string>(), kindRecognized: false);

    /// <summary>受控词种类解析（对齐声明面字符串）。</summary>
    public static bool TryParseKind(string? kind, out ContractCarrierKind parsed)
    {
        if (string.Equals(kind, nameof(ContractCarrierKind.StructuralFact), StringComparison.OrdinalIgnoreCase))
        {
            parsed = ContractCarrierKind.StructuralFact;
            return true;
        }

        parsed = ContractCarrierKind.StructuralFact;
        return false;
    }

    /// <summary>宽容构造（种类未识别时保留原事实 + 标记 <see cref="IsKindRecognized"/>=false）。</summary>
    public static ContractCarrier Create(string? kind, IEnumerable<string>? facts)
    {
        var recognized = TryParseKind(kind, out var parsed);
        var list = facts?.Where(f => f is not null).ToArray() ?? [];
        return new ContractCarrier(parsed, list, recognized);
    }

    /// <summary>文本是否含陈述性语义词（载体铁律黑名单）。</summary>
    public static bool ContainsNarrative(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var marker in NarrativeMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
