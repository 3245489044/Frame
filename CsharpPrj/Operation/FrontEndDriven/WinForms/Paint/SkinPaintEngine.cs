#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 皮肤绘制引擎（GDI+ 方言落点）：已解析材质层栈 → 控件背景。
/// <para>支持 fill/gradient/stroke/shadow/innerShadow/backdrop/specular；texture/decal/mask 由 M6 接管（进 Skipped，禁静默纯色）。</para>
/// <para>backdrop：有宿主快照时「采样背后 + 模糊」；无快照才回退旧近似并记账 <c>backdrop:emulated</c>，禁伪装原生模糊。</para>
/// </summary>
public static class SkinPaintEngine
{
    private static Func<string, Image?>? _assetLoader;

    /// <summary>资源装载钩子（宿主注入；null ⇒ 按 <see cref="AssetRoot"/> 从磁盘装载，仍缺件则显式跳过，禁静默纯色）。</summary>
    public static Func<string, Image?>? AssetLoader
    {
        get => _assetLoader;
        set
        {
            _assetLoader = value;
            ClearAssetCache();
        }
    }

    /// <summary>资源根目录（宿主注入；null ⇒ 不装载，贴图层进 Skipped）。</summary>
    public static string? AssetRoot { get; private set; }

    private static readonly Dictionary<string, Image?> AssetCache = new(StringComparer.Ordinal);

    /// <summary>配置资源根（单一入口；变更即失效缓存，防旧根别名串味）。</summary>
    /// <param name="root">资源根绝对路径（空 ⇒ 关闭磁盘装载）。</param>
    public static void ConfigureAssetRoot(string? root)
    {
        AssetRoot = string.IsNullOrWhiteSpace(root) ? null : root;
        ClearAssetCache();
    }

    /// <summary>清空资源缓存（换根/换装载器/换肤时调用）。</summary>
    public static void ClearAssetCache()
    {
        lock (AssetCache) AssetCache.Clear();
    }

    /// <summary>绘制一次材质（背景）。</summary>
    /// <param name="graphics">目标画布。</param>
    /// <param name="bounds">目标矩形。</param>
    /// <param name="material">已解析材质（null ⇒ 不绘制）。</param>
    /// <param name="backdrop">面板背后的快照（宿主提供；null ⇒ backdrop 层走旧近似并记账）。</param>
    public static IReadOnlyList<string> Paint(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, Image? backdrop = null,
        SurfaceLightState? light = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (material is null || bounds.Width <= 0 || bounds.Height <= 0) return Array.Empty<string>();

        var skipped = new List<string>();
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // 几何单源（按材质）：圆角由层声明提供，层未声明时沿用同材质内最大半径，
            // 避免「填充直角 + 描边圆角」的错位（材质 = 一个整体形状，不是各层各画各的）。
            var materialRadius = 0;
            foreach (var candidate in material.Layers)
            {
                var radius = IntParam(candidate, "radius", 0);
                if (radius > materialRadius) materialRadius = radius;
            }

            foreach (var layer in material.Layers)
            {
                // 明细由 PaintLayer 记账（unresolved-value:<raw>）；只有未记账的失败才补通用项。
                var before = skipped.Count;
                if (!PaintLayer(graphics, bounds, layer, materialRadius, skipped, backdrop, light) && skipped.Count == before)
                {
                    skipped.Add(layer.Primitive + ":" + layer.Role);
                }
            }
        }
        finally
        {
            graphics.Restore(state);
        }

