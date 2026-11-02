using System;
using TConfig.GameMath;

namespace MathematicalAlgorithms.Spatial;

/// <summary>体素命中结果：命中格 + 进入面法线 + 相邻可放置格 + 距离。不含方块身份语义。</summary>
public readonly record struct GridHit(
    int X, int Y, int Z,
    int NormalX, int NormalY, int NormalZ,
    int AdjacentX, int AdjacentY, int AdjacentZ,
    float Distance)
{
    public Vec3 Normal => new(NormalX, NormalY, NormalZ);
    public Vec3 Center => new(X + 0.5f, Y + 0.5f, Z + 0.5f);
}

/// <summary>格子是否实心：调用方提供，算法不解释“实心”的语义来源。</summary>
public delegate bool SolidSampler(int x, int y, int z);

/// <summary>
/// DDA 体素射线（Amanatides-Woo）：逐格步进，返回首个实心格与进入面法线。
/// <para>不认 BlockId / 图集 / 液体；是否穿过液体由 <see cref="SolidSampler"/> 的调用方决定。</para>
/// <para>确定性：同样的 origin/direction/距离与 sampler 输出同样的命中。</para>
/// </summary>
public static class GridRaycast
{
    public static GridHit? Cast(in Vec3 origin, in Vec3 direction, float maxDistance, SolidSampler solid)
    {
        ArgumentNullException.ThrowIfNull(solid);

        Vec3 dir = direction.Normalized();
        if (dir == Vec3.Zero) return null;

        int x = (int)MathF.Floor(origin.X);
        int y = (int)MathF.Floor(origin.Y);
        int z = (int)MathF.Floor(origin.Z);

        int stepX = dir.X > 0 ? 1 : dir.X < 0 ? -1 : 0;
        int stepY = dir.Y > 0 ? 1 : dir.Y < 0 ? -1 : 0;
        int stepZ = dir.Z > 0 ? 1 : dir.Z < 0 ? -1 : 0;

        float tDeltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / dir.X);
        float tDeltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / dir.Y);
        float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1f / dir.Z);

        float tMaxX = stepX == 0
            ? float.PositiveInfinity
            : ((stepX > 0 ? x + 1 - origin.X : origin.X - x) / MathF.Abs(dir.X));
        float tMaxY = stepY == 0
            ? float.PositiveInfinity
            : ((stepY > 0 ? y + 1 - origin.Y : origin.Y - y) / MathF.Abs(dir.Y));
        float tMaxZ = stepZ == 0
            ? float.PositiveInfinity
            : ((stepZ > 0 ? z + 1 - origin.Z : origin.Z - z) / MathF.Abs(dir.Z));

        float distance = 0f;
        int normalX = 0, normalY = 0, normalZ = 0;

        while (distance <= maxDistance)
        {
            if (solid(x, y, z))
            {
                return new GridHit(
                    x, y, z, normalX, normalY, normalZ,
                    x + normalX, y + normalY, z + normalZ,
                    distance);
            }

            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                distance = tMaxX;
                tMaxX += tDeltaX;
                x += stepX;
                normalX = -stepX; normalY = 0; normalZ = 0;
            }
            else if (tMaxY < tMaxZ)
            {
                distance = tMaxY;
                tMaxY += tDeltaY;
                y += stepY;
                normalX = 0; normalY = -stepY; normalZ = 0;
            }
            else
            {
                distance = tMaxZ;
                tMaxZ += tDeltaZ;
                z += stepZ;
                normalX = 0; normalY = 0; normalZ = -stepZ;
            }

            if (distance > maxDistance) break;
        }

        return null;
    }
}
