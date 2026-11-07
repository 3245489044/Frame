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
/// <param name="Delta">实际 vs 基线的 RGBA 欧氏距离（材质是否真正落像素）。</param>
/// <param name="ExpectedDelta">实际 vs 期望的 RGBA 欧氏距离（色值/alpha 是否正确）。</param>
public sealed record SkinPixelSample(
    string Point, Color Actual, Color Baseline, Color Expected, double Delta, double ExpectedDelta);

/// <summary>像素回归报告（**回归模式专用**；不含 pass/fail，判定请用 <see cref="GoldenPixelReport"/>）。</summary>
/// <param name="Material">被验证材质 id。</param>
/// <param name="Tolerance">期望拟合容差（RGBA 距离）。</param>
/// <param name="Samples">采样点集合。</param>
/// <param name="Painted">真实绘制是否真的执行过（<see cref="SkinDecorator"/> 的绘制计数是否推进，结构性事实）。</param>
/// <param name="Failure">绘制中止/抛异常时的结构化原因（非空 ⇒ 判定必红，不靠像素猜）。</param>
public sealed record SkinPixelReport(
    string Material, int Tolerance, IReadOnlyList<SkinPixelSample> Samples,
    bool Painted = false, string Failure = "")
{
    /// <summary>实际像素相对未换肤基线的最大偏移（0 ⇒ 材质完全没落像素）。</summary>
    public double MaxDelta => Samples.Count == 0 ? 0 : Samples.Max(sample => sample.Delta);

    /// <summary>与期望色吻合的采样点数。**回归诊断用**：期望由被测实现自绘，禁止据此判门禁。</summary>
    public int Matched => Samples.Count(sample => sample.ExpectedDelta <= Tolerance);

    /// <summary>是否可见（绘制确实发生且至少一个点偏离基线；中止绘制不算可见）。</summary>
    public bool Visible => Painted && MaxDelta > 0.5;

    /// <summary>单行证据（日志/失败文案）。</summary>
    public string Describe()
        => string.Format("material={0} samples={1} maxDelta={2:0.0} matched={3}/{4} actual={5} baseline={6} expected={7} painted={8}{9}",
            string.IsNullOrEmpty(Material) ? "-" : Material,
            Samples.Count,
            MaxDelta,
            Matched,
            Samples.Count,
            Samples.Count > 0 ? Samples[0].Actual.ToHex() : "-",
            Samples.Count > 0 ? Samples[0].Baseline.ToHex() : "-",
            Samples.Count > 0 ? Samples[0].Expected.ToHex() : "-",
            Painted,
            string.IsNullOrEmpty(Failure) ? string.Empty : " error=" + Failure);
}

/// <summary>单点实测样本（只读实测；**没有期望**，因而不可能自证）。</summary>
/// <param name="Point">采样点标签。</param>
/// <param name="Actual">控件真实绘制结果。</param>
/// <param name="Baseline">未换肤基线。</param>
/// <param name="Delta">实际 vs 基线的 RGBA 欧氏距离（材质是否真正落像素）。</param>
public sealed record SkinActualSample(string Point, Color Actual, Color Baseline, double Delta);

/// <summary>实测报告（运行时"材质是否落像素"用；不含任何期望图，判据只看结构事实）。</summary>
/// <param name="Material">实际生效材质 id（空 ⇒ 未挂载）。</param>
/// <param name="Samples">采样点集合。</param>
/// <param name="Painted">真实绘制是否执行过（绘制计数推进）。</param>
/// <param name="Failure">绘制中止/未执行的结构化原因。</param>
public sealed record SkinActualReport(
    string Material, IReadOnlyList<SkinActualSample> Samples, bool Painted = false, string Failure = "")
{
    /// <summary>实际像素相对未换肤基线的最大偏移。</summary>
    public double MaxDelta => Samples.Count == 0 ? 0 : Samples.Max(sample => sample.Delta);

    /// <summary>是否可见（绘制发生且至少一个点偏离基线）。</summary>
    public bool Visible => Painted && MaxDelta > 0.5;

    /// <summary>单行证据。</summary>
    public string Describe()
        => string.Format("material={0} samples={1} maxDelta={2:0.0} actual={3} baseline={4} painted={5}{6}",
            string.IsNullOrEmpty(Material) ? "-" : Material,
            Samples.Count,
            MaxDelta,
            Samples.Count > 0 ? Samples[0].Actual.ToHex() : "-",
            Samples.Count > 0 ? Samples[0].Baseline.ToHex() : "-",
            Painted,
            string.IsNullOrEmpty(Failure) ? string.Empty : " error=" + Failure);
}

