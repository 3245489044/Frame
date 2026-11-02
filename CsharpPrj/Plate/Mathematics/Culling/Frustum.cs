using TConfig.GameMath;

namespace MathematicalAlgorithms.Culling;

/// <summary>
/// 视锥——6 个半空间平面的可见性判据（唯一实现）。
/// <para>由相机姿态（位置 + yaw/pitch + 垂直 FOV + 宽高比）构造平面，以「AABB 中心 + 半径投影」判定可见；
/// 自 Engine VoxelMath 提取，纯数学、无平台绑定。</para>
/// </summary>
public sealed class Frustum
{
    private readonly Plane _left, _right, _bottom, _top, _near, _far;
    private readonly Vec3 _eye;
    private readonly Vec3 _forward;

    private Frustum(in Vec3 eye, in Vec3 forward, Plane left, Plane right, Plane bottom, Plane top, Plane near, Plane far)
    {
        _eye = eye;
        _forward = forward;
        _left = left; _right = right; _bottom = bottom; _top = top; _near = near; _far = far;
    }

    public Vec3 Eye => _eye;
    public Vec3 Forward => _forward;

    /// <summary>从相机姿态构造视锥（near/far 为世界单位）。</summary>
    public static Frustum FromPose(
        in Vec3 eye, float yaw, float pitch,
        float verticalFov, float aspect, float near, float far)
    {
        if (verticalFov <= 0f) throw new ArgumentOutOfRangeException(nameof(verticalFov), "FOV 必须为正");
        if (aspect <= 0f) throw new ArgumentOutOfRangeException(nameof(aspect), "宽高比必须为正");

        var forward = CameraMath.BuildForward(yaw, pitch).Normalized();
        var right = Vec3.Cross(forward, Vec3.Up);
        right = (right == Vec3.Zero) ? new Vec3(0, 0, 1) : right.Normalized();
        var up = Vec3.Cross(right, forward).Normalized();

        float tanV = (float)System.Math.Tan(verticalFov * 0.5f);
        float tanH = tanV * aspect;

        // 各平面法线朝向视锥内部（Plane 内部判据：dot(n, p) >= D）。
        // 注意：本引擎基为左手的 (R = F × Up, U = R × F)，左右/上下平面法线的叉乘顺序不对称。
        var left = Plane.Through(eye, Vec3.Cross(forward - right * tanH, up));
        var rightPlane = Plane.Through(eye, Vec3.Cross(up, forward + right * tanH));
        var bottom = Plane.Through(eye, Vec3.Cross(right, forward - up * tanV));
        var top = Plane.Through(eye, Vec3.Cross(forward + up * tanV, right));
        var nearPlane = Plane.Through(eye + forward * near, forward);
        var farPlane = Plane.Through(eye + forward * far, -forward);

        return new Frustum(eye, forward, left, rightPlane, bottom, top, nearPlane, farPlane);
    }

    /// <summary>相机在原点朝 +X / yaw=0 / pitch=0 时的默认视锥（无渲染目标纵横比信息时使用）。</summary>
    public static Frustum FromPose(in Vec3 eye, float yaw, float pitch, float verticalFov)
        => FromPose(eye, yaw, pitch, verticalFov, 16f / 9f, 0.1f, 512f);

    public bool Contains(in Vec3 point)
        => _left.Distance(point) >= 0 && _right.Distance(point) >= 0
        && _bottom.Distance(point) >= 0 && _top.Distance(point) >= 0
        && _near.Distance(point) >= 0 && _far.Distance(point) >= 0;

    /// <summary>AABB 与 6 个平面相交判定（中心 + 半径投影，保守且无假阴性）。</summary>
    public bool Intersects(in Aabb box)
    {
        float cx = (box.MinX + box.MaxX) * 0.5f;
        float cy = (box.MinY + box.MaxY) * 0.5f;
        float cz = (box.MinZ + box.MaxZ) * 0.5f;
        // +0.5：整数包围盒覆盖的是体素中心，半边长需含半个体素。
        float ex = (box.MaxX - box.MinX) * 0.5f + 0.5f;
        float ey = (box.MaxY - box.MinY) * 0.5f + 0.5f;
        float ez = (box.MaxZ - box.MinZ) * 0.5f + 0.5f;

        var center = new Vec3(cx, cy, cz);
        return _left.Outside(center, ex, ey, ez) is false
            && _right.Outside(center, ex, ey, ez) is false
            && _bottom.Outside(center, ex, ey, ez) is false
            && _top.Outside(center, ex, ey, ez) is false
            && _near.Outside(center, ex, ey, ez) is false
            && _far.Outside(center, ex, ey, ez) is false;
    }

    /// <summary>点到视锥中心的距离（供流式排序使用）。</summary>
    public float DistanceTo(in Vec3 point)
    {
        float dx = point.X - _eye.X, dy = point.Y - _eye.Y, dz = point.Z - _eye.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private readonly struct Plane
    {
        public readonly Vec3 Normal;
        public readonly float D;

        private Plane(in Vec3 normal, float d) => (Normal, D) = (normal, d);

        public static Plane Through(in Vec3 point, in Vec3 normal)
        {
            var n = normal.Normalized();
            return new Plane(n, Vec3.Dot(n, point));
        }

        /// <summary>平面内为正；AABB 在该平面外侧（完全被剔除）时返回 true。</summary>
        public bool Outside(in Vec3 center, float ex, float ey, float ez)
        {
            float radius = ex * MathF.Abs(Normal.X) + ey * MathF.Abs(Normal.Y) + ez * MathF.Abs(Normal.Z);
            return Vec3.Dot(Normal, center) - D < -radius;
        }

        public float Distance(in Vec3 point) => Vec3.Dot(Normal, point) - D;
    }
}
