using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Modules.Common.Drawing;

/// <summary>金样判定。</summary>
public enum GoldenVerdict
{
    /// <summary>实测与金样在容差内一致。</summary>
    Match,

    /// <summary>实测与金样超容差（唯一可判红的状态）。</summary>
    Mismatch,

    /// <summary>金样不可用/不可信（缺件、哈希不符、尺寸不符、来源非独立）——不许当通过。</summary>
    Unknown,
}

/// <summary>单张金样的判定门限（来自金样清单，属金样自身的一部分，不散落调用方）。</summary>
/// <param name="ChannelTolerance">单通道允许的最大差值（0 = 逐通道严格相等）。</param>
/// <param name="MaxDifferingRatio">整图允许的差异像素占比上限。</param>
/// <param name="MaxBoundaryDifferingRatio">边界带（外圈 2px）允许的差异像素占比上限。</param>
public sealed record GoldenCriteria(
    int ChannelTolerance, double MaxDifferingRatio, double MaxBoundaryDifferingRatio);

/// <summary>金样比对报告（判定只读本结构）。</summary>
/// <param name="Case">金样用例 id（清单声明，须与实际 sample/theme/dpi 对应）。</param>
/// <param name="GoldenPath">金样 PNG 路径。</param>
/// <param name="GoldenSha256">金样文件实际哈希（与清单声明比对通过后才参与判定）。</param>
/// <param name="Origin">金样来源（清单声明；只允许独立来源白名单内的值）。</param>
/// <param name="Verdict">判定结论（<see cref="GoldenVerdict.Unknown"/> 不算通过）。</param>
/// <param name="ComparedPixels">整图比较像素数。</param>
/// <param name="DifferingPixels">超容差像素数。</param>
/// <param name="DifferingRatio">整图差异占比。</param>
/// <param name="BoundaryComparedPixels">边界带比较像素数。</param>
/// <param name="BoundaryDifferingPixels">边界带超容差像素数。</param>
/// <param name="BoundaryDifferingRatio">边界带差异占比。</param>
/// <param name="Reason">Unknown/Mismatch 的结构化原因（Match 时为空）。</param>
public sealed record GoldenImageReport(
    string Case,
    string GoldenPath,
    string GoldenSha256,
    string Origin,
    GoldenVerdict Verdict,
    int ComparedPixels,
    int DifferingPixels,
    double DifferingRatio,
    int BoundaryComparedPixels,
    int BoundaryDifferingPixels,
    double BoundaryDifferingRatio,
    string Reason)
{
    /// <summary>关键区域覆盖率（= 1 - 整图差异占比）。</summary>
    public double Coverage => 1.0 - DifferingRatio;

    /// <summary>是否通过（只有 <see cref="GoldenVerdict.Match"/> 算通过）。</summary>
    public bool Passed => Verdict == GoldenVerdict.Match;

    /// <summary>单行证据。</summary>
    public string Describe()
        => string.Format(
            "case={0} verdict={1} diff={2}/{3}({4:0.0000}) boundaryDiff={5}/{6}({7:0.0000}) golden={8}{9}",
            string.IsNullOrEmpty(Case) ? "-" : Case,
            Verdict,
            DifferingPixels,
            ComparedPixels,
            DifferingRatio,
            BoundaryDifferingPixels,
            BoundaryComparedPixels,
            BoundaryDifferingRatio,
            string.IsNullOrEmpty(GoldenSha256) ? "-" : GoldenSha256.Substring(0, Math.Min(12, GoldenSha256.Length)),
            string.IsNullOrEmpty(Reason) ? string.Empty : " reason=" + Reason);
}

/// <summary>
/// 金样（独立期望图）装载与比对——**唯一金样判定实现**。
/// <para>结构约束：本类只读 PNG 字节 + 同名清单（&lt;name&gt;.golden.json），**不引用任何被测绘制实现**；
/// 清单 origin 只允许 <see cref="AllowedOrigins"/> 内的独立来源，引擎自绘产物无法通过加载（直接 Unknown）。</para>
/// <para>缺清单/哈希不符/尺寸不符/来源非法 ⇒ <see cref="GoldenVerdict.Unknown"/>（fail-closed，不许当通过）。</para>
/// </summary>
public static class GoldenImage
{
    /// <summary>清单文件名后缀：<c>foo.png</c> → <c>foo.golden.json</c>。</summary>
    public const string ManifestExtension = ".golden.json";