        return skipped;
    }

    private static bool PaintLayer(
        Graphics graphics, Rectangle bounds, ResolvedLayer layer, int materialRadius, List<string> skipped,
        Image? backdrop = null, SurfaceLightState? light = null)
    {
        switch (layer.Primitive)
        {
            case "fill":
            {
                if (!TryColor(layer, skipped, out var color)) return false;
                using var brush = new SolidBrush(color);
                using var path = CreateRoundedPath(bounds, RadiusOf(layer, bounds, materialRadius));
                if (path is null) graphics.FillRectangle(brush, bounds);
                else graphics.FillPath(brush, path);
                return true;
            }

            case "gradient":
            {
                if (!TrySplitColors(Param(layer, "value"), layer, skipped, out var colors)) return false;
                using var brush = new LinearGradientBrush(
                    bounds, colors[0], colors.Length > 1 ? colors[1] : colors[0], Angle(layer));
                graphics.FillRectangle(brush, bounds);
                return true;
            }

            case "stroke":
            {
                if (!TryColor(layer, skipped, out var color)) return false;
                var width = Math.Max(1, IntParam(layer, "width", 1));
                using var pen = new Pen(color, width);
                var inset = width / 2f;
                var strokeBounds = new Rectangle(
                    bounds.X + (int)inset, bounds.Y + (int)inset,
                    Math.Max(1, bounds.Width - width), Math.Max(1, bounds.Height - width));
                using var path = CreateRoundedPath(strokeBounds, Math.Max(0, RadiusOf(layer, bounds, materialRadius) - (int)inset));
                if (path is null) graphics.DrawRectangle(pen, strokeBounds);
                else graphics.DrawPath(pen, path);
                return true;
            }

            case "shadow":
            {
                if (!TryColor(layer, skipped, out var color)) return false;
                var blur = Math.Max(0, IntParam(layer, "blur", 0));
                var offset = Offset(layer);
                var rect = new Rectangle(bounds.X + offset.X, bounds.Y + offset.Y,
                    Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - 2));
                using var brush = new SolidBrush(Color.FromArgb(Math.Min(120, 40 + blur * 4), color));
                using var path = CreateRoundedPath(rect, RadiusOf(layer, bounds, materialRadius));
                if (path is null) graphics.FillRectangle(brush, rect);
                else graphics.FillPath(brush, path);
                return true;
            }

            case "innerShadow":
            {
                if (!TryColor(layer, skipped, out var color)) return false;
                using var pen = new Pen(Color.FromArgb(90, color), 2);
                graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1,
                    Math.Max(1, bounds.Width - 3), Math.Max(1, bounds.Height - 3));
                return true;
            }

            case "backdrop":
            {
                if (!TryColor(layer, skipped, out var color)) return false;
                using var path = CreateRoundedPath(bounds, RadiusOf(layer, bounds, materialRadius));
                if (backdrop is not null && backdrop.Width > 0 && backdrop.Height > 0)
                {
                    // 真「采样背后 + 模糊」：宿主快照铺满本层，再用半透明 fill（surface_glass）染色。
                    // 不再画自上而下自叠加渐变；磨砂外观来自背后内容本身。
                    var saved = graphics.Save();
                    try
                    {
                        if (path is not null) graphics.SetClip(path);
                        using var blurred = Blur(backdrop, IntParam(layer, "blur", 0));
                        graphics.DrawImage(blurred, bounds);

                        // 复用 backdrop 的 value 做轻染色：强度随时点光变化（不新增合成通道）。
                        if (light is { } lit)
                        {
                            using var tint = new SolidBrush(Color.FromArgb(
                                Math.Clamp((int)(22 * lit.Intensity), 0, 40), Warm(color, lit.Warmth)));
                            graphics.FillRectangle(tint, bounds);
                        }
                    }
                    finally
                    {
                        graphics.Restore(saved);
                    }

                    return true;
                }

                // 无快照来源（非窗口载体/宿主未注入）：保留旧近似，并显式记账，禁伪装真模糊。
                using var brush = new LinearGradientBrush(
                    bounds,
                    Color.FromArgb(150, Lighten(color, 0.28)),
                    Color.FromArgb(36, color),
                    LinearGradientMode.Vertical);
                if (path is null) graphics.FillRectangle(brush, bounds);
                else graphics.FillPath(brush, path);
                skipped.Add("backdrop:emulated");
                return true;
            }

            case "specular":
            {
                if (!TryColor(layer, skipped, out var color)) return false;
                if (light is { } state)
                {
                    // 运行时光：angle/value 已由 SurfaceLight 插值，按同一角度画一条窄高光带。
                    var peakAlpha = Math.Clamp((int)Math.Round(70 * state.Intensity), 0, 200);
                    var litColor = Warm(color, state.Warmth);
                    using var brush = new LinearGradientBrush(
                        bounds, Color.FromArgb(0, litColor), Color.FromArgb(0, litColor), state.Angle);
                    brush.InterpolationColors = new ColorBlend
                    {
                        Colors = new[]
                        {
                            Color.FromArgb(0, litColor),
                            Color.FromArgb(peakAlpha, litColor),
                            Color.FromArgb(0, litColor),
                        },
                        Positions = new[] { 0.38f, 0.5f, 0.62f },
                    };
                    using var lightPath = CreateRoundedPath(bounds, RadiusOf(layer, bounds, materialRadius));
                    if (lightPath is null) graphics.FillRectangle(brush, bounds);
                    else graphics.FillPath(brush, lightPath);
                    return true;
                }

                var sheen = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height / 3));
                using var staticBrush = new LinearGradientBrush(
                    sheen, Color.FromArgb(70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical);
                using var path = CreateRoundedPath(sheen, Math.Max(0, RadiusOf(layer, bounds, materialRadius) - 2));
                if (path is null) graphics.FillRectangle(staticBrush, sheen);
                else graphics.FillPath(staticBrush, path);
                return true;
            }

            // M6：贴图 / 贴画（资源缺失 ⇒ false ⇒ 进 Skipped，可见）
            case "texture":
            case "decal":
            {
                var asset = Param(layer, "asset");
                if (asset.Length == 0) return false;
                var image = LoadAsset(asset);
                if (image is null) return false;

                // 九宫格：slice>0 ⇒ 四角不拉伸（边框/卡片贴图的主用法）；否则整图平铺/拉伸。
                var slice = IntParam(layer, "slice", 0);
                if (slice > 0)
                {
                    DrawNineSlice(graphics, image, bounds, slice);
                    return true;
                }

                if (string.Equals(layer.Primitive, "decal", StringComparison.Ordinal)
                    || string.Equals(Param(layer, "repeat"), "no-repeat", StringComparison.Ordinal))
                {
                    graphics.DrawImage(image, bounds);
                    return true;
                }

                using var texture = new TextureBrush(image);
                graphics.FillRectangle(texture, bounds);
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>按时点暖度把高光/背景染向暖白（只调颜色，不改材质参数）。</summary>
    private static Color Warm(Color color, float warmth)
    {
        if (warmth <= 0) return color;
        var t = Math.Clamp(warmth, 0f, 1f) * 0.35f;
        return Color.FromArgb(
            color.A,
            (int)(color.R + (255 - color.R) * t),
            (int)(color.G + (224 - color.G) * t),
            (int)(color.B + (170 - color.B) * t));
    }

    /// <summary>
    /// 降采样再升采样近似模糊（GDI+ 无原生高斯；radius 越大采样越粗）。
    /// 只处理快照位图，不改变任何材质参数契约。
    /// </summary>
    private static Bitmap Blur(Image source, int radius)
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

    /// <summary>资源装载（进程内缓存；装载失败 ⇒ null，由调用方记账）。</summary>
    private static Image? LoadAsset(string asset)
    {
        lock (AssetCache)
        {
            if (AssetCache.TryGetValue(asset, out var cached)) return cached;
            var image = _assetLoader is not null ? InvokeLoader(_assetLoader, asset) : LoadFromRoot(asset);
            AssetCache[asset] = image;
            return image;
        }
    }

    private static Image? InvokeLoader(Func<string, Image?> loader, string asset)
    {
        try
        {
            return loader(asset);
        }
        catch (Exception)
        {
            // 装载异常 ⇒ 视为缺件（走 Skipped 可见路径，不抛给绘制线程）。
            return null;
        }
    }

    /// <summary>按资源根从磁盘装载（相对路径；绝对路径拒绝；缺件 ⇒ null）。</summary>
    private static Image? LoadFromRoot(string asset)
    {
        var root = AssetRoot;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(asset)) return null;
        if (Path.IsPathRooted(asset)) return null;

        try
        {
            var path = Path.GetFullPath(Path.Combine(root, asset.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(path) ? Image.FromFile(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>九宫格绘制：四角与四边按源切片缩放，中心拉伸（slice 夹到半边长内）。</summary>
    private static void DrawNineSlice(Graphics graphics, Image image, Rectangle bounds, int slice)
    {
        var width = image.Width;
        var height = image.Height;
        var cut = Math.Min(slice, Math.Min(width, height) / 2);
        if (cut <= 0)
        {
            graphics.DrawImage(image, bounds);
            return;
        }

        var sourceX = new[] { 0, cut, width - cut };
        var sourceW = new[] { cut, width - cut * 2, cut };
        var sourceY = new[] { 0, cut, height - cut };
        var sourceH = new[] { cut, height - cut * 2, cut };
        var targetX = new[] { bounds.X, bounds.X + cut, bounds.Right - cut };
        var targetY = new[] { bounds.Y, bounds.Y + cut, bounds.Bottom - cut };
        var targetW = new[] { cut, Math.Max(1, bounds.Width - cut * 2), cut };
        var targetH = new[] { cut, Math.Max(1, bounds.Height - cut * 2), cut };

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if (sourceW[column] <= 0 || sourceH[row] <= 0) continue;
                graphics.DrawImage(
                    image,
                    new Rectangle(targetX[column], targetY[row], targetW[column], targetH[row]),
                    new Rectangle(sourceX[column], sourceY[row], sourceW[column], sourceH[row]),
                    GraphicsUnit.Pixel);
            }
        }
    }

    /// <summary>层圆角半径（材质级兜底；未声明/≤0 ⇒ 0 = 直角）。夹到半边长内，防 GDI+ 弧参数非法。</summary>
    private static int RadiusOf(ResolvedLayer layer, Rectangle bounds, int materialRadius)
    {
        var radius = IntParam(layer, "radius", 0);
        if (radius <= 0) radius = materialRadius;
        if (radius <= 0) return 0;
        return Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
    }

    /// <summary>圆角矩形路径（radius≤0 ⇒ null，调用方走矩形快路径）。</summary>
    private static GraphicsPath? CreateRoundedPath(Rectangle bounds, int radius)
    {
        if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return null;

        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>提亮（磨砂高光；amount 0..1）。</summary>
    private static Color Lighten(Color color, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        int Mix(int channel) => (int)Math.Round(channel + (255 - channel) * t);
        return Color.FromArgb(color.A, Mix(color.R), Mix(color.G), Mix(color.B));
    }

    /// <summary>
    /// 材质底色（首个 fill/gradient 层的首个色）：非 owner-draw 控件的属性换肤用。
    /// 无可用底色 ⇒ false（调用方保持原属性，禁静默造色）。
    /// </summary>
    public static bool TryGetBaseColor(ResolvedMaterial? material, out Color color)
    {
        color = Color.Empty;
        if (material is null) return false;

        foreach (var layer in material.Layers)
        {
            if (string.Equals(layer.Primitive, "fill", StringComparison.Ordinal))
            {
                if (TryParseColor(Param(layer, "value"), 255, out color) && color.A > 0) return true;
            }
            else if (string.Equals(layer.Primitive, "gradient", StringComparison.Ordinal))
            {
                var first = Param(layer, "value")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (first.Length > 0 && TryParseColor(first[0], 255, out color) && color.A > 0) return true;
            }
        }

        return false;
    }

    private static string Param(ResolvedLayer layer, string name)
        => layer.Params.TryGetValue(name, out var value) ? value : string.Empty;

    private static int IntParam(ResolvedLayer layer, string name, int fallback)
        => layer.Params.TryGetValue(name, out var value)
           && int.TryParse(value, System.Globalization.NumberStyles.Integer,
               System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;

    private static Point Offset(ResolvedLayer layer)
    {
        var parts = Param(layer, "offset").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var x = parts.Length > 0 && int.TryParse(parts[0], out var px) ? px : 0;
        var y = parts.Length > 1 && int.TryParse(parts[1], out var py) ? py : Math.Max(2, x / 2);
        return new Point(x, y);
    }

    private static float Angle(ResolvedLayer layer)
        => float.TryParse(Param(layer, "angle"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var angle)
            ? angle
            : 180f;

    /// <summary>层色值解析（失败 ⇒ 记账并返回 false；禁静默画成透明）。</summary>
    private static bool TryColor(ResolvedLayer layer, List<string> skipped, out Color color)
    {
        var raw = Param(layer, "value");
        if (TryParseColor(raw, 255, out color)) return true;
        skipped.Add("unresolved-value:" + (raw.Length == 0 ? layer.Primitive : raw));
        return false;
    }

    private static bool TryParseColor(string value, int alpha, out Color color)
    {
        color = Color.Transparent;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        try
        {
            if (text.StartsWith('#'))
            {
                // 颜色格式唯一转换点（SurfaceColor）：CSS 8 位 = RRGGBBAA，不再交给 ColorTranslator（后者按 AARRGGBB 解释）。
                if (!SurfaceColor.TryParseHex(text, out var argb)) return false;
                color = Color.FromArgb(argb);
                return true;
            }

            if (Color.FromName(text) is { IsKnownColor: true } named)
            {
                color = named;
                return true;
            }

            var parts = text.Split(',');
            if (parts.Length >= 3
                && int.TryParse(parts[0], out var r)
                && int.TryParse(parts[1], out var g)
                && int.TryParse(parts[2], out var b))
            {
                color = Color.FromArgb(alpha, Clamp(r), Clamp(g), Clamp(b));
                return true;
            }
        }
        catch (Exception)
        {
            // 非法色值 ⇒ 未解析（调用方记账，禁抛给绘制路径）。
        }

        return false;
    }

    private static bool TrySplitColors(string value, ResolvedLayer layer, List<string> skipped, out Color[] colors)
    {
        var raw = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        colors = new Color[raw.Length];
        if (raw.Length == 0)
        {
            skipped.Add("unresolved-value:" + layer.Primitive);
            return false;
        }

        for (var i = 0; i < raw.Length; i++)
        {
            if (TryParseColor(raw[i], 255, out colors[i])) continue;
            skipped.Add("unresolved-value:" + raw[i]);
            return false;
        }

        return true;
    }

    private static int Clamp(int value) => Math.Min(255, Math.Max(0, value));
}
