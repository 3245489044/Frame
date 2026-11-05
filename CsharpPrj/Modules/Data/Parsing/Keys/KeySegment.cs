namespace Modules.Parsing.Keys;

/// <summary>键段类别（段序即文法；<see cref="Literal"/> 不参与取值）。</summary>
public enum KeySegmentKind
{
    /// <summary>字面量段（分隔符 / 固定前缀 / 固定后缀，取自模板原文）。</summary>
    Literal,

    /// <summary>值段（由调用方给定，如 container / sessionKey）。</summary>
    Value,

    /// <summary>序数段（由序数权威单源分配；语义上属值段，单独标出以便装配期校验）。</summary>
    Sequence,
}

/// <summary>
/// 值形状（**仅约束反解正则**，构造侧不做形状拒绝 —— 既有业务校验（如 <c>RequireId</c>）保留原语义）。
/// <para>形状存在理由：<c>.+?</c> 通用形状会让数值段吞掉分隔符（如 <c>#R1/cline</c> 的轮号段），
/// 使反解结果不确定；声明形状后正则精确可判定（对齐 QuantificationConstraint 有界量化）。</para>
/// </summary>
public enum KeyValueShape
{
    /// <summary>任意非空文本（<c>.+?</c>，既有语义默认）。</summary>
    Text,

    /// <summary>标识符（字母数字与 <c>_.-</c>；对齐 FactIdGrammar.IdPattern）。</summary>
    Id,

    /// <summary>正整数（拒绝前导 0；对齐 FactIdGrammar 轮号 / 代次段）。</summary>
    Number,

    /// <summary>非负整数（允许前导 0；对齐 <c>long.TryParse(NumberStyles.None)</c> 的序号段）。</summary>
    Digits,

    /// <summary>枚举名（v2/K4：标识符子集，供枚举段声明；反解同 Id，语义供声明面校验）。</summary>
    Enum,

    /// <summary>转义路径（v2/K4：含保留字符的成员值；反解容忍非空文本）。</summary>
    Path,
}

/// <summary>
/// 键段（段序即文法）：<paramref name="Text"/> 对字面量段为原文、对占位段为占位符名；
/// <paramref name="Region"/> = 可缺省区号（0 = 必填段；&gt;0 = 第 n 个可选区内的段）。
/// </summary>
public sealed record KeySegment(
    KeySegmentKind Kind,
    string Text,
    KeyValueShape Shape = KeyValueShape.Text,
    int Region = 0)
{
    /// <summary>是否占位符段（非字面量）。</summary>
    public bool IsPlaceholder => Kind != KeySegmentKind.Literal;

    /// <summary>是否可缺省区内的段（<see cref="Region"/> &gt; 0）。</summary>
    public bool IsOptional => Region > 0;
}