    /// <summary>清单 schema 版本。</summary>
    public const int ManifestSchema = 1;

    /// <summary>边界带宽度（像素；外圈环）。</summary>
    public const int BoundaryWidth = 2;

    /// <summary>独立来源白名单：实拍裁片 / 独立参考光栅。被测实现自绘的金样不得入档。</summary>
    public static readonly string[] AllowedOrigins = { "screenshot-crop", "reference-raster" };

    /// <summary>清单路径（与金样 PNG 同名）。</summary>
    public static string ManifestPathOf(string goldenPath)
        => Path.ChangeExtension(goldenPath, ManifestExtension);

    /// <summary>
    /// 比对实测位图与金样。
    /// </summary>
    /// <param name="actual">实测位图（控件真实绘制结果）。</param>
    /// <param name="goldenPath">金样 PNG 路径；同名清单必须存在。</param>
    public static GoldenImageReport Compare(Bitmap actual, string goldenPath)
    {
        if (actual == null) throw new ArgumentNullException(nameof(actual));
        if (string.IsNullOrWhiteSpace(goldenPath))
            return Unknown(string.Empty, goldenPath, string.Empty, string.Empty, "golden-path-empty");
        if (!File.Exists(goldenPath))
            return Unknown(string.Empty, goldenPath, string.Empty, string.Empty, "golden-missing");

        byte[] bytes;
        try { bytes = File.ReadAllBytes(goldenPath); }
        catch (Exception ex) { return Unknown(string.Empty, goldenPath, string.Empty, string.Empty, "golden-read-failed:" + ex.GetType().Name); }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var manifestPath = ManifestPathOf(goldenPath);
        if (!File.Exists(manifestPath))
            return Unknown(string.Empty, goldenPath, hash, string.Empty, "manifest-missing");
        JObject manifest;
        try { manifest = JObject.Parse(File.ReadAllText(manifestPath, Encoding.UTF8)); }
        catch (Exception ex) { return Unknown(string.Empty, goldenPath, hash, string.Empty, "manifest-invalid:" + ex.GetType().Name); }

        var caseId = (string)manifest["case"] ?? string.Empty;
        var origin = (string)manifest["origin"] ?? string.Empty;
        var declaredHash = (string)manifest["sha256"] ?? string.Empty;
        if (manifest.Value<int?>("schema") != ManifestSchema)
            return Unknown(caseId, goldenPath, hash, origin, "manifest-schema");
        if (Array.IndexOf(AllowedOrigins, origin) < 0)
            return Unknown(caseId, goldenPath, hash, origin, "origin-not-independent:" + origin);
        if (!string.Equals(declaredHash, hash, StringComparison.OrdinalIgnoreCase))
            return Unknown(caseId, goldenPath, hash, origin, "golden-sha-mismatch");

        var criteria = ReadCriteria(manifest);
        if (criteria == null)
            return Unknown(caseId, goldenPath, hash, origin, "criteria-missing");

        Bitmap golden;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var decoded = new Bitmap(stream);
            golden = ToArgb32(decoded);
        }
        catch (Exception ex)
        {
            return Unknown(caseId, goldenPath, hash, origin, "golden-decode-failed:" + ex.GetType().Name);
        }

