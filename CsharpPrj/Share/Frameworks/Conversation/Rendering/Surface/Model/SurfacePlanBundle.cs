#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>容器角色/层级（由声明 <c>parent</c> 链派生；不猜控件类型）。</summary>
/// <param name="View">表面视图键。</param>
/// <param name="ContainerKey">容器键。</param>
/// <param name="Role">声明角色。</param>
/// <param name="Tier">层级（root | nested）。</param>
/// <param name="Depth">父链深度（0 = 顶层）。</param>
public sealed record SurfaceRoleInfo(string View, string ContainerKey, string Role, string Tier, int Depth);

/// <summary>
/// <c>surface-plan</c> 的域产物（**唯一材质/结构来源**）：一个载体在全部表面视图上的解析结果。
/// <para>Composer 与两载体都只消费本产物，禁各自再调 <see cref="SurfaceResolver"/>。</para>
/// </summary>
/// <param name="Carrier">载体 id。</param>
/// <param name="ProfileId">外观档 id。</param>
/// <param name="Views">表面视图键 → 解析结果。</param>
/// <param name="Degradations">解析期降级原因（稳定去重）。</param>
public sealed record SurfacePlanBundle(
    string Carrier,
    string ProfileId,
    IReadOnlyDictionary<string, ResolvedSurface> Views,
    IReadOnlyList<SurfaceIssue> Degradations)
{
    /// <summary>空产物（未装配；消费方必须显式降级）。</summary>
    public static readonly SurfacePlanBundle Empty = new(
        string.Empty,
        string.Empty,
        new Dictionary<string, ResolvedSurface>(StringComparer.Ordinal),
        Array.Empty<SurfaceIssue>());

    /// <summary>是否来自可用声明。</summary>
    public bool IsDeclared => Views.Count > 0;

    /// <summary>按槽取材质（跨视图首个命中；未命中 ⇒ null）。</summary>
    /// <param name="slot">材质槽。</param>
    public ResolvedMaterial? FindMaterial(string slot)
    {
        if (string.IsNullOrEmpty(slot)) return null;
        foreach (var view in Views.Values)
        {
            foreach (var container in view.Containers)
            {
                if (container.Materials.TryGetValue(slot, out var material)) return material;
            }
        }

        return null;
    }

    /// <summary>按槽取容器角色/层级（未命中 ⇒ null）。</summary>
    /// <param name="slot">材质槽。</param>
    public SurfaceRoleInfo? Describe(string slot)
    {
        if (string.IsNullOrEmpty(slot)) return null;
        foreach (var pair in Views)
        {
            var view = pair.Value;
            foreach (var container in view.Containers)
            {
                if (!container.Materials.ContainsKey(slot)) continue;
                var depth = 0;
                var tier = "root";
                var parent = container.Parent;
                var guard = 0;
                while (!string.IsNullOrEmpty(parent) && guard++ < 32)
                {
                    tier = "nested";
                    depth++;
                    parent = view.Container(parent!)?.Parent;
                }

                return new SurfaceRoleInfo(view.View, container.Key, container.Role, tier, depth);
            }
        }

        return null;
    }

    /// <summary>按卡片几何取角色（容器键优先，其次材质槽）。</summary>
    /// <param name="containerKey">容器键（可空）。</param>
    /// <param name="slot">材质槽。</param>
    public SurfaceRoleInfo? DescribeCard(string? containerKey, string slot)
    {
        if (!string.IsNullOrEmpty(containerKey))
        {
            foreach (var pair in Views)
            {
                var view = pair.Value;
                var container = view.Container(containerKey!);
                if (container is null) continue;
                var depth = 0;
                var tier = "root";
                var parent = container.Parent;
                var guard = 0;
                while (!string.IsNullOrEmpty(parent) && guard++ < 32)
                {
                    tier = "nested";
                    depth++;
                    parent = view.Container(parent!)?.Parent;
                }

                return new SurfaceRoleInfo(view.View, container.Key, container.Role, tier, depth);
            }
        }

        return Describe(slot);
    }
}
