using System;
using TConfig.GameMath;

namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 基于列占用摘要的解析式扫掠：按"下一个整格前导面"跳步，面/体判定走位掩码，不再逐体素回调。
/// <para>语义与 <see cref="AabbSweep"/> 一致（axis 顺序 X→Z→Y、skin 内缩、地面 = 向下被阻挡且 delta.Y&lt;0），
/// 只是把「0.5 cm 子步 × 前导面全扫」换成「格面跳步 × 列掩码」，查询量与位移长度解耦。</para>
/// <para>约定：<paramref name="position"/> 是脚底中心；<paramref name="halfExtents"/> = (半径, 半高, 半径)。</para>
/// </summary>
public static class OccupancySweep
{
    private const float Epsilon = 1e-3f;
    private const int GuardLimit = 4096;

    /// <summary>体积判定（出生/传送/台阶落地校验用）。</summary>
    public static bool Collides(
        Vec3 position, Vec3 halfExtents, IOccupancySource source, float skin = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(source);

        CellRange(position, halfExtents, skin,
            out int x0, out int x1, out int y0, out int y1, out int z0, out int z1);
        if (x1 < x0 || y1 < y0 || z1 < z0) return false;

        return RegionSolid(x0, x1, y0, y1, z0, z1, source);
    }

    /// <summary>推进一位移；grounded = 向下被阻挡，hitHorizontal = X/Z 被阻挡。</summary>
    public static Vec3 Move(
        Vec3 position, Vec3 delta, Vec3 halfExtents, IOccupancySource source,
        out bool grounded, out bool hitHorizontal, float skin = 0.001f)
    {
        grounded = false;
        hitHorizontal = false;

        position = MoveAxis(position, delta.X, 0, halfExtents, source, out bool blockedX, skin);
        hitHorizontal |= blockedX;

        position = MoveAxis(position, delta.Z, 2, halfExtents, source, out bool blockedZ, skin);
        hitHorizontal |= blockedZ;

        position = MoveAxis(position, delta.Y, 1, halfExtents, source, out bool blockedY, skin);
        if (blockedY && delta.Y < 0) grounded = true;

        return position;
    }

    /// <summary>
    /// 台阶：水平被挡且着地时，尝试抬高 stepHeight 后继续水平位移；用占用摘要判定抬升净空与落点，不做全身积逐格扫描。
    /// </summary>
    public static Vec3 TryStepUp(
        Vec3 position, Vec3 delta, Vec3 halfExtents, float stepHeight,
        IOccupancySource source, bool grounded, float skin = 0.001f)
    {
        if (!grounded) return position;

        float horizontal = MathF.Abs(delta.X) + MathF.Abs(delta.Z);
        if (horizontal <= 0f) return position;

        Vec3 raised = position + new Vec3(0, stepHeight, 0);
        if (Collides(raised, halfExtents, source, skin)) return position;

        Vec3 target = raised + new Vec3(delta.X, 0, delta.Z);
        if (Collides(target, halfExtents, source, skin)) return position;

        Vec3 landed = MoveAxis(target, -stepHeight, 1, halfExtents, source, out _, skin);
        if (Collides(landed, halfExtents, source, skin)) return position;
        return landed;
    }

    private static Vec3 MoveAxis(
        Vec3 position, float amount, int axis, Vec3 halfExtents, IOccupancySource source,
        out bool blocked, float skin)
    {
        blocked = false;
        if (amount == 0f) return position;

        float sign = amount > 0f ? 1f : -1f;
        float remaining = MathF.Abs(amount);

        Vec3 current = position;
        int guard = 0;
        while (remaining > 0f && ++guard <= GuardLimit)
        {
            LeadingFace(current, halfExtents, axis, sign, skin, out int lead, out float toNext);

            float stepLength = MathF.Min(remaining, toNext);
            if (stepLength <= 0f) stepLength = MathF.Min(remaining, Epsilon);

            Vec3 tentative = Advance(current, sign * stepLength, axis);
            LeadingFace(tentative, halfExtents, axis, sign, skin, out int nextLead, out _);

            if (nextLead != lead)
            {
                // 只在新前导面被跨入时判一次；面已实心则停在面之前（与子步实现语义一致）。
                CellRange(tentative, halfExtents, skin,
                    out int x0, out int x1, out int y0, out int y1, out int z0, out int z1);

                if (FaceBlocked(nextLead, axis, x0, x1, y0, y1, z0, z1, source))
                {
                    blocked = true;
                    return current;
                }
            }

            current = tentative;
            remaining -= stepLength;
        }

        return current;
    }

