#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Modules.Common.Drawing;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>单点像素样本（实际 / 未换肤基线 / 按材质拟合的期望）。</summary>
/// <param name="Point">采样点标签（center/tl/tr/bl/br）。</param>
/// <param name="Actual">控件真实绘制结果（<see cref="Control.DrawToBitmap"/>）。</param>
/// <param name="Baseline">未换肤基线（控件 BackColor；owner-draw 未生效时的像素）。</param>
/// <param name="Expected">按已解析材质拟合的期望色（同一 <see cref="ResolvedMaterial"/>，单源）。</param>
/// <param name="Delta">实际 vs 基线的 RGB 欧氏距离（材质是否真正落像素）。</param>
/// <param name="ExpectedDelta">实际 vs 期望的 RGB 欧氏距离（色值是否正确）。</param>
public sealed record SkinPixelSample(
    string Point, Color Actual, Color Baseline, Color Expected, double Delta, double ExpectedDelta);

/// <summary>像素断言报告（判定只读本结构；不含执行者叙述）。</summary>
/// <param name="Material">被验证材质 id。</param>
/// <param name="Tolerance">期望拟合容差（RGB 距离）。</param>
/// <param name="Samples">采样点集合。</param>
public sealed record SkinPixelReport(string Material, int Tolerance, IReadOnlyList<SkinPixelSample> Samples)
{
    /// <summary>实际像素相对未换肤基线的最大偏移（0 ⇒ 材质完全没落像素）。</summary>
    public double MaxDelta => Samples.Count == 0 ? 0 : Samples.Max(sample => sample.Delta);

    /// <summary>与期望色吻合的采样点数（拟合命中数）。</summary>
    public int Matched => Samples.Count(sample => sample.ExpectedDelta <= Tolerance);

    /// <summary>是否可见（至少一个点偏离基线）。</summary>
    public bool Visible => MaxDelta > 0.5;

    /// <summary>单行证据（日志/失败文案）。</summary>
    public string Describe()
        => string.Format("material={0} samples={1} maxDelta={2:0.0} matched={3}/{4} actual={5} baseline={6} expected={7}",
            string.IsNullOrEmpty(Material) ? "-" : Material,
            Samples.Count,
            MaxDelta,
            Matched,
            Samples.Count,
            Samples.Count > 0 ? Samples[0].Actual.ToHex() : "-",
            Samples.Count > 0 ? Samples[0].Baseline.ToHex() : "-",
            Samples.Count > 0 ? Samples[0].Expected.ToHex() : "-");
}

/// <summary>
/// 像素探针（**唯一像素判定实现**）：把控件真实绘制结果与「未换肤基线 / 已解析材质期望」拟合。
/// <para>判据载体 = 像素值（结构事实），不是 Enabled/Count 等代理指标：Enabled=true 但层值不是色值时，这里必红。</para>
/// <para>复用：<see cref="Control.DrawToBitmap"/>（真实绘制路径）、<see cref="ColorExtensions.TryParse"/> /
/// <see cref="ColorExtensions.Blend"/>（颜色解析与合成）、<see cref="SkinDecorator.MaterialOf"/>（实际生效材质，单一来源）。</para>
/// </summary>
public static class SkinPixelProbe
{
    /// <summary>参与像素拟合的图元与覆盖率（其余图元走 asset 或仅边缘）。</summary>
    private static readonly Dictionary<string, double> Coverage = new(StringComparer.Ordinal)
    {
        ["fill"] = 1.0,
        ["gradient"] = 0.5,
        ["backdrop"] = 96.0 / 255.0,
        ["shadow"] = 0.35,
        ["innerShadow"] = 0.35,
        ["specular"] = 0.27,
    };

