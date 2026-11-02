using System;
using TConfig.GameMath;

namespace MathematicalAlgorithms.Sampling;

/// <summary>
/// 切片平面采样：屏幕像素 → 世界体素坐标（纯坐标映射，不含世界/体素语义）。
/// <para>cellSize 为浮点（&lt;1 的单元尺寸不会被截断为 0）；坐标统一 Floor 向下取整，
/// 负坐标区域与 <see cref="Spatial.ChunkSpace.ToVoxel"/> 语义一致。</para>
/// <para>自 Engine <c>SliceProjection</c> 提取：输出逐值保持原实现，仅去除引擎命名空间。</para>
/// </summary>
public static class SliceProjection
{
    public static (int worldX, int worldY, int worldZ) WorldFromScreen(
        SlicePlane plane, int screenX, int screenY,
        in Vec3 position, int halfWidth, int halfHeight, float cellSize, int sliceValue)
    {
        int worldX, worldY, worldZ;

        switch (plane)
        {
            case SlicePlane.XZ:
                worldX = (int)MathF.Floor(position.X + (screenX - halfWidth) * cellSize);
                worldY = sliceValue;
                worldZ = (int)MathF.Floor(position.Z + (screenY - halfHeight) * cellSize);
                break;
            case SlicePlane.XY:
                worldX = (int)MathF.Floor(position.X + (screenX - halfWidth) * cellSize);
                worldY = (int)MathF.Floor(position.Y + (screenY - halfHeight) * cellSize);
                worldZ = sliceValue;
                break;
            case SlicePlane.YZ:
                worldX = sliceValue;
                worldY = (int)MathF.Floor(position.Y + (screenY - halfHeight) * cellSize);
                worldZ = (int)MathF.Floor(position.Z + (screenX - halfWidth) * cellSize);
                break;
            default:
                worldX = worldY = worldZ = 0;
                break;
        }

        return (worldX, worldY, worldZ);
    }
}
