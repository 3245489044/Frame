
namespace Share.RoslynJson.Emitters;

/// <summary>内嵌 JSON 的 C# 字面量形态。</summary>
public enum EmbeddedJsonStyle
{
    /// <summary>原始字符串字面量（C# 11+，可读性最好；自动计算围栏长度）。</summary>
    RawStringLiteral,

    /// <summary>普通转义字面量（兼容所有 C# 版本；由 Roslyn SymbolDisplay 生成，绝对安全）。</summary>
    EscapedLiteral
}
