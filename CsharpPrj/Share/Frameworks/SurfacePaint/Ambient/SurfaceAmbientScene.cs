#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using MathematicalAlgorithms.Lighting;
using MathematicalAlgorithms.Surface;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Raster;
using Share.SurfacePaint.Diagnostics;
using Share.SurfacePaint.Engine;
using Share.SurfacePaint.Hosting;
using Share.SurfacePaint.Materials;
using UiBackend;

namespace Share.SurfacePaint.Ambient;

/// <summary>
/// 页面装饰层（**全仓唯一**的背景场绘制点）：渐变底 + 贯穿丝带 + 悬浮光点由一张页面场位图承载；
/// 卡片只通过 <see cref="PaintPanelChrome"/> 消费同一张图（留白环 / 外发光 / 双形态边缘光 / 折射纹理）。
/// <para>光源唯一：几何与色相全部取自 <see cref="SurfaceLightRegistry"/> 的同一帧；本层不另建光源。</para>
/// <para>主题唯一：颜色与几何来自 Profile 的 <c>page_gradient_*</c>/<c>ambient_*</c> 令牌，
/// 浅档为主档、深档为备档，代码端零颜色分支。</para>
/// <para>性能边界：页面场位图只在「尺寸 / 档位 / 光签名」变化时重建；每帧成本 = 一次位图搬运
/// （宿主）+ 命中缓存时每卡一次贴图（chrome）。成本计入 <see cref="SurfaceRenderProfile"/> 的
/// <c>decor:*</c> 图元项。</para>
/// </summary>
public static class SurfaceAmbientScene
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ChromeSnapshot> Chromes = new(StringComparer.Ordinal);
    private static SceneSnapshot? _scene;
    public static int SceneBuildCount { get; private set; }

    private static int _lightSamples;

    /// <summary>实际光采样次数（诊断/断言：每拍应 ≤ 窗口数）。</summary>
    public static int LightSampleCount => System.Threading.Volatile.Read(ref _lightSamples);
    private static SurfaceAmbientTokens _params = SurfaceAmbientTokens.Empty;
    private static Bitmap? _noise;
    private static float _noiseAlpha;
    private static long _stateFrameToken = long.MinValue;
    private static readonly HashSet<string> _fallbackLogged = new(StringComparer.Ordinal);
    private static SurfaceLightState? _state;
    private static Size _stateClient;
    private static Point _stateOrigin;
    private static float _stateDpi;

    /// <summary>装饰层是否可用（缺渐变令牌 ⇒ false：保持既有纯色页面，零回归）。</summary>
    public static bool Enabled => _params.HasScene;

    /// <summary>
    /// 安装期写入口：参数来自当前 Profile 的令牌表；不可用 ⇒ 关闭本层。
    /// <para>装饰能力不再在这里收集：它由材质自己的 <c>decor</c> 声明，经
    /// <see cref="SurfaceDecorPlan"/> 统一判定（见 <see cref="SkinDecorator"/>）。</para>
    /// </summary>
    public static void Configure(SurfaceAmbientTokens parameters)
    {
        lock (Gate)
        {
            _params = parameters;
            DisposeScene();
        }
    }

    /// <summary>卸载：释放页面场/卡片 chrome/纹理。</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _params = SurfaceAmbientTokens.Empty;
            DisposeScene();
            _noise?.Dispose();
            _noise = null;
            _noiseAlpha = 0;
        }

        var hosts = new List<string>(Chromes.Keys);
        foreach (var host in hosts) Release(host);
    }

    /// <summary>释放某宿主的 chrome 缓存（Detach/换材质时）。</summary>
    public static void Release(string? hostId)
    {
        if (string.IsNullOrEmpty(hostId)) return;
        if (!Chromes.TryGetValue(hostId!, out var cache)) return;
        Chromes.Remove(hostId!);
        cache.Image.Dispose();
    }

    /// <summary>
    /// 宿主背景：把页面场位图的对应切片搬到宿主客户区。
    /// <para>用 <see cref="CompositingMode.SourceCopy"/>：背景位图不透明，避免逐像素混合。</para>
    /// </summary>
    public static void PaintHostSurface(
        SurfaceViewport viewport, string hostId, Graphics graphics,
        ISurfaceEnginePort? enginePort = null, ISurfaceEventLog? log = null,
        SurfaceFrameLightContext? context = null)
    {
        if (!viewport.IsValid) return;

        // 正常路径：页面场也由 Engine 产生（同一虚拟矩阵光）；失败才走下面 CPU 降级。
        if (SurfaceEngineFramePump.Enabled && enginePort is not null)
        {
            // 快照上下文 ⇒ 用窗口级唯一光态；无上下文才自行采样（兼容旧宿主/测试）。
            var light = context is null ? ResolveState(viewport, hostId) : context.Light;
            if (light is not { HasValue: true })
            {
                // 光样缺失 ⇒ 不进 D3D（P0-5：降级判断与执行路径互锁），直接走 CPU 降级。
                log?.Write("surface.frame.degraded", new { reason = "light-missing", host = hostId });
            }
            else
            {
                var bounds = viewport.Bounds;
                var engine = SurfaceEngineFramePump.TryFrameRaw(
                    viewport, BuildSceneItems(bounds, viewport.HostScale, light),
                    "page:" + _params.Token, light, SurfaceEngineFramePump.Quality, enginePort, null, log);
                if (engine is not null)
                {
                    graphics.DrawImageUnscaled(engine, 0, 0);
                    return;
                }
            }
        }

        var scene = EnsureScene(viewport, hostId, context);
        if (scene is null) return;

        var offset = new Point(viewport.OffsetX, viewport.OffsetY);
        var source = new Rectangle(offset.X, offset.Y, viewport.Width, viewport.Height);
        var world = new Rectangle(0, 0, scene.Width, scene.Height);
        if (!source.IntersectsWith(world)) return;
        source.Intersect(world);

        var destination = new Rectangle(source.X - offset.X, source.Y - offset.Y, source.Width, source.Height);
        var begin = SurfaceRenderProfile.Begin();
        var previous = graphics.CompositingMode;
        var previousInterpolation = graphics.InterpolationMode;
        var previousOffset = graphics.PixelOffsetMode;
        try
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(scene.Image, destination, source, GraphicsUnit.Pixel);
        }
        finally
        {
            graphics.CompositingMode = previous;
            graphics.InterpolationMode = previousInterpolation;
            graphics.PixelOffsetMode = previousOffset;
            SurfaceRenderProfile.RecordLayer("decor:host", begin);
        }
    }

    /// <summary>
    /// 卡片 chrome（**卡片唯一消费入口**）：在同一张页面场上裁出留白环，再画外发光、
    /// 3.6px 细窄彩带、28px 宽柔高光（通道错位）、折射纹理与丝带连续段。
    /// <para>结果按「控件尺寸/屏幕位置/光签名/档位」缓存；命中时只贴一张位图。</para>
    /// </summary>
    public static void PaintPanelChrome(
        string hostId, SurfaceViewport viewport, Graphics graphics, Rectangle bounds, ResolvedMaterial material,
        SurfaceFrameLightContext? context = null)
    {
        // chrome 恒由载体绘制：引擎帧只接管 Base/Dynamic（阶段所有权），此处不得再整帧贴图（否则重复绘制）。
        var chrome = EnsureChrome(hostId, viewport, bounds, material, context);
        if (chrome is null) return;
        var begin = SurfaceRenderProfile.Begin();
        graphics.DrawImageUnscaled(chrome.Image, bounds.X, bounds.Y);
        SurfaceRenderProfile.RecordLayer("decor:chrome", begin);
    }

    private static SceneSnapshot? EnsureScene(
        SurfaceViewport viewport, string hostId, SurfaceFrameLightContext? context = null)
    {
        if (!Enabled) return null;
        var dpi = BorderUnits.NormalizeDpi(viewport.HostDpi);
        var size = new Size(viewport.HostWidth, viewport.HostHeight);
        if (size.Width <= 0 || size.Height <= 0) return null;

        // 快照上下文 ⇒ 窗口级唯一光态（不再逐卡采样）；无上下文才采样（兼容旧宿主/测试）。
        var state = context is null ? ResolveState(viewport, hostId) : context.Light;
        // 装饰层是“静态艺术底”，不跟随 1Hz 光签名抖动：只取量化后的方向/相位（分钟级才换）。
        var generation = context?.Generation ?? 0;
        var token = string.Create(CultureInfo.InvariantCulture,
            $"{size.Width}x{size.Height}|{dpi:F0}|{_params.Token}|{LightToken(state)}|g{generation}");

        lock (Gate)
        {
            if (_scene is { } existing && string.Equals(existing.Token, token, StringComparison.Ordinal))
            {
                return existing;
            }

            var begin = SurfaceRenderProfile.Begin();
            // 页面场是全不透明像素：PArgb 让宿主贴图走 SourceCopy 快路径（与目标位图同格式，免逐像素转换）。
            var image = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                PaintScene(graphics, size.Width, size.Height, dpi / 96f, state);
            }

            // 丝带必须走**像素阶段**：它要读到「刚刚被 GDI+ 画出来的」底色像素，而不是解析模型。
            // 原因见 PaintRibbons 注释；另外 LockBits 要求位图上没有存活的 Graphics，故放在 using 之后。
            PaintRibbons(image, size.Width, size.Height, dpi / 96f, state);

            SurfaceRenderProfile.RecordLayer("decor:scene", begin);
            SceneBuildCount++;

            DisposeScene();
            _scene = new SceneSnapshot(image, token, size.Width, size.Height);
            return _scene;
        }
    }

    private static void DisposeScene()
    {
        _scene?.Image.Dispose();
        _scene = null;
    }

    /// <summary>页面场绘制项（虚拟矩阵光 → Engine 帧；与 CPU 降级同一组参数）。</summary>
    private static IReadOnlyList<SurfaceDrawItem> BuildSceneItems(
        Rectangle bounds, float scale, SurfaceLightState? light)
    {
        var items = new List<SurfaceDrawItem>(6);
        if (_params.GradientFrom is { } from && _params.GradientTo is { } to)
        {
            items.Add(new SurfaceDrawItem(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                _params.CornerRadius * scale, unchecked((uint)from.ToArgb()), 1f, 0,
                SurfaceDrawKind.Gradient, SurfaceDrawFlags.Decor, 45f));
        }

        var sample = ToSample(light);
        if (_params.GlowAlpha > 0f && _params.ShadowTint is { } shadow)
        {
            items.Add(new SurfaceDrawItem(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                _params.CornerRadius * scale, unchecked((uint)shadow.ToArgb()),
                Math.Clamp(_params.GlowAlpha, 0f, 1f), 1,
                SurfaceDrawKind.Shadow, SurfaceDrawFlags.Decor, 0f, 0f, _params.GlowRadius * scale));
        }

        if (_params.RadialAlpha > 0f)
        {
            items.Add(new SurfaceDrawItem(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                0f, 0xFFFFFFFFu, Math.Clamp(_params.RadialAlpha * sample.Shade, 0f, 1f), 2,
                SurfaceDrawKind.Spot, SurfaceDrawFlags.Decor));
        }

        if (_params.RibbonAlpha > 0f)
        {
            items.Add(new SurfaceDrawItem(
                bounds.X, bounds.Y + bounds.Height * 0.46f, bounds.Width, Math.Max(1f, _params.RibbonWidth * scale),
                0f, 0x3377AABB, Math.Clamp(_params.RibbonAlpha, 0f, 1f), 3,
                SurfaceDrawKind.Gradient, SurfaceDrawFlags.Decor, 0f));
        }

        if (_params.EdgeWidth > 0f && _params.GradientTo is { } edge)
        {
            items.Add(new SurfaceDrawItem(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                _params.CornerRadius * scale, unchecked((uint)edge.ToArgb()), 1f, 4,
                SurfaceDrawKind.Edge, SurfaceDrawFlags.Decor, 0f, _params.EdgeWidth * scale));
        }

        if (_params.TextureAlpha > 0f)
        {
            items.Add(new SurfaceDrawItem(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                0f, 0xFFFFFFFFu, Math.Clamp(_params.TextureAlpha, 0f, 1f), 5,
                SurfaceDrawKind.Noise, SurfaceDrawFlags.Decor));
        }

        return items;
    }

    private static void PaintScene(Graphics graphics, int width, int height, float scale, SurfaceLightState? light)
    {
        var previousSmoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.None;
        try
        {
            using var gradient = new LinearGradientBrush(
                new Rectangle(0, 0, width, height), _params.GradientFrom!.Value, _params.GradientTo!.Value, 45f);
            graphics.FillRectangle(gradient, 0, 0, width, height);
        }
        finally
        {
            graphics.SmoothingMode = previousSmoothing;
        }

        // 径向光：以光斑为中心、向外衰减的局部亮度分量（参考 R2：径向梯度 1.5–10 L*/100px）。
        PaintRadialGlow(graphics, width, height, scale, light);

        // 光点（散斑）已删除：1.4–3.2px 的独立色相点在 8bit + 亚像素抗锯齿下不可控，
        // 且每个点都是一个独立色相团块（归因线实测 28 块碎片 / 最大块仅 1.6%）——
        // 违反「团块 ≤6、最大 ≥15%」与「1px 尺度禁止着色」。连续光带本身已由丝带承载。
    }

    /// <summary>
    /// 局部径向光：中心亮、向外衰减（参考 R2：径向梯度 1.5–10 L*/100px）。
    /// <para>光斑在世界（屏幕）上固定：中心 = 光态 <see cref="SurfaceLightState.SpotX"/>/<see cref="SurfaceLightState.SpotY"/>，
    /// 它们是「锚点 − 页面原点」/页面宽高，**故意不夹取到 [0,1]**。锚点落在页面外时中心就落在页面外，
    /// 椭圆被页面自然裁掉或整体不可见——而不是被拽到边缘，造一个不存在的假亮斑。</para>
    /// <para>强度再乘 <see cref="SurfaceLightState.Shade"/>（与 <see cref="SurfaceLightPort"/> 同一份照度权重）：
    /// 锚点超出光斑判定半径即归零，不会在远处硬留一圈亮边。</para>
    /// <para>高光色相中性（白）：页面本身已近白，只有白的光源能在近白底上继续抬升亮度；
    /// 具体强度/半径仍全部来自令牌，代码不做任何主题色分支。</para>
    /// </summary>
    private static void PaintRadialGlow(Graphics graphics, int width, int height, float scale, SurfaceLightState? light)
    {
        if (_params.GradientFrom is null || _params.RadialAlpha <= 0f || _params.RadialRadius <= 0f) return;

        var shade = Math.Clamp(light?.Shade ?? 1f, 0f, 1f);
        if (shade <= 0.02f) return;

        var alpha = Math.Clamp((int)Math.Round(_params.RadialAlpha * shade * 255f), 0, 255);
        if (alpha <= 0) return;

        var centerX = width * (light?.SpotX ?? 0.5f);
        var centerY = height * (light?.SpotY ?? 0.5f);
        var radius = Math.Max(8f, _params.RadialRadius * scale);
        // 光栅原语在 Share.SurfacePaint.Raster（载体无关）；本层只负责从光态算出中心/半径/强度。
        SurfaceRaster.RadialGlow(graphics, width, height, centerX, centerY, radius, alpha);
    }

    /// <summary>
    /// 丝带（整层唯一的装饰形态，**替换掉旧的 <c>RibbonBatch</c>**）。
    /// <para>**单带 + 单族**：旧实现两条带各自 96° 窗口 + 通道错位，叠加后实机测到 115° 连续弧
    /// 并溢出到绿/橙族外；且面积 = 页面 8.8%，超出 6% 预算。现在只保留一条主带，
    /// 掩膜是一个连续色场（团块数 1）。</para>
    /// <para>**色相原点只有一处**：<see cref="FamilyHueBase"/> = 虹彩环声明基色 190°。
    /// 旧实现把 `IridescencePhase`(0..1) 当**度**用（丝带 0.33→0.33°、光点 0..24°），
    /// 而边缘带走 190+phase×360=308.8° —— 同层两套色相原点，实机因此量到 346° 与粉色丝带。
    /// 几何相位仍随光帧变化（形状不僵死），色相族不随 1Hz 光帧漂移。</para>
    /// <para>**为什么必须是逐像素**：装饰只有 3 C*，明度必须钉在<b>这张位图上已经画好的</b>底色。
    /// 只要用「解析模型算底色 + 一块纯色盖一片区域」，就被两件事击穿：
    /// (1) GDI+ 的 45° 渐变是 8bit 抖动渲染，逐像素比解析模型差 ±1 通道 ≈0.18 L*；
    /// (2) 一块纯色内部的底色还在变（径向光最陡 0.035 L*/px，34px 带宽即 1.2 L*）。
    /// 实测旧块状实现大面板 max|ΔL*|=0.37（≈结构层差的 1.8 倍），这正是“丝带压过层差”的像素证据。</para>
    /// </summary>
    private static void PaintRibbons(Bitmap scene, int width, int height, float scale, SurfaceLightState? light)
    {
        if (_params.RibbonAlpha <= 0f || _params.RibbonSpan <= 0f) return;
        var hue0 = FamilyHueBase();
        var span = Math.Min(_params.RibbonSpan, Math.Min(HueBudget(), FamilySpan));
        var shapePhase = ShapePhase(light);
        var centerX = width * 0.18f;
        var centerY = height * 0.46f;
        var amplitude = height * _params.RibbonAmplitude;
        var wavelength = width * 1.05f;
        var bandWidth = _params.RibbonWidth * scale;
        SurfaceRaster.Ribbon(scene, width, height, centerX, centerY, amplitude, wavelength, bandWidth, shapePhase, hue0, span);
    }

    private static ChromeSnapshot? EnsureChrome(
        string hostId, SurfaceViewport viewport, Rectangle bounds, ResolvedMaterial material,
        SurfaceFrameLightContext? context = null)
    {
        var scene = EnsureScene(viewport, hostId, context);
        if (scene is null || bounds.Width <= 0 || bounds.Height <= 0) return null;

        var offset = new Point(viewport.OffsetX, viewport.OffsetY);
        var dpi = BorderUnits.NormalizeDpi(viewport.Dpi);
        var light = context is null ? ResolveState(viewport, hostId) : context.Light;
        var decoration = material.Decoration;
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{bounds.Width}x{bounds.Height}@{offset.X},{offset.Y}|{dpi:F0}|{scene.Token}|{_params.Token}|{(int)decoration}");

        if (Chromes.TryGetValue(hostId, out var cached)
            && string.Equals(cached.Key, key, StringComparison.Ordinal))
        {
            return cached;
        }

        var built = BuildChrome(bounds, offset, dpi, key, scene, light, decoration);
        if (built is null) return null;

        if (cached is not null)
        {
            cached.Image.Dispose();
            Chromes.Remove(hostId);
            Chromes.Add(hostId, built);
            return built;
        }

        Chromes.Add(hostId, built);
        return built;
    }

    /// <summary>构建卡片 chrome 位图（只在缓存键变化时执行）。</summary>
    private static ChromeSnapshot? BuildChrome(
        Rectangle bounds, Point offset, float dpi, string key, SceneSnapshot scene, SurfaceLightState? light,
        SurfaceDecoration decoration)
    {
        // 声明里没有任何可画的 chrome 特性 ⇒ 不生成空位图（叶子面本来就不该有装饰）。
        const SurfaceDecoration paintable = SurfaceDecoration.Chrome
                                             | SurfaceDecoration.EdgeBand
                                             | SurfaceDecoration.Texture;
        if ((decoration & paintable) == 0) return null;

        var scale = dpi / 96f;
        var gutter = Math.Clamp((int)Math.Round(_params.Gutter * scale), 0, Math.Min(bounds.Width, bounds.Height) / 3);
        if (gutter <= 0 && _params.TextureAlpha <= 0f) return null;
        // 尺度归一：装饰语法（gutter 环 + 圆角 + 3.6px 细边）是**卡片尺度**的语法。
        // 旧实现把它套到任意尺寸控件上：28px 宽柔带 > 20px 行高，整行被填成粉色胶囊（实机截图）。
        // 契约：最小边 < 2×(gutter+圆角) 的控件不消费 chrome，交回普通材质绘制，不做逐控件分支。
        if (Math.Min(bounds.Width, bounds.Height) < 2f * (gutter + _params.CornerRadius * scale)) return null;

        var radius = Math.Min(_params.CornerRadius * scale, Math.Min(bounds.Width, bounds.Height) / 2f);
        var inner = Rectangle.Inflate(bounds, -gutter, -gutter);
        inner.Width = Math.Max(1, inner.Width);
        inner.Height = Math.Max(1, inner.Height);

        // PArgb：SourceOver 贴图的 GDI+ 快路径（同像素数下比 Argb 快约 2×）。
        var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            // 1) 留白环：同一条页面场的切片（位置/渐变/丝带/光点都与宿主背景一致）。
            if ((decoration & SurfaceDecoration.Chrome) != 0 && gutter > 0)
            {
                using var outerPath = SurfaceRaster.RoundedRect(bounds, radius + gutter);
                using var innerPath = SurfaceRaster.RoundedRect(inner, radius);
                using var ring = new Region(outerPath);
                ring.Exclude(innerPath);
                graphics.SetClip(ring, CombineMode.Replace);
                var source = new Rectangle(offset.X, offset.Y, bounds.Width, bounds.Height);
                var world = new Rectangle(0, 0, scene.Width, scene.Height);
                if (source.IntersectsWith(world))
                {
                    source.Intersect(world);
                    graphics.DrawImage(scene.Image,
                        new Rectangle(source.X - offset.X, source.Y - offset.Y, source.Width, source.Height),
                        source, GraphicsUnit.Pixel);
                }

                graphics.ResetClip();
            }

            // 2) 外发光（参考峰值 3.59 L* / 扩散 31–36px）：沿内层边缘向内衰减，避免被控件裁剪。
            if ((decoration & SurfaceDecoration.Chrome) != 0) PaintGlow(graphics, inner, radius, scale, light);

            // 3) 边缘细带（3.6px / 单族色相）：只画在卡片尺度控件上，色相与明度都锁在族内与底色上。
            //    旧实现另有一条 28px 宽柔带（span 96°）+ 通道错位，是实机“塑料胶囊/一片糊”的主因：
            //    它在小控件上 > 控件短边、直接填满整块；在大卡片上与细带叠成两个独立色相窗口。
            if ((decoration & SurfaceDecoration.EdgeBand) != 0)
            {
                using var outerPath = SurfaceRaster.RoundedRect(bounds, radius + gutter);
                graphics.SetClip(outerPath, CombineMode.Replace);
                PaintEdgeBand(graphics, inner, radius, scale, light, offset, scene.Width, scene.Height);
            }

            if ((decoration & SurfaceDecoration.Texture) != 0)
            {
                using var innerPath = SurfaceRaster.RoundedRect(inner, radius);
                graphics.SetClip(innerPath, CombineMode.Replace);
                PaintTexture(graphics, innerPath, scale);
                graphics.ResetClip();
            }
        }

        return new ChromeSnapshot(bitmap, key, bounds.Width, bounds.Height);
    }

    private static void PaintGlow(Graphics graphics, Rectangle inner, float radius, float scale, SurfaceLightState? light)
    {
        if (_params.ShadowTint is null || _params.GlowAlpha <= 0f) return;
        var tint = _params.ShadowTint.Value;
        var spread = _params.GlowRadius * scale;
        var shadowX = (light?.ShadowX ?? 0.35f) * spread * 0.35f;
        var shadowY = (light?.ShadowY ?? 0.35f) * spread * 0.35f;
        var steps = 6;
        for (var i = 0; i < steps; i++)
        {
            var inset = spread * (i / (float)steps);
            var rect = new RectangleF(
                inner.X + inset + shadowX,
                inner.Y + inset + shadowY,
                Math.Max(1f, inner.Width - inset * 2f),
                Math.Max(1f, inner.Height - inset * 2f));
            var alpha = Math.Clamp((int)Math.Round(_params.GlowAlpha * 255f * (1f - i / (float)steps)), 0, 255);
            using var path = SurfaceRaster.RoundedRect(rect, Math.Max(0f, radius - inset));
            using var pen = new Pen(Color.FromArgb(alpha, tint), Math.Max(1f, 5f * scale));
            graphics.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// 3.6px 细窄彩带：骑在 inner 边界上，外半侧落在留白环、内半侧落在卡面。
    /// <para>**只承载色相**：每段颜色的 L* 取该段底色的 L*（45° 页面渐变 + 径向光），
    /// C* 固定 ≤3，因此细带不会在 1px/3.6px 尺度上引入高彩度描边（旧实现取 S0.85 的虹彩色，
    /// 实测按钮边出现橙 <c>#c48f51</c> C*≈35、蓝 <c>#8ec5de</c> C*≈28）。</para>
    /// <para>色相窗口 = <see cref="FamilySpan"/>（40°），四条直边平分 ⇒ 单边 10°，整圈 40°，
    /// 与丝带/光点同一个族，不再有第二个 96° 窗口（旧宽柔带已删）。</para>
    /// <para>为什么不用 <see cref="PathGradientBrush"/> 描边：GDI+ 把它当 Pen.Brush 时，
    /// 落在 brush 路径**内部**的笔触像素一律取 <c>CenterColor</c>（实测整条边恒色 165.2°），
    /// 无法做“沿边”渐变；按弧长给顶点取色也同样无效。改用每段 <see cref="LinearGradientBrush"/>
    /// 沿边铺色，几何、带宽、色相推进都完全可控。</para>
    /// </summary>
    private static void PaintEdgeBand(
        Graphics graphics, Rectangle inner, float radius, float scale,
        SurfaceLightState? light, Point offset, int worldWidth, int worldHeight)
    {
        var width = _params.EdgeWidth * scale;
        if (width < MinEdgeStroke) return;
        var hueStart = FamilyHueBase();
        var window = Math.Min(FamilySpan, 4f * Math.Max(1f, _params.EdgeHueSpan));
        var segment = window / 4f;
        var r = Math.Max(0f, radius);

        // 顺时针：上 0→1、右 1→2、下 2→3、左 3→4（4 与 0 在左上角相差一个窗口，是唯一的接缝）。
        var edges = new (PointF A, PointF B, float From)[]
        {
            (new PointF(inner.X + r, inner.Y), new PointF(inner.Right - r, inner.Y), 0f),
            (new PointF(inner.Right, inner.Y + r), new PointF(inner.Right, inner.Bottom - r), 1f),
            (new PointF(inner.Right - r, inner.Bottom), new PointF(inner.X + r, inner.Bottom), 2f),
            (new PointF(inner.X, inner.Bottom - r), new PointF(inner.X, inner.Y + r), 3f),
        };

        foreach (var (a, b, from) in edges)
        {
            PaintEdgeGradient(graphics, a, b, offset, worldWidth, worldHeight, scale, light, hueStart,
                from * segment, (from + 1f) * segment, width, EdgeAlpha);
        }

        if (r <= 0f) return;
        var corners = new (RectangleF Rect, float Start, float Segment)[]
        {
            (new RectangleF(inner.X, inner.Y, r * 2f, r * 2f), 180f, 0f),
            (new RectangleF(inner.Right - r * 2f, inner.Y, r * 2f, r * 2f), 270f, 1f),
            (new RectangleF(inner.Right - r * 2f, inner.Bottom - r * 2f, r * 2f, r * 2f), 0f, 2f),
            (new RectangleF(inner.X, inner.Bottom - r * 2f, r * 2f, r * 2f), 90f, 3f),
        };

        foreach (var (rect, start, segmentIndex) in corners)
        {
            BackdropLab(
                rect.X + offset.X, rect.Y + offset.Y, worldWidth, worldHeight, scale, light,
                out var lstar, out var baseA, out var baseB);
            using var rim = new Pen(DecorColor(lstar, baseA, baseB, hueStart + segmentIndex * segment, EdgeAlpha), width);
            graphics.DrawArc(rim, rect, start, 90f);
        }
    }

    /// <summary>一段沿边方向的色相渐变（<paramref name="degrees0"/> → <paramref name="degrees1"/> 是相对色相）。</summary>
    private static void PaintEdgeGradient(
        Graphics graphics, PointF a, PointF b, Point offset, int worldWidth, int worldHeight, float scale,
        SurfaceLightState? light, float hueStart, float degrees0, float degrees1, float width, int alpha)
    {
        var horizontal = Math.Abs(b.Y - a.Y) < 0.01f;
        var forward = horizontal ? b.X > a.X : b.Y > a.Y;
        var rect = horizontal
            ? new RectangleF(Math.Min(a.X, b.X), a.Y, Math.Max(1f, Math.Abs(b.X - a.X)), 1f)
            : new RectangleF(a.X, Math.Min(a.Y, b.Y), 1f, Math.Max(1f, Math.Abs(b.Y - a.Y)));
        // 两端的 L* 取各自位置的底色：沿边线性插值即可匹配 45° 渐变，ΔL* 保持在量化误差量级。
        BackdropLab(a.X + offset.X, a.Y + offset.Y, worldWidth, worldHeight, scale, light, out var l0, out var a0, out var b0);
        BackdropLab(b.X + offset.X, b.Y + offset.Y, worldWidth, worldHeight, scale, light, out var l1, out var a1, out var b1);
        var from = DecorColor(l0, a0, b0, hueStart + degrees0, alpha);
        var to = DecorColor(l1, a1, b1, hueStart + degrees1, alpha);
        using (var brush = new LinearGradientBrush(rect, forward ? from : to, forward ? to : from, horizontal ? 0f : 90f))
        using (var pen = new Pen(brush, width) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
        {
            graphics.DrawLine(pen, a, b);
        }
    }

    private static void PaintTexture(Graphics graphics, GraphicsPath innerPath, float scale)
    {
        if (_params.TextureAlpha <= 0f) return;
        var alpha = Math.Clamp((int)Math.Round(_params.TextureAlpha * 255f), 0, 255);
        if (_noise is null || Math.Abs(_noiseAlpha - alpha) > 0.5f)
        {
            _noise?.Dispose();
            _noise = SurfaceRaster.NoiseTile(alpha);
            _noiseAlpha = alpha;
        }

        using var brush = new TextureBrush(_noise, WrapMode.Tile);
        graphics.FillPath(brush, innerPath);
    }

    /// <summary>
    /// **单一色族**的宽度（度）：所有装饰色相都落在这一个连续窗口内。
    /// <para>声明的 <c>ambient_hue_budget</c>(150°) 只是上限；旧实现把 96° 丝带 + 96° 宽边带 +
    /// 各自相位叠在一起，实机量到 115° 连续弧且溢出到绿/橙，故把实际使用宽度收到 40°：
    /// 一个族 = 一段连续色相弧，族外 bin 占比按定义为 0。</para>
    /// </summary>
    private const float FamilySpan = SurfaceAmbientMath.FamilySpan;

    /// <summary>装饰描边最小宽度（px）：窄于此的笔触不做色相扫描（「1px 线禁止着色」）。</summary>
    private const float MinEdgeStroke = 2f;

    /// <summary>
    /// 细彩带笔触不透明度：**必须全不透明**。
    /// <para>半透明（旧 245/232）会让下方丝带透 4–9% 上来：装饰色只有 3 C*，Lab 色相在低彩度下
    /// 对混色极敏感，实测单边跨度被抬到 63–69°（即用户看到的「132°」那一类越界）。
    /// 因为笔触的 L* 已锁在该点底色上，不透明也不会留下亮度接缝。</para>
    /// </summary>
    private const int EdgeAlpha = (int)SurfaceAmbientMath.EdgeAlpha;

    /// <summary>
    /// 装饰色：L* 锁定在<b>该点底色</b>的明度上，只叠加 3 C* 的族内色相。
    /// <para>这是“丝带不承载明度”唯一能落地的做法：先用同一份渐变（+径向光）算出该点底色，
    /// 再在 LCh 空间只改 hue，因此叠加前后 ΔL*≈0；8bit 量化误差约 0.1 L*。</para>
    /// </summary>
    private static Color DecorColor(double lstar, double backdropA, double backdropB, float hueDegrees, int alpha)
    {
        // 唯一实现 = Plate.Mathematics（与引擎着色同一份公式；此处只做 ARGB → Color）。
        var argb = SurfaceAmbientMath.DecorColor(lstar, backdropA, backdropB, hueDegrees, alpha);
        return Color.FromArgb((argb >> 24) & 0xFF, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
    }

    /// <summary>
    /// 该点**底色**的 Lab（页面 45° 渐变 + 同一份径向光）。
    /// <para>装饰色与验收都基于它：装饰 = 底色 + ≤3 C* 的族内色相（L* 钉死），
    /// 因此「装饰的色相」只能按 Δ(a,b) 判，不能按合成色相判（底色本身是 C*≈9 的蓝）。</para>
    /// </summary>
    private static void BackdropLab(
        float x, float y, int width, int height, float scale, SurfaceLightState? light,
        out double lstar, out double a, out double b)
    {
        if (_params.GradientFrom is not { } from || _params.GradientTo is not { } to)
        {
            lstar = 90.0;
            a = 0.0;
            b = 0.0;
            return;
        }

        // 唯一实现 = Plate.Mathematics（虚拟矩阵光样本驱动；与引擎着色同一份公式）。
        var lab = SurfaceAmbientMath.BackdropLab(
            from.ToArgb(), to.ToArgb(), x, y, width, height, scale,
            _params.RadialAlpha, _params.RadialRadius, ToSample(light));
        lstar = lab.L;
        a = lab.A;
        b = lab.B;
    }

    /// <summary>径向光在该点的合成白量（0..1），与 <see cref="PaintRadialGlow"/> 同一形态。</summary>
    private static float RadialCoverage(float x, float y, int width, int height, float scale, SurfaceLightState? light)
        => SurfaceAmbientMath.RadialCoverage(
            _params.RadialAlpha, _params.RadialRadius, x, y, width, height, scale, ToSample(light));

    /// <summary>引擎光态 → 页面装饰数学样本（唯一投影点；无光 = <see cref="AmbientLightSample.None"/>，不做太阳近似）。</summary>
    private static AmbientLightSample ToSample(SurfaceLightState? light)
    {
        if (light is not { HasValue: true } value) return AmbientLightSample.None;
        return new AmbientLightSample(
            value.Angle, value.SpotX, value.SpotY, value.Shade,
            value.ShadowX, value.ShadowY, value.IridescencePhase, value.TintArgb);
    }

    /// <summary>
    /// 装饰层色相原点（度）= 虹彩环的声明基色 190°。
    /// <para>**全层只有一个色相原点**。旧实现有三套：丝带用 `IridescencePhase`(0..1) 当度（≈0.33°）、
    /// 光点用 `(i%9)/8 × span`（0..45°）、边缘用 `IridescenceRamp.HueAt(phase,0,0)=190+phase×360≈308.8°`。
    /// 于是同一帧里同时出现 190° 青族、308° 粉族和 0° 红族；实机整层统计因此量到 346.2°，
    /// 且粉色丝带在浅底上最显眼（浅底上 C*3 的粉比青可见度高），用户看到的就是“粉色贴纸”。
    /// 现在丝带、细边只取这一个原点，族外 bin 按定义为 0。</para>
    /// </summary>
    private static float FamilyHueBase() => SurfaceAmbientMath.FamilyHueBase;

    /// <summary>
    /// 丝带**几何**相位（只影响波形，不影响色相）：随光帧相位缓慢变化，避免形状僵死。
    /// <para>色相与几何解耦——旧实现把同一个 `IridescencePhase` 既当几何相位又当色相（差 360 倍），
    /// 正是“声明一个窗口、实机三个窗口”的来源。</para>
    /// </summary>
    private static float ShapePhase(SurfaceLightState? light) => SurfaceAmbientMath.ShapePhase(ToSample(light));

    /// <summary>整层色相预算（度）：所有彩色分量共用这一个窗口，禁止多条带各自漂移。</summary>
    private static float HueBudget() => SurfaceAmbientMath.HueBudget(_params.HueBudget);

    /// <summary>
    /// 量化光源签名：唯一实现 = Plate.Mathematics（方向 10° / 阴影 0.1 / 相位 0.1 / 光斑位置 0.05）。
    /// </summary>
    private static string LightToken(SurfaceLightState? state)
        => state is { } light ? SurfaceAmbientMath.LightToken(ToSample(light)) : "no-light";

    /// <summary>诊断：最近一次页面场解算实际使用的光态（验收用，不参与绘制）。</summary>
    public static SurfaceLightState? LastState => _state;

    /// <summary>
    /// 同一份光源：整个页面（宿主背景 + 所有卡片 chrome）只按**窗体**探针解算一次，
    /// 以 <see cref="SurfaceLightRegistry"/> 帧对象身份为缓存键，避免每帧重复采样。
    /// </summary>
    private static SurfaceLightState? ResolveState(SurfaceViewport viewport, string hostId)
    {
        System.Threading.Interlocked.Increment(ref _lightSamples);
        var read = SurfaceLightReader.Read();
        var frame = read.Frame;
        if (frame is null)
        {
            if (_fallbackLogged.Add(hostId))
            {
                SurfaceAmbientLog.RecordLightDegraded(read.Reason, "SurfaceLightReader=null");
            }

            return null;
        }

        var dpi = BorderUnits.NormalizeDpi(viewport.HostDpi);
        var screen = new Point(
            (int)Math.Round(viewport.HostOriginXDip), (int)Math.Round(viewport.HostOriginYDip));
        var client = new Size(viewport.HostWidth, viewport.HostHeight);
        var frameToken = frame.Signature;
        if (_stateFrameToken == frameToken
            && _stateDpi == dpi
            && _stateOrigin == screen
            && _stateClient == client)
        {
            return _state;
        }

        var scale = 96f / dpi;
        var carrier = new SurfaceCarrierFrame(
            (float)viewport.HostOriginXDip,
            (float)viewport.HostOriginYDip,
            viewport.HostWidth * scale,
            viewport.HostHeight * scale,
            dpi);
        var probe = new SurfaceLightProbe(
            carrier.ScreenXDip,
            carrier.ScreenYDip,
            carrier.WidthDip,
            carrier.HeightDip,
            ZOrder: 0,
            Dpi: carrier.Dpi);
        var sample = SurfaceLightPort.Sample(frame, probe);
        if (sample.HasValue)
        {
            if (!sample.SpotInsideBounds)
            {
                // 越界只记账、不夹取（光斑允许落在面板之外，Shade 决定是否受光）。
                SurfaceAmbientLog.RecordCarrierOutOfBounds("carrier:" + hostId, sample.SpotBoundsDetail);
            }

            SurfaceAmbientLog.RecordCarrierProjection(
                "carrier:" + hostId,
                frame.Anchor,
                carrier,
                new SurfaceLightCarrierPoint(
                    sample.SpotLocalX, sample.SpotLocalY, sample.SpotInsideBounds, sample.SpotBoundsDetail));
        }

        _stateFrameToken = frameToken;
        _stateDpi = dpi;
        _stateOrigin = screen;
        _stateClient = client;
        _state = sample.HasValue ? SurfaceLightState.From(sample) : null;
        return _state;
    }

    private sealed class SceneSnapshot
    {
        internal SceneSnapshot(Bitmap image, string token, int width, int height)
        {
            Image = image;
            Token = token;
            Width = width;
            Height = height;
        }

        internal Bitmap Image { get; }
        internal string Token { get; }
        internal int Width { get; }
        internal int Height { get; }
    }

    private sealed class ChromeSnapshot
    {
        internal ChromeSnapshot(Bitmap image, string key, int width, int height)
        {
            Image = image;
            Key = key;
            Width = width;
            Height = height;
        }

        internal Bitmap Image { get; }
        internal string Key { get; }
        internal int Width { get; }
        internal int Height { get; }
    }
}

/// <summary>
/// 装饰层参数（唯一来源 = 表面令牌；浅/深两档各一值）。
/// <para>缺渐变令牌 ⇒ <see cref="HasScene"/>=false，装饰层整体不接管绘制（零回归）。</para>
/// </summary>
public sealed class SurfaceAmbientTokens
{
    public static readonly SurfaceAmbientTokens Empty = new();

    internal Color? GradientFrom { get; private init; }
    internal Color? GradientTo { get; private init; }
    internal Color? SparkleTint { get; private init; }
    internal Color? ShadowTint { get; private init; }
    public float Gutter { get; private init; }
    public float CornerRadius { get; private init; }
    public float RibbonWidth { get; private init; }
    internal float RibbonAlpha { get; private init; }
    internal float RibbonSpan { get; private init; }
    public float HueBudget { get; private init; }
    internal float RibbonAmplitude { get; private init; }
    internal float ChannelOffset { get; private init; }
    internal float SparkleDensity { get; private init; }
    internal float GlowRadius { get; private init; }
    internal float GlowAlpha { get; private init; }
    public float RadialAlpha { get; private init; }
    public float RadialRadius { get; private init; }
    public float EdgeWidth { get; private init; }
    public float EdgeHueSpan { get; private init; }
    public float WideEdgeWidth { get; private init; }
    internal float TextureAlpha { get; private init; }
    public string Token { get; private init; } = "decor-off";

    /// <summary>档位显式开关（<c>ambient_enabled</c>）：0 = 该档关闭装饰层，即使配色齐全也不画。</summary>
    internal bool Enabled { get; private init; } = true;

    public bool HasScene => Enabled && GradientFrom is not null && GradientTo is not null && Gutter > 0 && CornerRadius > 0;

    /// <summary>令牌表 → 参数（令牌缺失用缺省值；颜色缺失即关闭）。</summary>
    public static SurfaceAmbientTokens FromTokens(IReadOnlyDictionary<string, string>? tokens)
    {
        if (tokens is null) return Empty;
        Color? ParseColor(string key)
            => tokens.TryGetValue(key, out var value) && SurfaceColor.TryParseColor(value, out var argb)
                ? System.Drawing.Color.FromArgb(argb)
                : null;
        float Number(string key, float fallback)
            => tokens.TryGetValue(key, out var value)
               && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;

        var gradientFrom = ParseColor("page_gradient_from");
        var gradientTo = ParseColor("page_gradient_to");
        // 页底色单源：装饰层已声明（ambient_enabled）但缺 stop 色时，用材质推导的页面底色兜底，
        // 避免「页底色」与「页场渐变起点」两处来源各说各话（玻璃快照/属性通道取前者）。
        if (tokens.ContainsKey("ambient_enabled") && gradientFrom is null
            && SurfaceMaterialCatalog.PageBackground is { } pageColor)
        {
            gradientFrom = pageColor;
        }

        if (gradientTo is null) gradientTo = gradientFrom;
        var sparkle = ParseColor("ambient_sparkle");
        var shadow = ParseColor("ambient_shadow");

        return new SurfaceAmbientTokens
        {
            Enabled = Number("ambient_enabled", 1f) >= 0.5f,
            GradientFrom = gradientFrom,
            GradientTo = gradientTo,
            SparkleTint = sparkle,
            ShadowTint = shadow,
            Gutter = Number("ambient_gutter", 20f),
            CornerRadius = Number("panel_radius", 24f),
            RibbonWidth = Number("ambient_ribbon_width", 34f),
            RibbonAlpha = Number("ambient_ribbon_alpha", 0.22f),
            RibbonSpan = Number("ambient_ribbon_span", 96f),
            HueBudget = Math.Clamp(Number("ambient_hue_budget", 150f), 1f, 360f),
            RibbonAmplitude = Number("ambient_ribbon_amplitude", 0.075f),
            ChannelOffset = Number("ambient_channel_offset", 12f),
            SparkleDensity = Number("ambient_sparkle_density", 0.000062f),
            GlowRadius = Number("ambient_glow_radius", 34f),
            GlowAlpha = Number("ambient_glow_alpha", 0.10f),
            RadialAlpha = Number("ambient_radial_alpha", 0.42f),
            RadialRadius = Number("ambient_radial_radius", 200f),
            EdgeWidth = Number("ambient_edge_width", 3.6f),
            EdgeHueSpan = Number("ambient_edge_span", 11f),
            WideEdgeWidth = Number("ambient_wide_edge_width", 28f),
            TextureAlpha = Number("ambient_texture_alpha", 0.06f),
            Token = string.Create(CultureInfo.InvariantCulture,
                $"{(gradientFrom is { } f ? f.ToArgb().ToString("X8", CultureInfo.InvariantCulture) : "-")}/" +
                $"{(gradientTo is { } t ? t.ToArgb().ToString("X8", CultureInfo.InvariantCulture) : "-")}/" +
                $"{Number("ambient_gutter", 20f):F1}/{Number("panel_radius", 24f):F1}/" +
                $"{Number("ambient_ribbon_width", 34f):F1}/{Number("ambient_ribbon_alpha", 0.22f):F2}/" +
                $"{Number("ambient_ribbon_span", 96f):F0}/{Number("ambient_hue_budget", 150f):F0}/" +
                $"{Number("ambient_ribbon_amplitude", 0.075f):F3}/" +
                $"{Number("ambient_channel_offset", 12f):F1}/{Number("ambient_sparkle_density", 0.000062f):F5}/" +
                $"{Number("ambient_glow_radius", 34f):F1}/{Number("ambient_glow_alpha", 0.10f):F2}/" +
                $"{Number("ambient_radial_alpha", 0.42f):F2}/{Number("ambient_radial_radius", 200f):F1}/" +
                $"{Number("ambient_edge_width", 3.6f):F1}/{Number("ambient_edge_span", 11f):F0}/" +
                $"{Number("ambient_wide_edge_width", 28f):F1}/{Number("ambient_texture_alpha", 0.06f):F2}"),
        };
    }
}