/// <summary>金样判据报告（**唯一可产出 pass/fail 的像素判定**；期望来自独立金样文件）。</summary>
/// <param name="Material">实际生效材质 id。</param>
/// <param name="Case">金样用例 id（清单声明）。</param>
/// <param name="GoldenPath">金样 PNG 路径。</param>
/// <param name="GoldenSha256">金样文件实际哈希。</param>
/// <param name="Verdict">判定（Unknown 不算通过）。</param>
/// <param name="ComparedPixels">整图比较像素数。</param>
/// <param name="DifferingPixels">超容差像素数。</param>
/// <param name="DifferingRatio">整图差异占比。</param>
/// <param name="Coverage">关键区域覆盖率（1 - 差异占比）。</param>
/// <param name="BoundaryDifferingPixels">边界带超容差像素数。</param>
/// <param name="BoundaryDifferingRatio">边界带差异占比。</param>
/// <param name="Reason">Unknown/Mismatch 的结构化原因。</param>
public sealed record GoldenPixelReport(
    string Material, string Case, string GoldenPath, string GoldenSha256, GoldenVerdict Verdict,
    int ComparedPixels, int DifferingPixels, double DifferingRatio, double Coverage,
    int BoundaryDifferingPixels, double BoundaryDifferingRatio, string Reason)
{
    /// <summary>是否通过（只有 <see cref="GoldenVerdict.Match"/> 算通过）。</summary>
    public bool Passed => Verdict == GoldenVerdict.Match;

    /// <summary>单行证据。</summary>
    public string Describe()
        => string.Format("material={0} case={1} verdict={2} diff={3}/{4}({5:0.0000}) boundaryDiff={6}({7:0.0000}) golden={8}{9}",
            string.IsNullOrEmpty(Material) ? "-" : Material,
            string.IsNullOrEmpty(Case) ? "-" : Case,
            Verdict,
            DifferingPixels,
            ComparedPixels,
            DifferingRatio,
            BoundaryDifferingPixels,
            BoundaryDifferingRatio,
            string.IsNullOrEmpty(GoldenSha256) ? "-" : GoldenSha256.Substring(0, Math.Min(12, GoldenSha256.Length)),
            string.IsNullOrEmpty(Reason) ? string.Empty : " reason=" + Reason);
}

/// <summary>
/// 像素探针：把控件真实绘制结果与「未换肤基线」或「独立金样」比对。
/// <para>判据载体 = 像素值（结构事实），不是 Enabled/Count 等代理指标：Enabled=true 但层值不是色值时，这里必红。</para>
/// <para>同源约束：<see cref="Inspect"/> 的期望位图由 <see cref="SkinPaintEngine"/> 自绘（回归诊断，不产出 pass/fail）；
/// 判定入口是 <see cref="InspectAgainstGolden"/>，期望只来自金样文件，**代码路径上无法取得实现自绘的期望**。
/// 运行时"材质是否落像素"用 <see cref="Probe"/>（只读实测，无期望）。</para>
/// </summary>
public static class SkinPixelProbe
{
    /// <summary>
    /// 回归模式：采样并与「同一实现自绘的期望」比对。
    /// <para>**不产出 pass/fail**：期望与实测同源，只用于观察差异；门禁判据必须用 <see cref="InspectAgainstGolden"/>。</para>
    /// </summary>
    /// <param name="control">目标控件（须已创建/可见）。</param>
    /// <param name="material">该控件实际生效材质（<see cref="SkinDecorator.MaterialOf"/>）。</param>
    /// <param name="tolerance">期望拟合容差（RGB 欧氏距离，默认 40；仅诊断输出）。</param>
    [Obsolete("回归模式：期望由被测实现自绘（同源），只输出差异、不产出 pass/fail；判据请用 InspectAgainstGolden。")]
    public static SkinPixelReport Inspect(Control control, ResolvedMaterial? material, int tolerance = 40)
    {
        ArgumentNullException.ThrowIfNull(control);
        using var raster = Rasterize(control, material);
        if (raster.Material is null || raster.Bitmap is null)
            return new SkinPixelReport(raster.MaterialId, tolerance, Array.Empty<SkinPixelSample>(), raster.Painted, raster.Failure);

        var width = raster.Bitmap.Width;
        var height = raster.Bitmap.Height;
        // 期望位图 = 预填基线上跑同一绘制实现（禁手工覆盖率，否则与实际算法不一致 ⇒ 假红）。
        using var expectedBitmap = ExpectedBitmap(material!, raster.Baseline, width, height, raster.Inputs!);

        var samples = new List<SkinPixelSample>();
        foreach (var (label, point) in Points(width, height))
        {
            var actual = raster.Bitmap.GetPixel(point.X, point.Y);
            var expected = expectedBitmap.GetPixel(point.X, point.Y);
            samples.Add(new SkinPixelSample(
                label, actual, raster.Baseline, expected, Distance(actual, raster.Baseline), Distance(actual, expected)));
        }

        return new SkinPixelReport(raster.MaterialId, tolerance, samples, raster.Painted);
    }

