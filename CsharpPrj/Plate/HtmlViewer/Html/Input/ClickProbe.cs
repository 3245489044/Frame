#nullable enable

using System;
using System.Globalization;
using System.Text.Json;

namespace UiBackend.Html;

/// <summary>
/// 真实点击前的元素级探测结果（rect + 视口 + elementFromPoint 归属 + 滚动复测）。
/// 探测只提供「根因分型 + 坐标自证」，最终送达判据仍是 DOM click 回执（ack）——
/// 系统命中测试与 elementFromPoint 不等价，二者不可互相替代。
/// </summary>
public sealed record ClickProbe(
    double X,
    double Y,
    double Width,
    double Height,
    double Dpr,
    bool InViewport,
    bool HitSame,
    string? HitName,
    string? HitTag,
    bool Scrolled)
{
    /// <summary>遮挡层矩形（`hitRect`，CSS 像素文本；探测脚本未回带 ⇒ null）。</summary>
    public string? HitRect { get; init; }

    /// <summary>遮挡层 z-index（`hitZIndex`；未回带 ⇒ null）。</summary>
    public string? HitZIndex { get; init; }

    /// <summary>遮挡层路径（`hitPath`，tag.class 链；未回带 ⇒ null）。</summary>
    public string? HitPath { get; init; }

    /// <summary>与目标矩形的重叠面积（`overlapArea`，CSS 像素²；未回带 ⇒ null）。</summary>
    public double? OverlapArea { get; init; }

    /// <summary>dpr 合法区间（越界=配置异常，显式判红，不再 Math.Max 静默夹紧）。</summary>
    public const double MinDpr = 0.5;

    public const double MaxDpr = 4.0;

    /// <summary>探测分型：视口外 / 被遮挡 / 可点。</summary>
    public ClickOutcome Verdict
        => !InViewport ? ClickOutcome.OffViewport : (HitSame ? ClickOutcome.Ok : ClickOutcome.Occluded);

    /// <summary>CSS 中心 → 客户区设备像素。dpr 越界 ⇒ false + 原因（NoSilentOutlet）。</summary>
    public bool TryMapToClient(out int clientX, out int clientY, out string error)
    {
        clientX = 0;
        clientY = 0;
        error = string.Empty;
        if (double.IsNaN(Dpr) || double.IsInfinity(Dpr) || Dpr < MinDpr || Dpr > MaxDpr)
        {
            error = $"dpr 越界（{Dpr.ToString("F3", CultureInfo.InvariantCulture)} 不在 [{MinDpr},{MaxDpr}]）";
            return false;
        }

        clientX = (int)Math.Round(X * Dpr, MidpointRounding.AwayFromZero);
        clientY = (int)Math.Round(Y * Dpr, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>dpr 与窗口 DPI 交叉核对（windowDpi=0 表示无法取得，不参与判定；仅取证，不静默修正坐标）。</summary>
    public bool IsDpiConsistent(uint windowDpi, double tolerance = 0.02)
        => windowDpi == 0 || Math.Abs(Dpr - windowDpi / 96.0) <= tolerance;

    public string ToEvidence()
        => $"probe=({F(X)},{F(Y)}) rect=({F(Width)}x{F(Height)}) dpr={Dpr.ToString("F2", CultureInfo.InvariantCulture)} "
           + $"inViewport={InViewport} hitSame={HitSame} hitName={HitName ?? "-"} hitTag={HitTag ?? "-"} scrolled={Scrolled}"
           + (HitRect is null ? string.Empty : $" hitRect={HitRect}")
           + (HitZIndex is null ? string.Empty : $" hitZIndex={HitZIndex}")
           + (HitPath is null ? string.Empty : $" hitPath={HitPath}")
           + (OverlapArea is null ? string.Empty : $" overlapArea={OverlapArea.Value.ToString("F1", CultureInfo.InvariantCulture)}");

    /// <summary>解析探测脚本负载（非法/非对象 ⇒ false，不抛）。</summary>
    public static bool TryParse(string? json, out ClickProbe? probe)
    {
        probe = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            probe = new ClickProbe(
                GetDouble(root, "x"),
                GetDouble(root, "y"),
                GetDouble(root, "w"),
                GetDouble(root, "h"),
                GetDouble(root, "dpr"),
                GetBool(root, "inViewport"),
                GetBool(root, "hitSame"),
                GetString(root, "hitName"),
                GetString(root, "hitTag"),
                GetBool(root, "scrolled"))
            {
                HitRect = GetString(root, "hitRect"),
                HitZIndex = GetString(root, "hitZIndex"),
                HitPath = GetString(root, "hitPath"),
                OverlapArea = GetNullableDouble(root, "overlapArea"),
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static double GetDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;

    private static double? GetNullableDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static bool GetBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