        using (golden)
        {
            if (manifest.Value<int?>("width") != golden.Width || manifest.Value<int?>("height") != golden.Height)
                return Unknown(caseId, goldenPath, hash, origin, "manifest-size-mismatch");
            if (actual.Width != golden.Width || actual.Height != golden.Height)
                return Unknown(caseId, goldenPath, hash, origin,
                    string.Format("size-mismatch actual={0}x{1} golden={2}x{3}", actual.Width, actual.Height, golden.Width, golden.Height));

            var actual32 = ToArgb32(actual);
            using (actual32)
            {
                var result = ComparePixels(actual32, golden, criteria);
                var verdict = result.DifferingRatio > criteria.MaxDifferingRatio
                              || result.BoundaryDifferingRatio > criteria.MaxBoundaryDifferingRatio
                    ? GoldenVerdict.Mismatch
                    : GoldenVerdict.Match;
                var reason = verdict == GoldenVerdict.Match
                    ? string.Empty
                    : string.Format("diff={0:0.0000}>{1:0.0000} or boundaryDiff={2:0.0000}>{3:0.0000}",
                        result.DifferingRatio, criteria.MaxDifferingRatio,
                        result.BoundaryDifferingRatio, criteria.MaxBoundaryDifferingRatio);
                return new GoldenImageReport(
                    caseId, goldenPath, hash, origin, verdict,
                    result.Compared, result.Differing, result.DifferingRatio,
                    result.BoundaryCompared, result.BoundaryDiffering, result.BoundaryDifferingRatio,
                    reason);
            }
        }
    }

    private static GoldenCriteria ReadCriteria(JObject manifest)
    {
        var criteria = manifest["criteria"] as JObject;
        if (criteria == null) return null;
        if (criteria["channelTolerance"] == null || criteria["maxDifferingRatio"] == null
            || criteria["maxBoundaryDifferingRatio"] == null) return null;
        return new GoldenCriteria(
            criteria.Value<int>("channelTolerance"),
            criteria.Value<double>("maxDifferingRatio"),
            criteria.Value<double>("maxBoundaryDifferingRatio"));
    }

    private static GoldenImageReport Unknown(string caseId, string path, string hash, string origin, string reason)
        => new GoldenImageReport(caseId, path, hash, origin, GoldenVerdict.Unknown, 0, 0, 0, 0, 0, 0, reason);

    private static Bitmap ToArgb32(Bitmap source)
    {
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(copy);
        graphics.DrawImageUnscaled(source, 0, 0);
        return copy;
    }

    private static (int Compared, int Differing, double DifferingRatio,
        int BoundaryCompared, int BoundaryDiffering, double BoundaryDifferingRatio)
        ComparePixels(Bitmap actual, Bitmap golden, GoldenCriteria criteria)
    {
        var width = actual.Width;
        var height = actual.Height;
        var rect = new Rectangle(0, 0, width, height);
        var actualData = actual.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var goldenData = golden.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var actualBytes = new byte[actualData.Stride * height];
            var goldenBytes = new byte[goldenData.Stride * height];
            System.Runtime.InteropServices.Marshal.Copy(actualData.Scan0, actualBytes, 0, actualBytes.Length);
            System.Runtime.InteropServices.Marshal.Copy(goldenData.Scan0, goldenBytes, 0, goldenBytes.Length);

            // 32bppArgb 内存序 = B,G,R,A（与 Platform 无关，GDI+ 固定）
            var tolerance = criteria.ChannelTolerance;
            var compared = 0;
            var differing = 0;
            var boundaryCompared = 0;
            var boundaryDiffering = 0;
            for (var y = 0; y < height; y++)
            {
                var actualRow = y * actualData.Stride;
                var goldenRow = y * goldenData.Stride;
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 4;
                    var a = actualRow + offset;
                    var g = goldenRow + offset;
                    var diff = Math.Abs(actualBytes[a] - goldenBytes[g]);
                    if (Math.Abs(actualBytes[a + 1] - goldenBytes[g + 1]) > diff) diff = Math.Abs(actualBytes[a + 1] - goldenBytes[g + 1]);
                    if (Math.Abs(actualBytes[a + 2] - goldenBytes[g + 2]) > diff) diff = Math.Abs(actualBytes[a + 2] - goldenBytes[g + 2]);
                    if (Math.Abs(actualBytes[a + 3] - goldenBytes[g + 3]) > diff) diff = Math.Abs(actualBytes[a + 3] - goldenBytes[g + 3]);

                    var isDifferent = diff > tolerance;
                    compared++;
                    if (isDifferent) differing++;

                    if (x < BoundaryWidth || y < BoundaryWidth || x >= width - BoundaryWidth || y >= height - BoundaryWidth)
                    {
                        boundaryCompared++;
                        if (isDifferent) boundaryDiffering++;
                    }
                }
            }

            return (compared, differing, compared == 0 ? 0 : (double)differing / compared,
                boundaryCompared, boundaryDiffering,
                boundaryCompared == 0 ? 0 : (double)boundaryDiffering / boundaryCompared);
        }
        finally
        {
            actual.UnlockBits(actualData);
            golden.UnlockBits(goldenData);
        }
    }
}
