#nullable enable

using System;
using System.Collections.Generic;

namespace Modules.CapabilityResolution;

/// <summary>
/// 载体 × 通道 × 图元的能力矩阵（**唯一能力查询点**）：未声明 ⇒ <see cref="CapabilityState.Unsupported"/>（可见降级）。
/// <para>通道是写面坐标（绘制 / 控件属性 / 样式表 / 纯文本），由调用方以受控词传入，内核不认识其语义。</para>
/// <para>声明键文法：<c>&lt;channel&gt;_&lt;primitive&gt;</c>；裸键 = <see cref="DefaultChannel"/>。<c>primitive 名不得含下划线</c>。</para>
/// </summary>
public sealed class CapabilityMatrix
{
    /// <summary>裸键（无通道前缀）归属的默认通道。</summary>
    public const string DefaultChannel = "draw";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilitySpec>>> _byCarrier;

    /// <summary>空矩阵（任何查询均 Unsupported）。</summary>
    public static CapabilityMatrix Empty { get; } = new(
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilitySpec>>>(StringComparer.Ordinal));

    /// <summary>按已解析条目构造。</summary>
    /// <param name="byCarrier">载体 → 通道 → 图元 → 能力条目。</param>
    public CapabilityMatrix(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilitySpec>>> byCarrier)
    {
        _byCarrier = byCarrier ?? throw new ArgumentNullException(nameof(byCarrier));
    }

    /// <summary>兼容重载：按「载体 → 图元 → 三态」构造（等价默认绘制通道）。</summary>
    /// <param name="byCarrier">载体 → 图元 → 三态。</param>
    public CapabilityMatrix(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilityState>> byCarrier)
        : this(ToSpecMatrix(byCarrier))
    {
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilitySpec>>> ToSpecMatrix(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilityState>>? byCarrier)
    {
        var matrix = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilitySpec>>>(StringComparer.Ordinal);
        if (byCarrier is null) return matrix;
        foreach (var carrier in byCarrier)
        {
            var primitives = new Dictionary<string, CapabilitySpec>(StringComparer.Ordinal);
            foreach (var pair in carrier.Value)
            {
                primitives[pair.Key] = pair.Value == CapabilityState.Supported
                    ? new CapabilitySpec(CapabilityState.Supported, new HashSet<string>(StringComparer.Ordinal) { CapabilitySpec.AnyDomain })
                    : new CapabilitySpec(pair.Value, new HashSet<string>(StringComparer.Ordinal) { CapabilitySpec.AnyDomain });
            }

            matrix[carrier.Key] = new Dictionary<string, IReadOnlyDictionary<string, CapabilitySpec>>(StringComparer.Ordinal)
            {
                [DefaultChannel] = primitives,
            };
        }

        return matrix;
    }

    /// <summary>按声明面文本构造（三态词 + 通道前缀；未知词按 unsupported）。</summary>
    /// <param name="byCarrier">载体 → 能力键 → 声明词。</param>
    public static CapabilityMatrix FromStrings(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? byCarrier)
    {
        var matrix = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilitySpec>>>(StringComparer.Ordinal);
        if (byCarrier is null) return new CapabilityMatrix(matrix);

        foreach (var carrier in byCarrier)
        {
            var channels = new Dictionary<string, IReadOnlyDictionary<string, CapabilitySpec>>(StringComparer.Ordinal);
            foreach (var cap in carrier.Value)
            {
                var (channel, primitive) = Split(cap.Key);
                if (!channels.TryGetValue(channel, out var primitives))
                {
                    primitives = new Dictionary<string, CapabilitySpec>(StringComparer.Ordinal);
                    channels[channel] = primitives;
                }

                ((Dictionary<string, CapabilitySpec>)primitives)[primitive] = CapabilityStates.ParseSpec(cap.Value);
            }

            matrix[carrier.Key] = channels;
        }

        return new CapabilityMatrix(matrix);
    }

    /// <summary>载体是否已声明。</summary>
    /// <param name="carrier">载体 id。</param>
    public bool HasCarrier(string carrier)
        => !string.IsNullOrEmpty(carrier) && _byCarrier.ContainsKey(carrier);

    /// <summary>查询某载体对某图元的能力三态（默认通道）。</summary>
    /// <param name="carrier">载体 id。</param>
    /// <param name="primitive">图元名。</param>
    public CapabilityState State(string carrier, string primitive)
        => StateOf(carrier, primitive);

    /// <summary>查询某载体对某图元的能力三态（默认通道；未声明 ⇒ unsupported）。</summary>
    /// <param name="carrier">载体 id。</param>
    /// <param name="primitive">图元名。</param>
    public CapabilityState StateOf(string carrier, string primitive)
        => SpecOf(carrier, DefaultChannel, primitive).State;

    /// <summary>查询某载体某通道对某图元的能力三态（未声明 ⇒ unsupported）。</summary>
    /// <param name="carrier">载体 id。</param>
    /// <param name="channel">通道（写面坐标）。</param>
    /// <param name="primitive">图元名。</param>
    public CapabilityState StateOf(string carrier, string channel, string primitive)
        => SpecOf(carrier, channel, primitive).State;

    /// <summary>查询能力条目（三态 + 接受值域集；未声明 ⇒ <see cref="CapabilitySpec.Unsupported"/>）。</summary>
    /// <param name="carrier">载体 id。</param>
    /// <param name="channel">通道（写面坐标）。</param>
    /// <param name="primitive">图元名。</param>
    public CapabilitySpec SpecOf(string carrier, string channel, string primitive)
    {
        var effectiveChannel = string.IsNullOrWhiteSpace(channel) ? DefaultChannel : channel;
        return HasCarrier(carrier)
               && _byCarrier[carrier].TryGetValue(effectiveChannel, out var primitives)
               && primitives.TryGetValue(primitive, out var spec)
            ? spec
            : CapabilitySpec.Unsupported;
    }

    /// <summary>该载体的默认通道能力表（快照；未声明 ⇒ 空表）。</summary>
    /// <param name="carrier">载体 id。</param>
    public IReadOnlyDictionary<string, CapabilityState> Caps(string carrier)
    {
        var caps = new Dictionary<string, CapabilityState>(StringComparer.Ordinal);
        if (!HasCarrier(carrier) || !_byCarrier[carrier].TryGetValue(DefaultChannel, out var primitives)) return caps;
        foreach (var pair in primitives) caps[pair.Key] = pair.Value.State;
        return caps;
    }

    /// <summary>该载体声明的通道集（诊断/门禁用）。</summary>
    /// <param name="carrier">载体 id。</param>
    public IReadOnlyCollection<string> Channels(string carrier)
        => HasCarrier(carrier)
            ? (IReadOnlyCollection<string>)_byCarrier[carrier].Keys
            : Array.Empty<string>();

    private static (string Channel, string Primitive) Split(string key)
    {
        var separator = key.IndexOf('_');
        return separator <= 0
            ? (DefaultChannel, key)
            : (key[..separator], key[(separator + 1)..]);
    }
}

