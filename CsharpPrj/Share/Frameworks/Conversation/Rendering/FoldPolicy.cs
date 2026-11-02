#nullable enable

using System;

namespace Share.Conversation.Rendering;

/// <summary>轮段折叠模式（受控词；声明面 <c>ui/fold.json</c>）。</summary>
public enum RoundFoldMode
{
    /// <summary>不折叠（全部展开）。</summary>
    None = 0,

    /// <summary>按轮折叠：默认收起，按策略打开。</summary>
    Closed = 1,

    /// <summary>按轮折叠但全展开。</summary>
    Open = 2,
}

/// <summary>
/// 折叠策略（声明驱动；唯一裁决点）：折叠与否由**声明 + 运行态**决定，
/// 禁止以模板键存在性隐式推断行为，也不在渲染器内硬编码「最新轮打开」。
/// </summary>
/// <param name="Mode">折叠模式。</param>
/// <param name="OpenLatestRound">收起态下是否打开本轮（默认打开，保留既有观感）。</param>
/// <param name="DeclaredWord">命中的声明词（null = 未经声明解析）。</param>
/// <param name="Degradation">降级原因诊断码（null = 正常；非空 ⇒ 渲染链须可见化）。</param>
/// <param name="SuppressHeader">是否抑制段头（true ⇒ 只出行、不产轮段/会话级段头；视图声明 `grouping=none`）。</param>
public sealed record FoldPolicy(
    RoundFoldMode Mode,
    bool OpenLatestRound = true,
    string? DeclaredWord = null,
    string? Degradation = null,
    bool SuppressHeader = false)
{
    /// <summary>默认策略（按轮折叠 + 打开最新轮；未经声明解析时的结构缺省）。</summary>
    public static readonly FoldPolicy Default = new(RoundFoldMode.Closed);

    /// <summary>不折叠。</summary>
    public static readonly FoldPolicy None = new(RoundFoldMode.None);

    /// <summary>不可判定策略（词未声明/未实现）：不折叠 + 携带诊断（禁静默等同 none）。</summary>
    /// <param name="word">声明词。</param>
    /// <param name="reason">诊断码。</param>
    /// <param name="declared">已声明的策略（未实现词的语义基底；null ⇒ 不折叠）。</param>
    /// <param name="suppressHeader">是否抑制段头（未声明词时沿用结构缺省 false）。</param>
    public static FoldPolicy Unresolved(string? word, string reason, FoldPolicy? declared = null, bool suppressHeader = false)
        => new(declared?.Mode ?? RoundFoldMode.None, declared?.OpenLatestRound ?? false, word, reason, suppressHeader || (declared?.SuppressHeader ?? false));

    /// <summary>该轮是否展开（<paramref name="latestRound"/> = 本轮最大轮）。</summary>
    public bool IsOpen(int round, int latestRound)
        => Mode switch
        {
            RoundFoldMode.None => true,
            RoundFoldMode.Open => true,
            RoundFoldMode.Closed => OpenLatestRound && round == latestRound,
            _ => true,
        };
}

