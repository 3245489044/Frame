#nullable enable

namespace Share.SurfacePaint.Ambient;

/// <summary>装饰层诊断端口（默认空；载体装配期注入 ModuleLog 适配器）。</summary>
public static class SurfaceAmbientLog
{
    /// <summary>当前日志端口（默认空实现）。</summary>
    public static Hosting.ISurfaceEventLog Log { get; set; } = Hosting.NullSurfaceEventLog.Instance;

    internal static void RecordLightDegraded(string reason, string detail)
        => Log.Write("surface.light.degraded", new { reason, detail });

    internal static void RecordCarrierOutOfBounds(string carrier, string detail)
        => Log.Write("surface.carrier.out-of-bounds", new { carrier, detail });

    internal static void RecordCarrierProjection(string carrier, object? anchor, object? frame, object? point)
        => Log.Write("surface.carrier.projection", new { carrier, anchor, frame, point });
}
