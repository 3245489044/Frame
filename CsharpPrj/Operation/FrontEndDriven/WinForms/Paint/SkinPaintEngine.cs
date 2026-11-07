#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 皮肤绘制引擎（GDI+ 方言落点）：已解析材质层栈 → 控件背景。
/// <para>支持 fill/gradient/stroke/shadow/innerShadow/backdrop/specular；texture/decal/mask 由 M6 接管（进 Skipped，禁静默纯色）。</para>
/// <para>backdrop：有宿主快照时「采样背后 + 模糊」；无快照才回退旧近似并记账 <c>backdrop:emulated</c>，禁伪装原生模糊。</para>
/// </summary>
public static class SkinPaintEngine
{
    private static Func<string, Image?>? _assetLoader;

    /// <summary>资源装载钩子（宿主注入；null ⇒ 按 <see cref="AssetRoot"/> 从磁盘装载，仍缺件则显式跳过，禁静默纯色）。</summary>
    public static Func<string, Image?>? AssetLoader
    {
        get => _assetLoader;
        set
        {
            _assetLoader = value;
            ClearAssetCache();
        }
    }

    /// <summary>资源根目录（宿主注入；null ⇒ 不装载，贴图层进 Skipped）。</summary>
    public static string? AssetRoot { get; private set; }

    /// <summary>资产代（换根/换装载器/清缓存递增；进静态底图缓存键，防旧底图贴旧贴图）。</summary>
    public static int AssetGeneration { get; private set; }

    private static readonly Dictionary<string, Image> AssetCache = new(StringComparer.Ordinal);

    /// <summary>配置资源根（单一入口；变更即失效缓存与底图键，防旧根别名串味）。</summary>
    /// <param name="root">资源根绝对路径（空 ⇒ 关闭磁盘装载）。</param>
    public static void ConfigureAssetRoot(string? root)
    {
        AssetRoot = string.IsNullOrWhiteSpace(root) ? null : root;
        ClearAssetCache();
    }

    /// <summary>清空资源缓存（换根/换装载器/换肤时调用）：释放已装载图像并推进资产代。</summary>
    public static void ClearAssetCache()
    {
        lock (AssetCache)
        {
            foreach (var image in AssetCache.Values) image.Dispose();
            AssetCache.Clear();
            AssetGeneration++;
        }
    }

    /// <summary>绘制一次材质（背景）。</summary>
    /// <param name="graphics">目标画布。</param>
    /// <param name="bounds">目标矩形。</param>
    /// <param name="material">已解析材质（null ⇒ 不绘制）。</param>
    /// <param name="backdrop">面板背后的快照（宿主提供；null ⇒ backdrop 层走旧近似并记账）。</param>
    /// <param name="light">运行时光（只作用于 specular）。</param>
    /// <param name="includeSpecular">false ⇒ 只画静态层（backdrop/fill/edge…），供静态底图缓存复用。</param>
    public static IReadOnlyList<string> Paint(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, Image? backdrop = null,
        SurfaceLightState? light = null, bool includeSpecular = true)
        => PaintCore(graphics, bounds, material, backdrop, light, includeSpecular, dynamicOnly: false, out _);

    /// <summary>绘制一次材质（背景），并回报**实际落笔图层数**（诊断「有没有画到」的唯一证据）。</summary>
    public static IReadOnlyList<string> Paint(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, out int paintedLayers,
        Image? backdrop = null, SurfaceLightState? light = null, bool includeSpecular = true)
        => PaintCore(graphics, bounds, material, backdrop, light, includeSpecular, dynamicOnly: false, out paintedLayers);

    /// <summary>只画随时间/光标变化的层（当前仅 specular）；静态层由缓存底图提供。</summary>
    public static IReadOnlyList<string> PaintDynamic(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, SurfaceLightState? light)
        => PaintCore(graphics, bounds, material, null, light, includeSpecular: true, dynamicOnly: true, out _);

    /// <summary>只画动态层，并回报实际落笔图层数。</summary>
    public static IReadOnlyList<string> PaintDynamic(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, SurfaceLightState? light, out int paintedLayers)
        => PaintCore(graphics, bounds, material, null, light, includeSpecular: true, dynamicOnly: true, out paintedLayers);

