#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace UiBackend;

/// <summary>四边颜色（打包 ARGB，<c>0xAARRGGBB</c>；色值解析统一经 <see cref="SurfaceColor"/>）。</summary>
public readonly record struct BorderColorSet(int Left, int Top, int Right, int Bottom)
{
    /// <summary>四边同色。</summary>
    public static BorderColorSet Uniform(int argb) => new(argb, argb, argb, argb);

    /// <summary>四边是否同色。</summary>
    public bool IsUniform => Left == Top && Top == Right && Right == Bottom;
}

/// <summary>模型归一化问题（解析端可见化，禁静默回落）。</summary>
/// <param name="Code">稳定码（如 <c>border-width-invalid</c>）。</param>
/// <param name="Detail">原始文本/上下文。</param>
public readonly record struct BorderIssue(string Code, string Detail);

/// <summary>一条描边（DIP 浮点；外沿贴 bounds）。</summary>
/// <param name="Z">层序（同类内排序）。</param>
/// <param name="Widths">四边宽度（DIP）。</param>
/// <param name="Colors">四边颜色（ARGB）。</param>
/// <param name="Radii">四角外沿半径（DIP）。</param>
/// <param name="Chamfer">四角倒角长度（DIP；0 = 圆角）。</param>
/// <param name="Align">对齐语义（<c>outer</c> 默认）。</param>
public sealed record BorderStroke(
    int Z, BorderEdgeSet Widths, BorderColorSet Colors, CornerRadii Radii, CornerRadii Chamfer, string Align)
{
    /// <summary>是否有可见宽度。</summary>
    public bool HasVisibleWidth => !Widths.IsZero;
}

/// <summary>外发光 / 外阴影（DIP）。</summary>
public sealed record BorderGlow(int Z, int Argb, float Blur, float OffsetX, float OffsetY, float Spread);

/// <summary>内高光 / 内阴影（DIP；<c>Sides</c> 语义见载体投影）。</summary>
public sealed record BorderInnerLight(int Z, int Argb, float Width, string Sides, float Strength);

/// <summary>模型输入：材质层的最小只读视图（Share 适配层只做搬运，不解释语义）。</summary>
public sealed record BorderLayerInput(string Role, string Primitive, int Z, IReadOnlyDictionary<string, string> Params);