    /// <summary>
    /// 运行时实测：只读控件像素与未换肤基线，**不含任何期望图**（结构性排除自证）。
    /// </summary>
    /// <param name="control">目标控件（须已创建/可见）。</param>
    public static SkinActualReport Probe(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        using var raster = Rasterize(control, SkinDecorator.MaterialOf(control));
        if (raster.Bitmap is null)
            return new SkinActualReport(raster.MaterialId, Array.Empty<SkinActualSample>(), raster.Painted, raster.Failure);

        var samples = new List<SkinActualSample>();
        foreach (var (label, point) in Points(raster.Bitmap.Width, raster.Bitmap.Height))
        {
            var actual = raster.Bitmap.GetPixel(point.X, point.Y);
            samples.Add(new SkinActualSample(label, actual, raster.Baseline, Distance(actual, raster.Baseline)));
        }

        return new SkinActualReport(raster.MaterialId, samples, raster.Painted);
    }

    /// <summary>
    /// 金样判定（**唯一 pass/fail 像素判据**）：实测 vs 独立金样 PNG（同名清单声明来源/哈希/门限）。
    /// <para>期望只来自金样文件；本方法不调用任何绘制实现产出期望，因而不可能"预期图与实测同源"。</para>
    /// </summary>
    /// <param name="control">目标控件（须已创建/可见）。</param>
    /// <param name="material">该控件实际生效材质；null ⇒ 取 <see cref="SkinDecorator.MaterialOf"/>。</param>
    /// <param name="goldenPath">金样 PNG；同名 <c>.golden.json</c> 必须存在且来源在白名单内。</param>
    public static GoldenPixelReport InspectAgainstGolden(Control control, ResolvedMaterial? material, string goldenPath)
    {
        ArgumentNullException.ThrowIfNull(control);
        material ??= SkinDecorator.MaterialOf(control);
        using var raster = Rasterize(control, material);
        if (raster.Bitmap is null)
            return new GoldenPixelReport(raster.MaterialId, string.Empty, goldenPath, string.Empty,
                GoldenVerdict.Unknown, 0, 0, 0, 0, 0, 0,
                string.IsNullOrEmpty(raster.Failure) ? "no-frame" : raster.Failure);

        var image = GoldenImage.Compare(raster.Bitmap, goldenPath);
        return new GoldenPixelReport(raster.MaterialId, image.Case, image.GoldenPath, image.GoldenSha256,
            image.Verdict, image.ComparedPixels, image.DifferingPixels, image.DifferingRatio, image.Coverage,
            image.BoundaryDifferingPixels, image.BoundaryDifferingRatio, image.Reason);
    }

