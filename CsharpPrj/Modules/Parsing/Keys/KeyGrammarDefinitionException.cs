namespace Modules.Parsing.Keys;

/// <summary>
/// 文法定义错误（装配期）—— 模板非法 / 占位符重复 / 序数占位缺失 / 可选区不连续。
/// 定义错误**不静默降级**：抛出并由装配点可见化（对齐 N9「未装配不兜底」）。
/// </summary>
public sealed class KeyGrammarDefinitionException : ArgumentException
{
    public KeyGrammarDefinitionException(string reason, string grammarId)
        : base($"{reason}（文法 '{grammarId}'）", "grammarId")
    {
        Reason = reason;
        GrammarId = grammarId;
    }

    /// <summary>失败原因（逐字可诊断的 reason 码/文案）。</summary>
    public string Reason { get; }

    /// <summary>出错文法 id。</summary>
    public string GrammarId { get; }
}
