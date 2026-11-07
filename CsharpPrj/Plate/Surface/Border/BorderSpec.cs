#nullable enable

using System;
using System.Collections.Generic;

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
/// <param name="Z">层序。</param>
/// <param name="Argb">颜色（ARGB）。</param>
/// <param name="Width">环宽 / 高光厚度（DIP）。</param>
/// <param name="Sides">受光边（载体无法逐边表达时显式降级记账）。</param>
/// <param name="Strength">强度（0..1）。</param>
/// <param name="Blur">模糊半径（DIP；innerShadow）。</param>
/// <param name="OffsetX">水平偏移（DIP；innerShadow）。</param>
/// <param name="OffsetY">垂直偏移（DIP；innerShadow）。</param>
/// <param name="Mode">形态（<c>inner</c> / <c>edge</c>）。</param>
public sealed record BorderInnerLight(
    int Z, int Argb, float Width, string Sides, float Strength,
    float Blur = 0f, float OffsetX = 0f, float OffsetY = 0f, string Mode = "inner");

/// <summary>
/// 光泽（specular 的 <c>spot</c>/<c>band</c> 形态；<c>edge</c>/<c>inner</c> 已并入 <see cref="BorderInnerLight"/>）。
/// <para>两载体都从这里取角度/强度/颜色：CSS 投影成 radial/linear-gradient，GDI+ 投影成聚光/亮带。</para>
/// </summary>
/// <param name="Z">层序。</param>
/// <param name="Argb">颜色（ARGB）。</param>
/// <param name="Mode"><c>spot</c> / <c>band</c>。</param>
/// <param name="Width">厚度（DIP；band）。</param>
/// <param name="Strength">强度（0..1）。</param>
/// <param name="Angle">角度（度）。</param>
public sealed record BorderSheen(int Z, int Argb, string Mode, float Width, float Strength, float Angle)
{
    /// <summary>spectral 色相跨度（度；0 ⇒ 非光谱）。由材质 <c>spectrum</c> 归一化。</summary>
    public float SpectralSpan { get; init; }
}

/// <summary>模型输入：材质层的最小只读视图（Share 适配层只做搬运，不解释语义）。</summary>
public sealed record BorderLayerInput(string Role, string Primitive, int Z, IReadOnlyDictionary<string, string> Params);

