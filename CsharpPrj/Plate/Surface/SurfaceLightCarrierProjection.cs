#nullable enable

using System.Diagnostics;

namespace UiBackend;

/// <summary>
/// 载体在屏幕上的物理矩形（DIP）：唯一来源是 WinForms 控件的 <c>PointToScreen(Point.Empty)</c> + 客户区尺寸。
/// <para>字段名带 <c>Screen</c> 前缀，调用方不可能把载体局部坐标误当输入。</para>
/// </summary>
public readonly record struct SurfaceCarrierFrame(
    float ScreenXDip,
    float ScreenYDip,
    float WidthDip,
    float HeightDip,
    int Dpi);

/// <summary>世界锚点在载体坐标系中的投影结果（**不夹取**：允许落在载体之外）。</summary>
public readonly record struct SurfaceLightCarrierPoint(
    float X,
    float Y,
    bool Inside,
    string? OutOfBoundsDetail);

/// <summary>
/// **唯一**的世界锚点 → 载体坐标换算入口。所有发布点（HTML 推送 / WinForms 采样）必须走这里，
/// 防止「有的减了原点、有的没减」的双重减法。
/// <para>输入 <see cref="SurfaceCarrierFrame"/> 是物理屏幕 DIP；输出是载体客户区坐标。
/// 越界只记账，不把值拉回边界——光斑是否可见由 <c>Shade</c> 判定（见 <see cref="SurfaceLightAnchor"/> 契约）。</para>
/// </summary>
public static class SurfaceLightCarrierProjection
{
    /// <summary>把世界锚点投影到载体坐标。</summary>
    /// <param name="world">世界固定锚点（屏幕 DIP）。</param>
    /// <param name="carrier">载体客户区的屏幕矩形（DIP）；必须由宿主控件实测产出。</param>
    public static SurfaceLightCarrierPoint Project(SurfaceLightAnchor world, SurfaceCarrierFrame carrier)
    {
        Debug.Assert(float.IsFinite(world.X) && float.IsFinite(world.Y), "world anchor must be finite screen DIP");
        Debug.Assert(float.IsFinite(carrier.ScreenXDip) && float.IsFinite(carrier.ScreenYDip),
            "carrier origin must be finite screen DIP");
        Debug.Assert(carrier.WidthDip > 0f && carrier.HeightDip > 0f,
            "carrier size must be positive; pass the measured client rect, not an uninitialized control");

        var x = world.X - carrier.ScreenXDip;
        var y = world.Y - carrier.ScreenYDip;
        var inside = x >= 0f && y >= 0f && x <= carrier.WidthDip && y <= carrier.HeightDip;
        var detail = inside
            ? null
            : $"world=({world.X:F1},{world.Y:F1}) origin=({carrier.ScreenXDip:F1},{carrier.ScreenYDip:F1}) "
              + $"local=({x:F1},{y:F1}) rect={carrier.WidthDip:F1}x{carrier.HeightDip:F1}";

        return new SurfaceLightCarrierPoint(x, y, inside, detail);
    }
}