/// <summary>
/// 边框单一模型（载体无关）：从现有材质层归一化而来，不拥有层、不替代 <c>ResolvedMaterial</c>。
/// <para>本程序集 BCL-only：颜色只存打包 ARGB，几何只存浮点/DIP；GDI+ 与 CSS 各自投影。</para>
/// </summary>
public sealed record BorderSpec(
    IReadOnlyList<BorderStroke> Strokes, IReadOnlyList<BorderGlow> Glows, IReadOnlyList<BorderInnerLight> InnerLights)
{
    /// <summary>空模型。</summary>
    public static readonly BorderSpec Empty = new(
        Array.Empty<BorderStroke>(), Array.Empty<BorderGlow>(), Array.Empty<BorderInnerLight>());

    /// <summary>是否没有任何边框元素。</summary>
    public bool IsEmpty => Strokes.Count == 0 && Glows.Count == 0 && InnerLights.Count == 0;

    /// <summary>
    /// 把材质层归一化为边框模型。非法宽度/颜色会产出 <see cref="BorderIssue"/> 并跳过该元素，
    /// **不静默回落**（调用方必须把 issues 记账）。
    /// </summary>
    /// <param name="layers">材质层只读视图。</param>
    /// <param name="issues">归一化问题（空数组 = 无问题）。</param>
    public static BorderSpec FromLayers(IReadOnlyList<BorderLayerInput> layers, out IReadOnlyList<BorderIssue> issues)
    {
        issues = Array.Empty<BorderIssue>();
        if (layers is null || layers.Count == 0) return Empty;

        var issueList = new List<BorderIssue>();
        var strokes = new List<BorderStroke>();
        var glows = new List<BorderGlow>();
        var lights = new List<BorderInnerLight>();

        // 材质级半径兜底：层未声明半径时沿用同材质内最大半径（两载体同口径）。
        var materialRadius = 0f;
        foreach (var layer in layers)
        {
            if (TryFloatParam(layer, "radius", out var radius) && radius > materialRadius) materialRadius = radius;
            if (TryRadiiParam(layer, out var radii) && radii.Max > materialRadius) materialRadius = radii.Max;
        }

        foreach (var layer in layers)
        {
            switch (layer.Primitive)
            {
                case "stroke":
                {
                    if (!TryParseWidths(layer, issueList, out var widths)) continue;
                    if (!TryParseColors(layer, issueList, out var colors)) continue;
                    var radii = ParseRadii(layer, materialRadius, issueList);
                    var chamfer = ParseChamfer(layer, issueList);
                    var align = Param(layer, "align");
                    strokes.Add(new BorderStroke(
                        layer.Z, widths, colors, radii, chamfer,
                        string.IsNullOrWhiteSpace(align) ? "outer" : align));
                    break;
                }

                case "shadow":
                {
                    if (!TryParseArgb(Param(layer, "value"), layer, issueList, out var argb)) continue;
                    var (offsetX, offsetY) = ParseOffset(layer);
                    glows.Add(new BorderGlow(
                        layer.Z, argb,
                        Math.Max(0, FloatParam(layer, "blur", 0)),
                        offsetX, offsetY,
                        Math.Max(0, FloatParam(layer, "spread", 0))));
                    break;
                }

                case "innerShadow":
                {
                    if (!TryParseArgb(Param(layer, "value"), layer, issueList, out var argb)) continue;
                    lights.Add(new BorderInnerLight(
                        layer.Z, argb,
                        Math.Max(0, FloatParam(layer, "width", 1)),
                        Sides(layer, "all"),
                        Clamp01(FloatParam(layer, "strength", 1))));
                    break;
                }

                case "specular":
                {
                    // 只有 edge/inner 形态进入内高光位；spot/default 由载体高光分支自行处理。
                    var mode = Param(layer, "mode");
                    if (!string.Equals(mode, "edge", StringComparison.Ordinal)
                        && !string.Equals(mode, "inner", StringComparison.Ordinal)) continue;
                    if (!TryParseArgb(Param(layer, "value"), layer, issueList, out var argb)) continue;
                    lights.Add(new BorderInnerLight(
                        layer.Z, argb,
                        Math.Max(0, FloatParam(layer, "width", 2)),
                        string.Equals(mode, "edge", StringComparison.Ordinal)
                            ? "top,left"
                            : Sides(layer, "all"),
                        Clamp01(FloatParam(layer, "strength", 0.65f))));
                    break;
                }
            }
        }

        strokes.Sort((left, right) => left.Z.CompareTo(right.Z));
        glows.Sort((left, right) => left.Z.CompareTo(right.Z));
        lights.Sort((left, right) => left.Z.CompareTo(right.Z));
        issues = issueList;
        return new BorderSpec(strokes, glows, lights);
    }

    private static bool TryParseWidths(BorderLayerInput layer, List<BorderIssue> issues, out BorderEdgeSet widths)
    {
        var shorthand = Param(layer, "widths");
        if (!string.IsNullOrWhiteSpace(shorthand))
        {
            if (BorderEdgeSet.TryParseShorthand(shorthand, out widths)) return true;
            issues.Add(new BorderIssue("border-width-invalid", shorthand));
            widths = BorderEdgeSet.Zero;
            return false;
        }

        var raw = Param(layer, "width");
        if (string.IsNullOrWhiteSpace(raw))
        {
            widths = BorderEdgeSet.Uniform(1f);
            return true;
        }

        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            || float.IsNaN(width) || float.IsInfinity(width) || width < 0)
        {
            issues.Add(new BorderIssue("border-width-invalid", raw));
            widths = BorderEdgeSet.Zero;
            return false;
        }

        widths = BorderEdgeSet.Uniform(width);
        return true;
    }

    private static bool TryParseColors(BorderLayerInput layer, List<BorderIssue> issues, out BorderColorSet colors)
    {
        var rawColors = Param(layer, "colors");
        if (!string.IsNullOrWhiteSpace(rawColors))
        {
            var parts = rawColors.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 4
                && SurfaceColor.TryParseColor(parts[0], out var left)
                && SurfaceColor.TryParseColor(parts[1], out var top)
                && SurfaceColor.TryParseColor(parts[2], out var right)
                && SurfaceColor.TryParseColor(parts[3], out var bottom))
            {
                colors = new BorderColorSet(left, top, right, bottom);
                return true;
            }

            issues.Add(new BorderIssue("border-color-invalid", rawColors));
            colors = BorderColorSet.Uniform(0);
            return false;
        }

        return TryParseSingleColor(Param(layer, "value"), layer, issues, out colors);
    }

    private static bool TryParseArgb(string raw, BorderLayerInput layer, List<BorderIssue> issues, out int argb)
    {
        if (SurfaceColor.TryParseColor(raw, out argb)) return true;
        issues.Add(new BorderIssue("border-color-invalid", raw.Length == 0 ? layer.Primitive : raw));
        argb = 0;
        return false;
    }

    private static bool TryParseSingleColor(
        string raw, BorderLayerInput layer, List<BorderIssue> issues, out BorderColorSet colors)
    {
        if (TryParseArgb(raw, layer, issues, out var argb))
        {
            colors = BorderColorSet.Uniform(argb);
            return true;
        }

        colors = BorderColorSet.Uniform(0);
        return false;
    }

    private static CornerRadii ParseRadii(BorderLayerInput layer, float materialRadius, List<BorderIssue> issues)
    {
        if (TryRadiiParam(layer, out var radii)) return radii;

        var raw = Param(layer, "radius");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return materialRadius > 0 ? CornerRadii.Uniform(materialRadius) : CornerRadii.Zero;
        }

        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius)
            || float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0)
        {
            issues.Add(new BorderIssue("border-radius-invalid", raw));
            return materialRadius > 0 ? CornerRadii.Uniform(materialRadius) : CornerRadii.Zero;
        }

        return CornerRadii.Uniform(radius);
    }

    private static CornerRadii ParseChamfer(BorderLayerInput layer, List<BorderIssue> issues)
    {
        var shorthand = Param(layer, "chamfers");
        if (!string.IsNullOrWhiteSpace(shorthand))
        {
            if (CornerRadii.TryParseShorthand(shorthand, out var chamfers)) return chamfers;
            issues.Add(new BorderIssue("border-chamfer-invalid", shorthand));
            return CornerRadii.Zero;
        }

        var raw = Param(layer, "chamfer");
        if (string.IsNullOrWhiteSpace(raw)) return CornerRadii.Zero;
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var chamfer)
            || float.IsNaN(chamfer) || float.IsInfinity(chamfer) || chamfer < 0)
        {
            issues.Add(new BorderIssue("border-chamfer-invalid", raw));
            return CornerRadii.Zero;
        }

        return CornerRadii.Uniform(chamfer);
    }

    private static bool TryRadiiParam(BorderLayerInput layer, out CornerRadii radii)
    {
        var shorthand = Param(layer, "radii");
        if (!string.IsNullOrWhiteSpace(shorthand) && CornerRadii.TryParseShorthand(shorthand, out radii)) return true;
        radii = CornerRadii.Zero;
        return false;
    }

    private static (float X, float Y) ParseOffset(BorderLayerInput layer)
    {
        var raw = Param(layer, "offset");
        if (string.IsNullOrWhiteSpace(raw)) return (0, 0);

        var parts = raw.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var x = parts.Length > 0 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
            ? px : 0;
        var y = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var py)
            ? py : 0;
        return (x, y);
    }

    private static bool TryFloatParam(BorderLayerInput layer, string name, out float value)
    {
        var raw = Param(layer, name);
        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value) && !float.IsInfinity(value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static float FloatParam(BorderLayerInput layer, string name, float fallback)
        => TryFloatParam(layer, name, out var value) ? value : fallback;

    private static string Param(BorderLayerInput layer, string name)
        => layer.Params.TryGetValue(name, out var value) ? value : string.Empty;

    private static string Sides(BorderLayerInput layer, string fallback)
        => Param(layer, "sides") is var sides && sides.Length > 0 ? sides : fallback;

    private static float Clamp01(float value) => Math.Min(1, Math.Max(0, value));
}
