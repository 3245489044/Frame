#nullable enable

using System.Collections.Generic;
using System.Linq;
using Modules.CapabilityResolution;

namespace Share.Conversation.Rendering.Surface;

/// <summary>解析期可见问题（缺失/降级/不支持；禁静默）。</summary>
/// <param name="Code">受控码（material-unknown / mapping-missing / unsupported / emulated / cap-undeclared）。</param>
/// <param name="Detail">定位（容器/槽/图元/层）。</param>
public sealed record SurfaceIssue(string Code, string Detail);

/// <summary>已解析的一层（参数中的 token 已换值）。</summary>
/// <param name="Role">层角色（base/edge/shadow/blur/body/sheen…）。</param>
/// <param name="Primitive">图元。</param>
/// <param name="Z">叠放序。</param>
/// <param name="Params">已解析参数。</param>
public sealed record ResolvedLayer(
    string Role, string Primitive, int Z, IReadOnlyDictionary<string, string> Params);

/// <summary>已解析材质（容器级或部件级共用）。</summary>
/// <param name="Slot">语义槽（panel_surface / bubble_surface / row_hover…）。</param>
/// <param name="MaterialId">声明的材质 id（可能已回退）。</param>
/// <param name="FallbackUsed">实际生效的回退材质（null = 未回退）。</param>
/// <param name="Layers">层栈（z 升序）。</param>
/// <param name="Issues">解析问题。</param>
public sealed record ResolvedMaterial(
    string Slot,
    string MaterialId,
    string? FallbackUsed,
    IReadOnlyList<ResolvedLayer> Layers,
    IReadOnlyList<SurfaceIssue> Issues)
{
    /// <summary>回落梯度步（L0–L5；属性通道最低 L2，默认 L0 保持旧调用方零变化）。</summary>
    public SurfaceFallbackStep FallbackStep { get; init; } = SurfaceFallbackStep.L0Full;
}

/// <summary>已解析容器（结构 + 容器级外观）。</summary>
/// <param name="Id">view:key。</param>
/// <param name="Key">DOM 名。</param>
/// <param name="Role">角色。</param>
/// <param name="Slot">结构槽。</param>
/// <param name="Write">写模式。</param>
/// <param name="Parent">父容器键（可空）。</param>
/// <param name="Structure">载体结构属性（tag/cls/control…）。</param>
/// <param name="Materials">槽 → 已解析材质。</param>
/// <param name="Issues">解析问题。</param>
public sealed record ResolvedContainer(
    string Id,
    string Key,
    string Role,
    string Slot,
    string Write,
    string? Parent,
    IReadOnlyDictionary<string, string> Structure,
    IReadOnlyDictionary<string, ResolvedMaterial> Materials,
    IReadOnlyList<SurfaceIssue> Issues);

/// <summary>
/// 一次解析产物（**唯一外观/结构解析结果**）：视图 × 外观档 × 载体。
/// <para>指纹 = program + 视图 + 档 + 载体 + 覆盖（写槽键与外观缓存的失效依据）。</para>
/// </summary>
/// <param name="View">视图键。</param>
/// <param name="Profile">外观档。</param>
/// <param name="Carrier">载体 id。</param>
/// <param name="Fingerprint">解析指纹。</param>
/// <param name="Containers">容器（声明序）。</param>
/// <param name="Issues">全局问题（程序未装配/视图未声明…）。</param>
public sealed record ResolvedSurface(
    string View,
    string Profile,
    string Carrier,
    string Fingerprint,
    IReadOnlyList<ResolvedContainer> Containers,
    IReadOnlyList<SurfaceIssue> Issues)
{
    /// <summary>按 DOM 名取容器（未声明 ⇒ null）。</summary>
    /// <param name="key">容器键。</param>
    public ResolvedContainer? Container(string key)
        => Containers.FirstOrDefault(c => string.Equals(c.Key, key, System.StringComparison.Ordinal));
}