    /// <summary>采样点：中心 + 四角内缩 + 四边中点（描边不再被结构性避开）。</summary>
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
            ("border-left", new Point(0, height / 2)),
            ("border-top", new Point(width / 2, 0)),
            ("border-right", new Point(Math.Max(0, width - 1), height / 2)),
            ("border-bottom", new Point(width / 2, Math.Max(0, height - 1))),
        };
    }

    /// <summary>未换肤基线：控件 BackColor（透明 ⇒ 取父级 BackColor；无父 ⇒ 系统窗体色）。</summary>
    private static Color BaselineOf(Control control)
    {
        var color = control.BackColor;
        if (color.A != 0) return color;
        return control.Parent is { } parent && parent.BackColor.A != 0 ? parent.BackColor : SystemColors.Control;
    }

    /// <summary>
    /// 捕获一帧真实绘制（冻结同一份动态输入：时间/光标/背后快照只解析一次）。
    /// 失败 ⇒ Bitmap=null + 结构化 Failure（由调用方判红，不靠像素猜）。
    /// </summary>
    private static Raster Rasterize(Control control, ResolvedMaterial? material)
    {
        var raster = new Raster
        {
            Material = material,
            MaterialId = material?.MaterialId ?? string.Empty,
            Baseline = BaselineOf(control),
        };
        if (material is null) return raster;

        var width = control.ClientSize.Width > 0 ? control.ClientSize.Width : control.Width;
        var height = control.ClientSize.Height > 0 ? control.ClientSize.Height : control.Height;
        if (width <= 0 || height <= 0) return raster;

        raster.Inputs = SkinDecorator.BuildRenderInputs(control, material);
        var paintBefore = SkinDecorator.PaintVersionOf(control);
        var attached = SkinDecorator.MaterialOf(control) is not null;

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        // 预填基线：绘制中止时像素保持基线（MaxDelta=0），不再把未初始化的白/红当成「材质颜色」。
        using (var prefill = Graphics.FromImage(bitmap)) prefill.Clear(raster.Baseline);

        Exception? failure = null;
        try
        {
            using var pin = SkinDecorator.PinInputs(control, raster.Inputs);
            control.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        raster.Painted = SkinDecorator.PaintVersionOf(control) > paintBefore;
        if (failure is not null || (!raster.Painted && attached))
        {
            // 已接线却一次绘制都没跑 ⇒ 绘制中止/未执行，判红；未接线则继续采样，用「等于基线」证明没有换肤。
            raster.Failure = failure is null ? "paint-not-run" : failure.GetType().Name + ": " + failure.Message;
            bitmap.Dispose();
            return raster;
        }

        raster.Bitmap = bitmap;
        return raster;
    }

    /// <summary>期望位图：预填未换肤基线后**复刻实际的两段绘制**（静态层 → 动态 sheen），并吃冻结的同一份 light/backdrop。</summary>
    private static Bitmap ExpectedBitmap(
        ResolvedMaterial material, Color baseline, int width, int height, SkinRenderInputs inputs)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(baseline);
        var bounds = new Rectangle(0, 0, width, height);
        // 与实际 owner-draw 同序：静态段不含 specular，动态段只画 sheen（单趟拟合会漏掉两段时点差异 ⇒ 假绿）。
        SkinPaintEngine.Paint(
            graphics, bounds, material, inputs.Backdrop, inputs.Light, includeSpecular: false);
        SkinPaintEngine.PaintDynamic(graphics, bounds, material, inputs.Light);
        return bitmap;
    }

    /// <summary>RGBA 欧氏距离（含 alpha；半透明材质只比 RGB 会把 alpha 错误判成通过）。</summary>
    private static double Distance(Color left, Color right)
    {
        var da = left.A - right.A;
        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        return Math.Sqrt(da * da + dr * dr + dg * dg + db * db);
    }

    /// <summary>一帧捕获结果（Bitmap/Inputs 由 using 释放；Material 为 null ⇒ 未接线）。</summary>
    private sealed class Raster : IDisposable
    {
        public ResolvedMaterial? Material;
        public string MaterialId = string.Empty;
        public Bitmap? Bitmap;
        public SkinRenderInputs? Inputs;
        public bool Painted;
        public string Failure = string.Empty;
        public Color Baseline;

        public void Dispose()
        {
            Bitmap?.Dispose();
            Bitmap = null;
            Inputs?.Dispose();
            Inputs = null;
        }
    }
}
