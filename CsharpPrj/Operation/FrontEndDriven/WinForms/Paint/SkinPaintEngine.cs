#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 皮肤绘制引擎（GDI+ 方言落点）：已解析材质层栈 → 控件背景。
/// <para>支持 fill/gradient/stroke/shadow/innerShadow/backdrop/specular；texture/decal/mask 由 M6 接管（进 Skipped，禁静默纯色）。</para>
/// <para>backdrop 为 emulated（半透明叠色 + 记账），不伪装原生模糊。</para>
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
    public static IReadOnlyList<string> Paint(Graphics graphics, Rectangle bounds, ResolvedMaterial? material)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (material is null || bounds.Width <= 0 || bounds.Height <= 0) return Array.Empty<string>();

        var skipped = new List<string>();
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var layer in material.Layers)
            {
                // 明细由 PaintLayer 记账（unresolved-value:<raw>）；只有未记账的失败才补通用项。
                var before = skipped.Count;
                if (!PaintLayer(graphics, bounds, layer, skipped) && skipped.Count == before)
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

    private static bool PaintLayer(Graphics graphics, Rectangle bounds, ResolvedLayer layer, List<string> skipped)
    {
        switch (layer.Primitive)
        {
            case "fill":
            {
                if (!TryColor(layer, skipped, out var color)) return false;
                using var brush = new SolidBrush(color);
                graphics.FillRectangle(brush, bounds);
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
                graphics.DrawRectangle(pen, bounds.X + inset, bounds.Y + inset,
                    Math.Max(1, bounds.Width - width), Math.Max(1, bounds.Height - width));
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
                graphics.FillRectangle(brush, rect);
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
                using var brush = new SolidBrush(Color.FromArgb(96, color));
                graphics.FillRectangle(brush, bounds);
                skipped.Add("backdrop:emulated");
                return true;
            }

            case "specular":
            {
                if (!TryColor(layer, skipped, out var color)) return false;
                var sheen = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height / 3));
                using var brush = new LinearGradientBrush(
                    sheen, Color.FromArgb(70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical);
                graphics.FillRectangle(brush, sheen);
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
                color = ColorTranslator.FromHtml(text);
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
