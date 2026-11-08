#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Share.SurfaceRaster;

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
}
