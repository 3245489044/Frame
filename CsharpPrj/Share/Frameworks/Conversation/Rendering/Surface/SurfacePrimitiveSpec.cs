#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>图元类型（解析期一次定型；未知图元保留原始名，由载体按「未实现」记账）。</summary>
public enum SurfacePrimitiveKind
{
    /// <summary>无法识别（保持原始 primitive 文本）。</summary>
    Unknown = 0,

    /// <summary>纯色填充。</summary>
    Fill,

    /// <summary>线性渐变。</summary>
    Gradient,

    /// <summary>描边（几何另行经 <c>BorderSpec</c> 归一化）。</summary>
    Stroke,

    /// <summary>外阴影。</summary>
    Shadow,

    /// <summary>内阴影。</summary>
    InnerShadow,

    /// <summary>背景模糊/采样。</summary>
    Backdrop,

    /// <summary>高光（唯一动态层）。</summary>
    Specular,

    /// <summary>纹理贴图。</summary>
    Texture,

    /// <summary>贴画。</summary>
    Decal,

    /// <summary>遮罩。</summary>
    Mask,

    /// <summary>噪点。</summary>
    Noise,
}

/// <summary>
/// 类型化图元参数（K1）：把 <see cref="ResolvedLayer.Params"/> 字符串袋在**一次解析**里读成强类型，
/// 非法值产出 <see cref="SurfaceIssue"/> 而不是运行期静默 fallback。
/// <para>颜色一律是打包 ARGB（0xAARRGGBB），解析经 <see cref="SurfaceColor"/>；本类 BCL-only，不含 GDI/CSS 方言。</para>
/// <para>字段按图元复用（未用到的字段为默认值）：<see cref="GradientStops"/> 仅 Gradient；<see cref="Mode"/> 仅 Specular。</para>
/// </summary>
/// <param name="Kind">图元类型。</param>
/// <param name="Primitive">原始图元名（未知类型时保真）。</param>
/// <param name="ColorArgb">主色（打包 ARGB；无法解析 ⇒ 0 且 Issues 有记录）。</param>
/// <param name="GradientStops">渐变色标（≥1；仅 Gradient）。</param>
/// <param name="Angle">角度（度；gradient / specular）。</param>
/// <param name="Width">宽度（DIP；stroke / innerShadow / specular-edge）。</param>
/// <param name="Radius">圆角半径（DIP；几何层共用）或 specular-spot 的半径比例。</param>
/// <param name="Blur">模糊半径（DIP）。</param>
/// <param name="OffsetX">水平偏移（DIP）。</param>
/// <param name="OffsetY">垂直偏移（DIP）。</param>
/// <param name="Spread">阴影扩散（DIP）。</param>
/// <param name="Strength">强度（0..1；specular）。</param>
/// <param name="Mode">高光模式（band / edge / spot）。</param>
/// <param name="Asset">资源相对路径（texture / decal）。</param>
/// <param name="Slice">九宫格切片（设备像素；0 = 不切）。</param>
/// <param name="NoRepeat">是否禁止平铺。</param>
public sealed record SurfacePrimitiveSpec(
    SurfacePrimitiveKind Kind,
    string Primitive,
    int ColorArgb,
    IReadOnlyList<int> GradientStops,
    float Angle,
    float Width,
    float Radius,
    float Blur,
    float OffsetX,
    float OffsetY,
    float Spread,
    float Strength,
    string Mode,
    string Asset,
    int Slice,
    bool NoRepeat)
{
    /// <summary>主色 alpha（0..255）。</summary>
    public int Alpha => (ColorArgb >> 24) & 0xFF;

    /// <summary>解析一层（一次成型；非法/缺失值落 Issues，缺省值取模型常量）。</summary>
    /// <param name="layer">已解析层。</param>
    /// <param name="issues">产出问题（无则空）。</param>
    public static SurfacePrimitiveSpec Parse(ResolvedLayer layer, out IReadOnlyList<SurfaceIssue> issues)
    {
        var list = new List<SurfaceIssue>();
        var kind = KindOf(layer.Primitive);
        var role = layer.Role;

        var stops = kind == SurfacePrimitiveKind.Gradient
            ? ParseStops(layer, list)
            : Array.Empty<int>();
        // 渐变的 value 是「色标列表」而不是单色；贴图/贴画用 asset，不参与色值判定。
        var colorArgb = kind switch
        {
            SurfacePrimitiveKind.Gradient => stops.Count > 0 ? stops[0] : 0,
            SurfacePrimitiveKind.Texture or SurfacePrimitiveKind.Decal => 0,
            _ => ParseColor(layer, "value", list),
        };

        var angle = ParseFloat(layer, "angle", DefaultAngle(kind), list);
        var width = ParseFloat(layer, "width", DefaultWidth(kind), list);
        var radius = ParseFloat(layer, "radius", DefaultRadius(kind), list);
        var blur = ParseFloat(layer, "blur", 0f, list);
        var (offsetX, offsetY) = ParseOffset(layer, kind, list);
        var spread = ParseFloat(layer, "spread", 0f, list);
        var strength = ParseFloat(layer, "strength", 0.7f, list);
        var mode = ParseMode(layer);
        var asset = Param(layer, "asset");
        var slice = (int)Math.Max(0, ParseFloat(layer, "slice", 0f, list));
        var noRepeat = string.Equals(Param(layer, "repeat"), "no-repeat", StringComparison.Ordinal);

        if (kind is SurfacePrimitiveKind.Texture or SurfacePrimitiveKind.Decal && asset.Length == 0)
        {
            list.Add(new SurfaceIssue("param-missing", role + ":asset"));
        }

        issues = list;
        return new SurfacePrimitiveSpec(
            kind, layer.Primitive, colorArgb, stops, angle, width, radius, blur,
            offsetX, offsetY, spread, strength, mode, asset, slice, noRepeat);
    }

    /// <summary>图元 → 类型（未知保留 Unknown）。</summary>
    /// <param name="primitive">图元名。</param>
    public static SurfacePrimitiveKind KindOf(string primitive) => primitive switch
    {
        "fill" => SurfacePrimitiveKind.Fill,
        "gradient" => SurfacePrimitiveKind.Gradient,
        "stroke" => SurfacePrimitiveKind.Stroke,
        "shadow" => SurfacePrimitiveKind.Shadow,
        "innerShadow" => SurfacePrimitiveKind.InnerShadow,
        "backdrop" => SurfacePrimitiveKind.Backdrop,
        "specular" => SurfacePrimitiveKind.Specular,
        "texture" => SurfacePrimitiveKind.Texture,
        "decal" => SurfacePrimitiveKind.Decal,
        "mask" => SurfacePrimitiveKind.Mask,
        "noise" => SurfacePrimitiveKind.Noise,
        _ => SurfacePrimitiveKind.Unknown,
    };

    private static int ParseColor(ResolvedLayer layer, string name, List<SurfaceIssue> issues)
    {
        var raw = Param(layer, name);
        if (raw.Length == 0)
        {
            issues.Add(new SurfaceIssue("param-missing", layer.Role + ":" + name));
            return 0;
        }

        if (SurfaceColor.TryParseColor(raw, out var argb)) return argb;
        // 统一沿用既有 `unresolved-value` 记账口径（断言/日志按此前缀判定，禁改名造成假绿）。
        issues.Add(new SurfaceIssue("unresolved-value", raw));
        return 0;
    }

    private static IReadOnlyList<int> ParseStops(ResolvedLayer layer, List<SurfaceIssue> issues)
    {
        var raw = Param(layer, "value");
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            issues.Add(new SurfaceIssue("param-missing", layer.Role + ":value=<empty>"));
            return Array.Empty<int>();
        }

        var stops = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!SurfaceColor.TryParseColor(parts[i], out stops[i]))
            {
                issues.Add(new SurfaceIssue("color-invalid", layer.Role + ":value[" + i + "]=" + parts[i]));
                return Array.Empty<int>();
            }
        }

        return stops;
    }

    private static float ParseFloat(ResolvedLayer layer, string name, float fallback, List<SurfaceIssue> issues)
    {
        var raw = Param(layer, name);
        if (raw.Length == 0) return fallback;
        if (TryNumber(raw, out var number)) return number;
        issues.Add(new SurfaceIssue("param-invalid", layer.Role + ":" + name + "=" + raw));
        return fallback;
    }

    private static (float X, float Y) ParseOffset(
        ResolvedLayer layer, SurfacePrimitiveKind kind, List<SurfaceIssue> issues)
    {
        if (kind is not (SurfacePrimitiveKind.Shadow or SurfacePrimitiveKind.InnerShadow)) return (0f, 0f);
        var raw = Param(layer, "offset");
        if (raw.Length == 0) return (0f, DefaultOffsetY);

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (0f, DefaultOffsetY);

        if (!TryNumber(parts[0], out var x))
        {
            issues.Add(new SurfaceIssue("param-invalid", layer.Role + ":offset=" + raw));
            return (0f, DefaultOffsetY);
        }

        if (parts.Length < 2) return (x, DefaultOffsetY);
        if (!TryNumber(parts[1], out var y))
        {
            issues.Add(new SurfaceIssue("param-invalid", layer.Role + ":offset=" + raw));
            return (x, DefaultOffsetY);
        }

        return (x, y);
    }

    private static string ParseMode(ResolvedLayer layer) => Param(layer, "mode") switch
    {
        "edge" => "edge",
        "spot" => "spot",
        _ => "band",
    };

    private static bool TryNumber(string text, out float value)
    {
        var trimmed = text.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^2];
        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string Param(ResolvedLayer layer, string name)
        => layer.Params.TryGetValue(name, out var value) ? value : string.Empty;

    private static float DefaultAngle(SurfacePrimitiveKind kind)
        => kind == SurfacePrimitiveKind.Specular ? 45f : 180f;

    private static float DefaultWidth(SurfacePrimitiveKind kind) => kind switch
    {
        SurfacePrimitiveKind.InnerShadow => 1f,
        SurfacePrimitiveKind.Specular => 2f,
        _ => 0f,
    };

    private static float DefaultRadius(SurfacePrimitiveKind kind)
        => kind == SurfacePrimitiveKind.Specular ? 0.55f : 0f;

    private const float DefaultOffsetY = 2f;
}