    private static IReadOnlyList<string> PaintCore(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, Image? backdrop,
        SurfaceLightState? light, bool includeSpecular, bool dynamicOnly, out int paintedLayers)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        paintedLayers = 0;
        if (material is null || bounds.Width <= 0 || bounds.Height <= 0) return Array.Empty<string>();

        var skipped = new List<string>();
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // K1：整组层一次解析并缓存（同一层栈跨帧复用），解析问题随 spec 携带；
            // 几何单源（按材质）：圆角由层声明提供，层未声明时沿用同材质内最大半径，
            // 避免「填充直角 + 描边圆角」的错位（材质 = 一个整体形状，不是各层各画各的）。
            var specSet = ResolvedLayerSpecProjection.ParseCached(material.Layers);
            var materialRadius = specSet.MaterialRadius;

            var painted = 0;
            foreach (var spec in OrderedSpecs(specSet.Specs, includeSpecular, dynamicOnly))
            {
                // 明细由 PaintLayer 记账（unresolved-value:<raw>）；未落笔时再补「层身份」项。
                var layerBegin = SkinRenderProfile.Begin();
                var paintedLayer = PaintLayer(graphics, bounds, spec, materialRadius, skipped, backdrop, light);
                SkinRenderProfile.RecordLayer(spec.Primitive, layerBegin);
                if (paintedLayer) painted++;
                else
                {
                    // 未落笔必须同时给「层身份 + 具体原因」：身份供断言按图元定位，原因供人看。
                    skipped.Add(spec.Primitive + ":" + spec.Role);
                }
            }

