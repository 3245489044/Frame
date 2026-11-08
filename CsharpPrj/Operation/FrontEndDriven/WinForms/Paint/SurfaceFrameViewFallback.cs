#nullable enable

using System;
using Modules.Gather;

namespace FrontEndDriven.WinForms.Paint;

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

    private static readonly Lazy<GatherViewSpec> Spec = new(Create, isThreadSafe: true);

    /// <summary>回退视图声明（惰性；首次构造时记一次可见降级）。</summary>
    internal static GatherViewSpec View => Spec.Value;

    private static GatherViewSpec Create()
    {
        FrontEndDriven.Diagnostics.FrontEndDrivenLog.Log(
            "surface-frame-view-unavailable",
            new { view = ViewId, reason = "declaration-source-missing", action = "visible-fallback" });

        return new GatherViewSpec(
            ViewId,
            new[] { new GatherStageSpec("frame", new[] { InputDomain }, Array.Empty<string>(), new[] { "frame" }) },
            new[] { new GatherNeedSpec(InputDomain, "window", GatherCostClass.High, Reusable: false) },
            new[] { new GatherProduceSpec("surface-frame", "bitmap-frame", "transient", "micro") });
    }
}
