#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using UiBackend;

namespace Share.SurfacePaint.Raster;

/// <summary>
/// 表面光栅原语（**载体无关**）：噪声瓦片、径向掩膜、模糊近似、圆角路径。
/// <para>只吃 <see cref="Bitmap"/>/<see cref="Image"/>/<see cref="Graphics"/>/<see cref="GraphicsPath"/> 与显式参数；
/// 不读任何前端静态状态，不含 Control/Form 类型。</para>
/// <para>像素算法逐位稳定：同参数输出必须与迁移前实现完全一致（迁移只搬位置，不改算术）。</para>
/// </summary>
public static class SurfaceRaster
{
    /// <summary>96×96 灰度噪声瓦片（固定种子 0x4E4F4953；alpha 由调用方给）。</summary>
    /// <param name="alpha">噪声点 alpha（0..255）。</param>
    public static Bitmap NoiseTile(int alpha)
    {
        const int size = 96;
        var tile = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var random = new Random(unchecked((int)0x4E4F4953));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = 128 + random.Next(-64, 65);
                tile.SetPixel(x, y, Color.FromArgb(alpha, value, value, value));
            }
        }

        return tile;
    }

    private static readonly object SpotMaskSync = new();
    private static Bitmap? _spotMask;

    /// <summary>白色径向 alpha 掩膜（中心 255 → 边缘 0，二次衰减），进程内复用。</summary>
    public static Bitmap SpotMask()
    {
        if (_spotMask is not null) return _spotMask;
        lock (SpotMaskSync)
        {
            if (_spotMask is not null) return _spotMask;
            const int side = 128;
            var bitmap = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            var center = (side - 1) / 2.0;
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var dx = (x - center) / center;
                    var dy = (y - center) / center;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    var alpha = distance >= 1.0 ? 0 : (int)Math.Round(255 * (1 - distance) * (1 - distance));
                    bitmap.SetPixel(x, y, Color.FromArgb(alpha, Color.White));
                }
            }

            _spotMask = bitmap;
            return bitmap;
        }
    }

    /// <summary>
    /// 降采样再升采样近似模糊（GDI+ 无原生高斯；radius 越大采样越粗）。
    /// 只处理快照位图，不改变任何材质参数契约。
    /// </summary>
    /// <param name="source">源位图（不修改、不释放）。</param>
    /// <param name="radius">模糊半径（≤0 ⇒ 直接复制）。</param>
    public static Bitmap Blur(Image source, int radius)
    {
        if (radius <= 0)
        {
            var copy = new Bitmap(source.Width, source.Height);
            using var copyGraphics = Graphics.FromImage(copy);
            copyGraphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            return copy;
        }

        var factor = Math.Max(2, radius / 4); // 18 ⇒ 4（约 4×4 降采样）
        var smallWidth = Math.Max(1, source.Width / factor);
        var smallHeight = Math.Max(1, source.Height / factor);
        var small = new Bitmap(smallWidth, smallHeight);
        try
        {
            using (var graphics = Graphics.FromImage(small))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, smallWidth, smallHeight));
            }

            var blurred = new Bitmap(source.Width, source.Height);
            using (var graphics = Graphics.FromImage(blurred))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(small, new Rectangle(0, 0, source.Width, source.Height));
            }

            return blurred;
        }
        finally
        {
            small.Dispose();
        }
    }

    /// <summary>圆角矩形路径（radius 按半径口径：直径 = 2×radius，夹到短边）。</summary>
    /// <param name="rect">目标矩形。</param>
    /// <param name="radius">圆角半径（DIP/像素由调用方换算）。</param>
    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        if (diameter <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 逐像素沿带中心线把 (a,b) 推进一个族内色相（**ΔY 恒为 0 ⇒ L* 不动**）。
    /// <para>逐列算中心线与法线，带内每像素直接读/写位图：底色是刚刚画出来的真实 8bit 像素，
    /// 因此 ΔL* 只剩「ΔY=0 的线性化误差 + 整数取整」两项。输入全部显式，不读前端静态状态。</para>
    /// </summary>
    /// <param name="scene">目标位图（原地读写；无存活 Graphics）。</param>
    /// <param name="width">位图宽。</param>
    /// <param name="height">位图高。</param>
    /// <param name="centerX">带中心线参考点 X。</param>
    /// <param name="centerY">带中心线参考点 Y。</param>
    /// <param name="amplitude">波形振幅（像素）。</param>
    /// <param name="wavelength">波长（像素）。</param>
    /// <param name="bandWidth">带宽（垂直距离，像素）。</param>
    /// <param name="shapePhase">几何相位（0..1）。</param>
    /// <param name="hue0">色相起点（度）。</param>
    /// <param name="span">沿页宽的色相跨度（度）。</param>
    public static void Ribbon(
        Bitmap scene, int width, int height,
        float centerX, float centerY, float amplitude, float wavelength, float bandWidth,
        float shapePhase, float hue0, float span)
    {
        var half = bandWidth * 0.5f;
        if (half <= 0f) return;
        var data = scene.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppPArgb);
        try
        {
            var stride = data.Stride;
            var buffer = data.Scan0;
            var radians = (float)(2 * Math.PI / wavelength);
            for (var x = 0; x < width; x++)
            {
                var phase = radians * (x - centerX) + shapePhase * 2f * (float)Math.PI;
                var centerline = centerY + (x - centerX) * 0.34f + amplitude * MathF.Sin(phase);
                var slope = 0.34f + amplitude * radians * MathF.Cos(phase);
                // 法线 = (−slope, 1)/length；带宽 half 是**垂直距离**，故列内纵向半高 = half/length。
                var halfHeight = half / MathF.Sqrt(1f + slope * slope);
                var top = Math.Max(0, (int)Math.Floor(centerline - halfHeight));
                var bottom = Math.Min(height - 1, (int)Math.Ceiling(centerline + halfHeight));
                var hue = hue0 + Math.Clamp(x / Math.Max(1f, width), 0f, 1f) * span;
                for (var y = top; y <= bottom; y++)
                {
                    var offset = y * stride + x * 4;
                    var value = Marshal.ReadInt32(buffer, offset);
                    var blue = value & 0xFF;
                    var green = (value >> 8) & 0xFF;
                    var red = (value >> 16) & 0xFF;
                    var (shiftR, shiftG, shiftB) =
                        MathematicalAlgorithms.Color.ColorScience.ShiftChroma(red, green, blue, hue);
                    Marshal.WriteInt32(buffer, offset, (0xFF << 24) | (shiftR << 16) | (shiftG << 8) | shiftB);
                }
            }
        }
        finally
        {
            scene.UnlockBits(data);
        }
    }

    /// <summary>
    /// 局部径向光（中心亮、向外衰减）：中心/半径/强度**全部由调用方给出**，本函数不读光场状态。
    /// <para>焦点区 = 中心纯色区；盖不满页面时按包围盒早退，不造页面外的假亮斑。</para>
    /// </summary>
    /// <param name="graphics">目标画布。</param>
    /// <param name="width">页面宽（像素）。</param>
    /// <param name="height">页面高（像素）。</param>
    /// <param name="centerX">光斑中心 X（像素，可落在页面外）。</param>
    /// <param name="centerY">光斑中心 Y（像素，可落在页面外）。</param>
    /// <param name="radius">半径（像素，≥8）。</param>
    /// <param name="alpha">峰值 alpha（0..255）。</param>
    public static void RadialGlow(
        Graphics graphics, int width, int height,
        float centerX, float centerY, float radius, int alpha)
    {
        // 椭圆的包围盒与页面不相交 ⇒ 整颗光斑在页面外，不画（锚点在宿主外时的真实行为）。
        if (centerX + radius < 0f || centerY + radius < 0f
            || centerX - radius > width || centerY - radius > height) return;

        using var path = new GraphicsPath();
        path.AddEllipse(centerX - radius, centerY - radius, radius * 2f, radius * 2f);
        var points = path.PointCount;
        if (points <= 0) return;

        var surround = new Color[points];
        var clear = Color.FromArgb(0, Color.White);
        for (var i = 0; i < points; i++) surround[i] = clear;

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, Color.White),
            SurroundColors = surround,
            CenterPoint = new PointF(centerX, centerY),
            // 焦点区 = 中心纯色区；越小核心越亮、向外衰减越长。
            FocusScales = new PointF(0.12f, 0.12f),
        };
        graphics.FillRectangle(brush, 0, 0, width, height);
    }
}
