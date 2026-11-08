#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 绘制范围（宿主侧别名；判定与排序已在 <see cref="SurfacePaintPlan"/>）。
/// </summary>
internal enum PaintScope
{
    /// <summary>全部相位（含高光）。</summary>
    All,

    /// <summary>静态相位（Underlay/Fill/Edge/Overlay；供底图缓存）。</summary>
    Static,

    /// <summary>仅动态相位（Sheen）。</summary>
    Dynamic,
}

/// <summary>
/// WinForms 适配门面（**薄层**）：只做范围映射、报告映射与成本采样接线；
/// 绘制实现单源 = <see cref="SurfacePaintEngine"/>（Share.SurfacePaint，载体无关）。
/// </summary>
public static class SkinPaintEngine
{
    private static readonly SurfacePaintTelemetry Telemetry = new(
        SkinRenderProfile.Begin, SkinRenderProfile.RecordLayer, SkinRenderProfile.RecordBlur);

    /// <summary>资源装载钩子（转发框架）。</summary>
    public static Func<string, Image?>? AssetLoader
    {
        get => SurfacePaintEngine.AssetLoader;
        set => SurfacePaintEngine.AssetLoader = value;
    }

    /// <summary>资源根目录（转发框架）。</summary>
    public static string? AssetRoot => SurfacePaintEngine.AssetRoot;

    /// <summary>资产代（转发框架）。</summary>
    public static int AssetGeneration => SurfacePaintEngine.AssetGeneration;

    /// <summary>配置资源根（转发框架）。</summary>
    public static void ConfigureAssetRoot(string? root) => SurfacePaintEngine.ConfigureAssetRoot(root);

    /// <summary>清空资源缓存（转发框架）。</summary>
    public static void ClearAssetCache() => SurfacePaintEngine.ClearAssetCache();

    /// <summary>绘制一次材质（背景）。</summary>
    public static IReadOnlyList<string> Paint(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, Image? backdrop = null,
        SurfaceLightState? light = null, bool includeSpecular = true)
        => Paint(graphics, bounds, material, out _, backdrop, light, includeSpecular);

    /// <summary>绘制一次材质（背景），并回报实际落笔图层数。</summary>
    public static IReadOnlyList<string> Paint(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, out int paintedLayers,
        Image? backdrop = null, SurfaceLightState? light = null, bool includeSpecular = true)
    {
        var report = PaintReport(graphics, bounds, material, backdrop, light,
            includeSpecular ? PaintScope.All : PaintScope.Static);
        paintedLayers = report.PaintedLayers;
        return report.Details;
    }

    /// <summary>只画随时间/光标变化的层（当前仅 specular）。</summary>
    public static IReadOnlyList<string> PaintDynamic(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, SurfaceLightState? light)
        => PaintDynamic(graphics, bounds, material, light, out _);

    /// <summary>只画动态层，并回报实际落笔图层数。</summary>
    public static IReadOnlyList<string> PaintDynamic(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, SurfaceLightState? light, out int paintedLayers)
    {
        var report = PaintReport(graphics, bounds, material, null, light, PaintScope.Dynamic);
        paintedLayers = report.PaintedLayers;
        return report.Details;
    }

    /// <summary>绘制并回传宿主报告（K6）；实现单源 = 框架引擎。</summary>
    internal static SkinPaintReport PaintReport(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, Image? backdrop,
        SurfaceLightState? light, PaintScope scope)
    {
        var report = SurfacePaintEngine.Paint(graphics, bounds, material, backdrop, light, Map(scope), Telemetry);
        return new SkinPaintReport(report.Total, report.Ok, report.Degraded, report.Skipped, report.Failed, report.Details);
    }

    private static SurfacePaintScope Map(PaintScope scope) => scope switch
    {
        PaintScope.Static => SurfacePaintScope.Static,
        PaintScope.Dynamic => SurfacePaintScope.Dynamic,
        _ => SurfacePaintScope.All,
    };

    /// <summary>材质底色（兼容保留；属性通道请走 WinFormsPropertyProjector）。</summary>
    [Obsolete("属性通道统一经 WinFormsPropertyProjector（不透明化）；本方法仅为二进制兼容保留")]
    public static bool TryGetBaseColor(ResolvedMaterial? material, out Color color)
        => SurfacePaintEngine.TryGetBaseColor(material, out color);
}