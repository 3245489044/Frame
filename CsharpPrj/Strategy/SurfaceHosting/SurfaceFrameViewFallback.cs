#nullable enable

using System;
using Modules.Gather;

namespace Strategy.SurfaceHosting;

/// <summary>
/// 窗口帧视图的**显式回退**（仅在声明面不可用时使用）：形状与
/// <c>gather-views.ushar#render-frame</c> 一致，首次使用记
/// <c>surface-frame-view-unavailable</c>（可见降级，禁静默内联）。
/// <para>回退不构成第二声明源：一旦宿主注入 <see cref="ISurfaceFrameGatherSource"/>，本类不再参与。</para>
/// </summary>
internal static class SurfaceFrameViewFallback
{
    /// <summary>回退视图 id（与声明面同名，保证两条路径语义一致）。</summary>
    internal const string ViewId = "render-frame";

    /// <summary>唯一取数域（声明面同一域）。</summary>
    internal const string InputDomain = "surface-frame-input";

    /// <summary>
    /// 最小回退视图（仅 1 need）：仅在宿主子模块不可用时使用，保证窗口不因装配失败而挂载中断。
    /// <para>产物名仍为 <c>surface-frame</c>（出口闭合）；其余域全部缺席 ⇒ Composer 显式降级。</para>
    /// </summary>
    private static readonly Lazy<GatherViewSpec> Legacy = new(
        () => new GatherViewSpec(
            ViewId,
            new[] { new GatherStageSpec("frame", new[] { InputDomain }, Array.Empty<string>(), new[] { "frame" }) },
            new[] { new GatherNeedSpec(InputDomain, "window", GatherCostClass.High, Reusable: false) },
            new[] { new GatherProduceSpec("surface-frame", "window-frame", "transient", "micro") }),
        isThreadSafe: true);

    /// <summary>最小回退视图（宿主子模块不可用时的可见降级）。</summary>
    internal static GatherViewSpec LegacyView => Legacy.Value;

    private static readonly Lazy<GatherViewSpec> Spec = new(Create, isThreadSafe: true);

    /// <summary>回退视图声明（惰性；首次构造时记一次可见降级）。</summary>
    internal static GatherViewSpec View => Spec.Value;

    private static GatherViewSpec Create()
    {
        Share.SurfacePaint.Compat.SurfaceCompatLog.Log(
            "surface-frame-view-unavailable",
            new { view = ViewId, reason = "declaration-source-missing", action = "visible-fallback" });

        return new GatherViewSpec(
            ViewId,
            new[]
            {
                new GatherStageSpec("policy", new[] { "render-policy", "carrier-capabilities" },
                    Array.Empty<string>(), new[] { "feed" }),
                new GatherStageSpec("declaration", new[] { "surface-program", "surface-selection", "surface-plan" },
                    new[] { "policy" }, new[] { "feed" }),
                new GatherStageSpec("context",
                    new[]
                    {
                        "surface-light", "surface-pointer", "surface-composition",
                        "surface-backdrop", "surface-glass", "surface-frame-budget",
                    },
                    new[] { "declaration" }, new[] { "frame" }),
                new GatherStageSpec("compose",
                    new[] { "surface-frame-input", "surface-decor", "surface-viewport" },
                    new[] { "context" }, new[] { "frame" }),
            },
            new[]
            {
                new GatherNeedSpec("render-policy", "config", GatherCostClass.Low, Reusable: true),
                new GatherNeedSpec("carrier-capabilities", "config", GatherCostClass.Low, Reusable: true),
                new GatherNeedSpec("surface-program", "config", GatherCostClass.Low, Reusable: true),
                new GatherNeedSpec("surface-selection", "profile", GatherCostClass.Low, Reusable: true),
                new GatherNeedSpec("surface-plan", "view", GatherCostClass.Low, Reusable: false),
                new GatherNeedSpec("surface-light", "frame", GatherCostClass.Low, Reusable: false),
                new GatherNeedSpec("surface-pointer", "frame", GatherCostClass.Low, Reusable: false),
                new GatherNeedSpec("surface-composition", "host", GatherCostClass.Low, Reusable: true),
                new GatherNeedSpec("surface-backdrop", "window", GatherCostClass.High, Reusable: false),
                new GatherNeedSpec("surface-glass", "frame", GatherCostClass.Low, Reusable: false),
                new GatherNeedSpec("surface-frame-budget", "config", GatherCostClass.Low, Reusable: true),
                new GatherNeedSpec("surface-decor", "view", GatherCostClass.Low, Reusable: false),
                new GatherNeedSpec("surface-viewport", "frame", GatherCostClass.Low, Reusable: false),
                new GatherNeedSpec("surface-frame-input", "window", GatherCostClass.High, Reusable: false),
            },
            new[]
            {
                new GatherProduceSpec("surface-frame", "window-frame", "transient", "micro"),
                new GatherProduceSpec("render-frame-diagnostics", "report", "contextual", "macro",
                    "Frame\\Invoke\\Invoke-Frame.ps1 -Action invoke-verify"),
            });
    }
}
