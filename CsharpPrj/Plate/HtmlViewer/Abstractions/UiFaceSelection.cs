#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>
/// 面选择声明（机制层，**载荷无关**）：面 Id + 「接受的所有者 / 通道」白名单。
/// <para>
/// 纪律：① <b>肯定式</b>——只列出「可进入本面」的结构取值，未列出即不接受
/// （区别于 <see cref="UiWindowContextSpec"/> 的否认词表，新增 kind 不会默认可见）；
/// ② 两维皆须声明，任一维为空 ⇒ <see cref="IsDeclared"/>=false ⇒ 该面不产出（fail-closed）；
/// ③ 只承载结构词（owner/channel），不含文案 / 徽标 / 领域判定。
/// </para>
/// </summary>
/// <param name="FaceId">面标识（与产品 / 区域声明的面 Id 一致）。</param>
/// <param name="AcceptsOwner">接受的所有者取值（如实体类别键；空 = 未声明）。</param>
/// <param name="AcceptsChannel">接受的通道取值（如事实来源通道；空 = 未声明）。</param>
public sealed record UiFaceSpec(
    string FaceId,
    IReadOnlySet<string> AcceptsOwner,
    IReadOnlySet<string> AcceptsChannel)
{
    /// <summary>空声明（未声明任何白名单 ⇒ 该面永不接受事实）。</summary>
    public static UiFaceSpec Empty { get; } = new(
        string.Empty,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>是否已声明（两维白名单皆非空；任一为空 ⇒ 不可用）。</summary>
    public bool IsDeclared => AcceptsOwner.Count > 0 && AcceptsChannel.Count > 0;
}

/// <summary>
/// 面选择判定（通用件，唯一实现）—— 白名单双维命中才可见。
/// <para>
/// 语义：未声明 / 任一分量缺失或空白 ⇒ 不接受（禁按 Role/Stage 二次猜测归属）；
/// 命中判定为 OrdinalIgnoreCase 且去首尾空白（声明与事实两侧同名即同义）。
/// </para>
/// </summary>
public static class UiFaceSelector
{
    /// <summary>单条事实是否可进入该面（空声明 ⇒ false）。</summary>
    /// <param name="spec">面声明（非 null）。</param>
    /// <param name="owner">事实所有者取值（null/空白 ⇒ 不可判定 ⇒ false）。</param>
    /// <param name="channel">事实通道取值（null/空白 ⇒ 不可判定 ⇒ false）。</param>
    public static bool Accepts(UiFaceSpec spec, string? owner, string? channel)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!spec.IsDeclared) return false;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(channel)) return false;

        return spec.AcceptsOwner.Contains(owner.Trim())
               && spec.AcceptsChannel.Contains(channel.Trim());
    }
}
