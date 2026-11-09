#nullable enable

using System;

namespace MathematicalAlgorithms.Surface;

/// <summary>像素采样点（标签 + 图像坐标）。</summary>
public readonly record struct SurfaceSamplePoint(string Label, int X, int Y);

/// <summary>
/// 像素探针数学（**纯数值**）：采样点布点与 RGBA 欧氏距离。
/// <para>控件抓帧/金样比对仍留在载体层；本类只给坐标与距离。</para>
/// </summary>
public static class SurfaceProbeMath
{
    /// <summary>中心 + 四角内缩 + 四边中点（描边不被结构性避开）。</summary>
    public static SurfaceSamplePoint[] SamplePoints(int width, int height)
    {
        var x1 = Math.Max(1, width / 5);
        var x2 = Math.Max(1, width - width / 5 - 1);
        var y1 = Math.Max(1, height / 5);
        var y2 = Math.Max(1, height - height / 5 - 1);
        return new[]
        {
            new SurfaceSamplePoint("center", width / 2, height / 2),
            new SurfaceSamplePoint("tl", x1, y1),
            new SurfaceSamplePoint("tr", x2, y1),
            new SurfaceSamplePoint("bl", x1, y2),
            new SurfaceSamplePoint("br", x2, y2),
            new SurfaceSamplePoint("border-left", 0, height / 2),
            new SurfaceSamplePoint("border-top", width / 2, 0),
            new SurfaceSamplePoint("border-right", Math.Max(0, width - 1), height / 2),
            new SurfaceSamplePoint("border-bottom", width / 2, Math.Max(0, height - 1)),
        };
    }

    /// <summary>RGBA 欧氏距离（含 alpha；半透明材质只比 RGB 会把 alpha 错误判成通过）。</summary>
    public static double RgbaDistance(
        int leftA, int leftR, int leftG, int leftB,
        int rightA, int rightR, int rightG, int rightB)
    {
        var da = leftA - rightA;
        var dr = leftR - rightR;
        var dg = leftG - rightG;
        var db = leftB - rightB;
        return Math.Sqrt(da * da + dr * dr + dg * dg + db * db);
    }
}
