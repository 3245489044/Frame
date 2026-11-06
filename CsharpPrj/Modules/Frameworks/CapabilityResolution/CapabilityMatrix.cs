#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.CapabilityResolution;

/// <summary>
/// 载体 × 图元的能力矩阵（**唯一能力查询点**）：未声明 ⇒ <see cref="CapabilityState.Unsupported"/>（可见降级）。
/// </summary>
public sealed class CapabilityMatrix
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilityState>> _byCarrier;

    /// <summary>空矩阵（任何查询均 Unsupported）。</summary>
    public static CapabilityMatrix Empty { get; } = new(
        new Dictionary<string, IReadOnlyDictionary<string, CapabilityState>>(StringComparer.Ordinal));

    /// <summary>按已解析三态构造。</summary>
    /// <param name="byCarrier">载体 → 图元 → 三态。</param>
    public CapabilityMatrix(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilityState>> byCarrier)
    {
        _byCarrier = byCarrier ?? throw new ArgumentNullException(nameof(byCarrier));
    }

    /// <summary>按声明面文本构造（supported/emulated/unsupported，未知词按 unsupported）。</summary>
    /// <param name="byCarrier">载体 → 图元 → 声明词。</param>
    public static CapabilityMatrix FromStrings(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? byCarrier)
    {
        var matrix = new Dictionary<string, IReadOnlyDictionary<string, CapabilityState>>(StringComparer.Ordinal);
        if (byCarrier is null) return new CapabilityMatrix(matrix);

        foreach (var carrier in byCarrier)
        {
            var caps = new Dictionary<string, CapabilityState>(StringComparer.Ordinal);
            foreach (var cap in carrier.Value)
            {
                caps[cap.Key] = CapabilityStates.Parse(cap.Value);
            }

            matrix[carrier.Key] = caps;
        }

        return new CapabilityMatrix(matrix);
    }

    /// <summary>载体是否已声明。</summary>
    /// <param name="carrier">载体 id。</param>
    public bool HasCarrier(string carrier)
        => !string.IsNullOrEmpty(carrier) && _byCarrier.ContainsKey(carrier);

    /// <summary>查询某载体对某图元的能力三态（未声明 ⇒ unsupported）。</summary>
    /// <param name="carrier">载体 id。</param>
    /// <param name="primitive">图元名。</param>
    public CapabilityState State(string carrier, string primitive)
        => StateOf(carrier, primitive);

    /// <summary>查询某载体对某图元的能力三态（未声明 ⇒ unsupported）。</summary>
    /// <param name="carrier">载体 id。</param>
    /// <param name="primitive">图元名。</param>
    public CapabilityState StateOf(string carrier, string primitive)
        => HasCarrier(carrier)
           && _byCarrier[carrier].TryGetValue(primitive, out var state)
            ? state
            : CapabilityState.Unsupported;

    /// <summary>该载体的能力表（快照；未声明 ⇒ 空表）。</summary>
    /// <param name="carrier">载体 id。</param>
    public IReadOnlyDictionary<string, CapabilityState> Caps(string carrier)
        => HasCarrier(carrier)
            ? _byCarrier[carrier]
            : new Dictionary<string, CapabilityState>(StringComparer.Ordinal);
}
