#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 槽 → 材质选择（**唯一实现**）：背景槽优先 + 声明序兜底。
/// <para>装饰器（逐控件）与安装器（全局回退）共用本规则，禁各写一套「优先哪个槽」。</para>
/// </summary>
public static class SurfaceSlotSelection
{
    /// <summary>背景槽受控名（与声明面 profile.materials 的语义槽同名）。</summary>
    public const string BackgroundSlot = "panel_surface";

    /// <summary>容器内选背景材质：精确背景槽 → <c>*_surface</c> → 声明序首个；无 ⇒ null。</summary>
    /// <param name="materials">槽 → 已解析材质（声明序）。</param>
    public static ResolvedMaterial? SelectBackground(IReadOnlyDictionary<string, ResolvedMaterial>? materials)
    {
        if (materials is not { Count: > 0 }) return null;
        if (materials.TryGetValue(BackgroundSlot, out var preferred)) return preferred;

        foreach (var pair in materials)
        {
            if (pair.Key.EndsWith("_surface", StringComparison.Ordinal)) return pair.Value;
        }

        return materials.Values.FirstOrDefault();
    }

    /// <summary>跨容器选全局回退材质：逐容器按 <see cref="SelectBackground"/> 规则，声明序首个有层栈者；无 ⇒ null。</summary>
    /// <param name="byKey">容器键 → 槽 → 已解析材质（声明序）。</param>
    /// <param name="slot">优先槽名（默认 <see cref="BackgroundSlot"/>）。</param>
    public static ResolvedMaterial? SelectGlobal(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>? byKey,
        string slot = BackgroundSlot)
    {
        if (byKey is not { Count: > 0 }) return null;

        foreach (var container in byKey.Values)
        {
            if (container.TryGetValue(slot, out var material) && material.Layers.Count > 0) return material;
        }

        foreach (var container in byKey.Values)
        {
            var candidate = SelectBackground(container);
            if (candidate is { Layers.Count: > 0 }) return candidate;
        }

        return null;
    }
}