    private static bool FaceBlocked(
        int plane, int axis,
        int x0, int x1, int y0, int y1, int z0, int z1,
        IOccupancySource source)
    {
        if (x1 < x0 || y1 < y0 || z1 < z0) return false;

        return axis switch
        {
            0 => RegionSolid(plane, plane, y0, y1, z0, z1, source),
            1 => RegionSolid(x0, x1, plane, plane, z0, z1, source),
            _ => RegionSolid(x0, x1, y0, y1, plane, plane, source),
        };
    }

    /// <summary>整数区域实心判定：按分块分组，一次取掩码判 32 层，内层只做位与。</summary>
    private static bool RegionSolid(
        int x0, int x1, int y0, int y1, int z0, int z1, IOccupancySource source)
    {
        int size = source.ChunkSize;
        int mask0 = size - 1;

        int cx0 = ChunkSpace.ChunkIndex(x0, size), cx1 = ChunkSpace.ChunkIndex(x1, size);
        int cy0 = ChunkSpace.ChunkIndex(y0, size), cy1 = ChunkSpace.ChunkIndex(y1, size);
        int cz0 = ChunkSpace.ChunkIndex(z0, size), cz1 = ChunkSpace.ChunkIndex(z1, size);

        for (int cz = cz0; cz <= cz1; cz++)
        {
            int lz0 = Math.Max(z0, cz * size) & mask0;
            int lz1 = Math.Min(z1, cz * size + mask0) & mask0;
            for (int cy = cy0; cy <= cy1; cy++)
            {
                int ly0 = Math.Max(y0, cy * size) & mask0;
                int ly1 = Math.Min(y1, cy * size + mask0) & mask0;
                uint yMask = RangeMask(ly0, ly1);
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    var occupancy = source.Get(new ChunkCoord(cx, cy, cz));
                    if (occupancy is null) continue;

                    int lx0 = Math.Max(x0, cx * size) & mask0;
                    int lx1 = Math.Min(x1, cx * size + mask0) & mask0;
                    uint[] masks = occupancy.Masks;
                    for (int lz = lz0; lz <= lz1; lz++)
                    {
                        int row = lz * size;
                        for (int lx = lx0; lx <= lx1; lx++)
                        {
                            if ((masks[row + lx] & yMask) != 0) return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>[lo, hi] 的本地 Y 位掩码。</summary>
    private static uint RangeMask(int lo, int hi)
    {
        int count = hi - lo + 1;
        if (count >= 32) return uint.MaxValue;
        if (count <= 0) return 0u;
        return ((1u << count) - 1u) << lo;
    }

    private static void CellRange(
        Vec3 position, Vec3 halfExtents,
        out int x0, out int x1, out int y0, out int y1, out int z0, out int z1,
        float skin)
    {
        float minX = position.X - halfExtents.X;
        float maxX = position.X + halfExtents.X;
        float minY = position.Y;
        float maxY = position.Y + halfExtents.Y * 2f;
        float minZ = position.Z - halfExtents.Z;
        float maxZ = position.Z + halfExtents.Z;

        x0 = (int)MathF.Floor(minX + skin);
        x1 = (int)MathF.Floor(maxX - skin);
        y0 = (int)MathF.Floor(minY + skin);
        y1 = (int)MathF.Floor(maxY - skin);
        z0 = (int)MathF.Floor(minZ + skin);
        z1 = (int)MathF.Floor(maxZ - skin);
    }

    private static void CellRange(
        Vec3 position, Vec3 halfExtents, float skin,
        out int x0, out int x1, out int y0, out int y1, out int z0, out int z1)
        => CellRange(position, halfExtents, out x0, out x1, out y0, out y1, out z0, out z1, skin);

    private static void LeadingFace(
        Vec3 position, Vec3 halfExtents, int axis, float sign, float skin,
        out int lead, out float toNext)
    {
        float center = axis == 0 ? position.X : axis == 1 ? position.Y : position.Z;
        float half = axis == 0 ? halfExtents.X : axis == 1 ? halfExtents.Y : halfExtents.Z;
        float min = center - half;
        float max = axis == 1 ? center + half * 2f : center + half;

        if (sign > 0f)
        {
            lead = (int)MathF.Floor(max - skin);
            toNext = (lead + 1f + skin) - max;
        }
        else
        {
            lead = (int)MathF.Floor(min + skin);
            toNext = (min + skin) - lead;
        }

        if (toNext < 0f) toNext = 0f;
    }

    private static Vec3 Advance(Vec3 position, float amount, int axis) =>
        axis == 0 ? new Vec3(position.X + amount, position.Y, position.Z)
        : axis == 1 ? new Vec3(position.X, position.Y + amount, position.Z)
        : new Vec3(position.X, position.Y, position.Z + amount);
}
