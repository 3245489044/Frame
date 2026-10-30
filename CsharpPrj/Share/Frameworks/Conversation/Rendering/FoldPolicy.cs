#nullable enable

using System;

namespace Share.Conversation.Rendering;

/// <summary>轮段折叠模式（受控词；声明面 <c>ui-regions.json:content.fold</c>）。</summary>
public enum RoundFoldMode
{
    /// <summary>不折叠（全部展开；等价旧 sectionHeader 平铺）。</summary>
    None = 0,

    /// <summary>按轮折叠（<c>round-closed</c>）：默认收起，按策略打开。</summary>
    Closed = 1,

    /// <summary>按轮折叠但全展开（<c>round-open</c>）。</summary>
    Open = 2,
}

/// <summary>
/// 折叠策略（声明驱动；唯一裁决点）：折叠与否由**声明 + 运行态**决定，
/// 禁止以模板键存在性隐式推断行为，也不在渲染器内硬编码「最新轮打开」。
/// </summary>
/// <param name="Mode">折叠模式。</param>
/// <param name="OpenLatestRound">收起态下是否打开本轮（默认打开，保留既有观感）。</param>
public sealed record FoldPolicy(RoundFoldMode Mode, bool OpenLatestRound = true)
{
    /// <summary>默认策略（按轮折叠 + 打开最新轮）。</summary>
    public static readonly FoldPolicy Default = new(RoundFoldMode.Closed);

    /// <summary>不折叠。</summary>
    public static readonly FoldPolicy None = new(RoundFoldMode.None);

    /// <summary>声明词 → 策略（未声明/未知词 ⇒ <see cref="None"/>：不折叠，可见且零回归）。</summary>
    /// <param name="fold">声明值（<c>round-closed</c> / <c>round-open</c> / <c>panel-open</c> / <c>none</c>）。</param>
    public static FoldPolicy FromDeclaration(string? fold)
        => fold?.Trim().ToLowerInvariant() switch
        {
            "round-closed" => Default,
            "round-open" => new FoldPolicy(RoundFoldMode.Open),
            _ => None,
        };

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
