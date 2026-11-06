#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 皮肤绘制引擎（GDI+ 方言落点）：已解析材质层栈 → 控件背景。
/// <para>支持 fill/gradient/stroke/shadow/innerShadow/backdrop/specular；texture/decal/mask 由 M6 接管（进 Skipped，禁静默纯色）。</para>
/// <para>backdrop 为 emulated（半透明叠色 + 记账），不伪装原生模糊。</para>
/// </summary>
public static class SkinPaintEngine
{
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
                if (!PaintLayer(graphics, bounds, layer, skipped)) skipped.Add(layer.Primitive + ":" + layer.Role);
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
        var value = Param(layer, "value");
        var color = ParseColor(value, 255);
        switch (layer.Primitive)
        {
            case "fill":
                using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, bounds);
                return true;

            case "gradient":
            {
                var colors = SplitColors(value);
                if (colors.Length == 0) return false;
                using var brush = new LinearGradientBrush(
                    bounds, colors[0], colors.Length > 1 ? colors[1] : colors[0], Angle(layer));
                graphics.FillRectangle(brush, bounds);
                return true;
            }

            case "stroke":
            {
                var width = Math.Max(1, IntParam(layer, "width", 1));
                using var pen = new Pen(color, width);
                var inset = width / 2f;
                graphics.DrawRectangle(pen, bounds.X + inset, bounds.Y + inset,
                    Math.Max(1, bounds.Width - width), Math.Max(1, bounds.Height - width));
                return true;
            }

            case "shadow":
            {
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
                using var pen = new Pen(Color.FromArgb(90, color), 2);
                graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1,
                    Math.Max(1, bounds.Width - 3), Math.Max(1, bounds.Height - 3));
                return true;
            }

            case "backdrop":
            {
                using var brush = new SolidBrush(Color.FromArgb(96, color));
                graphics.FillRectangle(brush, bounds);
                skipped.Add("backdrop:emulated");
                return true;
            }

            case "specular":
            {
                var sheen = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height / 3));
                using var brush = new LinearGradientBrush(
                    sheen, Color.FromArgb(70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical);
                graphics.FillRectangle(brush, sheen);
                return true;
            }

            default:
                return false;
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

    private static Color[] SplitColors(string value)
    {
        var raw = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var colors = new Color[raw.Length];
        for (var i = 0; i < raw.Length; i++) colors[i] = ParseColor(raw[i], 255);
        return colors;
    }

    private static Color ParseColor(string value, int alpha)
    {
        if (string.IsNullOrWhiteSpace(value)) return Color.Transparent;
        var text = value.Trim();
        try
        {
            if (text.StartsWith('#')) return ColorTranslator.FromHtml(text);
            if (Color.FromName(text) is { IsKnownColor: true } named) return named;
            var parts = text.Split(',');
            if (parts.Length >= 3
                && int.TryParse(parts[0], out var r)
                && int.TryParse(parts[1], out var g)
                && int.TryParse(parts[2], out var b))
            {
                return Color.FromArgb(alpha, Clamp(r), Clamp(g), Clamp(b));
            }
        }
        catch (Exception)
        {
            // 非法色值 ⇒ Transparent（图元仍记账，禁抛给绘制路径）。
        }

        return Color.Transparent;
    }

    private static int Clamp(int value) => Math.Min(255, Math.Max(0, value));
}