/// <summary>
/// 边框单一模型（载体无关）：从现有材质层归一化而来，不拥有层、不替代 <c>ResolvedMaterial</c>。
/// <para>本程序集 BCL-only：颜色只存打包 ARGB，几何只存浮点/DIP；GDI+ 与 CSS 各自投影。</para>
/// </summary>
public sealed record BorderSpec(
    IReadOnlyList<BorderStroke> Strokes, IReadOnlyList<BorderGlow> Glows,
    IReadOnlyList<BorderInnerLight> InnerLights, IReadOnlyList<BorderSheen> Sheens)
{
    /// <summary>空模型。</summary>
    public static readonly BorderSpec Empty = new(
        Array.Empty<BorderStroke>(), Array.Empty<BorderGlow>(),
        Array.Empty<BorderInnerLight>(), Array.Empty<BorderSheen>());

    /// <summary>是否没有任何边框元素。</summary>
    public bool IsEmpty =>
        Strokes.Count == 0 && Glows.Count == 0 && InnerLights.Count == 0 && Sheens.Count == 0;

    /// <summary>最上层描边（按 Z 取最大；两载体投影器与遮挡判定唯一读取点）。</summary>
    public BorderStroke? TopStroke
    {
        get
        {
            BorderStroke? best = null;
            foreach (var stroke in Strokes)
            {
                if (best is null || stroke.Z >= best.Z) best = stroke;
            }

            return best;
        }
    }

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
        var sheens = new List<BorderSheen>();

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
                    var align = LayerParams.Read(layer.Params, "align");
                    strokes.Add(new BorderStroke(
                        layer.Z, widths, colors, radii, chamfer,
                        string.IsNullOrWhiteSpace(align) ? "outer" : align));
                    break;
                }

                case "shadow":
                {
                    if (!TryParseArgb(LayerParams.Read(layer.Params, "value"), layer, issueList, out var argb)) continue;
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
                    if (!TryParseArgb(LayerParams.Read(layer.Params, "value"), layer, issueList, out var argb)) continue;
                    var (offsetX, offsetY) = ParseOffset(layer);
                    lights.Add(new BorderInnerLight(
                        layer.Z, argb,
                        Math.Max(0, FloatParam(layer, "width", 1)),
                        Sides(layer, "all"),
                        Clamp01(FloatParam(layer, "strength", 1)),
                        Math.Max(0, FloatParam(layer, "blur", 0)),
                        offsetX, offsetY));
                    break;
                }

                case "specular":
                {
                    if (!TryParseArgb(LayerParams.Read(layer.Params, "value"), layer, issueList, out var argb)) continue;
                    var mode = LayerParams.Read(layer.Params, "mode");
                    if (string.Equals(mode, "edge", StringComparison.Ordinal)
                        || string.Equals(mode, "inner", StringComparison.Ordinal))
                    {
                        lights.Add(new BorderInnerLight(
                            layer.Z, argb,
                            Math.Max(0, FloatParam(layer, "width", 2)),
                            string.Equals(mode, "edge", StringComparison.Ordinal) ? "top,left" : Sides(layer, "all"),
                            Clamp01(FloatParam(layer, "strength", 0.65f)), 0f, 0f, 0f, mode));
                        break;
                    }

                    // spot / band / spectral：把原先散落在两载体的角度/强度/色相跨度归一到这里。
                    var spectral = string.Equals(mode, "spectral", StringComparison.Ordinal);
                    var spectrum = Math.Clamp(FloatParam(layer, "spectrum", 140), 0, 360);
                    sheens.Add(new BorderSheen(
                        layer.Z, argb,
                        string.Equals(mode, "spot", StringComparison.Ordinal)
                            ? "spot"
                            : spectral ? "spectral" : "band",
                        Math.Max(0, FloatParam(layer, "width", 2)),
                        Clamp01(FloatParam(layer, "strength", 0.65f)),
                        FloatParam(layer, "angle", 45))
                    {
                        SpectralSpan = spectral ? (spectrum > 0 ? spectrum : 140) : 0f,
                    });
                    break;
                }
            }
        }

        strokes.Sort((left, right) => left.Z.CompareTo(right.Z));
        glows.Sort((left, right) => left.Z.CompareTo(right.Z));
        lights.Sort((left, right) => left.Z.CompareTo(right.Z));
        sheens.Sort((left, right) => left.Z.CompareTo(right.Z));
        issues = issueList;
        return new BorderSpec(strokes, glows, lights, sheens);
    }

    private static bool TryParseWidths(BorderLayerInput layer, List<BorderIssue> issues, out BorderEdgeSet widths)
    {
        var shorthand = LayerParams.Read(layer.Params, "widths");
        if (!string.IsNullOrWhiteSpace(shorthand))
        {
            if (BorderEdgeSet.TryParseShorthand(shorthand, out widths)) return true;
            issues.Add(new BorderIssue("border-width-invalid", shorthand));
            widths = BorderEdgeSet.Zero;
            return false;
        }

        var raw = LayerParams.Read(layer.Params, "width");
        if (string.IsNullOrWhiteSpace(raw))
        {
            widths = BorderEdgeSet.Uniform(1f);
            return true;
        }

        if (!LayerParams.TryFloat(raw, out var width) || width < 0)
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
        var rawColors = LayerParams.Read(layer.Params, "colors");
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

        return TryParseSingleColor(LayerParams.Read(layer.Params, "value"), layer, issues, out colors);
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

        var raw = LayerParams.Read(layer.Params, "radius");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return materialRadius > 0 ? CornerRadii.Uniform(materialRadius) : CornerRadii.Zero;
        }

        if (!LayerParams.TryFloat(raw, out var radius) || radius < 0)
        {
            issues.Add(new BorderIssue("border-radius-invalid", raw));
            return materialRadius > 0 ? CornerRadii.Uniform(materialRadius) : CornerRadii.Zero;
        }

        return CornerRadii.Uniform(radius);
    }

    private static CornerRadii ParseChamfer(BorderLayerInput layer, List<BorderIssue> issues)
    {
        var shorthand = LayerParams.Read(layer.Params, "chamfers");
        if (!string.IsNullOrWhiteSpace(shorthand))
        {
            if (CornerRadii.TryParseShorthand(shorthand, out var chamfers)) return chamfers;
            issues.Add(new BorderIssue("border-chamfer-invalid", shorthand));
            return CornerRadii.Zero;
        }

        var raw = LayerParams.Read(layer.Params, "chamfer");
        if (string.IsNullOrWhiteSpace(raw)) return CornerRadii.Zero;
        if (!LayerParams.TryFloat(raw, out var chamfer) || chamfer < 0)
        {
            issues.Add(new BorderIssue("border-chamfer-invalid", raw));
            return CornerRadii.Zero;
        }

        return CornerRadii.Uniform(chamfer);
    }

    private static bool TryRadiiParam(BorderLayerInput layer, out CornerRadii radii)
    {
        var shorthand = LayerParams.Read(layer.Params, "radii");
        if (!string.IsNullOrWhiteSpace(shorthand) && CornerRadii.TryParseShorthand(shorthand, out radii)) return true;
        radii = CornerRadii.Zero;
        return false;
    }

    private static (float X, float Y) ParseOffset(BorderLayerInput layer)
    {
        var raw = LayerParams.Read(layer.Params, "offset");
        if (string.IsNullOrWhiteSpace(raw)) return (0, 0);

        var parts = raw.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var x = parts.Length > 0 && LayerParams.TryFloat(parts[0], out var px) ? px : 0;
        var y = parts.Length > 1 && LayerParams.TryFloat(parts[1], out var py) ? py : 0;
        return (x, y);
    }

    private static bool TryFloatParam(BorderLayerInput layer, string name, out float value)
    {
        var raw = LayerParams.Read(layer.Params, name);
        if (LayerParams.TryFloat(raw, out value)) return true;

        value = 0;
        return false;
    }

    private static float FloatParam(BorderLayerInput layer, string name, float fallback)
        => TryFloatParam(layer, name, out var value) ? value : fallback;

    private static string Sides(BorderLayerInput layer, string fallback)
        => LayerParams.Read(layer.Params, "sides") is var sides && sides.Length > 0 ? sides : fallback;

    private static float Clamp01(float value) => Math.Min(1, Math.Max(0, value));
}
