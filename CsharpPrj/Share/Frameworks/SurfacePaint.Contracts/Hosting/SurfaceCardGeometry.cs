#nullable enable

using System.Drawing;

namespace Share.SurfacePaint.Hosting;

/// <summary>
/// 一张卡片的几何事实（**采集期唯一产物**）：只描述「哪里有卡片、多大、属于谁」，不携带材质。
/// <para>材质由 <c>surface-plan</c> + <c>surface-decor</c> 决定；采集期不读材质/不解析光。</para>
/// </summary>
/// <param name="Key">宿主键（与 <c>ISurfaceFrameConsumer.Key</c> 同源）。</param>
/// <param name="BoundsDip">窗口客户区坐标（DIP；原点 0,0）。</param>
/// <param name="HostKind">宿主形态（<c>control</c> | <c>html-element</c>）。</param>
/// <param name="ContainerKey">已能映射到的表面容器键（可空）。</param>
/// <param name="DepthHint">父链深度（结构事实；角色兜底用）。</param>
/// <param name="MaterialSlot">宿主当前生效的材质槽（只传槽名，不传解析结果）。</param>
public sealed record SurfaceCardGeometry(
    string Key,
    Rectangle BoundsDip,
    string HostKind,
    string? ContainerKey,
    int DepthHint,
    string MaterialSlot = "")
{
    /// <summary>空槽（未挂载材质）。</summary>
    public const string NoMaterial = "";
}