            paintedLayers = painted;
        }
        finally
        {
            graphics.Restore(state);
        }

        return skipped;
    }

    /// <summary>
    /// 图层相位序（K3）：同相位内按 Z，跨相位按 <see cref="LayerPhase"/>。
    /// <para>动态/静态拆分由相位判定（唯一判据），不再靠字面量 <c>"specular"</c>；shadow/backdrop 在 Underlay，不得盖住 fill/stroke。</para>
    /// </summary>
    private static IEnumerable<ResolvedLayerSpec> OrderedSpecs(
        IReadOnlyList<ResolvedLayerSpec> specs, bool includeSpecular, bool dynamicOnly)
    {
        var ordered = new List<ResolvedLayerSpec>(specs.Count);
        foreach (var spec in specs)
        {
            var phase = SurfaceLayerPhase.Of(spec.Primitive);
            if (dynamicOnly ? phase != LayerPhase.Sheen : (!includeSpecular && phase == LayerPhase.Sheen))
            {
                continue;
            }

            ordered.Add(spec);
        }

        ordered.Sort(static (left, right) =>
        {
            var phaseOrder = (int)SurfaceLayerPhase.Of(left.Primitive) - (int)SurfaceLayerPhase.Of(right.Primitive);
            return phaseOrder != 0 ? phaseOrder : left.Z - right.Z;
        });
        return ordered;
    }

    private static bool PaintLayer(
        Graphics graphics, Rectangle bounds, ResolvedLayerSpec spec, float materialRadius, List<string> skipped,
        Image? backdrop = null, SurfaceLightState? light = null)
    {
        // 描边几何走 Plate.Surface 的 BorderSpec（四边宽度/颜色/圆角/DPI 浮点都在那里归一化）；
        // 层参数经唯一强类型入口（ResolvedLayerSpecProjection）读取，本文件不再直接读层参数袋。
        foreach (var issue in spec.Issues) skipped.Add(issue.Code + ":" + issue.Detail);

        if (spec.Kind == LayerPrimitive.Stroke)
        {
            var fatal = false;
            foreach (var issue in spec.Issues)
            {
                if (issue.Code == "border-width-invalid" || issue.Code == "border-color-invalid") fatal = true;
            }

            if (fatal || spec.Border.Strokes.Count == 0) return false;
            BorderPainter.DrawStroke(graphics, bounds, spec.Border.Strokes[spec.Border.Strokes.Count - 1], graphics.DpiX);
            return true;
        }

        var valid = spec.Issues.Count == 0;

        switch (spec.Kind)
        {
            case LayerPrimitive.Fill:
            {
                if (!valid) return false;
                using var brush = new SolidBrush(Color.FromArgb(spec.ColorArgb));
                using var path = SkinGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                if (path is null) graphics.FillRectangle(brush, bounds);
                else graphics.FillPath(brush, path);
                return true;
            }

            case LayerPrimitive.Gradient:
            {
                if (!valid || spec.Gradient.Stops.Count == 0) return false;
                using var brush = CreateGradientBrush(bounds, spec);
                using var path = SkinGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                if (path is null) graphics.FillRectangle(brush, bounds);
                else graphics.FillPath(brush, path);
                return true;
            }

            case LayerPrimitive.Shadow:
            {
                if (!valid) return false;
                var color = Color.FromArgb(spec.ColorArgb);
                // 阴影方位与全帧光源同源：声明只给「离面距离」，方向由光场 ShadowX/Y 决定（光在左上 ⇒ 影在右下）。
                var offsetX = spec.OffsetX;
                var offsetY = spec.OffsetY;
                if (light is { HasValue: true } shadowLight)
                {
                    var magnitude = Math.Max(2f, MathF.Sqrt(offsetX * offsetX + offsetY * offsetY));
                    offsetX = shadowLight.ShadowX * magnitude;
                    offsetY = shadowLight.ShadowY * magnitude;
                }

                // 偏移/扩散按声明值落成浮点盒（K2：不做整数截断；不再用固定 -2 内缩吃掉声明 spread）。
                var rect = new RectangleF(
                    bounds.X + offsetX - spec.Spread,
                    bounds.Y + offsetY - spec.Spread,
                    Math.Max(1f, bounds.Width + spec.Spread * 2),
                    Math.Max(1f, bounds.Height + spec.Spread * 2));
                // 外阴影相位 = Underlay（先于 fill/stroke），不会盖成内部暗带；模糊仍按 alpha 近似并记账。
                using var brush = new SolidBrush(Color.FromArgb(
                    Math.Min(120, 40 + (int)Math.Round(Math.Max(0, spec.Blur)) * 4), color));
                using var path = SkinGeometry.Shaped(rect, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                if (path is null) graphics.FillRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                else graphics.FillPath(brush, path);
                return true;
            }

            case LayerPrimitive.InnerShadow:
            {
                if (!valid) return false;
                PaintInnerRing(graphics, bounds, RadiusOf(spec, bounds, materialRadius), spec.Width,
                    Color.FromArgb(spec.ColorArgb));
                return true;
            }

            case LayerPrimitive.Backdrop:
            {
                if (!valid) return false;
                var color = Color.FromArgb(spec.ColorArgb);
                using var path = SkinGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                if (backdrop is not null && backdrop.Width > 0 && backdrop.Height > 0)
                {
                    // 真「采样背后 + 模糊」：只做 blur（与 CSS backdrop-filter 同一权威）；
                    // 染色由 fill 层的声明 alpha 承担，不再用固定 alpha 覆盖声明值。
                    var saved = graphics.Save();
                    try
                    {
                        if (path is not null) graphics.SetClip(path);
                        var blurBegin = SkinRenderProfile.Begin();
                        using var blurred = Blur(backdrop, (int)Math.Round(Math.Max(0, spec.Blur)));
                        SkinRenderProfile.RecordBlur(blurBegin);
                        graphics.DrawImage(blurred, bounds);
                    }
                    finally
                    {
                        graphics.Restore(saved);
                    }

                    return true;
                }

                // 无快照来源（非窗口载体/宿主未注入）：按声明 alpha 近似，并显式记账，禁伪装真模糊。
                var topAlpha = spec.Alpha;
                var bottomAlpha = (int)Math.Round(topAlpha * 0.24);
                using var brush = new LinearGradientBrush(
                    bounds, Color.FromArgb(topAlpha, Lighten(color, 0.28)), Color.FromArgb(bottomAlpha, color),
                    LinearGradientMode.Vertical);
                if (path is null) graphics.FillRectangle(brush, bounds);
                else graphics.FillPath(brush, path);
                skipped.Add("backdrop:emulated");
                return true;
            }

            case LayerPrimitive.Specular:
            {
                if (!valid) return false;
                var color = Color.FromArgb(spec.ColorArgb);
                if (light is { } state)
                {
                    if (spec.Mode == "inner")
                    {
                        // 模型形态 <c>inner</c>：与 innerShadow 同一环填充（不再被误当成 band）。
                        PaintInnerRing(graphics, bounds, RadiusOf(spec, bounds, materialRadius), spec.Width, color);
                        return true;
                    }

                    if (spec.Mode == "edge")
                    {
                        PaintEdgeHighlight(graphics, bounds, spec, state, materialRadius);
                        return true;
                    }

                    if (spec.Mode == "spot")
                    {
                        PaintSpotHighlight(graphics, bounds, spec, state, materialRadius);
                        return true;
                    }

                    // 运行时光：强度由光态（矩阵镜面权重）驱动，声明的 strength 只作上限。
                    var peakAlpha = SheenPeak(spec.Strength, 90f, state);
                    var litColor = LitColor(color, state);
                    var bandPx = Math.Clamp(Math.Min(bounds.Width, bounds.Height) * 0.12, 28, 64);
                    var radians = state.Angle * Math.PI / 180.0;
                    var extent = Math.Max(1.0, Math.Abs(bounds.Width * Math.Cos(radians)) + Math.Abs(bounds.Height * Math.Sin(radians)));
                    var half = Math.Clamp(bandPx / 2.0 / extent, 0.012, 0.12);
                    var low = (float)(0.5 - half);
                    var high = (float)(0.5 + half);
                    using var brush = new LinearGradientBrush(
                        bounds, Color.FromArgb(0, litColor), Color.FromArgb(0, litColor), state.Angle);
                    brush.InterpolationColors = new ColorBlend
                    {
                        Colors = new[]
                        {
                            Color.FromArgb(0, litColor),
                            Color.FromArgb(0, litColor),
                            Color.FromArgb(peakAlpha, litColor),
                            Color.FromArgb(0, litColor),
                            Color.FromArgb(0, litColor),
                        },
                        Positions = new[] { 0f, low, 0.5f, high, 1f },
                    };
                    using var lightPath = SkinGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                    if (lightPath is null) graphics.FillRectangle(brush, bounds);
                    else graphics.FillPath(brush, lightPath);
                    return true;
                }

                // 无运行时光：静态 sheen 也按声明 strength/angle，避免固定 alpha 把高光画成暗带。
                var sheenAlpha = Math.Clamp((int)Math.Round(70 * spec.Strength), 0, 160);
                var sheen = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height / 3));
                using var staticBrush = new LinearGradientBrush(
                    sheen, Color.FromArgb(sheenAlpha, color), Color.FromArgb(0, color), spec.Angle);
                using var path = SkinGeometry.Shaped(sheen,
                    Math.Max(0f, RadiusOf(spec, bounds, materialRadius) - 2f), graphics.DpiX);
                if (path is null) graphics.FillRectangle(staticBrush, sheen);
                else graphics.FillPath(staticBrush, path);
                return true;
            }

            // M6：贴图 / 贴画（资源缺失 ⇒ false ⇒ 进 Skipped，可见）
            case LayerPrimitive.Texture:
            case LayerPrimitive.Decal:
            {
                if (spec.Asset.Length == 0) return false;
                var image = LoadAsset(spec.Asset);
                if (image is null) return false;

                // 九宫格：slice>0 ⇒ 四角不拉伸（边框/卡片贴图的主用法）；否则整图平铺/拉伸。
                if (spec.Slice > 0)
                {
                    DrawNineSlice(graphics, image, bounds, spec.Slice);
                    return true;
                }

                if (spec.Kind == LayerPrimitive.Decal || spec.NoRepeat)
                {
                    graphics.DrawImage(image, bounds);
                    return true;
                }

                using var texture = new TextureBrush(image);
                graphics.FillRectangle(texture, bounds);
                return true;
            }

            default:
                // 声明 supported 却无 case（mask/noise/未知图元）⇒ 显式记账，禁静默当已实现。
                skipped.Add("unimplemented:" + spec.Primitive);
                return false;
        }
    }

    /// <summary>渐变画刷（多色标走 InterpolationColors；单色退化纯色）。</summary>
    private static Brush CreateGradientBrush(Rectangle bounds, ResolvedLayerSpec spec)
    {
        var stops = spec.Gradient.Stops;
        if (stops.Count <= 1)
        {
            return new SolidBrush(Color.FromArgb(stops.Count == 1 ? stops[0].Argb : spec.ColorArgb));
        }

        var brush = new LinearGradientBrush(
            bounds, Color.FromArgb(stops[0].Argb), Color.FromArgb(stops[stops.Count - 1].Argb), spec.Angle);
        if (stops.Count == 2) return brush;

        var colors = new Color[stops.Count];
        var positions = new float[stops.Count];
        for (var i = 0; i < stops.Count; i++)
        {
            colors[i] = Color.FromArgb(stops[i].Argb);
            // 声明位置优先；未声明才均匀分布（位置已在 LayerSpec/GradientSpec 里归一）。
            positions[i] = stops[i].HasPosition ? stops[i].Position : i / (float)(stops.Count - 1);
        }

        brush.InterpolationColors = new ColorBlend { Colors = colors, Positions = positions };
        return brush;
    }

    /// <summary>内环填充（innerShadow / specular inner 共用同一模型口径）。</summary>
    private static void PaintInnerRing(
        Graphics graphics, Rectangle bounds, float radiusDips, float widthDips, Color color)
    {
        var dpi = graphics.DpiX;
        var ringWidth = Math.Max(1f, BorderUnits.DipsToPixels(widthDips, dpi));
        var radiusPx = BorderUnits.DipsToPixels(radiusDips, dpi);
        var outerRect = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var innerRect = new RectangleF(
            outerRect.X + ringWidth, outerRect.Y + ringWidth,
            Math.Max(0.5f, outerRect.Width - ringWidth * 2),
            Math.Max(0.5f, outerRect.Height - ringWidth * 2));
        using var ring = new GraphicsPath(FillMode.Alternate);
        using var outerPath = SkinGeometry.Path(outerRect, CornerRadii.Uniform(radiusPx));
        using var innerPath = SkinGeometry.Path(innerRect,
            CornerRadii.Uniform(Math.Max(0f, radiusPx - ringWidth)));
        ring.AddPath(outerPath, false);
        ring.AddPath(innerPath, false);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, ring);
    }

    /// <summary>
    /// 倒角高光：四边各按光场权重（<see cref="SurfaceLightState.EdgeTop"/>/Left/Right/Bottom）落笔，
    /// 每边都双向收敛（不画贯穿面板的等宽亮带）。光在左上 ⇒ 顶边/左边最亮，右/下边趋近 0。
    /// </summary>
    private static void PaintEdgeHighlight(
        Graphics graphics, Rectangle bounds, ResolvedLayerSpec spec, SurfaceLightState state, float materialRadius)
    {
        var color = Color.FromArgb(spec.ColorArgb);
        var peak = SheenPeak(spec.Strength, 150f, state);
        if (peak <= 0) return;

        var lit = LitColor(color, state);
        var thickness = Math.Clamp((int)Math.Round(spec.Width), 1, 6);
        var inset = 1;
        var saved = graphics.Save();
        GraphicsPath? clip = null;
        try
        {
            clip = SkinGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
            if (clip is not null) graphics.SetClip(clip, CombineMode.Intersect);

            var top = EdgeAlpha(peak, state.EdgeTop);
            if (top > 0)
            {
                // 顶边：中间亮、两端透明（沿倒角渐隐）。
                var rect = new Rectangle(bounds.X + inset, bounds.Y + inset, Math.Max(1, bounds.Width - inset * 2), thickness);
                using var brush = new LinearGradientBrush(
                    rect, Color.FromArgb(0, lit), Color.FromArgb(0, lit), LinearGradientMode.Horizontal);
                brush.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, lit), Color.FromArgb(top, lit), Color.FromArgb(0, lit) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                graphics.FillRectangle(brush, rect);
            }

            var left = EdgeAlpha(peak, state.EdgeLeft);
            if (left > 0)
            {
                // 左倒角：上亮下隐，长度取面板高度的 1/3，避免形成纵向整条。
                var rect = new Rectangle(bounds.X + inset, bounds.Y + inset, thickness, Math.Max(1, bounds.Height / 3));
                using var brush = new LinearGradientBrush(
                    rect, Color.FromArgb(left, lit), Color.FromArgb(0, lit), LinearGradientMode.Vertical);
                graphics.FillRectangle(brush, rect);
            }

            var right = EdgeAlpha(peak, state.EdgeRight);
            if (right > 0)
            {
                // 右倒角与左侧镜像：顶部最亮、向下渐隐（光源同在上方）。
                var rect = new Rectangle(bounds.Right - inset - thickness, bounds.Y + inset, thickness, Math.Max(1, bounds.Height / 3));
                using var brush = new LinearGradientBrush(
                    rect, Color.FromArgb(right, lit), Color.FromArgb(0, lit), LinearGradientMode.Vertical);
                graphics.FillRectangle(brush, rect);
            }

            var bottom = EdgeAlpha(peak, state.EdgeBottom);
            if (bottom > 0)
            {
                var rect = new Rectangle(bounds.X + inset, bounds.Bottom - inset - thickness, Math.Max(1, bounds.Width - inset * 2), thickness);
                using var brush = new LinearGradientBrush(
                    rect, Color.FromArgb(0, lit), Color.FromArgb(0, lit), LinearGradientMode.Horizontal);
                brush.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, lit), Color.FromArgb(bottom, lit), Color.FromArgb(0, lit) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                graphics.FillRectangle(brush, rect);
            }
        }
        finally
        {
            clip?.Dispose();
            graphics.Restore(saved);
        }
    }

    /// <summary>
    /// 径向聚光：中心与形状来自端口（<see cref="SurfaceLightState.SpotX"/>/<see cref="SurfaceLightState.SpotY"/>），
    /// 强度来自矩阵镜面权重；中心亮、随距离衰减到 0，不产生「一束光柱」。
    /// </summary>
    private static void PaintSpotHighlight(
        Graphics graphics, Rectangle bounds, ResolvedLayerSpec spec, SurfaceLightState state, float materialRadius)
    {
        var color = Color.FromArgb(spec.ColorArgb);
        var peak = SheenPeak(spec.Strength, 90f, state);
        if (peak <= 0) return;

        var lit = LitColor(color, state);
        var radius = Math.Max(8f, Math.Min(bounds.Width, bounds.Height) * Math.Clamp(spec.Radius, 0.25f, 1.2f));
        // 光场已给出光斑中心（左上光源 ⇒ 归一化 y < 0.5），载体不再按角度自行推算。
        var centerX = bounds.X + bounds.Width * Math.Clamp(state.SpotX, 0.05f, 0.95f);
        var centerY = bounds.Y + bounds.Height * Math.Clamp(state.SpotY, 0.05f, 0.95f);
        var size = (int)Math.Round(radius * 2);
        var target = new Rectangle((int)Math.Round(centerX - radius), (int)Math.Round(centerY - radius), size, size);

        var saved = graphics.Save();
        GraphicsPath? clip = null;
        try
        {
            clip = SkinGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
            if (clip is not null) graphics.SetClip(clip, CombineMode.Intersect);

            // 预渲染的 128px 径向 alpha 掩膜 + ColorMatrix 着色：一次缩放贴图，避免每帧 PathGradientBrush。
            var mask = SpotMask();
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix(new[]
            {
                new[] { lit.R / 255f, 0f, 0f, 0f, 0f },
                new[] { 0f, lit.G / 255f, 0f, 0f, 0f },
                new[] { 0f, 0f, lit.B / 255f, 0f, 0f },
                new[] { 0f, 0f, 0f, peak / 255f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f },
            }));
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(
                mask, target, 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel, attributes);
        }
        finally
        {
            clip?.Dispose();
            graphics.Restore(saved);
        }
    }

    private static readonly object SpotMaskSync = new();
    private static Bitmap? _spotMask;

    /// <summary>白色径向 alpha 掩膜（中心 255 → 边缘 0，二次衰减），进程内复用。</summary>
    private static Bitmap SpotMask()
    {
        if (_spotMask is not null) return _spotMask;
        lock (SpotMaskSync)
        {
            if (_spotMask is not null) return _spotMask;
            const int side = 128;
            var bitmap = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            var center = (side - 1) / 2.0;
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var dx = (x - center) / center;
                    var dy = (y - center) / center;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    var alpha = distance >= 1.0 ? 0 : (int)Math.Round(255 * (1 - distance) * (1 - distance));
                    bitmap.SetPixel(x, y, Color.FromArgb(alpha, Color.White));
                }
            }

            _spotMask = bitmap;
            return bitmap;
        }
    }

    /// <summary>按时点暖度把高光/背景染向暖白（只调颜色，不改材质参数）。</summary>
    private static Color Warm(Color color, float warmth)
    {
        if (warmth <= 0) return color;
        var t = Math.Clamp(warmth, 0f, 1f) * 0.35f;
        return Color.FromArgb(
            color.A,
            (int)(color.R + (255 - color.R) * t),
            (int)(color.G + (224 - color.G) * t),
            (int)(color.B + (170 - color.B) * t));
    }

    /// <summary>
    /// 高光峰值（**全局光态驱动**）：矩阵镜面权重决定强弱，材质声明的 <c>strength</c> 只作上限。
    /// 无光场（<see cref="SurfaceLightState.HasValue"/>=false）⇒ 0，保持声明值的静态路径。
    /// </summary>
    private static int SheenPeak(float declaredStrength, float baseAlpha, SurfaceLightState state)
    {
        if (!state.HasValue) return 0;
        var specular = Math.Clamp(state.Specular, 0f, 1f);
        var gain = 0.25f + 0.75f * specular;
        var cap = Math.Clamp(declaredStrength, 0f, 1f);
        return Math.Clamp((int)Math.Round(baseAlpha * gain * cap), 0, 200);
    }

    /// <summary>单边高光 alpha（权重已由端口按最亮边归一到 1）。</summary>
    private static int EdgeAlpha(int peak, float weight)
        => Math.Clamp((int)Math.Round(peak * Math.Clamp(weight, 0f, 1f)), 0, 200);

    /// <summary>高光色：暖度 + 环境色；两者都只做轻微偏置（材质声明仍是色彩权威）。</summary>
    private static Color LitColor(Color color, SurfaceLightState state)
    {
        var lit = Warm(color, state.Warmth);
        if (!state.HasValue) return lit;
        var tint = Color.FromArgb(state.TintArgb);
        const float mix = 0.15f;
        return Color.FromArgb(
            lit.A,
            (int)Math.Round(lit.R * (1 - mix) + tint.R * mix),
            (int)Math.Round(lit.G * (1 - mix) + tint.G * mix),
            (int)Math.Round(lit.B * (1 - mix) + tint.B * mix));
    }

    /// <summary>
    /// 降采样再升采样近似模糊（GDI+ 无原生高斯；radius 越大采样越粗）。
    /// 只处理快照位图，不改变任何材质参数契约。
    /// </summary>
    private static Bitmap Blur(Image source, int radius)
    {
        if (radius <= 0)
        {
            var copy = new Bitmap(source.Width, source.Height);
            using var copyGraphics = Graphics.FromImage(copy);
            copyGraphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            return copy;
        }

        var factor = Math.Max(2, radius / 4); // 18 ⇒ 4（约 4×4 降采样）
        var smallWidth = Math.Max(1, source.Width / factor);
        var smallHeight = Math.Max(1, source.Height / factor);
        var small = new Bitmap(smallWidth, smallHeight);
        try
        {
            using (var graphics = Graphics.FromImage(small))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, smallWidth, smallHeight));
            }

            var blurred = new Bitmap(source.Width, source.Height);
            using (var graphics = Graphics.FromImage(blurred))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(small, new Rectangle(0, 0, source.Width, source.Height));
            }

            return blurred;
        }
        finally
        {
            small.Dispose();
        }
    }

    /// <summary>资源装载（进程内缓存；装载失败 ⇒ null 且**不缓存**，文件/资源后补可重试）。</summary>
    private static Image? LoadAsset(string asset)
    {
        lock (AssetCache)
        {
            if (AssetCache.TryGetValue(asset, out var cached)) return cached;
        }

        var image = _assetLoader is not null ? InvokeLoader(_assetLoader, asset) : LoadFromRoot(asset);
        if (image is null) return null;
        lock (AssetCache) AssetCache[asset] = image;
        return image;
    }

    private static Image? InvokeLoader(Func<string, Image?> loader, string asset)
    {
        try
        {
            return loader(asset);
        }
        catch (Exception)
        {
            // 装载异常 ⇒ 视为缺件（走 Skipped 可见路径，不抛给绘制线程）。
            return null;
        }
    }

    /// <summary>按资源根从磁盘装载（相对路径；绝对路径拒绝；缺件 ⇒ null）。</summary>
    private static Image? LoadFromRoot(string asset)
    {
        var root = AssetRoot;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(asset)) return null;
        if (Path.IsPathRooted(asset)) return null;

        try
        {
            var path = Path.GetFullPath(Path.Combine(root, asset.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(path)) return null;
            // 先读字节再解码：避免 Image.FromFile 独占文件句柄（资源被替换后旧图仍锁住磁盘文件）。
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>九宫格绘制：四角与四边按源切片缩放，中心拉伸（slice 夹到半边长内）。</summary>
    private static void DrawNineSlice(Graphics graphics, Image image, Rectangle bounds, int slice)
    {
        var width = image.Width;
        var height = image.Height;
        var cut = Math.Min(slice, Math.Min(width, height) / 2);
        if (cut <= 0)
        {
            graphics.DrawImage(image, bounds);
            return;
        }

        var sourceX = new[] { 0, cut, width - cut };
        var sourceW = new[] { cut, width - cut * 2, cut };
        var sourceY = new[] { 0, cut, height - cut };
        var sourceH = new[] { cut, height - cut * 2, cut };
        var targetX = new[] { bounds.X, bounds.X + cut, bounds.Right - cut };
        var targetY = new[] { bounds.Y, bounds.Y + cut, bounds.Bottom - cut };
        var targetW = new[] { cut, Math.Max(1, bounds.Width - cut * 2), cut };
        var targetH = new[] { cut, Math.Max(1, bounds.Height - cut * 2), cut };

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if (sourceW[column] <= 0 || sourceH[row] <= 0) continue;
                graphics.DrawImage(
                    image,
                    new Rectangle(targetX[column], targetY[row], targetW[column], targetH[row]),
                    new Rectangle(sourceX[column], sourceY[row], sourceW[column], sourceH[row]),
                    GraphicsUnit.Pixel);
            }
        }
    }

    /// <summary>层生效圆角（声明优先，未声明沿用材质级最大半径；≤0 ⇒ 0 = 直角）。唯一推导。</summary>
    private static float RadiusOf(ResolvedLayerSpec spec, Rectangle bounds, float materialRadius)
    {
        var radius = spec.CornerRadius(materialRadius);
        if (radius <= 0) return 0f;
        return radius; // DIP；设备像素上限统一由 SkinGeometry 在换算后收敛（不在 DIP/px 混算）
    }

    /// <summary>提亮（磨砂高光；amount 0..1）。</summary>
    private static Color Lighten(Color color, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        int Mix(int channel) => (int)Math.Round(channel + (255 - channel) * t);
        return Color.FromArgb(color.A, Mix(color.R), Mix(color.G), Mix(color.B));
    }

    /// <summary>
    /// 材质底色（首个 fill/gradient 层的首个色）——**兼容保留**；属性通道请走
    /// <c>WinFormsPropertyProjector</c>（会做不透明化，避免 Control.BackColor 拒绝 alpha）。
    /// </summary>
    /// <param name="material">已解析材质。</param>
    /// <param name="color">首个可用底色（可能含 alpha）。</param>
    [Obsolete("属性通道统一经 WinFormsPropertyProjector（不透明化）；本方法仅为二进制兼容保留")]
    public static bool TryGetBaseColor(ResolvedMaterial? material, out Color color)
    {
        color = Color.Empty;
        if (material is null) return false;

        foreach (var layerSpec in ResolvedLayerSpecProjection.Parse(material.Layers).Specs)
        {
            if (layerSpec.Kind is not (LayerPrimitive.Fill or LayerPrimitive.Gradient) || !layerSpec.HasColor) continue;
            var candidate = Color.FromArgb(layerSpec.ColorArgb);
            if (candidate.A > 0)
            {
                color = candidate;
                return true;
            }
        }

        return false;
    }
}
