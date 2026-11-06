#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Share.Conversation.Rendering.Surface;
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
/// 未覆盖的图元（texture/decal/noise/mask）进 <see cref="SurfaceStyleSheet.Skipped"/>（M6 资源批接入）。
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

                var declarations = new List<KeyValuePair<string, string>>();
                foreach (var layer in material.Layers)
                {
                    var projected = LayersToCss(layer).ToList();
                    if (projected.Count == 0) skipped.Add(layer.Primitive + ":" + layer.Role);
                    else foreach (var pair in projected) declarations.Add(pair);
                }

                builder.Rule("." + className, declarations);
            }
        }

        return new SurfaceStyleSheet(builder.Build(), skipped.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>图元是否已由 CSS 方言覆盖（noise/mask 未接：留可见 Skipped）。</summary>
    private static IEnumerable<KeyValuePair<string, string>> LayersToCss(ResolvedLayer layer)
    {
        var value = layer.Params.TryGetValue("value", out var v) ? v : string.Empty;
        switch (layer.Primitive)
        {
            case "fill":
                yield return Pair("background-color", value);
                break;
            case "gradient":
                yield return Pair("background-image", $"linear-gradient({Deg(layer)}deg, {ReplaceCommas(value, ", ")})");
                break;
            case "stroke":
                yield return Pair("border", $"{Px(layer, "width", "1")} solid {value}");
                if (layer.Params.TryGetValue("radius", out var radius)) yield return Pair("border-radius", radius + "px");
                break;
            case "shadow":
                yield return Pair("box-shadow", $"{Offset(layer)} {Px(layer, "blur", "0")} {value}");
                break;
            case "innerShadow":
                yield return Pair("box-shadow", $"inset {Offset(layer)} {Px(layer, "blur", "0")} {value}");
                break;
            case "backdrop":
                yield return Pair("backdrop-filter", $"blur({Px(layer, "blur", "0")})");
                break;
            case "specular":
                yield return Pair("background-image", $"linear-gradient({Deg(layer)}deg, transparent 40%, {value} 50%, transparent 60%)");
                break;
            // M6：贴图 / 贴画（资源缺失 ⇒ 无 asset ⇒ 空投影 ⇒ 进 Skipped，禁静默纯色）
            case "texture":
            {
                var asset = Asset(layer);
                if (asset.Length == 0) break;
                yield return Pair("background-image", $"url(\"{asset}\")");
                yield return Pair("background-repeat", Param(layer, "repeat", "repeat"));
                if (layer.Params.TryGetValue("anchor", out var anchor) && anchor.Length > 0)
                    yield return Pair("background-position", anchor);
                foreach (var pair in NineSlice(layer, asset)) yield return pair;
                break;
            }
            case "decal":
            {
                var asset = Asset(layer);
                if (asset.Length == 0) break;
                yield return Pair("background-image", $"url(\"{asset}\")");
                yield return Pair("background-repeat", "no-repeat");
                yield return Pair("background-position", Param(layer, "anchor", "center"));
                yield return Pair("background-size", Param(layer, "size", "contain"));
                foreach (var pair in NineSlice(layer, asset)) yield return pair;
                break;
            }
        }
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
        => layer.Params.TryGetValue(name, out var value) && value.Length > 0 ? value + "px" : fallback + "px";

    private static string Offset(ResolvedLayer layer)
        => layer.Params.TryGetValue("offset", out var offset) && offset.Length > 0
            ? string.Join(' ', offset.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part + "px"))
            : "0 0";

    private static string ReplaceCommas(string value, string separator)
        => string.Join(separator, value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
