#nullable enable

using System;
using System.Collections.Generic;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 一帧的渲染事实（**唯一读取面**）：光 / 背景 / 玻璃计划 / 阶段所有权 / 降级原因。
/// <para>载体只读这一份，禁各自直读 registry/host（L-1/L-2/G-2 收口）。</para>
/// </summary>
/// <param name="Light">运行时光读取结果（帧 + 原因）。</param>
/// <param name="Backdrop">背后内容源（按控件/按帧传入）。</param>
/// <param name="Glass">玻璃计划（选层 + 所有权 + 降级）。</param>
/// <param name="Ownership">阶段所有权（= Glass.Ownership 的快捷读取）。</param>
/// <param name="Degradations">降级原因码（稳定去重）。</param>
public sealed record SurfaceRenderFacts(
    SurfaceLightRead Light,
    SurfaceTextureFrame Backdrop,
    SurfaceGlassPlan Glass,
    SurfaceStageOwnership Ownership,
    IReadOnlyList<string> Degradations)
{
    /// <summary>载体是否应贴引擎帧（Base 阶段归引擎）。</summary>
    public bool EngineOwnsFace => Glass.EngineOwns(SurfacePaintStage.Base);

    internal static IReadOnlyList<string> Distinct(List<string> values)
    {
        if (values.Count == 0) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (seen.Add(value)) result.Add(value);
        }

        return result;
    }
}

/// <summary>
/// 渲染事实装配（**唯一入口**）：一次取光帧 + 宿主事实 + 玻璃计划。
/// </summary>
public static class SurfaceRenderFactsReader
{
    /// <summary>
    /// 读取一帧事实。
    /// </summary>
    /// <param name="backdrop">本控件/本帧的背景源（null ⇒ 用宿主事实里的登记值）。</param>
    public static SurfaceRenderFacts Read(SurfaceTextureFrame? backdrop = null)
    {
        var host = SurfaceHostFacts.Current;
        var light = SurfaceLightReader.Read();
        var source = backdrop ?? host.Backdrop;
        var plan = SurfaceGlassPlan.Negotiate(host.Composition, source, host.Budget, host.DeclaredTier, host.Look);

        var degradations = new List<string>(plan.Degradations);
        if (!light.HasFrame && !string.IsNullOrWhiteSpace(light.Reason)) degradations.Add(light.Reason);
        if (!source.Available) degradations.Add(string.IsNullOrWhiteSpace(source.Reason) ? "backdrop-unavailable" : source.Reason);

        return new SurfaceRenderFacts(light, source, plan, plan.Ownership, SurfaceRenderFacts.Distinct(degradations));
    }
}
