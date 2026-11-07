#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Share.Conversation.Rendering.Surface;
using UiBackend;
using UiBackend.Css;

namespace FrontEndDriven.HtmlViewerBridge.Surface;

/// <summary>CSS 投影产物（文本 + 未落地图元清单）。</summary>
/// <param name="Css">样式文本。</param>
/// <param name="Skipped">未投影的图元（可见清单，禁静默丢弃）。</param>
public sealed record SurfaceStyleSheet(string Css, IReadOnlyList<string> Skipped);

/// <summary>
/// 表面 → CSS 投影器（**唯一材质类生成点**）：消费 <see cref="ResolvedSurface"/>，产出 <c>.material-*</c> 规则。
/// <para>
/// 值不在此出现（token 已在解析期换值）；图元 → CSS 属性映射是**方言落点**（`Operation` 层）。
/// 同一材质的 box-shadow / background-image 必须**合并成一条声明**，不再后写覆盖前写。
/// </para>
/// <para>
/// 边框宽度/颜色与 GDI+ 共用 <see cref="BorderSpec"/>（<see cref="BorderSpecProjection"/>）：
/// 单条 border 是 CSS 硬限制，多描边显式进 <see cref="SurfaceStyleSheet.Skipped"/>。
/// </para>
/// </summary>
public static class CssSurfaceProjector
{
    /// <summary>材质类前缀（DOM 上以 <c>material-&lt;id&gt;</c> 出现）。</summary>
    public const string MaterialClassPrefix = "material-";