    /// <summary>采样并拟合（不抛：绘制异常 ⇒ 空样本，由调用方判红）。</summary>
    /// <param name="control">目标控件（须已创建/可见）。</param>
    /// <param name="material">该控件实际生效材质（<see cref="SkinDecorator.MaterialOf"/>）。</param>
    /// <param name="tolerance">期望拟合容差（RGB 欧氏距离，默认 40）。</param>
    public static SkinPixelReport Inspect(Control control, ResolvedMaterial? material, int tolerance = 40)
    {
        ArgumentNullException.ThrowIfNull(control);
        var materialId = material?.MaterialId ?? string.Empty;
        var baseline = BaselineOf(control);
        if (material is null) return new SkinPixelReport(materialId, tolerance, Array.Empty<SkinPixelSample>());

        var width = control.ClientSize.Width > 0 ? control.ClientSize.Width : control.Width;
        var height = control.ClientSize.Height > 0 ? control.ClientSize.Height : control.Height;
        if (width <= 0 || height <= 0) return new SkinPixelReport(materialId, tolerance, Array.Empty<SkinPixelSample>());

        var expected = Composite(material, baseline);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            control.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
        }
        catch (Exception)
        {
            return new SkinPixelReport(materialId, tolerance, Array.Empty<SkinPixelSample>());
        }

        var samples = new List<SkinPixelSample>();
        foreach (var (label, point) in Points(width, height))
        {
            var actual = bitmap.GetPixel(point.X, point.Y);
            samples.Add(new SkinPixelSample(
                label, actual, baseline, expected, Distance(actual, baseline), Distance(actual, expected)));
        }

        return new SkinPixelReport(materialId, tolerance, samples);
    }

    /// <summary>采样点：中心 + 四角内缩（避开 1px 描边与文本行）。</summary>
    private static IReadOnlyList<(string Label, Point Point)> Points(int width, int height)
    {
        var x1 = Math.Max(1, width / 5);
        var x2 = Math.Max(1, width - width / 5 - 1);
        var y1 = Math.Max(1, height / 5);
        var y2 = Math.Max(1, height - height / 5 - 1);
        return new[]
        {
            ("center", new Point(width / 2, height / 2)),
            ("tl", new Point(x1, y1)),
            ("tr", new Point(x2, y1)),
            ("bl", new Point(x1, y2)),
            ("br", new Point(x2, y2)),
        };
    }

    /// <summary>未换肤基线：控件 BackColor（透明 ⇒ 取父级 BackColor；无父 ⇒ 系统窗体色）。</summary>
    private static Color BaselineOf(Control control)
    {
        var color = control.BackColor;
        if (color.A != 0) return color;
        return control.Parent is { } parent && parent.BackColor.A != 0 ? parent.BackColor : SystemColors.Control;
    }

    /// <summary>按层序把材质合成到基线上（与 <see cref="SkinPaintEngine"/> 的层序/透明度语义对齐）。</summary>
    private static Color Composite(ResolvedMaterial material, Color baseline)
    {
        var result = baseline;
        foreach (var layer in material.Layers)
        {
            if (!Coverage.TryGetValue(layer.Primitive, out var coverage)) continue;
            if (!layer.Params.TryGetValue("value", out var value) || string.IsNullOrWhiteSpace(value)) continue;

            var color = FirstColor(value);
            if (color.A == 0) continue; // 未解析值 ⇒ 透明 ⇒ 不改变基线（由 SkinDiagnostics.unresolvedTokens 可见化）

            var ratio = (float)Math.Clamp(coverage * (color.A / 255.0), 0.0, 1.0);
            result = result.Blend(color, ratio);
        }

        return result;
    }

    private static Color FirstColor(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !SkinDiagnostics.IsLiteralColor(parts[0])) return Color.Transparent;
        return ColorExtensions.TryParse(parts[0], out var color) ? color : Color.Transparent;
    }

    /// <summary>RGB 欧氏距离（与 ColorUtils.ColorDistance 同度量；阈值语义可直接对照）。</summary>
    private static double Distance(Color left, Color right)
    {
        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}


