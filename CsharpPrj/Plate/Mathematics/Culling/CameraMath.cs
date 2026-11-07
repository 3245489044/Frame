using TConfig.GameMath;

namespace MathematicalAlgorithms.Culling;

/// <summary>
/// 世界空间射线：<see cref="Origin"/> 为世界坐标（cm），<see cref="Direction"/> 为单位向量。
/// <para>屏幕拾取的唯一中间产物；与 <c>Spatial.GridRaycast</c> / 业务拾取直接对接，不含视口/DPI 语义。</para>
/// </summary>
public readonly record struct WorldRay(Vec3 Origin, Vec3 Direction);

/// <summary>
/// 相机朝向基向量（yaw/pitch → forward/right/up）与屏幕反投影。
/// <para>自 Engine VoxelMath 提取：纯数学、无平台绑定；左手基约定 R = F × Up、U = R × F。</para>
/// <para>反投影只接受 NDC + View/Proj（像素 → NDC 的 letterbox/DPI 归一化由调用方做），
/// 避免共享数学层依赖某个具体渲染路径。</para>
/// </summary>
public static class CameraMath
{
    public static Vec3 BuildForward(float yaw, float pitch)
    {
        float cp = (float)System.Math.Cos(pitch);
        return new Vec3(
            (float)System.Math.Cos(yaw) * cp,
            (float)System.Math.Sin(pitch),
            (float)System.Math.Sin(yaw) * cp);
    }

    public static Vec3 BuildRight(in Vec3 forward)
    {
        Vec3 right = Vec3.Cross(forward, Vec3.Up);
        return Vec3.Normalize(right);
    }

    public static (Vec3 forward, Vec3 right, Vec3 up) BuildBasis(float yaw, float pitch)
    {
        Vec3 forward = BuildForward(yaw, pitch);
        Vec3 right = BuildRight(forward);
        Vec3 up = Vec3.Cross(right, forward);
        return (forward, right, up);
    }

    /// <summary>
    /// NDC 点 → 世界点（unproject）：<paramref name="ndc"/> 的 z 采用 [0,1]（近平面 0、远平面 1，与
    /// <see cref="Matrix4x4.Perspective"/> 的约定一致）。<paramref name="inverseViewProjection"/> 必须是
    /// <c>(view * proj)⁻¹</c>，且乘法沿用行向量约定 <c>v' = v * M</c>。
    /// </summary>
    public static Vec3 UnprojectNdc(in Vec3 ndc, in Matrix4x4 inverseViewProjection)
    {
        var m = inverseViewProjection;
        float x = ndc.X, y = ndc.Y, z = ndc.Z;
        float w = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
        // 齐次除法：w 恰为 0 只可能来自退化的投影；夹到极小值让调用方的射线长度判据拒绝该点。
        if (System.Math.Abs(w) < 1e-6f)
            w = w < 0f ? -1e-6f : 1e-6f;

        return new Vec3(
            (x * m.M11 + y * m.M21 + z * m.M31 + m.M41) / w,
            (x * m.M12 + y * m.M22 + z * m.M32 + m.M42) / w,
            (x * m.M13 + y * m.M23 + z * m.M33 + m.M43) / w);
    }

    /// <summary>
    /// 屏幕（NDC）→ 世界射线：近/远平面两点反投影后取方向，原点固定为相机位置
    /// （比近平面点更贴近“从眼睛出发”的拾取语义，也不受近裁剪面偏移影响）。
    /// <para>View/Proj 奇异、或近远点退化为同一位置时返回 false（调用方按“无命中”处理）。</para>
    /// </summary>
    public static bool TryScreenToRay(
        float ndcX, float ndcY,
        in Matrix4x4 view, in Matrix4x4 proj, in Vec3 origin,
        out WorldRay ray)
    {
        ray = default;
        if (!(view * proj).TryInvert(out var inverseViewProjection))
            return false;

        Vec3 near = UnprojectNdc(new Vec3(ndcX, ndcY, 0f), inverseViewProjection);
        Vec3 far = UnprojectNdc(new Vec3(ndcX, ndcY, 1f), inverseViewProjection);
        Vec3 delta = far - near;
        float lengthSquared = Vec3.Dot(delta, delta);
        if (!(lengthSquared > 1e-12f) || float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared))
            return false;

        ray = new WorldRay(origin, Vec3.Normalize(delta));
        return true;
    }
}