    /// <summary>材质 id → CSS 类名（下划线保留，非法字符滤除）。</summary>
    /// <param name="materialId">材质 id。</param>
    public static string ClassName(string materialId)
    {
        var builder = new StringBuilder(MaterialClassPrefix);
        foreach (var ch in materialId ?? string.Empty)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_') builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>按已解析表面投影材质类。</summary>
    /// <param name="surface">解析产物。</param>
    public static SurfaceStyleSheet Project(ResolvedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var builder = new CssDocumentBuilder();
        var skipped = new List<string>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var container in surface.Containers)
        {
            foreach (var material in container.Materials.Values)
            {
                if (material.Layers.Count == 0) continue;
                var className = ClassName(material.FallbackUsed ?? material.MaterialId);
                if (!emitted.Add(className)) continue;

                builder.Rule("." + className, MaterialToCss(material, skipped));
            }
        }

        return new SurfaceStyleSheet(builder.Build(), skipped.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>单个材质 → 声明列表（阴影/背景多值合并；边框走 BorderSpec）。</summary>
    private static List<KeyValuePair<string, string>> MaterialToCss(
        ResolvedMaterial material, List<string> skipped)
    {
        var declarations = new List<KeyValuePair<string, string>>();
        var backgrounds = new List<string>();
        var shadows = new List<string>();
        var extra = new List<KeyValuePair<string, string>>();

        string backgroundColor = string.Empty;
        string? backdropFilter = null;

        foreach (var layer in material.Layers)
        {
            var value = Param(layer, "value", string.Empty);
            switch (layer.Primitive)
            {
                case "fill":
                    backgroundColor = value;
                    break;

                case "gradient":
                    backgrounds.Add($"linear-gradient({Deg(layer)}deg, {ReplaceCommas(value, ", ")})");
                    break;

                case "specular":
                {
                    // 尊重 mode：edge = 内高光（inset shadow）；spot = 径向光斑；缺省保留旧对角带。
                    var mode = Param(layer, "mode", string.Empty);
                    if (string.Equals(mode, "edge", StringComparison.Ordinal))
                    {
                        shadows.Add($"inset 0 {Px(layer, "width", "2")} 0 {value}");
                    }
                    else if (string.Equals(mode, "spot", StringComparison.Ordinal))
                    {
                        var (x, y) = SpotCenter(layer);
                        backgrounds.Add(
                            $"radial-gradient(circle at {x}% {y}%, {value} 0%, transparent 70%)");
                    }
                    else
                    {
                        backgrounds.Add(
                            $"linear-gradient({Deg(layer)}deg, transparent 40%, {value} 50%, transparent 60%)");
                    }

                    break;
                }

                case "shadow":
                    shadows.Add($"{Offset(layer)} {Px(layer, "blur", "0")} {value}");
                    break;

                case "innerShadow":
                    shadows.Add($"inset {Offset(layer)} {Px(layer, "blur", "0")} {value}");
                    break;

                case "backdrop":
                    backdropFilter = $"blur({Px(layer, "blur", "0")})";
                    break;

                case "stroke":
                    // 几何/颜色统一在循环后由 BorderSpec + 原始层文本输出（这里只标记已投影）。
                    break;

                // M6：贴图 / 贴画（资源缺失 ⇒ 无 asset ⇒ 进 Skipped，禁静默纯色）
                case "texture":
                {
                    var asset = Asset(layer);
                    if (asset.Length == 0) break;
                    backgrounds.Add($"url(\"{asset}\")");
                    extra.Add(Pair("background-repeat", Param(layer, "repeat", "repeat")));
                    if (layer.Params.TryGetValue("anchor", out var anchor) && anchor.Length > 0)
                        extra.Add(Pair("background-position", anchor));
                    foreach (var pair in NineSlice(layer, asset)) extra.Add(pair);
                    break;
                }

                case "decal":
                {
                    var asset = Asset(layer);
                    if (asset.Length == 0) break;
                    backgrounds.Add($"url(\"{asset}\")");
                    extra.Add(Pair("background-repeat", "no-repeat"));
                    extra.Add(Pair("background-position", Param(layer, "anchor", "center")));
                    extra.Add(Pair("background-size", Param(layer, "size", "contain")));
                    foreach (var pair in NineSlice(layer, asset)) extra.Add(pair);
                    break;
                }
            }
        }

        // 边框：同一模型（BorderSpec）取最大 z 的描边；多描边因 CSS 单 border 限制显式记账。
        var spec = BorderSpecProjection.From(material);
        var primary = PrimaryStroke(material);
        if (primary is not null)
        {
            if (spec.Strokes.Count > 1)
            {
                skipped.Add("border-multi-stroke-emulated:" + spec.Strokes.Count);
            }

            AddBorder(declarations, primary, spec);
        }

        if (backgroundColor.Length > 0) declarations.Add(Pair("background-color", backgroundColor));
        if (backgrounds.Count > 0) declarations.Add(Pair("background-image", string.Join(", ", backgrounds)));
        foreach (var pair in extra) declarations.Add(pair);
        if (shadows.Count > 0) declarations.Add(Pair("box-shadow", string.Join(", ", shadows)));
        if (backdropFilter is not null) declarations.Add(Pair("backdrop-filter", backdropFilter));
        // border-box：外沿与 GDI+ 的 bounds 同锚点（width/height 含边框，不再向外撑大）。
        declarations.Add(Pair("box-sizing", "border-box"));

        // 未投影图元可见化（texture/decal 缺件、noise/mask 等）。
        foreach (var layer in material.Layers)
        {
            if (layer.Primitive is "texture" or "decal" && Asset(layer).Length == 0)
                skipped.Add(layer.Primitive + ":" + layer.Role);
            else if (layer.Primitive is not ("fill" or "gradient" or "stroke" or "shadow"
                     or "innerShadow" or "backdrop" or "specular" or "texture" or "decal"))
                skipped.Add(layer.Primitive + ":" + layer.Role);
        }

        return declarations;
    }

    private static void AddBorder(
        List<KeyValuePair<string, string>> declarations, ResolvedLayer stroke, BorderSpec spec)
    {
        var colors = Param(stroke, "colors", string.Empty);
        var color = Param(stroke, "value", string.Empty);
        var shorthand = Param(stroke, "widths", string.Empty);

        if (shorthand.Length > 0 && BorderEdgeSet.TryParseShorthand(shorthand, out var edges))
        {
            declarations.Add(Pair("border-style", "solid"));
            declarations.Add(Pair("border-top-width", FormatPx(edges.Top)));
            declarations.Add(Pair("border-right-width", FormatPx(edges.Right)));
            declarations.Add(Pair("border-bottom-width", FormatPx(edges.Bottom)));
            declarations.Add(Pair("border-left-width", FormatPx(edges.Left)));
            if (colors.Length > 0)
            {
                var parts = colors.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                declarations.Add(Pair("border-top-color", parts.Length > 0 ? parts[0] : color));
                declarations.Add(Pair("border-right-color", parts.Length > 1 ? parts[1] : color));
                declarations.Add(Pair("border-bottom-color", parts.Length > 2 ? parts[2] : color));
                declarations.Add(Pair("border-left-color", parts.Length > 3 ? parts[3] : color));
            }
            else
            {
                declarations.Add(Pair("border-color", color));
            }
        }
        else
        {
            var width = Param(stroke, "width", "1");
            if (colors.Length > 0)
            {
                // 四边独立颜色：CSS 单 border 简写会丢色，改 longhand（宽度仍可简写）。
                var parts = colors.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                declarations.Add(Pair("border-style", "solid"));
                declarations.Add(Pair("border-width", FormatPx(float.TryParse(
                    width, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1)));
                declarations.Add(Pair("border-top-color", parts.Length > 0 ? parts[0] : color));
                declarations.Add(Pair("border-right-color", parts.Length > 1 ? parts[1] : color));
                declarations.Add(Pair("border-bottom-color", parts.Length > 2 ? parts[2] : color));
                declarations.Add(Pair("border-left-color", parts.Length > 3 ? parts[3] : color));
            }
            else
            {
                declarations.Add(Pair("border", width + "px solid " + color));
            }
        }

        var radius = RadiusCss(stroke, spec);
        if (radius.Length > 0) declarations.Add(Pair("border-radius", radius));
    }

    private static string RadiusCss(ResolvedLayer stroke, BorderSpec spec)
    {
        var shorthand = Param(stroke, "radii", string.Empty);
        if (shorthand.Length > 0 && CornerRadii.TryParseShorthand(shorthand, out var radii))
        {
            return string.Join(' ',
                FormatPx(radii.TopLeft), FormatPx(radii.TopRight),
                FormatPx(radii.BottomRight), FormatPx(radii.BottomLeft));
        }

        var radius = Param(stroke, "radius", string.Empty);
        if (radius.Length > 0)
        {
            return float.TryParse(radius, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? FormatPx(parsed)
                : radius + "px";
        }

        // 层未声明半径 ⇒ 沿用模型材质级兜底（与 GDI+ 同口径），避免填充圆角/描边直角错位。
        var fallback = spec.Strokes.Count == 0 ? 0 : spec.Strokes[^1].Radii.Max;
        return fallback > 0 ? FormatPx(fallback) : string.Empty;
    }

    private static ResolvedLayer? PrimaryStroke(ResolvedMaterial material)
    {
        ResolvedLayer? best = null;
        foreach (var layer in material.Layers)
        {
            if (string.Equals(layer.Primitive, "stroke", StringComparison.Ordinal)
                && (best is null || layer.Z >= best.Z))
            {
                best = layer;
            }
        }

        return best;
    }

    private static (int X, int Y) SpotCenter(ResolvedLayer layer)
    {
        var raw = Param(layer, "angle", "45");
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle))
            return (50, 50);

        var radians = angle * Math.PI / 180;
        var x = (int)Math.Round(50 + Math.Cos(radians) * 26);
        var y = (int)Math.Round(50 - Math.Sin(radians) * 20);
        return (Math.Clamp(x, 0, 100), Math.Clamp(y, 0, 100));
    }

    private static string Asset(ResolvedLayer layer)
        => layer.Params.TryGetValue("asset", out var asset) ? asset : string.Empty;

    private static string Param(ResolvedLayer layer, string name, string fallback)
        => layer.Params.TryGetValue(name, out var value) && value.Length > 0 ? value : fallback;

    /// <summary>九宫格（slice 非空才产出 border-image 三元组）。</summary>
    private static IEnumerable<KeyValuePair<string, string>> NineSlice(ResolvedLayer layer, string asset)
    {
        if (!layer.Params.TryGetValue("slice", out var slice) || slice.Length == 0) yield break;
        yield return Pair("border-image-source", $"url(\"{asset}\")");
        yield return Pair("border-image-slice", slice + " fill");
        yield return Pair("border-image-repeat", Param(layer, "repeat", "stretch"));
    }

    private static KeyValuePair<string, string> Pair(string property, string value) => new(property, value);

    private static string Deg(ResolvedLayer layer)
        => layer.Params.TryGetValue("angle", out var angle) && angle.Length > 0 ? angle : "180";

    private static string Px(ResolvedLayer layer, string name, string fallback)
    {
        var raw = layer.Params.TryGetValue(name, out var value) && value.Length > 0 ? value : fallback;
        return FormatPx(float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0);
    }

    private static string Offset(ResolvedLayer layer)
        => layer.Params.TryGetValue("offset", out var offset) && offset.Length > 0
            ? string.Join(' ', offset.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part + "px"))
            : "0 0";

    private static string ReplaceCommas(string value, string separator)
        => string.Join(separator, value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string FormatPx(float value)
        => Math.Abs(value - Math.Round(value)) < 1e-6
            ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture) + "px"
            : value.ToString("0.###", CultureInfo.InvariantCulture) + "px";
}
