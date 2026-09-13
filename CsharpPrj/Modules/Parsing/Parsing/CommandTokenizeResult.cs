namespace Modules.Parsing.Parsing;

/// <summary>
/// 切词结果：token 序列 + **未闭合引号**位置（显式回报，不做静默修补/静默丢弃）。
/// 是否把未闭合引号升级为失败由调用方裁决（对齐 <c>ErrorCode.UnclosedQuote</c>）。
/// </summary>
public sealed class CommandTokenizeResult
{
    /// <summary>构造结果。</summary>
    /// <param name="tokens">已识别 token（不含未闭合引号内的残留片段）。</param>
    /// <param name="unterminatedQuoteIndex">未闭合引号的起始偏移（0 基）；-1 表示无。</param>
    public CommandTokenizeResult(IReadOnlyList<string> tokens, int unterminatedQuoteIndex)
    {
        Tokens = tokens;
        UnterminatedQuoteIndex = unterminatedQuoteIndex;
    }

    /// <summary>已识别 token（按出现顺序）。</summary>
    public IReadOnlyList<string> Tokens { get; }

    /// <summary>未闭合引号的起始偏移（0 基）；-1 表示无。</summary>
    public int UnterminatedQuoteIndex { get; }

    /// <summary>是否存在未闭合引号。</summary>
    public bool HasUnterminatedQuote => UnterminatedQuoteIndex >= 0;
}
