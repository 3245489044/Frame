using System;
using TConfig.GameMath;

namespace MathematicalAlgorithms.Spatial;

/// <summary>AABB 扫掠结果：最终位置 + 落地/水平受阻标记。</summary>
public readonly record struct SweepResult(Vec3 Position, bool Grounded, bool HitHorizontal);

/// <summary>
/// 轴对齐包围盒与体素格的扫掠碰撞：按轴分离推进 + 小步逼近，避免高速穿墙。
/// <para>约定：<paramref name="position"/> 是脚底中心；<paramref name="halfExtents"/> = (半径, 半高, 半径)。</para>
/// <para>算法只认“是否实心”的回调，不含方块身份/重力/输入语义。</para>
/// </summary>
public static class AabbSweep
{
    private const float Skin = 0.001f;
    private const float SubStep = 0.05f;

    public static bool Collides(Vec3 position, Vec3 halfExtents, SolidSampler solid)
    {
        ArgumentNullException.ThrowIfNull(solid);

        float minX = position.X - halfExtents.X;
        float maxX = position.X + halfExtents.X;
        float minY = position.Y;
        float maxY = position.Y + halfExtents.Y * 2f;
        float minZ = position.Z - halfExtents.Z;
        float maxZ = position.Z + halfExtents.Z;

        int x0 = (int)MathF.Floor(minX + Skin);
        int x1 = (int)MathF.Floor(maxX - Skin);
        int y0 = (int)MathF.Floor(minY + Skin);
        int y1 = (int)MathF.Floor(maxY - Skin);
        int z0 = (int)MathF.Floor(minZ + Skin);
        int z1 = (int)MathF.Floor(maxZ - Skin);

        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (solid(x, y, z))
                return true;

        return false;
    }

    /// <summary>推进一位移；grounded = 向下被阻挡，hitHorizontal = X/Z 被阻挡。</summary>
    public static Vec3 Move(
        Vec3 position, Vec3 delta, Vec3 halfExtents, SolidSampler solid,
        out bool grounded, out bool hitHorizontal)
    {
        grounded = false;
        hitHorizontal = false;

        position = MoveAxis(position, delta.X, 0, halfExtents, solid, out bool blockedX);
        hitHorizontal |= blockedX;

        position = MoveAxis(position, delta.Z, 2, halfExtents, solid, out bool blockedZ);
        hitHorizontal |= blockedZ;

        position = MoveAxis(position, delta.Y, 1, halfExtents, solid, out bool blockedY);
        if (blockedY && delta.Y < 0) grounded = true;

        return position;
    }

    /// <summary>台阶抬升：水平被挡且已着地时，尝试抬高 stepHeight 后继续水平位移。</summary>
    public static Vec3 TryStepUp(
        Vec3 position, Vec3 delta, Vec3 halfExtents, float stepHeight,
        SolidSampler solid, bool grounded)
    {
        if (!grounded) return position;

        float horizontal = MathF.Abs(delta.X) + MathF.Abs(delta.Z);
        if (horizontal <= 0f) return position;

        Vec3 raised = position + new Vec3(0, stepHeight, 0);
        if (Collides(raised, halfExtents, solid)) return position;

        Vec3 target = raised + new Vec3(delta.X, 0, delta.Z);
        if (Collides(target, halfExtents, solid)) return position;

        Vec3 landed = MoveAxis(target, -stepHeight, 1, halfExtents, solid, out _);
        if (Collides(landed, halfExtents, solid)) return position;
        return landed;
    }

    private static Vec3 MoveAxis(
        Vec3 position, float amount, int axis, Vec3 halfExtents, SolidSampler solid, out bool blocked)
    {
        blocked = false;
        if (amount == 0f) return position;

        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(amount) / SubStep));
        float sign = amount > 0 ? 1f : -1f;
        float perStep = MathF.Abs(amount) / steps;

        Vec3 current = position;
        for (int i = 0; i < steps; i++)
        {
            Vec3 next = current;
            if (axis == 0) next.X += sign * perStep;
            else if (axis == 1) next.Y += sign * perStep;
            else next.Z += sign * perStep;

            if (Collides(next, halfExtents, solid))
            {
                blocked = true;
                break;
            }

            current = next;
        }

        return current;
    }
}
