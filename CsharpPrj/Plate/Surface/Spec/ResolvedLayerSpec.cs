#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend;

/// <summary>图元类型（解析期一次定型；未知图元保留原始名，由载体按「未实现」记账）。</summary>
public enum LayerPrimitive
{
    /// <summary>无法识别（保持原始 primitive 文本）。</summary>
    Unknown = 0,

    /// <summary>纯色填充。</summary>
    Fill,

    /// <summary>线性渐变。</summary>
    Gradient,

    /// <summary>描边（几何由 <see cref="BorderSpec"/> 归一化）。</summary>
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

/// <summary>层参数解析问题（稳定码 + 原始文本；禁静默回落）。</summary>
/// <param name="Code">稳定码（<c>param-missing</c> / <c>param-invalid</c> / <c>unresolved-value</c> / <c>color-invalid</c> / <c>border-*</c>）。</param>
/// <param name="Detail">原始文本 / 上下文。</param>
public readonly record struct LayerIssue(string Code, string Detail);

/// <summary>
/// 单层的**强类型参数模型**（两载体唯一解析入口，K1）。
/// <para>
/// 一次读取 <see cref="BorderLayerInput.Params"/> 成强类型；颜色只存打包 ARGB，几何只存浮点/DIP；
/// 缺省值全部在本类显式（<see cref="DefaultAngle"/> / <see cref="DefaultWidth"/> / <see cref="DefaultRadius"/> /
/// <see cref="CornerRadius"/>）；非法值产 <see cref="LayerIssue"/> 而不是运行期静默 fallback。
/// </para>
/// <para>
/// 边框类图元的几何归一化仍复用 <see cref="BorderSpec"/>（子模型），本类只把它映射成字段，
/// 因此 GDI+ / CSS 不可能各解一套。
/// </para>
/// </summary>
/// <param name="Kind">图元类型。</param>
/// <param name="Primitive">原始图元名（未知类型时保真）。</param>
/// <param name="Role">层角色。</param>
/// <param name="Z">层序。</param>
/// <param name="HasColor">是否解析到主色（<c>transparent</c> 也为真）。</param>
/// <param name="ColorArgb">主色（打包 ARGB）。</param>
/// <param name="Gradient">渐变色标（仅 <see cref="LayerPrimitive.Gradient"/>）。</param>
/// <param name="Angle">角度（度；gradient / specular）。</param>
/// <param name="Width">厚度（DIP；innerShadow / specular / stroke）。</param>
/// <param name="Radius">声明半径原值（DIP；0 = 未声明；specular 缺省 0.55 沿用既有语义）。</param>
/// <param name="RadiusDeclared">声明半径（DIP；0 = 未声明，供 <see cref="CornerRadius"/> 兜底）。</param>
/// <param name="Blur">模糊半径（DIP）。</param>
/// <param name="OffsetX">水平偏移（DIP）。</param>
/// <param name="OffsetY">垂直偏移（DIP）。</param>
/// <param name="Spread">阴影扩散（DIP）。</param>
/// <param name="Strength">强度（0..1）。</param>
/// <param name="Mode">高光形态（<c>band</c> / <c>edge</c> / <c>spot</c>）。</param>
/// <param name="Sides">受光边（内阴影 / 内高光）。</param>
/// <param name="Align">描边对齐语义（<c>outer</c> 缺省）。</param>
/// <param name="Border">边框子模型（单层归一化；非边框类为 <see cref="BorderSpec.Empty"/>）。</param>
/// <param name="Asset">资源相对路径（texture / decal）。</param>
/// <param name="Slice">九宫格切片（整数；0 = 不切）。</param>
/// <param name="SliceText">九宫格切片原文本（CSS <c>border-image-*</c> 需要原样）。</param>
/// <param name="Repeat">平铺语义原文本（空 = 由载体取各自缺省）。</param>
/// <param name="NoRepeat">是否声明 <c>no-repeat</c>。</param>
/// <param name="Anchor">贴图锚点（CSS <c>background-position</c>）。</param>
/// <param name="Size">贴图尺寸（CSS <c>background-size</c>）。</param>
public sealed record ResolvedLayerSpec(
    LayerPrimitive Kind,
    string Primitive,
    string Role,
    int Z,
    bool HasColor,
    int ColorArgb,
    GradientSpec Gradient,
    float Angle,
    float Width,
    float Radius,
    float RadiusDeclared,
    float Blur,
    float OffsetX,
    float OffsetY,
    float Spread,
    float Strength,
    string Mode,
    string Sides,
    string Align,
    BorderSpec Border,
    string Asset,
    int Slice,
    string SliceText,
    string Repeat,
    bool NoRepeat,
    string Anchor,
    string Size)
{
    /// <summary>主色 alpha（0..255）。</summary>
    public int Alpha => (ColorArgb >> 24) & 0xFF;

    /// <summary>生效圆角：声明值优先，未声明沿用材质级最大半径（与 <see cref="BorderSpec"/> 同口径）。</summary>
    /// <param name="materialRadius">材质级最大半径（DIP）。</param>
    public float CornerRadius(float materialRadius)
        => RadiusDeclared > 0 ? RadiusDeclared : materialRadius;

    /// <summary>解析一层（一次成型；非法/缺失值落 issues，缺省值取本类常量）。</summary>
    /// <param name="layer">材质层最小只读视图。</param>
    /// <param name="issues">产出问题（无则空）。</param>
    public static ResolvedLayerSpec Parse(BorderLayerInput layer, out IReadOnlyList<LayerIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(layer);

        var list = new List<LayerIssue>();
        var parameters = layer.Params;
        var role = layer.Role;
        var kind = KindOf(layer.Primitive);
        var isBorder = kind is LayerPrimitive.Stroke or LayerPrimitive.Shadow
            or LayerPrimitive.InnerShadow or LayerPrimitive.Specular;

        var border = BorderSpec.Empty;
        if (isBorder)
        {
            border = BorderSpec.FromLayers(new[] { layer }, out var borderIssues);
            foreach (var issue in borderIssues) list.Add(new LayerIssue(issue.Code, issue.Detail));
        }

        var hasColor = false;
        var colorArgb = 0;
        var gradient = GradientSpec.Empty;
        switch (kind)
        {
            case LayerPrimitive.Gradient:
                gradient = GradientSpec.Parse(LayerParams.Read(parameters, "value"), role, list);
                hasColor = gradient.Stops.Count > 0;
                colorArgb = hasColor ? gradient.Stops[0].Argb : 0;
                break;

            case LayerPrimitive.Texture:
            case LayerPrimitive.Decal:
                // 贴图/贴画用 asset，不参与色值判定。
                break;

            case LayerPrimitive.Stroke:
            case LayerPrimitive.Shadow:
            case LayerPrimitive.InnerShadow:
            case LayerPrimitive.Specular:
                // 主色由边框子模型给出（避免与 BorderSpec 的 border-color-invalid 重复登记）。
                break;

            default:
                colorArgb = ParseColor(parameters, "value", role, list, out hasColor);
                break;
        }

        var angle = DefaultAngle(kind);
        var radius = DefaultRadius(kind);
        var width = DefaultWidth(kind);
        var blur = 0f;
        var offsetX = 0f;
        var offsetY = 0f;
        var spread = 0f;
        var strength = 0.7f;
        var mode = ParseMode(parameters);
        var sides = LayerParams.Read(parameters, "sides");
        var align = LayerParams.Read(parameters, "align");

        if (kind is not LayerPrimitive.Stroke)
        {
            // 与既有 ParseBorderKind 对齐：angle / radius 逐参登记；其余数值对边框类由 BorderSpec 静默归一。
            angle = ParseFloat(parameters, "angle", angle, role, list);
            radius = ParseFloat(parameters, "radius", radius, role, list);
            if (!isBorder)
            {
                width = ParseFloat(parameters, "width", width, role, list);
                blur = ParseFloat(parameters, "blur", 0f, role, list);
                spread = ParseFloat(parameters, "spread", 0f, role, list);
                strength = ParseFloat(parameters, "strength", 0.7f, role, list);
            }
        }

        switch (kind)
        {
            case LayerPrimitive.Stroke:
            {
                var stroke = border.TopStroke;
                if (stroke is not null)
                {
                    colorArgb = stroke.Colors.Top;
                    hasColor = true;
                    width = stroke.Widths.Top;
                    radius = stroke.Radii.TopLeft;
                    align = stroke.Align;
                }

                break;
            }

            case LayerPrimitive.Shadow:
                if (border.Glows.Count > 0)
                {
                    var glow = border.Glows[0];
                    colorArgb = glow.Argb;
                    hasColor = true;
                    blur = glow.Blur;
                    offsetX = glow.OffsetX;
                    offsetY = glow.OffsetY;
                    spread = glow.Spread;
                    strength = 1f;
                }

                break;

            case LayerPrimitive.InnerShadow:
                if (border.InnerLights.Count > 0)
                {
                    var light = border.InnerLights[0];
                    colorArgb = light.Argb;
                    hasColor = true;
                    width = light.Width;
                    strength = light.Strength;
                    blur = light.Blur;
                    offsetX = light.OffsetX;
                    offsetY = light.OffsetY;
                    mode = light.Mode;
                    sides = light.Sides;
                }

                break;

            case LayerPrimitive.Specular:
                if (border.InnerLights.Count > 0)
                {
                    var light = border.InnerLights[0];
                    colorArgb = light.Argb;
                    hasColor = true;
                    width = light.Width;
                    strength = light.Strength;
                    mode = light.Mode;
                    sides = light.Sides;
                }
                else if (border.Sheens.Count > 0)
                {
                    var sheen = border.Sheens[0];
                    colorArgb = sheen.Argb;
                    hasColor = true;
                    width = sheen.Width;
                    strength = sheen.Strength;
                    mode = sheen.Mode;
                    angle = sheen.Angle;
                }

                break;
        }

        var asset = LayerParams.Read(parameters, "asset");
        var sliceText = LayerParams.Read(parameters, "slice");
        var slice = isBorder ? 0 : (int)Math.Max(0, ParseFloat(parameters, "slice", 0f, role, list));
        var repeat = LayerParams.Read(parameters, "repeat");

        if (kind is LayerPrimitive.Texture or LayerPrimitive.Decal && asset.Length == 0)
        {
            list.Add(new LayerIssue("param-missing", role + ":asset"));
        }

        issues = list;
        return new ResolvedLayerSpec(
            kind, layer.Primitive, role, layer.Z, hasColor, colorArgb, gradient,
            angle, width, radius, LayerParams.Float(parameters, "radius", 0f),
            blur, offsetX, offsetY, spread, strength, mode, sides, align, border,
            asset, slice, sliceText, repeat,
            string.Equals(repeat, "no-repeat", StringComparison.Ordinal),
            LayerParams.Read(parameters, "anchor"),
            LayerParams.Read(parameters, "size"));
    }

    /// <summary>图元 → 类型（未知保留 <see cref="LayerPrimitive.Unknown"/>）。</summary>
    /// <param name="primitive">图元名。</param>
    public static LayerPrimitive KindOf(string primitive) => primitive switch
    {
        "fill" => LayerPrimitive.Fill,
        "gradient" => LayerPrimitive.Gradient,
        "stroke" => LayerPrimitive.Stroke,
        "shadow" => LayerPrimitive.Shadow,
        "innerShadow" => LayerPrimitive.InnerShadow,
        "backdrop" => LayerPrimitive.Backdrop,
        "specular" => LayerPrimitive.Specular,
        "texture" => LayerPrimitive.Texture,
        "decal" => LayerPrimitive.Decal,
        "mask" => LayerPrimitive.Mask,
        "noise" => LayerPrimitive.Noise,
        _ => LayerPrimitive.Unknown,
    };

    private static int ParseColor(
        IReadOnlyDictionary<string, string> parameters, string name, string role,
        List<LayerIssue> issues, out bool hasColor)
    {
        hasColor = false;
        var raw = LayerParams.Read(parameters, name);
        if (raw.Length == 0)
        {
            issues.Add(new LayerIssue("param-missing", role + ":" + name));
            return 0;
        }

        if (SurfaceColor.TryParseColor(raw, out var argb))
        {
            hasColor = true;
            return argb;
        }

        // 沿用既有 `unresolved-value` 记账口径（断言/日志按此前缀判定，禁改名造成假绿）。
        issues.Add(new LayerIssue("unresolved-value", raw));
        return 0;
    }

    private static float ParseFloat(
        IReadOnlyDictionary<string, string> parameters, string name, float fallback,
        string role, List<LayerIssue> issues)
    {
        var raw = LayerParams.Read(parameters, name);
        if (raw.Length == 0) return fallback;
        if (LayerParams.TryFloat(raw, out var value)) return value;

        issues.Add(new LayerIssue("param-invalid", role + ":" + name + "=" + raw));
        return fallback;
    }

    private static string ParseMode(IReadOnlyDictionary<string, string> parameters)
        => LayerParams.Read(parameters, "mode") switch
        {
            "edge" => "edge",
            "spot" => "spot",
            _ => "band",
        };

    private static float DefaultAngle(LayerPrimitive kind)
        => kind == LayerPrimitive.Specular ? 45f : 180f;

    private static float DefaultWidth(LayerPrimitive kind) => kind switch
    {
        LayerPrimitive.InnerShadow => 1f,
        LayerPrimitive.Specular => 2f,
        _ => 0f,
    };

    private static float DefaultRadius(LayerPrimitive kind)
        => kind == LayerPrimitive.Specular ? 0.55f : 0f;
}
