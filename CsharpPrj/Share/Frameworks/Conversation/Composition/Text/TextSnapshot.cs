#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Composition.Text;

/// <summary>快照段的来源（使「重算」与「实测」可辨）。</summary>
public enum SnapshotOrigin
{
    /// <summary>真实装配产出。</summary>
    Live,

    /// <summary>从持久化快照读回。</summary>
    Snapshot,

    /// <summary>由输入推导重建（UI 无快照时的兜底，必须可见标记）。</summary>
    Derived,
}

/// <summary>快照段 —— 键/组/序/状态/文本哈希/原因（不含正文，防泄漏与日志膨胀）。</summary>
public sealed record TextSnapshotSegment(
    TextKey Key,
    string Group,
    OrderKey Order,
    TextState State,
    string TextHash,
    string? Reason);

/// <summary>
/// 装配快照 —— 写侧与展示侧共用的只读投影（UI ≡ 投喂）。
/// </summary>
/// <param name="Segments">逐段快照（计划序）。</param>
/// <param name="Verdict">契约判定标签（<see cref="TextVerdictStatus"/> 名）。</param>
/// <param name="Fingerprint">装配指纹（与 <see cref="TextAssembly.Fingerprint"/> 相等）。</param>
/// <param name="PrefixFingerprint">前缀指纹（有序前缀段哈希的摘要；无前缀谓词时为空串哈希）。</param>
/// <param name="Origin">来源。</param>
public sealed record TextSnapshot(
    IReadOnlyList<TextSnapshotSegment> Segments,
    string Verdict,
    string Fingerprint,
    string PrefixFingerprint,
    SnapshotOrigin Origin)
{
    /// <summary>空快照。</summary>
    public static TextSnapshot Empty { get; } = new(
        Array.Empty<TextSnapshotSegment>(),
        TextVerdictStatus.Complete.ToString(),
        TextDigest.Hash(string.Empty),
        TextDigest.Hash(string.Empty),
        SnapshotOrigin.Snapshot);
}
