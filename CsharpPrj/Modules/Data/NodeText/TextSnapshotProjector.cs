#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.Text;

/// <summary>
/// 快照投影唯一实现 —— 装配 × 结论 → 快照（写侧与展示侧同一入口，禁止 UI 二次重算）。
/// <para>前缀资格由域侧谓词注入（内核不解释「哪些序可进前缀」的缓存语义）。</para>
/// </summary>
public static class TextSnapshotProjector
{
    /// <summary>投影快照。</summary>
    /// <param name="assembly">装配产物。</param>
    /// <param name="verdict">契约结论（null → Complete）。</param>
    /// <param name="isPrefix">前缀资格谓词（null → 无前缀段）。</param>
    /// <param name="origin">来源标记。</param>
    public static TextSnapshot Project(
        TextAssembly assembly,
        TextVerdict? verdict = null,
        Func<OrderKey, bool>? isPrefix = null,
        SnapshotOrigin origin = SnapshotOrigin.Live)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var segments = new List<TextSnapshotSegment>(assembly.Bindings.Count);
        var prefixHashes = new List<string>();
        foreach (var binding in assembly.Bindings)
        {
            segments.Add(new TextSnapshotSegment(
                binding.Key, binding.Group, binding.Order, binding.State, binding.TextHash, binding.Reason));
            if (isPrefix is not null && isPrefix(binding.Order)) prefixHashes.Add(binding.TextHash);
        }

        return new TextSnapshot(
            segments,
            (verdict?.Status ?? TextVerdictStatus.Complete).ToString(),
            assembly.Fingerprint,
            TextDigest.HashLines(prefixHashes),
            origin);
    }
}
