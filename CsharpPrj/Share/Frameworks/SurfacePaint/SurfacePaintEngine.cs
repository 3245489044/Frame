#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using MathematicalAlgorithms.Lighting;
using MathematicalAlgorithms.Surface;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Raster;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.SurfacePaint;

/// <summary>
/// 表面绘制引擎（**载体无关**）：已解析材质层栈 → GDI+ 图元。
/// <para>支持 fill/gradient/stroke/shadow/innerShadow/backdrop/specular；texture/decal 缺资源进 Skipped（禁静默纯色）。</para>
/// <para>backdrop：有宿主快照时「采样背后 + 模糊」；无快照才回退近似并记账 <c>backdrop:emulated</c>，禁伪装原生模糊。</para>
/// <para>宿主（WinForms 等）只做生命周期与参数装配；排序/几何/颜色/光栅全部在本程序集与 Plate.Surface / Share.SurfacePaint.Raster。</para>
/// </summary>
public static class SurfacePaintEngine
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

    /// <summary>绘制一次材质（背景）并回传结构化报告（载体无关）。</summary>
    /// <param name="graphics">目标画布。</param>
    /// <param name="bounds">目标矩形。</param>
    /// <param name="material">已解析材质（null ⇒ 不绘制，报告 Skipped）。</param>
    /// <param name="backdrop">面板背后的快照（宿主提供；null ⇒ backdrop 层走近似并记账）。</param>
    /// <param name="light">运行时光（只作用于 specular）。</param>
    /// <param name="scope">绘制范围（All / Static / Dynamic）。</param>
    /// <param name="telemetry">可选成本采样钩子（null ⇒ 不采样）。</param>
    public static SurfacePaintReport Paint(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, Image? backdrop,
        SurfaceLightState? light, SurfacePaintScope scope, SurfacePaintTelemetry? telemetry = null)
    {
        var previous = SurfacePaintTelemetry.Push(telemetry ?? SurfacePaintTelemetry.None);
        try
        {
            return PaintCore(graphics, bounds, material, backdrop, light, scope);
        }
        finally
        {
            SurfacePaintTelemetry.Pop(previous);
        }
    }

    private static SurfacePaintReport PaintCore(
        Graphics graphics, Rectangle bounds, ResolvedMaterial? material, Image? backdrop,
        SurfaceLightState? light, SurfacePaintScope scope)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (material is null)
        {
            // 无材质不是「画成功」：报告必须显式 Skipped（禁空列表冒充已绘）。
            return new SurfacePaintReport(0, 0, 0, 0, 0, new[] { "no-material" });
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new SurfacePaintReport(0, 0, 0, 0, 0, new[] { "empty-bounds" });
        }

        var details = new List<string>();
        var ok = 0;
        var degraded = 0;
        var skippedLayers = 0;
        var failed = 0;
        var total = 0;
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // K1：整组层一次解析并缓存（同一层栈跨帧复用），解析问题随 spec 携带；
            // 几何单源（按材质）：圆角由层声明提供，层未声明时沿用同材质内最大半径，
            // 避免「填充直角 + 描边圆角」的错位（材质 = 一个整体形状，不是各层各画各的）。
            var specSet = ResolvedLayerSpecProjection.ParseCached(material.Layers);
            var materialRadius = specSet.MaterialRadius;

            // 落笔序列单源：Share.SurfacePaintPlan（相位→Z），载体不再自行排序。
            var ordered = SurfacePaintPlan.Order(
                specSet.Specs,
                includeStatic: scope != SurfacePaintScope.Dynamic,
                includeDynamic: scope != SurfacePaintScope.Static);
            total = ordered.Count;
            foreach (var spec in ordered)
            {
                // 明细由 PaintLayer 记账（unresolved-value:<raw>）；未落笔时再补「层身份」项。
                var layerBegin = SurfacePaintTelemetry.Current.Begin();
                var degradedLayer = false;
                var outcome = SurfacePaintOutcome.Skipped;
                try
                {
                    outcome = PaintLayer(graphics, bounds, spec, materialRadius, details,
                        backdrop, light, out degradedLayer)
                        ? (degradedLayer ? SurfacePaintOutcome.Degraded : SurfacePaintOutcome.Ok)
                        : SurfacePaintOutcome.Skipped;
                }
                catch (Exception ex)
                {
                    // K6：单层异常记 Failed 并继续画后续层；原因进明细供探针/门禁读取（不静默）。
                    outcome = SurfacePaintOutcome.Failed;
                    failed++;
                    details.Add("failed:" + spec.Primitive + ":" + ex.GetType().Name + ":" + ex.Message);
                }

                SurfacePaintTelemetry.Current.RecordLayer(spec.Primitive, layerBegin);
                switch (outcome)
                {
                    case SurfacePaintOutcome.Ok: ok++; break;
                    case SurfacePaintOutcome.Degraded: degraded++; break;
                    case SurfacePaintOutcome.Failed: break; // 异常分支已计数
                    default:
                        skippedLayers++;
                        // 未落笔必须同时给「层身份 + 具体原因」：身份供断言按图元定位，原因供人看。
                        details.Add(spec.Primitive + ":" + spec.Role);
                        break;
                }
            }
        }
        finally
        {
            graphics.Restore(state);
        }

        return new SurfacePaintReport(total, ok, degraded, skippedLayers, failed, details);
    }

    private static bool PaintLayer(
        Graphics graphics, Rectangle bounds, ResolvedLayerSpec spec, float materialRadius, List<string> skipped,
        Image? backdrop, SurfaceLightState? light, out bool degraded)
    {
        degraded = false;
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
            // 描边取值与属性通道/边框宿主同口径：末条「可见宽度」描边（不是盲取最后一条）。
            var strokes = spec.Border.Strokes;
            var stroke = strokes[strokes.Count - 1];
            for (var index = strokes.Count - 1; index >= 0; index--)
            {
                if (!strokes[index].HasVisibleWidth) continue;
                stroke = strokes[index];
                break;
            }

            SurfaceBorderPainter.DrawStroke(graphics, bounds, stroke, graphics.DpiX);
            return true;
        }

        var valid = spec.Issues.Count == 0;

        switch (spec.Kind)
        {
            case LayerPrimitive.Fill:
            {
                if (!valid) return false;
                using var brush = new SolidBrush(Color.FromArgb(spec.ColorArgb));
                using var path = SurfaceGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                if (path is null) graphics.FillRectangle(brush, bounds);
                else graphics.FillPath(brush, path);
                return true;
            }

            case LayerPrimitive.Gradient:
            {
                if (!valid || spec.Gradient.Stops.Count == 0) return false;
                using var brush = CreateGradientBrush(bounds, spec);
                using var path = SurfaceGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                if (path is null) graphics.FillRectangle(brush, bounds);
                else graphics.FillPath(brush, path);
                return true;
            }

            case LayerPrimitive.Shadow:
            {
                if (!valid) return false;
                var color = Color.FromArgb(spec.ColorArgb);
                // 阴影方位与全帧光源同源：声明只给「离面距离」，方向由光场 ShadowX/Y 决定（光在左上 ⇒ 影在右下）。
                SurfaceLayerMath.ShadowOffset(
                    spec.OffsetX, spec.OffsetY,
                    light is { HasValue: true }, light?.ShadowX ?? 0f, light?.ShadowY ?? 0f,
                    out var offsetX, out var offsetY);

                // E2 单次落点：阴影必须按 blur 生成。旧实现只用 blur 算 alpha + SolidBrush 平移圆角矩形，
                // 偏移被控件位图裁掉后只剩内侧一条硬边色块（F5）。
                var shadow = SurfaceLayerMath.ShadowBoxSoft(
                    bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    offsetX, offsetY, spec.Spread, spec.Blur);
                var rect = new RectangleF(
                    shadow.Box.X, shadow.Box.Y, shadow.Box.Width, shadow.Box.Height);
                var blurPx = SurfaceLayerMath.ShadowBlurPx(spec.Blur);
                var width = Math.Max(1, (int)Math.Ceiling(shadow.Box.Width));
                var height = Math.Max(1, (int)Math.Ceiling(shadow.Box.Height));
                var radius = RadiusOf(spec, bounds, materialRadius);

                // 离屏面 + 模糊：把边缘摊开；失败不画（显式 shadow-degraded），禁回退成硬边。
                Bitmap? layer = null;
                try
                {
                    layer = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    using (var layerGraphics = Graphics.FromImage(layer))
                    {
                        layerGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                        var local = new RectangleF(0, 0, width, height);
                        using var brush = new SolidBrush(Color.FromArgb(shadow.Alpha, color));
                        using var path = SurfaceGeometry.Shaped(local, radius, graphics.DpiX);
                        if (path is null) layerGraphics.FillRectangle(brush, local);
                        else layerGraphics.FillPath(brush, path);
                    }

                    using var blurred = SurfaceRaster.Blur(layer, blurPx);
                    // 阴影只落在面外（挖空本控件矩形）：否则模糊后的整块暗面会压在卡面下，
                    // 半透明填充被二次压暗（LightTier 面层亮度判据失败）。
                    var saved = graphics.Save();
                    try
                    {
                        var target = Rectangle.Round(rect);
                        if (target.IntersectsWith(bounds))
                        {
                            using var region = new Region(target);
                            region.Exclude(bounds);
                            graphics.SetClip(region, CombineMode.Replace);
                        }

                        graphics.DrawImage(blurred, rect);
                    }
                    finally
                    {
                        graphics.Restore(saved);
                    }
                }
                catch (Exception ex)
                {
                    degraded = true;
                    skipped.Add(SurfaceReasonCode.Of(SurfaceReason.ShadowDegraded) + ":" + ex.GetType().Name);
                    return false;
                }
                finally
                {
                    layer?.Dispose();
                }

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
                using var path = SurfaceGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                if (backdrop is not null && backdrop.Width > 0 && backdrop.Height > 0)
                {
                    // 真「采样背后 + 模糊」：只做 blur（与 CSS backdrop-filter 同一权威）；
                    // 染色由 fill 层的声明 alpha 承担，不再用固定 alpha 覆盖声明值。
                    var saved = graphics.Save();
                    try
                    {
                        if (path is not null) graphics.SetClip(path);
                        var blurBegin = SurfacePaintTelemetry.Current.Begin();
                        using var blurred = SurfaceRaster.Blur(
                            backdrop, SurfaceLayerMath.RoundedNonNegative(spec.Blur));
                        SurfacePaintTelemetry.Current.RecordBlur(blurBegin);
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
                var bottomAlpha = SurfaceLayerMath.BackdropBottomAlpha(topAlpha);
                using var brush = new LinearGradientBrush(
                    bounds, Color.FromArgb(topAlpha, Lighten(color, 0.28)), Color.FromArgb(bottomAlpha, color),
                    LinearGradientMode.Vertical);
                if (path is null) graphics.FillRectangle(brush, bounds);
                else graphics.FillPath(brush, path);
                skipped.Add("backdrop:emulated");
                degraded = true; // K6：近似落笔 = Degraded（有像素但语义不完整）
                return true;
            }

            case LayerPrimitive.Specular:
            {
                if (!valid) return false;
                var color = Color.FromArgb(spec.ColorArgb);
                // K6：LightState.Unavailable（HasValue=false）不得当作「有光但权重 0」；
                // 保持声明 strength/angle 的静态 sheen，避免高光整层消失。
                // 指针光与环境光帧解耦：无环境帧但存在指针发射体时，仍按 spot 成员落笔（环境层不落笔）。
                if (light is { } state && (state.HasValue || (spec.Mode == "spot" && HasPointerEmitter(state))))
                {
                    if (spec.Mode == "inner")
                    {
                        // 模型形态 <c>inner</c>：与 innerShadow 同一环填充（不再被误当成 band）。
                        PaintInnerRing(graphics, bounds, RadiusOf(spec, bounds, materialRadius), spec.Width, color);
                        return true;
                    }

                    if (spec.Mode == "edge" || spec.Mode == "spectral")
                    {
                        // spectral 与 edge 共用同一落笔路径（每边 1 次 FillRectangle，绘制调用数不增）；
                        // 是否走色相环由 PaintEdgeHighlight 依据 Mode/SpectralSpan 判定。
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
                    var dpiScale = graphics.DpiX / 96f;
                    // 条带中心 = 世界固定锚点（屏幕 DIP）：几何单源 = Plate.Mathematics。
                    var band = SurfaceLayerMath.SpecularBand(
                        bounds.Width, bounds.Height, state.Angle,
                        state.SpotLocalX, state.SpotLocalY, state.SpotRadiusDips, dpiScale);
                    var bandBounds = new Rectangle(
                        bounds.X + band.ShiftX, bounds.Y + band.ShiftY, bounds.Width, bounds.Height);
                    using var brush = new LinearGradientBrush(
                        bandBounds, Color.FromArgb(0, litColor), Color.FromArgb(0, litColor), state.Angle);
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
                        Positions = new[] { 0f, band.Low, 0.5f, band.High, 1f },
                    };
                    using var lightPath = SurfaceGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
                    if (lightPath is null) graphics.FillRectangle(brush, bounds);
                    else graphics.FillPath(brush, lightPath);
                    return true;
                }

                // 无运行时光：静态 sheen 也按声明 strength/angle，避免固定 alpha 把高光画成暗带。
                var sheenAlpha = SurfaceLayerMath.StaticSheenAlpha(spec.Strength);
                var sheen = new Rectangle(
                    bounds.X, bounds.Y, bounds.Width, SurfaceLayerMath.StaticSheenHeight(bounds.Height));
                using var staticBrush = new LinearGradientBrush(
                    sheen, Color.FromArgb(sheenAlpha, color), Color.FromArgb(0, color), spec.Angle);
                using var path = SurfaceGeometry.Shaped(sheen,
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
        var ringWidth = BorderUnits.DipsToPixels(widthDips, dpi);
        var radiusPx = BorderUnits.DipsToPixels(radiusDips, dpi);
        var geometry = SurfaceLayerMath.InnerRing(
            bounds.X, bounds.Y, bounds.Width, bounds.Height, ringWidth, radiusPx);
        var innerRect = new RectangleF(
            geometry.Inner.X, geometry.Inner.Y, geometry.Inner.Width, geometry.Inner.Height);
        using var ring = new GraphicsPath(FillMode.Alternate);
        using var outerPath = SurfaceGeometry.Path(
            new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            CornerRadii.Uniform(geometry.OuterRadiusPx));
        using var innerPath = SurfaceGeometry.Path(innerRect,
            CornerRadii.Uniform(geometry.InnerRadiusPx));
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
        // 环的基色 = 材质令牌色（glass_edge_light）的 HSV 色相；两载体共用同一令牌来源。
        // 近白令牌保持旧公式，深档逐像素零差异。
        var tokenDriven = SaturationOf(color) >= 0.2f;
        var baseHue = HueOf(color);
        // spectral：颜色来源换成色相环（ColorBlend 多停靠点），仍为每条边 1 次 FillRectangle。
        var spectral = spec.Mode == "spectral" && spec.SpectralSpan > 0f;
        var thickness = SurfaceLayerMath.EdgeThickness(spec.Width);
        var inset = 1;
        var saved = graphics.Save();
        GraphicsPath? clip = null;
        try
        {
            clip = SurfaceGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
            if (clip is not null) graphics.SetClip(clip, CombineMode.Intersect);

            var top = EdgeAlpha(peak, state.EdgeTop);
            if (top > 0)
            {
                // 顶边：中间亮、两端透明（沿倒角渐隐）。周长参数 t 顺时针 0→0.25。
                var rect = new Rectangle(bounds.X + inset, bounds.Y + inset, Math.Max(1, bounds.Width - inset * 2), thickness);
                if (spectral)
                {
                    FillSpectral(graphics, rect, LinearGradientMode.Horizontal,
                        SpectralColors(state, spec.SpectralSpan, baseHue, tokenDriven, 0f, 0.25f, top,
                            new[] { 0f, 0.75f, 1f, 0.75f, 0f }));
                }
                else
                {
                    using var brush = new LinearGradientBrush(
                        rect, Color.FromArgb(0, lit), Color.FromArgb(0, lit), LinearGradientMode.Horizontal);
                    brush.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, lit), Color.FromArgb(top, lit), Color.FromArgb(0, lit) },
                        Positions = new[] { 0f, 0.5f, 1f },
                    };
                    graphics.FillRectangle(brush, rect);
                }
            }

            var left = EdgeAlpha(peak, state.EdgeLeft);
            if (left > 0)
            {
                // 左倒角：上亮下隐，长度取面板高度的 1/3，避免形成纵向整条。周长 t 由 1（上）到 0.75（下）。
                var rect = new Rectangle(bounds.X + inset, bounds.Y + inset, thickness, SurfaceLayerMath.EdgeRunLength(bounds.Height));
                if (spectral)
                {
                    FillSpectral(graphics, rect, LinearGradientMode.Vertical,
                        SpectralColors(state, spec.SpectralSpan, baseHue, tokenDriven, 1f, 0.75f, left,
                            new[] { 1f, 0.7f, 0.3f, 0f, 0f }));
                }
                else
                {
                    using var brush = new LinearGradientBrush(
                        rect, Color.FromArgb(left, lit), Color.FromArgb(0, lit), LinearGradientMode.Vertical);
                    graphics.FillRectangle(brush, rect);
                }
            }

            var right = EdgeAlpha(peak, state.EdgeRight);
            if (right > 0)
            {
                // 右倒角与左侧镜像：顶部最亮、向下渐隐（光源同在上方）。周长 t 由 0.25（上）到 0.5（下）。
                var rect = new Rectangle(bounds.Right - inset - thickness, bounds.Y + inset, thickness, SurfaceLayerMath.EdgeRunLength(bounds.Height));
                if (spectral)
                {
                    FillSpectral(graphics, rect, LinearGradientMode.Vertical,
                        SpectralColors(state, spec.SpectralSpan, baseHue, tokenDriven, 0.25f, 0.5f, right,
                            new[] { 1f, 0.7f, 0.3f, 0f, 0f }));
                }
                else
                {
                    using var brush = new LinearGradientBrush(
                        rect, Color.FromArgb(right, lit), Color.FromArgb(0, lit), LinearGradientMode.Vertical);
                    graphics.FillRectangle(brush, rect);
                }
            }

            var bottom = EdgeAlpha(peak, state.EdgeBottom);
            if (bottom > 0)
            {
                // 底边周长反向：左端 t=0.75、右端 t=0.5。
                var rect = new Rectangle(bounds.X + inset, bounds.Bottom - inset - thickness, Math.Max(1, bounds.Width - inset * 2), thickness);
                if (spectral)
                {
                    FillSpectral(graphics, rect, LinearGradientMode.Horizontal,
                        SpectralColors(state, spec.SpectralSpan, baseHue, tokenDriven, 0.75f, 0.5f, bottom,
                            new[] { 0f, 0.75f, 1f, 0.75f, 0f }));
                }
                else
                {
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
        }
        finally
        {
            clip?.Dispose();
            graphics.Restore(saved);
        }
    }

    /// <summary>沿周长参数 t0→t1 取色相环颜色，并按 alpha 剖面调制（不含额外绘制）。</summary>
    private static Color[] SpectralColors(
        SurfaceLightState state, float span, float baseHue, bool tokenDriven, float t0, float t1, int peak, float[] alphaProfile)
    {
        var colors = new Color[alphaProfile.Length];
        var last = alphaProfile.Length - 1;
        for (var i = 0; i <= last; i++)
        {
            var t = t0 + (t1 - t0) * (i / (float)last);
            var argb = tokenDriven
                ? IridescenceRamp.CenteredArgbAt(state.IridescencePhase, t, span, baseHue)
                : IridescenceRamp.LegacyArgbAt(state.IridescencePhase, t, span);
            colors[i] = Color.FromArgb(
                SurfaceShadingMath.SpectralStop(peak, alphaProfile[i]),
                Color.FromArgb(argb));
        }

        return colors;
    }

    /// <summary>RGB → HSV 色相（0..360；两载体环基色的同一取值口径）。</summary>
    private static float HueOf(Color color) => SurfaceShadingMath.HueOf(color.ToArgb());

    /// <summary>RGB → HSV 饱和度（0..1；用于区分彩色令牌与深档近白令牌）。</summary>
    private static float SaturationOf(Color color) => SurfaceShadingMath.SaturationOf(color.ToArgb());

    /// <summary>把色相环停靠点装进 <see cref="ColorBlend"/>，一次填充整条边。</summary>
    private static void FillSpectral(Graphics graphics, Rectangle rect, LinearGradientMode mode, Color[] colors)
    {
        using var brush = new LinearGradientBrush(rect, colors[0], colors[^1], mode);
        var positions = new float[colors.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            positions[i] = i / (float)(colors.Length - 1);
        }

        brush.InterpolationColors = new ColorBlend { Colors = colors, Positions = positions };
        graphics.FillRectangle(brush, rect);
    }

    /// <summary>
    /// 径向聚光：环境光与指针光都是**发射体成员**，各自用本成员的局部位置/半径绘制。
    /// <para>环境光中心来自 <see cref="SurfaceLightState.SpotLocalX"/>/<see cref="SurfaceLightState.SpotLocalY"/>；
    /// 指针光中心来自 <see cref="SurfaceLightEmitter.LocalX"/>/<see cref="SurfaceLightEmitter.LocalY"/>。
    /// 两者都走同一个 <see cref="SurfaceLayerMath.SpotGeometry"/>，不再把指针混进归一化 SpotX/SpotY。</para>
    /// </summary>
    private static void PaintSpotHighlight(
        Graphics graphics, Rectangle bounds, ResolvedLayerSpec spec, SurfaceLightState state, float materialRadius)
    {
        // 指针光独立于环境光帧：无环境帧但有指针发射体时仍要落笔（环境段会被 envPeak=0 跳过）。
        if (!state.HasValue && !HasPointerEmitter(state)) return;

        var color = Color.FromArgb(spec.ColorArgb);
        var lit = LitColor(color, state);
        var dpiScale = graphics.DpiX / 96f;

        // 成员一：环境光（照度权重 Shade 驱动；无指针时行为与历史一致）。
        var envPeak = SheenPeak(spec.Strength, 90f, state);
        if (envPeak > 0)
        {
            PaintSpotAt(
                graphics, bounds, spec, materialRadius, lit, envPeak,
                state.SpotLocalX, state.SpotLocalY, state.SpotRadiusDips,
                state.SpotX, state.SpotY, dpiScale);
        }

        // 成员二：指针光（独立成员；权重来自 light_pointer_weight，不乘环境 Shade）。
        if (state.Emitters is not { Count: > 0 } emitters) return;
        for (var i = 0; i < emitters.Count; i++)
        {
            var emitter = emitters[i];
            if (emitter.Kind != SurfaceLightEmitterKind.Pointer || emitter.Weight <= 0f || emitter.RadiusDips <= 0f) continue;

            var peak = (int)Math.Round(
                SurfaceShadingMath.SheenPeak(90f, spec.Strength, state.Specular, 1f) * emitter.Weight);
            if (peak <= 0) continue;

            PaintSpotAt(
                graphics, bounds, spec, materialRadius, lit, peak,
                emitter.LocalX, emitter.LocalY, emitter.RadiusDips,
                emitter.LocalX, emitter.LocalY, dpiScale);
        }
    }

    /// <summary>按单个发射体画一次光斑（几何与着色单源）。</summary>
    private static void PaintSpotAt(
        Graphics graphics,
        Rectangle bounds,
        ResolvedLayerSpec spec,
        float materialRadius,
        Color lit,
        int peak,
        float localX,
        float localY,
        float radiusDips,
        float normalizedX,
        float normalizedY,
        float dpiScale)
    {
        // 光斑中心 = 发射体在**控件本地坐标系**下的位置（DIP，非百分比）；
        // 控件移动时局部位置同步反向变化，屏幕上光斑不动。几何单源 = Plate.Mathematics。
        var spot = SurfaceLayerMath.SpotGeometry(
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            normalizedX, normalizedY, localX, localY,
            radiusDips, spec.Radius, dpiScale);

        // 光斑完全落在面板外 ⇒ 不落笔（避免无谓的大位图/Matrix 运算）。
        if (!SurfaceLayerMath.SpotVisible(
                spot.TargetX, spot.TargetY, spot.TargetSize,
                bounds.Left, bounds.Top, bounds.Right, bounds.Bottom))
        {
            return;
        }

        var target = new Rectangle(spot.TargetX, spot.TargetY, spot.TargetSize, spot.TargetSize);

        var saved = graphics.Save();
        GraphicsPath? clip = null;
        try
        {
            clip = SurfaceGeometry.Shaped(bounds, RadiusOf(spec, bounds, materialRadius), graphics.DpiX);
            if (clip is not null) graphics.SetClip(clip, CombineMode.Intersect);

            // 预渲染的 128 像素 径向 alpha 掩膜 + ColorMatrix 着色：一次缩放贴图，避免每帧 PathGradientBrush。
            var mask = SurfaceRaster.SpotMask();
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

    /// <summary>是否存在可落笔的指针发射体（与 <see cref="SurfaceLightState.HasValue"/> 无关，指针光不依赖环境光帧）。</summary>
    private static bool HasPointerEmitter(SurfaceLightState state)
    {
        if (state.Emitters is not { Count: > 0 } emitters) return false;
        for (var i = 0; i < emitters.Count; i++)
        {
            var emitter = emitters[i];
            if (emitter.Kind == SurfaceLightEmitterKind.Pointer && emitter.Weight > 0f && emitter.RadiusDips > 0f)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 高光峰值（**全局光态驱动**）：矩阵镜面权重决定强弱，材质声明的 <c>strength</c> 只作上限。
    /// 无光场（<see cref="SurfaceLightState.HasValue"/>=false）⇒ 0，保持声明值的静态路径。
    /// </summary>
    private static int SheenPeak(float declaredStrength, float baseAlpha, SurfaceLightState state)
    {
        if (!state.HasValue) return 0;
        // 公式唯一实现：MathematicalAlgorithms.Lighting.SurfaceShadingMath（CSS 峰值同源）。
        return SurfaceShadingMath.SheenPeak(baseAlpha, declaredStrength, state.Specular, state.Shade);
    }

    /// <summary>单边高光 alpha（权重已由端口按最亮边归一到 1）。</summary>
    private static int EdgeAlpha(int peak, float weight) => SurfaceShadingMath.EdgeAlpha(peak, weight);

    /// <summary>
    /// 高光色：暖度 + 环境色；两者都只做轻微偏置（材质声明仍是色彩权威）。
    /// <para>
    /// **已知不对称**：GDI+ 会在声明色上再混 15% 光照 tint，CSS 载体只叠 <c>SheenAlpha</c> 的峰值 alpha、
    /// 不混 tint。两载体的**峰值（亮度）同源**，颜色仍差这一档；不要把两边当作同色。
    /// </para>
    /// </summary>
    private static Color LitColor(Color color, SurfaceLightState state)
        => Color.FromArgb(SurfaceShadingMath.LitColor(
            color.ToArgb(), state.Warmth, state.TintArgb, state.HasValue));

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
        var cut = SurfaceLayerMath.NineSliceCut(slice, width, height);
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
        var targetW = new[] { cut, SurfaceLayerMath.NineSliceCenter(bounds.Width, cut), cut };
        var targetH = new[] { cut, SurfaceLayerMath.NineSliceCenter(bounds.Height, cut), cut };

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

    /// <summary>提亮（磨砂高光；amount 0..1）。公式单源 = <see cref="SurfaceLayerMath"/>。</summary>
    private static Color Lighten(Color color, double amount)
        => Color.FromArgb(
            color.A,
            SurfaceLayerMath.LightenChannel(color.R, amount),
            SurfaceLayerMath.LightenChannel(color.G, amount),
            SurfaceLayerMath.LightenChannel(color.B, amount));

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
