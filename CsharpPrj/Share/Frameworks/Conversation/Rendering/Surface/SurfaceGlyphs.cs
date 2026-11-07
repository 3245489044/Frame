#nullable enable

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 载体字形/占位词的中立来源（模型只存语义键，载体在此解析）。
/// <para>
/// 纪律：语义层禁出现具体字形；本类只提供「可断言、可替换」的占位/折叠文本。
/// 与 <c>Observability/unknown_tokens.json</c> 的 unknown 词保持同值（迁移期由
/// <c>Check-UnknownVisible</c> 对齐；后续由声明驱动替换本类）。
/// </para>
/// </summary>
public static class SurfaceGlyphs
{
    /// <summary>未知占位（宽载体：HTML/WinForms）。</summary>
    public const string Unknown = "未知";

    /// <summary>未知占位（窄载体：ASCII/日志；仍可断言，不是裸 ?）。</summary>
    public const string UnknownNarrow = "[?]";

    /// <summary>折叠：展开态前缀（窄载体）。</summary>
    public const string FoldOpen = "[-]";

    /// <summary>折叠：收起态前缀（窄载体）。</summary>
    public const string FoldClosed = "[+]";

    /// <summary>结构缺失（载体映射未声明）的可见前缀。</summary>
    public const string MissingStructure = "未支持单元";

    /// <summary>窄载体不承载的装饰字形集合（▸/⊘/ℹ 等）；出现即降级。</summary>
    public static bool IsDecorative(char value) => value is '▸' or '▾' or '⊘' or 'ℹ' or '▶' or '⟳';
}
