using TConfig.GameMath;

namespace MathematicalAlgorithms.Culling;

/// <summary>
/// 相机朝向基向量（yaw/pitch → forward/right/up）。
/// <para>自 Engine VoxelMath 提取：纯数学、无平台绑定；左手基约定 R = F × Up、U = R × F。</para>
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
}
