#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MathematicalAlgorithms.Lighting;
using MathematicalAlgorithms.Surface;
using Share.Conversation.Rendering.Surface;
using UiBackend;
using UiBackend.Css;

namespace FrontEndDriven.HtmlViewerBridge.Surface;

/// <summary>CSS 投影产物（文本 + 未落地图元清单）。</summary>
/// <param name="Css">样式文本。</param>
/// <param name="Skipped">未投影的图元（可见清单，禁静默丢弃）。</param>
public sealed record SurfaceStyleSheet(string Css, IReadOnlyList<string> Skipped)
{
    /// <summary>需要光谱 <c>::after</c> 的材质类名（样式正文由独立光谱模块文件产出）。</summary>
    public IReadOnlyList<string> SpectralClasses { get; init; } = Array.Empty<string>();
}

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
        => CssIdentifier.Sanitize(materialId, MaterialClassPrefix);

    /// <summary>按已解析表面投影材质类（可带一份全局光态，写成 <c>:root</c> 的 <c>--sl-*</c> 变量）。</summary>
    /// <param name="surface">解析产物。</param>
    /// <param name="light">全局光场采样；null ⇒ 只用材质声明值（不伪装动态）。</param>
    /// <param name="tokens">外观令牌（可选；用于 pageField 的页面渐变/径向光/丝带）。</param>
    public static SurfaceStyleSheet Project(
        ResolvedSurface surface, SurfaceLightSample? light = null, IReadOnlyDictionary<string, string>? tokens = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var materials = new List<(string Slot, ResolvedMaterial Material)>();
        foreach (var container in surface.Containers)
        {
            foreach (var pair in container.Materials) materials.Add((pair.Key, pair.Value));
        }

        return ProjectMaterials(materials, tokens, ToState(light));
    }

    /// <summary>按聚拢产物投影材质类（HTML 侧唯一入口；Plan 决定类名，Tokens 决定页场）。</summary>
    /// <param name="bundle">表面计划产物（全部视图 + 令牌）。</param>
    /// <param name="light">全局光场采样；null ⇒ 只用材质声明值。</param>
    public static SurfaceStyleSheet Project(SurfacePlanBundle bundle, SurfaceLightSample? light = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return Project(bundle, ToState(light));
    }

    /// <summary>按聚拢产物投影材质类（光态入口：与窗口帧同一份窄视图）。</summary>
    /// <param name="bundle">表面计划产物。</param>
    /// <param name="light">运行时光态（null/无值 ⇒ 静态声明值）。</param>
    public static SurfaceStyleSheet Project(SurfacePlanBundle bundle, SurfaceLightState? light)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var materials = new List<(string Slot, ResolvedMaterial Material)>();
        foreach (var surface in bundle.Views.Values)
        {
            foreach (var container in surface.Containers)
            {
                foreach (var pair in container.Materials) materials.Add((pair.Key, pair.Value));
            }
        }

        var tokens = bundle.Tokens.Count > 0 ? bundle.Tokens : null;
        return ProjectMaterials(materials, tokens, light is { HasValue: true } ? light : null);
    }

    /// <summary>采样 → 载体窄视图（null/无值 ⇒ null，不伪装动态光）。</summary>
    private static SurfaceLightState? ToState(SurfaceLightSample? light)
        => light is { HasValue: true } sample ? SurfaceLightState.From(sample) : null;

    /// <summary>唯一投影实现：光/页场只算一次，材质与语义槽规则同源。</summary>
    private static SurfaceStyleSheet ProjectMaterials(
        IReadOnlyList<(string Slot, ResolvedMaterial Material)> materials,
        IReadOnlyDictionary<string, string>? tokens,
        SurfaceLightState? light)
    {
        var builder = new CssDocumentBuilder();
        var skipped = new List<string>();
        var emittedClasses = new HashSet<string>(StringComparer.Ordinal);
        var emittedSlots = new HashSet<string>(StringComparer.Ordinal);
        var spectral = new List<string>();

        if (light is { } lightState) builder.Rule(":root", LightVariables(lightState));

        // 页场：令牌开关打开时，HTML 宿主自身承载与 WinForms 同一套渐变/径向光/丝带。
        // 声明表只构造一次（原实现同一调用内重复构造 3 次）。
        if (tokens is not null && PageFieldEnabled(tokens))
        {
            var pageField = PageFieldDeclarations(tokens);
            builder.Rule("body", pageField);
            builder.Rule("[data-surface=\"panel_card\"]", pageField);
            builder.Rule("[data-surface=\"panel_surface\"]", pageField);
            builder.Rule(CssSelector.Class(ClassName("panel_card")), pageField);
        }

        foreach (var (slot, material) in materials)
        {
            if (material.Layers.Count == 0) continue;

            var className = ClassName(material.FallbackUsed ?? material.MaterialId);
            if (emittedClasses.Add(className))
            {
                builder.Rule(CssSelector.Class(className), MaterialToCss(material, light, tokens, skipped, out var isSpectral));
                if (isSpectral) spectral.Add(className);
            }

            // 语义槽规则：硬编码 data-surface 的外壳也解析到「当前档位实际材质」，
            // 不再要求模板知道 panel_card_quiet 之类的档位别名（D6）。
            if (!string.IsNullOrEmpty(slot) && emittedSlots.Add(slot))
            {
                builder.Rule(
                    CssSelector.Attr("data-surface", slot),
                    MaterialToCss(material, light, tokens, skipped, out var slotSpectral));
                if (slotSpectral && !spectral.Contains(className)) spectral.Add(className);
            }
        }

        return new SurfaceStyleSheet(builder.Build(), skipped.Distinct(StringComparer.Ordinal).ToArray())
        {
            SpectralClasses = spectral.ToArray(),
        };
    }

    /// <summary>运行时光态 → <c>--sl-*</c>（与采样入口共用同一套字段，不做光数学）。</summary>
    private static List<KeyValuePair<string, string>> LightVariables(SurfaceLightState state)
    {
        var angle = (int)Math.Round(state.Angle);
        var specular = Math.Clamp(state.Specular, 0f, 1f);
        var sheenAlpha = (int)Math.Round(255 * (0.25 + 0.75 * specular));
        var sheenTint = unchecked((sheenAlpha << 24) | (state.TintArgb & 0x00FFFFFF));
        var iridAngle = (int)Math.Round(Math.Clamp(state.IridescencePhase, 0f, 1f) * 360) % 360;
        return new List<KeyValuePair<string, string>>
        {
            Pair("--sl-angle", $"{CssValue.Deg(angle)}deg"),
            Pair("--sl-tint", SurfaceColor.ToCssHexCompact(sheenTint)),
            Pair("--sl-irid-angle", $"{CssValue.Deg(iridAngle)}deg"),
        };
    }

    /// <summary>单个材质 → 声明列表（阴影/背景多值合并；边框走 BorderSpec）。</summary>
    private static List<KeyValuePair<string, string>> MaterialToCss(
        ResolvedMaterial material,
        SurfaceLightState? light,
        IReadOnlyDictionary<string, string>? tokens,
        List<string> skipped,
        out bool spectral)
    {
        spectral = false;
        var declarations = new List<KeyValuePair<string, string>>();
        var backgrounds = new List<string>();
        var backgroundAttachments = new List<string>();
        var shadows = new List<string>();
        var extra = new List<KeyValuePair<string, string>>();

        string backgroundColor = string.Empty;
        string? backdropFilter = null;

        // 强类型层模型（与 WinForms 载体同一份解析）：本文件不再直接读层参数袋。
        var parsed = ResolvedLayerSpecProjection.Parse(material.Layers);
        foreach (var layerSpec in parsed.Specs)
        {
            switch (layerSpec.Kind)
            {
                case LayerPrimitive.Fill:
                    if (layerSpec.HasColor) backgroundColor = SurfaceColor.ToCssHexCompact(layerSpec.ColorArgb);
                    break;

                case LayerPrimitive.Gradient:
                    if (layerSpec.Gradient.Stops.Count == 0)
                    {
                        SkipLayer(skipped, "gradient", layerSpec.Role);
                        break;
                    }

                    backgrounds.Add($"linear-gradient({CssValue.Deg(layerSpec.Angle)}deg, {GradientStopsCss(layerSpec.Gradient)})");
                    backgroundAttachments.Add("scroll");
                    break;

                case LayerPrimitive.Backdrop:
                    backdropFilter = $"blur({CssValue.Px(layerSpec.Blur)})";
                    break;

                case LayerPrimitive.Stroke:
                    // 几何/颜色统一在循环后由 BorderSpec 输出（这里只标记已投影）。
                    break;

                // M6：贴图 / 贴画（资源缺失 ⇒ 无 asset ⇒ 进 Skipped，禁静默纯色）
                case LayerPrimitive.Texture:
                {
                    if (layerSpec.Asset.Length == 0) break;
                    // 九宫格只用 border-image 一条路径，禁止同一张图再铺 background（否则同图双画、边缘发虚）。
                    if (layerSpec.SliceText.Length > 0)
                    {
                        foreach (var pair in NineSlice(layerSpec)) extra.Add(pair);
                        break;
                    }

                    backgrounds.Add($"url(\"{layerSpec.Asset}\")");
                    backgroundAttachments.Add("scroll");
                    extra.Add(Pair("background-repeat", layerSpec.Repeat.Length > 0 ? layerSpec.Repeat : "repeat"));
                    if (layerSpec.Anchor.Length > 0) extra.Add(Pair("background-position", layerSpec.Anchor));
                    break;
                }

                case LayerPrimitive.Decal:
                {
                    if (layerSpec.Asset.Length == 0) break;
                    if (layerSpec.SliceText.Length > 0)
                    {
                        foreach (var pair in NineSlice(layerSpec)) extra.Add(pair);
                        break;
                    }

                    backgrounds.Add($"url(\"{layerSpec.Asset}\")");
                    backgroundAttachments.Add("scroll");
                    extra.Add(Pair("background-repeat", "no-repeat"));
                    extra.Add(Pair("background-position", layerSpec.Anchor.Length > 0 ? layerSpec.Anchor : "center"));
                    extra.Add(Pair("background-size", layerSpec.Size.Length > 0 ? layerSpec.Size : "contain"));
                    break;
                }
            }
        }

        // 解析期问题可见化（缺失 asset / 非法值 / 未换值 token 不得静默吞掉）。
        foreach (var issue in parsed.Issues) SkipLayer(skipped, "parse", $"{issue.Code}:{issue.Detail}");

        // 边框：同一模型（BorderSpec）取最大 z 的描边；多描边因 CSS 单 border 限制显式记账。
        var spec = BorderSpecProjection.From(material);

        // 阴影/内高光/光泽一律从归一化产物投影（不再从原始 layer 重拼，模型改参数两载体同时生效）。
        foreach (var glow in spec.Glows)
        {
            var parts = new List<string>
            {
                CssValue.Px(glow.OffsetX), CssValue.Px(glow.OffsetY), CssValue.Px(glow.Blur),
            };
            if (glow.Spread > 0) parts.Add(CssValue.Px(glow.Spread));
            parts.Add(SurfaceColor.ToCssHexCompact(glow.Argb));
            shadows.Add(string.Join(" ", parts));
        }

        foreach (var innerLight in spec.InnerLights)
        {
            var edgeMode = string.Equals(innerLight.Mode, "edge", StringComparison.Ordinal);
            if (edgeMode)
            {
                // 顶高光；CSS 单条 inset 不能只亮左边 ⇒ 逐边语义显式降级记账。
                shadows.Add($"inset 0 {CssValue.Px(innerLight.Width)} 0 {SurfaceColor.ToCssHexCompact(innerLight.Argb)}");
            }

            if (edgeMode || !string.Equals(innerLight.Sides, "all", StringComparison.Ordinal))
                SkipLayer(skipped, "inner-light-sides-degraded", innerLight.Sides);
            if (edgeMode) continue;
            var inset = innerLight.Blur > 0
                ? $"inset {CssValue.ZeroAware(innerLight.OffsetX)} {CssValue.ZeroAware(innerLight.OffsetY)} {CssValue.Px(innerLight.Blur)}"
                : $"inset {CssValue.ZeroAware(innerLight.OffsetX)} {CssValue.ZeroAware(innerLight.OffsetY)} 0";
            shadows.Add($"{inset} {SurfaceColor.ToCssHexCompact(innerLight.Argb)}");
        }

        // 光斑半径乘子 = 材质声明 radius（与 GDI+ PaintSpotHighlight 的 clamp(radius, 0.25, 1.2) 同一来源）。
        var spotRadiusFactor = 0.55f;
        foreach (var layerSpec in parsed.Specs)
        {
            if (layerSpec.Kind == LayerPrimitive.Specular
                && string.Equals(layerSpec.Mode, "spot", StringComparison.Ordinal))
            {
                spotRadiusFactor = SurfaceLayerMath.SpotRadiusFactor(layerSpec.Radius);
                break;
            }
        }

        foreach (var sheen in spec.Sheens)
        {
            if (string.Equals(sheen.Mode, "spectral", StringComparison.Ordinal))
            {
                // 光谱正文由独立样式模块（SpectralStyleModule）产出：这里只标记类名 + 带宽/峰值变量。
                spectral = true;
                // 带宽 = 材质声明 width（不再由模块写死 6px）；峰值与 GDI+ 走同一公式。
                declarations.Add(Pair("--sl-spectral-w", CssValue.Px(sheen.Width)));
                var spectralPeak = SurfaceLightState.SheenAlpha(
                    150f, sheen.Strength, light?.Specular ?? 1f, light?.Shade ?? 1f);
                declarations.Add(Pair("--sl-spectral-a", CssValue.Ratio(spectralPeak / 255f)));
                // 与 WinForms 同一公式：令牌色相 + 世界相位 + (t-0.5)*span；HTML 用 conic 的角度区间表达 t。
                declarations.Add(Pair("--sl-spectral-hue",
                    $"{CssValue.Deg(SurfaceShadingMath.HueOf(sheen.Argb))}deg"));
                declarations.Add(Pair("--sl-spectral-span",
                    $"{CssValue.Deg(sheen.SpectralSpan)}deg"));
                continue;
            }

            if (string.Equals(sheen.Mode, "spot", StringComparison.Ordinal))
            {
                // 光斑中心 = 视口固定锚点（px）；background-attachment:fixed 使定位区 = 视口 ⇒ 面板移动光斑不动。
                // 无帧回落也必须锚在视口：用 vw/vh/vmin，**不能**退回元素自身百分比（那是局部坐标）。
                if (light is { SpotRadiusDips: > 0f } lit)
                {
                    // 半径 = 光斑半径 × 材质 radius 乘子（与 GDI+ PaintSpotHighlight 同一乘子）；
                    // 色标走到 100% ⇒ 视觉半径不再被旧的 70% 缩成 1.27×。
                    declarations.Add(Pair("--sl-spot-r", CssValue.Px(lit.SpotRadiusDips * spotRadiusFactor)));
                    // 峰值颜色 = 材质声明色 + **两载体同一峰值公式**（不再用 --sl-tint 的 220/255 且忽略 strength/Shade）。
                    // 注意：峰值（亮度）同源；颜色仍有一档差异 —— GDI+ 的 PaintSpotHighlight 会再叠 LitColor
                    // 的 15% 光照 tint，CSS 侧只叠 alpha、不混 tint。这是**已知不对称**，别当成同色。
                    var peak = SurfaceLightState.SheenAlpha(90f, sheen.Strength, lit.Specular, lit.Shade);
                    declarations.Add(Pair("--sl-spot-color", SurfaceColor.ToCssHexCompact((peak << 24) | (sheen.Argb & 0x00FFFFFF))));
                }

                var spotShape = "circle var(--sl-spot-r, 22vmin) at var(--sl-anchor-x, 50%) var(--sl-anchor-y, 30%)";
                backgrounds.Add(
                    $"radial-gradient({spotShape}, var(--sl-spot-color, {SurfaceColor.ToCssHexCompact(sheen.Argb)}) 0%, transparent 100%)");
                backgroundAttachments.Add("fixed");

                // 指针光：光源集合的第二成员（仅在显式豁免开关打开时投影）。
                // 位置由页面内 33ms 节流 mousemove 写入 --sl-pointer-x/y（只读位置，不参与光数学）；
                // 开关关闭时不生成任何 --sl-pointer-* 声明，也不注册页面监听（无隐形旁路）。
                if (SurfaceLightPointer.HtmlLocalRead
                    && light is { HasValue: true, PointerWeight: > 0f, PointerRadiusDips: > 0f } pointerLight)
                {
                    var pointerPeak = (int)Math.Round(
                        SurfaceShadingMath.SheenPeak(90f, sheen.Strength, pointerLight.Specular, 1f)
                        * pointerLight.PointerWeight);
                    if (pointerPeak > 0)
                    {
                        declarations.Add(Pair("--sl-pointer-r",
                            CssValue.Px(pointerLight.PointerRadiusDips * spotRadiusFactor)));
                        declarations.Add(Pair("--sl-pointer-color",
                            SurfaceColor.ToCssHexCompact((pointerPeak << 24) | (sheen.Argb & 0x00FFFFFF))));
                        // 默认把圆心放到视口外：页面尚未收到 mouseMove 时不显示指针光。
                        backgrounds.Add(
                            "radial-gradient(circle var(--sl-pointer-r, 22vmin) at " +
                            "var(--sl-pointer-x, -100vmax) var(--sl-pointer-y, -100vmax), " +
                            "var(--sl-pointer-color, transparent) 0%, transparent 100%)");
                        backgroundAttachments.Add("fixed");
                    }
                }
            }
            else
            {
                // 非 spot 的 sheen：峰值同样走两载体唯一公式（strength × Specular × Shade），
                // 不再用全局 --sl-tint 的 220/255 且忽略 strength/Shade。
                if (light is { HasValue: true } bandLight)
                {
                    var bandPeak = SurfaceLightState.SheenAlpha(
                        90f, sheen.Strength, bandLight.Specular, bandLight.Shade);
                    declarations.Add(Pair(
                        "--sl-tint", SurfaceColor.ToCssHexCompact((bandPeak << 24) | (sheen.Argb & 0x00FFFFFF))));
                }

                backgrounds.Add(
                    $"linear-gradient(var(--sl-angle, {CssValue.Deg(sheen.Angle)}deg), transparent 40%, var(--sl-tint, {SurfaceColor.ToCssHexCompact(sheen.Argb)}) 50%, transparent 60%)");
                backgroundAttachments.Add("scroll");
            }
        }

        var primary = spec.TopStroke;
        if (primary is not null)
        {
            if (spec.Strokes.Count > 1)
            {
                SkipLayer(skipped, "border-multi-stroke-emulated", spec.Strokes.Count.ToString());
            }

            AddBorder(declarations, primary);
        }

        // chrome：留白环/外发光在 HTML 侧用「令牌内衬边 + gutter 变量」表达；页面场由 body 承载。
        // gutter/rim 必须取生效令牌（声明 + 用户覆盖），不得再引用无生产者的 --ui-* 变量。
        if ((material.Decoration & SurfaceDecoration.Chrome) != 0)
        {
            if (tokens is not null
                && tokens.TryGetValue("ambient_gutter", out var gutterText)
                && float.TryParse(gutterText, NumberStyles.Float, CultureInfo.InvariantCulture, out var gutter))
            {
                declarations.Add(Pair("--sl-chrome-gutter", CssValue.Px(gutter)));
            }

            if (tokens is not null
                && tokens.TryGetValue("panel_rim", out var rimText)
                && SurfaceColor.TryParseColor(rimText, out var rimArgb))
            {
                shadows.Add("inset 0 0 0 1px " + SurfaceColor.ToCssHexCompact(rimArgb));
            }
        }

        if (backgroundColor.Length > 0) declarations.Add(Pair("background-color", backgroundColor));
        // fill+radius 以 WinForms 为准：无描边材质也要圆角（材质级最大半径），否则填充直角/描边圆角错位。
        if (primary is null)
        {
            if (parsed.MaterialRadius > 0) declarations.Add(Pair("border-radius", CssValue.Px(parsed.MaterialRadius)));
        }
        // CSS 背景层「先写者在上」：按 z 升序收集，输出时反转，保证高 z 图元压在上面（不再后写覆盖先写）。
        if (backgrounds.Count > 0)
        {
            var ordered = new List<string>(backgrounds.Count);
            var orderedAttachments = new List<string>(backgroundAttachments.Count);
            for (var i = backgrounds.Count - 1; i >= 0; i--)
            {
                ordered.Add(backgrounds[i]);
                orderedAttachments.Add(backgroundAttachments[i]);
            }

            declarations.Add(Pair("background-image", string.Join(", ", ordered)));
            // 只有存在「屏幕固定」层时才写 attachment 列表（顺序与 background-image 对齐）。
            if (orderedAttachments.Contains("fixed", StringComparer.Ordinal))
            {
                declarations.Add(Pair("background-attachment", string.Join(", ", orderedAttachments)));
            }
        }
        foreach (var pair in extra) declarations.Add(pair);
        if (shadows.Count > 0) declarations.Add(Pair("box-shadow", string.Join(", ", shadows)));
        if (backdropFilter is not null) declarations.Add(Pair("backdrop-filter", backdropFilter));
        // border-box：外沿与 GDI+ 的 bounds 同锚点（width/height 含边框，不再向外撑大）。
        declarations.Add(Pair("box-sizing", "border-box"));

        // 未投影图元可见化（texture/decal 缺件、noise/mask 等）。
        foreach (var layerSpec in parsed.Specs)
        {
            if (layerSpec.Kind is LayerPrimitive.Texture or LayerPrimitive.Decal && layerSpec.Asset.Length == 0)
                SkipLayer(skipped, layerSpec.Primitive, layerSpec.Role);
            else if (layerSpec.Kind is LayerPrimitive.Mask or LayerPrimitive.Noise or LayerPrimitive.Unknown)
                SkipLayer(skipped, layerSpec.Primitive, layerSpec.Role);
        }

        return declarations;
    }

    /// <summary>降级记账唯一出口：所有"跳过"都必须以 kind:detail 形式可见，禁静默丢弃。</summary>
    private static void SkipLayer(List<string> skipped, string kind, string detail)
        => skipped.Add($"{kind}:{detail}");

    /// <summary>
    /// 描边投影（唯一模型读取点）：宽度/颜色/半径全部来自 <see cref="BorderSpec.TopStroke"/>，
    /// 不再重解析层参数（解析规则单源 = Plate；缺边颜色不再静默借用整体色）。
    /// </summary>
    private static void AddBorder(List<KeyValuePair<string, string>> declarations, BorderStroke stroke)
    {
        declarations.Add(Pair("border-style", "solid"));
        declarations.Add(Pair("border-top-width", CssValue.Px(stroke.Widths.Top)));
        declarations.Add(Pair("border-right-width", CssValue.Px(stroke.Widths.Right)));
        declarations.Add(Pair("border-bottom-width", CssValue.Px(stroke.Widths.Bottom)));
        declarations.Add(Pair("border-left-width", CssValue.Px(stroke.Widths.Left)));

        if (stroke.Colors.IsUniform)
        {
            declarations.Add(Pair("border-color", SurfaceColor.ToCssHexCompact(stroke.Colors.Top)));
        }
        else
        {
            declarations.Add(Pair("border-top-color", SurfaceColor.ToCssHexCompact(stroke.Colors.Top)));
            declarations.Add(Pair("border-right-color", SurfaceColor.ToCssHexCompact(stroke.Colors.Right)));
            declarations.Add(Pair("border-bottom-color", SurfaceColor.ToCssHexCompact(stroke.Colors.Bottom)));
            declarations.Add(Pair("border-left-color", SurfaceColor.ToCssHexCompact(stroke.Colors.Left)));
        }

        var radii = stroke.Radii;
        if (radii.Max > 0)
        {
            // P1·圆角硬夹取：min(r, 50%) 从源头杜绝「半径 > 半边长 → 胶囊/圆斑」，
            // 与 GDI+ 侧 BorderGeometry.Clamp 的下限同源（任何令牌值都不会把控件画成药丸）。
            declarations.Add(Pair("border-radius", string.Join(' ',
                ClampHalf(radii.TopLeft), ClampHalf(radii.TopRight),
                ClampHalf(radii.BottomRight), ClampHalf(radii.BottomLeft))));
        }
    }

    /// <summary>半径 → <c>min(r, 50%)</c>（CSS 百分比按对应轴/短边解析，等价于半边长硬上限）。</summary>
    private static string ClampHalf(float radius)
        => $"min({CssValue.Px(radius)}, 50%)";

    /// <summary>全局光态 → <c>--sl-*</c> 变量（唯一格式化实现；采样入口先投影成窄视图）。</summary>
    private static List<KeyValuePair<string, string>> LightVariables(SurfaceLightSample sample)
        => LightVariables(SurfaceLightState.From(sample));

    /// <summary>页场开关：令牌显式打开且渐变两端都有值。</summary>
    private static bool PageFieldEnabled(IReadOnlyDictionary<string, string> tokens)
        => tokens.TryGetValue("ambient_enabled", out var enabled)
           && (string.Equals(enabled, "1", StringComparison.Ordinal)
               || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
           && tokens.ContainsKey("page_gradient_from")
           && tokens.ContainsKey("page_gradient_to");

    /// <summary>页场 CSS（页面渐变 + 世界固定径向光 + 单条丝带）；值全部来自令牌。</summary>
    private static List<KeyValuePair<string, string>> PageFieldDeclarations(
        IReadOnlyDictionary<string, string> tokens)
    {
        var from = tokens["page_gradient_from"];
        var to = tokens["page_gradient_to"];
        var images = new List<string>
        {
            $"linear-gradient(45deg, {from}, {to})",
        };
        var attachments = new List<string> { "scroll" };

        var radialAlpha = Number(tokens, "ambient_radial_alpha", 0.42f);
        var radialRadius = Number(tokens, "ambient_radial_radius", 200f);
        var radialShape = $"radial-gradient(circle {CssValue.Px(radialRadius)} at var(--sl-anchor-x,50%) var(--sl-anchor-y,30%)";
        images.Add($"{radialShape}, rgba(255,255,255,{CssValue.Ratio(radialAlpha)}) 0%, transparent 100%)");
        attachments.Add("fixed");

        if (tokens.TryGetValue("glass_edge_light", out var ribbonColor)
            && tokens.TryGetValue("ambient_ribbon_alpha", out _))
        {
            var ribbonAlpha = Number(tokens, "ambient_ribbon_alpha", 0.30f);
            images.Add(
                $"linear-gradient(135deg, transparent 35%, {SurfaceColor.ToCssRgba(ribbonColor, ribbonAlpha)} 50%, transparent 65%)");
            attachments.Add("scroll");
        }

        return new List<KeyValuePair<string, string>>
        {
            Pair("background-image", string.Join(", ", images)),
            Pair("background-attachment", string.Join(", ", attachments)),
            Pair("background-repeat", "no-repeat"),
            Pair("background-size", "auto"),
        };
    }

    private static float Number(IReadOnlyDictionary<string, string> tokens, string key, float fallback)
        => tokens.TryGetValue(key, out var text)
           && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>九宫格（slice 非空才产出 border-image 一套；调用方保证不再叠加 background 同图）。</summary>
    private static IEnumerable<KeyValuePair<string, string>> NineSlice(ResolvedLayerSpec layerSpec)
    {
        var slice = layerSpec.SliceText;
        yield return Pair("border-style", "solid");
        yield return Pair("border-image-source", $"url(\"{layerSpec.Asset}\")");
        yield return Pair("border-image-slice", $"{slice} fill");
        yield return Pair("border-image-repeat", layerSpec.Repeat.Length > 0 ? layerSpec.Repeat : "stretch");
        yield return Pair("border-image-width", slice);
    }

    /// <summary>渐变 stop → CSS（未声明位置则不带百分比，保持既有输出形态）。</summary>
    private static string GradientStopsCss(GradientSpec gradient)
    {
        var parts = new List<string>(gradient.Stops.Count);
        foreach (var stop in gradient.Stops)
        {
            var text = SurfaceColor.ToCssHexCompact(stop.Argb);
            if (stop.HasPosition)
            {
                var percent = ((int)Math.Round(stop.Position * 100)).ToString(CultureInfo.InvariantCulture);
                text = $"{text} {percent}%";
            }

            parts.Add(text);
        }

        return string.Join(", ", parts);
    }

    private static KeyValuePair<string, string> Pair(string property, string value) => new(property, value);
}
