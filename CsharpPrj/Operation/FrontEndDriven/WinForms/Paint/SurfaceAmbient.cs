#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 页面装饰层（**全仓唯一**的背景场绘制点）：渐变底 + 贯穿丝带 + 悬浮光点由一张页面场位图承载；
/// 卡片只通过 <see cref="PaintPanelChrome"/> 消费同一张图（留白环 / 外发光 / 双形态边缘光 / 折射纹理）。
/// <para>光源唯一：几何与色相全部取自 <see cref="SurfaceLightRegistry"/> 的同一帧；本层不另建光源。</para>
/// <para>主题唯一：颜色与几何来自 Profile 的 <c>page_gradient_*</c>/<c>ambient_*</c> 令牌，
/// 浅档为主档、深档为备档，代码端零颜色分支。</para>
/// <para>性能边界：页面场位图只在「尺寸 / 档位 / 光签名」变化时重建；每帧成本 = 一次位图搬运
/// （宿主）+ 命中缓存时每卡一次贴图（chrome）。成本计入 <see cref="SkinRenderProfile"/> 的
/// <c>decor:*</c> 图元项。</para>
/// </summary>
internal static class SurfaceAmbient
{
    private static readonly object Gate = new();
    private static readonly ConditionalWeakTable<Control, ChromeSnapshot> Chromes = new();
    private static SceneSnapshot? _scene;
    internal static int SceneBuildCount { get; private set; }
    private static SurfaceAmbientParams _params = SurfaceAmbientParams.Empty;
    private static Bitmap? _noise;
    private static float _noiseAlpha;
    private static SurfaceLightFrame? _defaultFrame;
    private static Size _defaultFrameViewport;
    private static float _defaultFrameDpi;
    private static long _stateFrameToken = long.MinValue;
    private static SurfaceLightState? _state;
    private static Size _stateClient;
    private static Point _stateOrigin;
    private static float _stateDpi;

    /// <summary>装饰层是否可用（缺渐变令牌 ⇒ false：保持既有纯色页面，零回归）。</summary>
    internal static bool Enabled => _params.HasScene;

    /// <summary>
    /// 安装期写入口：参数来自当前 Profile 的令牌表；不可用 ⇒ 关闭本层。
    /// <para>装饰能力不再在这里收集：它由材质自己的 <c>decor</c> 声明，经
    /// <see cref="SurfaceDecorPlan"/> 统一判定（见 <see cref="SkinDecorator"/>）。</para>
    /// </summary>
    internal static void Configure(SurfaceAmbientParams parameters)
    {
        lock (Gate)
        {
            _params = parameters;
            DisposeScene();
        }
    }

    /// <summary>卸载：释放页面场/卡片 chrome/纹理。</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _params = SurfaceAmbientParams.Empty;
            DisposeScene();
            _noise?.Dispose();
            _noise = null;
            _noiseAlpha = 0;
        }

        var controls = new List<Control>();
        foreach (var pair in Chromes) controls.Add(pair.Key);
        foreach (var control in controls) Release(control);
    }

    /// <summary>释放某控件的 chrome 缓存（Detach/换材质时）。</summary>
    internal static void Release(Control? control)
    {
        if (control is null) return;
        if (!Chromes.TryGetValue(control, out var cache)) return;
        Chromes.Remove(control);
        cache.Image.Dispose();
    }

    /// <summary>
    /// 宿主背景：把页面场位图的对应切片搬到宿主客户区。
    /// <para>用 <see cref="CompositingMode.SourceCopy"/>：背景位图不透明，避免逐像素混合。</para>
    /// </summary>
    internal static void PaintHostSurface(Control control, Graphics graphics)
    {
        var scene = EnsureScene(control);
        if (scene is null) return;

        var offset = ScreenOffset(control);
        var source = new Rectangle(offset.X, offset.Y, control.ClientSize.Width, control.ClientSize.Height);
        var world = new Rectangle(0, 0, scene.Width, scene.Height);
        if (!source.IntersectsWith(world)) return;
        source.Intersect(world);

        var destination = new Rectangle(source.X - offset.X, source.Y - offset.Y, source.Width, source.Height);
        var begin = SkinRenderProfile.Begin();
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
            SkinRenderProfile.RecordLayer("decor:host", begin);
        }
    }

    /// <summary>
    /// 卡片 chrome（**卡片唯一消费入口**）：在同一张页面场上裁出留白环，再画外发光、
    /// 3.6px 细窄彩带、28px 宽柔高光（通道错位）、折射纹理与丝带连续段。
    /// <para>结果按「控件尺寸/屏幕位置/光签名/档位」缓存；命中时只贴一张位图。</para>
    /// </summary>
    internal static void PaintPanelChrome(Control control, Graphics graphics, Rectangle bounds, ResolvedMaterial material)
    {
        var chrome = EnsureChrome(control, bounds, material);
        if (chrome is null) return;
        var begin = SkinRenderProfile.Begin();
        graphics.DrawImageUnscaled(chrome.Image, bounds.X, bounds.Y);
        SkinRenderProfile.RecordLayer("decor:chrome", begin);
    }

    private static SceneSnapshot? EnsureScene(Control control)
    {
        if (!Enabled) return null;
        var form = control.FindForm() ?? control;
        var dpi = form.DeviceDpi <= 0 ? 96f : form.DeviceDpi;
        var size = form.ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return null;

        var state = ResolveState(form, dpi);
        // 装饰层是“静态艺术底”，不跟随 1Hz 光签名抖动：只取量化后的方向/相位（分钟级才换）。
        var token = string.Create(CultureInfo.InvariantCulture,
            $"{size.Width}x{size.Height}|{dpi:F0}|{_params.Token}|{LightToken(state)}");

        lock (Gate)
        {
            if (_scene is { } existing && string.Equals(existing.Token, token, StringComparison.Ordinal))
            {
                return existing;
            }

            var begin = SkinRenderProfile.Begin();
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

            SkinRenderProfile.RecordLayer("decor:scene", begin);
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
        // 椭圆的包围盒与页面不相交 ⇒ 整颗光斑在页面外，不画（锚点在宿主外时的真实行为）。
        if (centerX + radius < 0f || centerY + radius < 0f
            || centerX - radius > width || centerY - radius > height) return;

        using var path = new GraphicsPath();
        path.AddEllipse(centerX - radius, centerY - radius, radius * 2f, radius * 2f);
        var points = path.PointCount;
        if (points <= 0) return;

        var surround = new Color[points];
        var clear = Color.FromArgb(0, Color.White);
        for (var i = 0; i < points; i++) surround[i] = clear;

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, Color.White),
            SurroundColors = surround,
            CenterPoint = new PointF(centerX, centerY),
            // 焦点区 = 中心纯色区；越小核心越亮、向外衰减越长。
            FocusScales = new PointF(0.12f, 0.12f),
        };
        graphics.FillRectangle(brush, 0, 0, width, height);
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
        PaintBand(scene, width, height, centerX, centerY, amplitude, wavelength, bandWidth, shapePhase, hue0, span);
    }

    /// <summary>
    /// 逐像素沿带中心线把 (a,b) 推进一个族内色相（**ΔY 恒为 0 ⇒ L* 不动**）。
    /// <para>逐列算中心线与法线，带内每像素直接读/写位图：底色是刚刚画出来的真实 8bit 像素，
    /// 因此 ΔL* 只剩「ΔY=0 的线性化误差 + 整数取整」两项，实测 ≤0.09 L*（结构层差 0.2 L* 以内）。</para>
    /// <para>冷启动一次性成本：带宽 34px × 页宽 ≈ 4 万像素，每像素 3 次 cbrt + 27 格取整搜索。</para>
    /// </summary>
    private static void PaintBand(
        Bitmap scene, int width, int height,
        float centerX, float centerY, float amplitude, float wavelength, float bandWidth,
        float shapePhase, float hue0, float span)
    {
        var half = bandWidth * 0.5f;
        if (half <= 0f) return;
        var data = scene.LockBits(
            new Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadWrite,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var stride = data.Stride;
            var buffer = data.Scan0;
            var radians = (float)(2 * Math.PI / wavelength);
            for (var x = 0; x < width; x++)
            {
                var phase = radians * (x - centerX) + shapePhase * 2f * (float)Math.PI;
                var centerline = centerY + (x - centerX) * 0.34f + amplitude * MathF.Sin(phase);
                var slope = 0.34f + amplitude * radians * MathF.Cos(phase);
                // 法线 = (−slope, 1)/length；带宽 half 是**垂直距离**，故列内纵向半高 = half/length。
                var halfHeight = half / MathF.Sqrt(1f + slope * slope);
                var top = Math.Max(0, (int)Math.Floor(centerline - halfHeight));
                var bottom = Math.Min(height - 1, (int)Math.Ceiling(centerline + halfHeight));
                var hue = hue0 + Math.Clamp(x / Math.Max(1f, width), 0f, 1f) * span;
                for (var y = top; y <= bottom; y++)
                {
                    var offset = y * stride + x * 4;
                    var value = Marshal.ReadInt32(buffer, offset);
                    var blue = value & 0xFF;
                    var green = (value >> 8) & 0xFF;
                    var red = (value >> 16) & 0xFF;
                    var (shiftR, shiftG, shiftB) = ShiftChroma(red, green, blue, hue);
                    Marshal.WriteInt32(buffer, offset, (0xFF << 24) | (shiftR << 16) | (shiftG << 8) | shiftB);
                }
            }
        }
        finally
        {
            scene.UnlockBits(data);
        }
    }

    /// <summary>
    /// 把一个 8bit 像素的 (a,b) 沿 <paramref name="hueDegrees"/> 推进 <see cref="DecorChroma"/>，**L* 不变**。
    /// <para>原理：L* = 116·fy − 16，所以「L* 不动」等价于 Δfy = 0；此时
    /// Δa = 500·Δfx、Δb = −200·Δfz，用 fx/fz 的立方关系反解 ΔX/ΔZ，再过一次线性矩阵得到 ΔRGB。
    /// 取整后再在 ±1 邻域里挑亮度增量最小的格点（残差 ≤0.02 L*）。</para>
    /// <para>这样色相与彩度都是**按构造**成立的（不再依赖搜索去“猜”），实测 edgeChroma 从 7.85 收到 ≈3。</para>
    /// </summary>
    private static (int R, int G, int B) ShiftChroma(int red, int green, int blue, float hueDegrees)
    {
        var radians = hueDegrees * Math.PI / 180.0;
        var linearR = LinearChannel(red);
        var linearG = LinearChannel(green);
        var linearB = LinearChannel(blue);
        var fx = Math.Cbrt(linearR / 0.95047);
        var fz = Math.Cbrt(linearB / 1.08883);
        var deltaX = 3.0 * 0.95047 * fx * fx * (DecorChroma * Math.Cos(radians) / 500.0);
        var deltaZ = 3.0 * 1.08883 * fz * fz * (-DecorChroma * Math.Sin(radians) / 200.0);
        // ΔRGB 是**线性光**增量，必须回编码成 8bit 才算台阶；直接拿线性当字节用会小 300 倍
        // （实测整条带只动了 1 个字节 ⇒ 丝带在实机“看不见”）。这里回编码后取整，残差只剩量化。
        var targetR = Math.Max(0.0, linearR + 3.2406 * deltaX - 0.4986 * deltaZ);
        var targetG = Math.Max(0.0, linearG - 0.9689 * deltaX + 0.0415 * deltaZ);
        var targetB = Math.Max(0.0, linearB + 0.0557 * deltaX + 1.0570 * deltaZ);
        var deltaR = (int)Math.Round(Encode(targetR) * 255.0) - red;
        var deltaG = (int)Math.Round(Encode(targetG) * 255.0) - green;
        var deltaB = (int)Math.Round(Encode(targetB) * 255.0) - blue;

        var best = (R: red + deltaR, G: green + deltaG, B: blue + deltaB);
        var bestLuminance = Math.Abs(0.2126 * deltaR + 0.7152 * deltaG + 0.0722 * deltaB);
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dg = -1; dg <= 1; dg++)
            {
                for (var db = -1; db <= 1; db++)
                {
                    var luminance = Math.Abs(0.2126 * (deltaR + dr) + 0.7152 * (deltaG + dg) + 0.0722 * (deltaB + db));
                    if (luminance >= bestLuminance) continue;
                    bestLuminance = luminance;
                    best = (R: red + deltaR + dr, G: green + deltaG + dg, B: blue + deltaB + db);
                }
            }
        }

        return (Math.Clamp(best.R, 0, 255), Math.Clamp(best.G, 0, 255), Math.Clamp(best.B, 0, 255));
    }

    private static ChromeSnapshot? EnsureChrome(Control control, Rectangle bounds, ResolvedMaterial material)
    {
        var scene = EnsureScene(control);
        if (scene is null || bounds.Width <= 0 || bounds.Height <= 0) return null;

        var offset = ScreenOffset(control);
        var dpi = control.DeviceDpi <= 0 ? 96f : control.DeviceDpi;
        var form = control.FindForm() ?? control;
        var formDpi = form.DeviceDpi <= 0 ? 96f : form.DeviceDpi;
        var light = ResolveState(form, formDpi);
        var decoration = material.Decoration;
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{bounds.Width}x{bounds.Height}@{offset.X},{offset.Y}|{dpi:F0}|{scene.Token}|{_params.Token}|{(int)decoration}");

        if (Chromes.TryGetValue(control, out var cached)
            && string.Equals(cached.Key, key, StringComparison.Ordinal))
        {
            return cached;
        }

        var built = BuildChrome(bounds, offset, dpi, key, scene, light, decoration);
        if (built is null) return null;

        if (cached is not null)
        {
            cached.Image.Dispose();
            Chromes.Remove(control);
            Chromes.Add(control, built);
            return built;
        }

        Chromes.Add(control, built);
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
                using var outerPath = RoundedRect(bounds, radius + gutter);
                using var innerPath = RoundedRect(inner, radius);
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
                using var outerPath = RoundedRect(bounds, radius + gutter);
                graphics.SetClip(outerPath, CombineMode.Replace);
                PaintEdgeBand(graphics, inner, radius, scale, light, offset, scene.Width, scene.Height);
            }

            if ((decoration & SurfaceDecoration.Texture) != 0)
            {
                using var innerPath = RoundedRect(inner, radius);
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
            using var path = RoundedRect(rect, Math.Max(0f, radius - inset));
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
            _noise = BuildNoiseTile(alpha);
            _noiseAlpha = alpha;
        }

        using var brush = new TextureBrush(_noise, WrapMode.Tile);
        graphics.FillPath(brush, innerPath);
    }

    private static Bitmap BuildNoiseTile(int alpha)
    {
        const int size = 96;
        var tile = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var random = new Random(unchecked((int)0x4E4F4953));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = 128 + random.Next(-64, 65);
                tile.SetPixel(x, y, Color.FromArgb(alpha, value, value, value));
            }
        }

        return tile;
    }

    /// <summary>
    /// **单一色族**的宽度（度）：所有装饰色相都落在这一个连续窗口内。
    /// <para>声明的 <c>ambient_hue_budget</c>(150°) 只是上限；旧实现把 96° 丝带 + 96° 宽边带 +
    /// 各自相位叠在一起，实机量到 115° 连续弧且溢出到绿/橙，故把实际使用宽度收到 40°：
    /// 一个族 = 一段连续色相弧，族外 bin 占比按定义为 0。</para>
    /// </summary>
    private const float FamilySpan = 40f;

    /// <summary>装饰色彩度上限（C*）：丝带/细边只承载色相，不承载彩度，避免“贴纸感”。</summary>
    private const float DecorChroma = 3f;

    /// <summary>装饰描边最小宽度（px）：窄于此的笔触不做色相扫描（「1px 线禁止着色」）。</summary>
    private const float MinEdgeStroke = 2f;

    /// <summary>
    /// 细彩带笔触不透明度：**必须全不透明**。
    /// <para>半透明（旧 245/232）会让下方丝带透 4–9% 上来：装饰色只有 3 C*，Lab 色相在低彩度下
    /// 对混色极敏感，实测单边跨度被抬到 63–69°（即用户看到的「132°」那一类越界）。
    /// 因为笔触的 L* 已锁在该点底色上，不透明也不会留下亮度接缝。</para>
    /// </summary>
    private const int EdgeAlpha = 255;

    /// <summary>
    /// 装饰色：L* 锁定在<b>该点底色</b>的明度上，只叠加 <see cref="DecorChroma"/> 的色相。
    /// <para>这是“丝带不承载明度”唯一能落地的做法：先用同一份渐变（+径向光）算出该点底色，
    /// 再在 LCh 空间只改 hue，因此叠加前后 ΔL*≈0；8bit 量化误差约 0.1 L*。</para>
    /// </summary>
    private static Color DecorColor(double lstar, double backdropA, double backdropB, float hueDegrees, int alpha)
    {
        // **叠加式装饰**：在底色自身的 Lab 上叠加 ≤3 C* 的族内色相，L* 钉死。
        // 不能直接写成「绝对 Lab(L*, 3C*, 族色相)」：页面渐变本身是 C*≈9、H≈258 的蓝，
        // 绝对写法等于把蓝抽掉再补一点黄 —— 实测既抬 ΔL*(0.38) 又把合成色相钉在蓝族外，
        // 统计时就会误判成「装饰溢出族外」。装饰正确的定义是**增量**：Δ(a,b) 的色相 = 族色相。
        var radians = hueDegrees * Math.PI / 180.0;
        var targetA = backdropA + DecorChroma * Math.Cos(radians);
        var targetB = backdropB + DecorChroma * Math.Sin(radians);
        LabToRgb(lstar, targetA, targetB, out var baseR, out var baseG, out var baseB);

        // 色相与彩度**按构造成立**（Lab→RGB 是解析的），只剩 8bit 取整的 L* 残差：
        // 在 ±1 邻域里挑 |ΔL*| 最小的格点即可。彩度/色相仍带轻惩罚，防止为 0.01 L* 跑到 3.8 C*。
        baseR = Math.Clamp(baseR, 1, 254);
        baseG = Math.Clamp(baseG, 1, 254);
        baseB = Math.Clamp(baseB, 1, 254);
        var linear = new double[3][];
        for (var c = 0; c < 3; c++)
        {
            linear[c] = new double[3];
            var baseValue = c == 0 ? baseR : c == 1 ? baseG : baseB;
            for (var k = -1; k <= 1; k++) linear[c][k + 1] = LinearChannel(baseValue + k);
        }

        var rl = linear[0][1];
        var gl = linear[1][1];
        var bl2 = linear[2][1];
        LabAB(rl, gl, bl2, out var originA, out var originB);
        var slopeA = new double[3];
        var slopeB = new double[3];
        for (var c = 0; c < 3; c++)
        {
            var plus = c == 0 ? linear[0][2] : c == 1 ? linear[1][2] : linear[2][2];
            var minus = c == 0 ? linear[0][0] : c == 1 ? linear[1][0] : linear[2][0];
            LabAB(c == 0 ? plus : rl, c == 1 ? plus : gl, c == 2 ? plus : bl2, out var a1, out var b1);
            LabAB(c == 0 ? minus : rl, c == 1 ? minus : gl, c == 2 ? minus : bl2, out var a0, out var b0);
            slopeA[c] = (a1 - a0) / 2.0;
            slopeB[c] = (b1 - b0) / 2.0;
        }

        const double HueWeight = 0.002;   // 每度色相误差折算 0.002 L*
        const double ChromaWeight = 0.05; // 每 1 C* 彩度偏差折算 0.05 L*
        var bestR = baseR;
        var bestG = baseG;
        var bestB = baseB;
        var bestCost = double.MaxValue;
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dg = -1; dg <= 1; dg++)
            {
                for (var db = -1; db <= 1; db++)
                {
                    var luminance = 0.2126 * linear[0][dr + 1] + 0.7152 * linear[1][dg + 1] + 0.0722 * linear[2][db + 1];
                    var candidate = luminance <= 0.008856 ? 903.3 * luminance : 116.0 * Math.Cbrt(luminance) - 16.0;
                    var chromaA = originA + slopeA[0] * dr + slopeA[1] * dg + slopeA[2] * db - backdropA;
                    var chromaB = originB + slopeB[0] * dr + slopeB[1] * dg + slopeB[2] * db - backdropB;
                    var chromaError = Math.Abs(Math.Sqrt(chromaA * chromaA + chromaB * chromaB) - DecorChroma);
                    var hueError = Math.Abs(Wrap180(Math.Atan2(chromaB, chromaA) * 180.0 / Math.PI - hueDegrees));
                    var cost = Math.Abs(candidate - lstar) + hueError * HueWeight + chromaError * ChromaWeight;
                    if (cost >= bestCost) continue;
                    bestCost = cost;
                    bestR = baseR + dr;
                    bestG = baseG + dg;
                    bestB = baseB + db;
                }
            }
        }

        return Color.FromArgb(alpha, bestR, bestG, bestB);
    }

    /// <summary>Lab → 8bit sRGB（D65）。与 <see cref="DecorColor"/> 的 Δ(a,b) 增量口径配套。</summary>
    private static void LabToRgb(double lstar, double a, double b, out int red, out int green, out int blue)
    {
        var fy = (lstar + 16.0) / 116.0;
        var fx = fy + a / 500.0;
        var fz = fy - b / 200.0;
        double Inverse(double t) => t * t * t > 0.008856 ? t * t * t : (t - 16.0 / 116.0) / 7.787;
        var x = 0.95047 * Inverse(fx);
        var y = Inverse(fy);
        var z = 1.08883 * Inverse(fz);
        var r = 3.2406 * x - 1.5372 * y - 0.4986 * z;
        var g = -0.9689 * x + 1.8758 * y + 0.0415 * z;
        var bl = 0.0557 * x - 0.2040 * y + 1.0570 * z;
        red = (int)Math.Clamp(Math.Round(Encode(r) * 255.0), 0, 255);
        green = (int)Math.Clamp(Math.Round(Encode(g) * 255.0), 0, 255);
        blue = (int)Math.Clamp(Math.Round(Encode(bl) * 255.0), 0, 255);
    }

    /// <summary>线性 RGB → Lab 的 (a,b) 分量（只算色相需要的两项，不算 L*）。</summary>
    private static void LabAB(double r, double g, double b, out double a, out double bOut)
    {
        var x = 0.4124 * r + 0.3576 * g + 0.1805 * b;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = 0.0193 * r + 0.1192 * g + 0.9505 * b;
        double Forward(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
        var fx = Forward(x / 0.95047);
        var fy = Forward(y);
        var fz = Forward(z / 1.08883);
        a = 500.0 * (fx - fy);
        bOut = 200.0 * (fy - fz);
    }

    /// <summary>角度差（-180..180）。</summary>
    private static double Wrap180(double degrees)
    {
        var value = degrees % 360.0;
        if (value > 180.0) value -= 360.0;
        if (value < -180.0) value += 360.0;
        return value;
    }

    /// <summary>sRGB 8bit 通道 → 线性亮度分量（D65 相对亮度公式的输入）。</summary>
    private static double LinearChannel(int channel)
    {
        var value = Math.Clamp(channel, 0, 255) / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>线性分量 → sRGB 0..1。</summary>
    private static double Encode(double value)
    {
        value = value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
        return value;
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
        // GDI+ 45° 线性渐变：t = (x + y) / (w + h) 恰好从 (0,0) 的 from 走到 (w,h) 的 to。
        var t = Math.Clamp((x + y) / Math.Max(1f, width + height), 0f, 1f);
        double red = from.R + (to.R - from.R) * t;
        double green = from.G + (to.G - from.G) * t;
        double blue = from.B + (to.B - from.B) * t;
        var radial = RadialCoverage(x, y, width, height, scale, light);
        if (radial > 0f)
        {
            red += (255.0 - red) * radial;
            green += (255.0 - green) * radial;
            blue += (255.0 - blue) * radial;
        }

        var r = (int)Math.Round(red);
        var g = (int)Math.Round(green);
        var bl = (int)Math.Round(blue);
        LabAB(LinearChannel(r), LinearChannel(g), LinearChannel(bl), out a, out b);
        lstar = Lstar(r, g, bl);
    }

    /// <summary>径向光在该点的合成白量（0..1），与 <see cref="PaintRadialGlow"/> 同一形态（焦点 0.12 + 线性衰减）。</summary>
    private static float RadialCoverage(float x, float y, int width, int height, float scale, SurfaceLightState? light)
    {
        if (_params.RadialAlpha <= 0f || _params.RadialRadius <= 0f) return 0f;
        var shade = Math.Clamp(light?.Shade ?? 1f, 0f, 1f);
        if (shade <= 0.02f) return 0f;
        var radius = Math.Max(8f, _params.RadialRadius * scale);
        var dx = x - width * (light?.SpotX ?? 0.5f);
        var dy = y - height * (light?.SpotY ?? 0.5f);
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance >= radius) return 0f;
        var focus = radius * 0.12f;
        var falloff = distance <= focus ? 1.0 : 1.0 - (distance - focus) / (radius - focus);
        return (float)(_params.RadialAlpha * shade * falloff);
    }

    /// <summary>sRGB → L*（D65）。</summary>
    private static double Lstar(int red, int green, int blue)
    {
        double Linear(int channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
        return luminance <= 0.008856 ? 903.3 * luminance : 116.0 * Math.Cbrt(luminance) - 16.0;
    }

    /// <summary>
    /// 装饰层色相原点（度）= 虹彩环的声明基色 190°。
    /// <para>**全层只有一个色相原点**。旧实现有三套：丝带用 `IridescencePhase`(0..1) 当度（≈0.33°）、
    /// 光点用 `(i%9)/8 × span`（0..45°）、边缘用 `IridescenceRamp.HueAt(phase,0,0)=190+phase×360≈308.8°`。
    /// 于是同一帧里同时出现 190° 青族、308° 粉族和 0° 红族；实机整层统计因此量到 346.2°，
    /// 且粉色丝带在浅底上最显眼（浅底上 C*3 的粉比青可见度高），用户看到的就是“粉色贴纸”。
    /// 现在丝带、细边只取这一个原点，族外 bin 按定义为 0。</para>
    /// </summary>
    private static float FamilyHueBase() => IridescenceRamp.HueAt(0f, 0f, 0f);

    /// <summary>
    /// 丝带**几何**相位（只影响波形，不影响色相）：随光帧相位缓慢变化，避免形状僵死。
    /// <para>色相与几何解耦——旧实现把同一个 `IridescencePhase` 既当几何相位又当色相（差 360 倍），
    /// 正是“声明一个窗口、实机三个窗口”的来源。</para>
    /// </summary>
    private static float ShapePhase(SurfaceLightState? light) => light?.IridescencePhase ?? 0.5f;

    /// <summary>整层色相预算（度）：所有彩色分量共用这一个窗口，禁止多条带各自漂移。</summary>
    private static float HueBudget() => _params.HueBudget > 0f ? _params.HueBudget : 150f;

    /// <summary>
    /// 量化光源签名：方向 10° / 阴影 0.1 / 相位 0.1 / **光斑位置 0.05 页面宽高**。
    /// <para>光斑位置必须入键：锚点在屏幕上固定，窗口一移动 SpotX/SpotY 就变；不入键会留下上一位置的径向光。</para>
    /// <para>0.05 量化 = 1920 宽页面约 96px 一跳，避免拖动窗口时每一像素都重建 14ms 的页面场。</para>
    /// </summary>
    private static string LightToken(SurfaceLightState? state)
    {
        // Nullable<struct> 不能直接点成员：先解包成窄视图（与 SkinPaintEngine 的取值方式一致）。
        if (state is not { } light) return "no-light";
        var angle = (int)Math.Round(light.Angle / 10f) * 10;
        var spotX = (int)Math.Round(light.SpotX / 0.05f);
        var spotY = (int)Math.Round(light.SpotY / 0.05f);
        return string.Create(CultureInfo.InvariantCulture,
            $"A{angle}:X{light.ShadowX:F1}:Y{light.ShadowY:F1}:P{light.IridescencePhase:F1}:S{spotX}:T{spotY}");
    }

    /// <summary>诊断：最近一次页面场解算实际使用的光态（验收用，不参与绘制）。</summary>
    internal static SurfaceLightState? LastState => _state;

    /// <summary>
    /// 同一份光源：整个页面（宿主背景 + 所有卡片 chrome）只按**窗体**探针解算一次，
    /// 以 <see cref="SurfaceLightRegistry"/> 帧对象身份为缓存键，避免每帧重复采样。
    /// </summary>
    private static SurfaceLightState? ResolveState(Control form, float dpi)
    {
        var coverage = SurfaceLightPump.ScreenCoverage(form);
        var frame = SurfaceLightRegistry.Current;
        if (frame is null)
        {
            if (_defaultFrame is null
                || _defaultFrameDpi != dpi
                || _defaultFrameViewport.Width != (int)Math.Round(coverage.Width)
                || _defaultFrameViewport.Height != (int)Math.Round(coverage.Height))
            {
                _defaultFrame = SurfaceLight.DefaultFrame(coverage);
                _defaultFrameDpi = dpi;
                _defaultFrameViewport = new Size(
                    (int)Math.Round(coverage.Width), (int)Math.Round(coverage.Height));
            }

            frame = _defaultFrame;
        }

        var carrier = SurfaceLightCarrierOrigin.Of(form);
        dpi = carrier.Dpi;
        var screen = form.PointToScreen(Point.Empty);
        var frameToken = frame.Signature;
        if (_stateFrameToken == frameToken
            && _stateDpi == dpi
            && _stateOrigin == screen
            && _stateClient == form.ClientSize)
        {
            return _state;
        }

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
                SkinDiagnostics.RecordCarrierOutOfBounds(
                    "winforms:" + (string.IsNullOrEmpty(form.Name) ? form.GetType().Name : form.Name),
                    sample.SpotBoundsDetail);
            }

            SkinDiagnostics.RecordCarrierProjection(
                "winforms:" + (string.IsNullOrEmpty(form.Name) ? form.GetType().Name : form.Name),
                frame.Anchor,
                carrier,
                new SurfaceLightCarrierPoint(
                    sample.SpotLocalX, sample.SpotLocalY, sample.SpotInsideBounds, sample.SpotBoundsDetail));
        }

        _stateFrameToken = frameToken;
        _stateDpi = dpi;
        _stateOrigin = screen;
        _stateClient = form.ClientSize;
        _state = sample.HasValue ? SurfaceLightState.From(sample) : null;
        return _state;
    }

    /// <summary>控件客户区 → 其所属窗体客户区左上角的像素偏移。</summary>
    private static Point ScreenOffset(Control control)
    {
        var form = control.FindForm() ?? control;
        var origin = form.PointToScreen(Point.Empty);
        var target = control.PointToScreen(Point.Empty);
        return new Point(target.X - origin.X, target.Y - origin.Y);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        if (diameter <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
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
internal sealed class SurfaceAmbientParams
{
    internal static readonly SurfaceAmbientParams Empty = new();

    internal Color? GradientFrom { get; private init; }
    internal Color? GradientTo { get; private init; }
    internal Color? SparkleTint { get; private init; }
    internal Color? ShadowTint { get; private init; }
    internal float Gutter { get; private init; }
    internal float CornerRadius { get; private init; }
    internal float RibbonWidth { get; private init; }
    internal float RibbonAlpha { get; private init; }
    internal float RibbonSpan { get; private init; }
    internal float HueBudget { get; private init; }
    internal float RibbonAmplitude { get; private init; }
    internal float ChannelOffset { get; private init; }
    internal float SparkleDensity { get; private init; }
    internal float GlowRadius { get; private init; }
    internal float GlowAlpha { get; private init; }
    internal float RadialAlpha { get; private init; }
    internal float RadialRadius { get; private init; }
    internal float EdgeWidth { get; private init; }
    internal float EdgeHueSpan { get; private init; }
    internal float WideEdgeWidth { get; private init; }
    internal float TextureAlpha { get; private init; }
    internal string Token { get; private init; } = "decor-off";

    /// <summary>档位显式开关（<c>ambient_enabled</c>）：0 = 该档关闭装饰层，即使配色齐全也不画。</summary>
    internal bool Enabled { get; private init; } = true;

    internal bool HasScene => Enabled && GradientFrom is not null && GradientTo is not null && Gutter > 0 && CornerRadius > 0;

    /// <summary>令牌表 → 参数（令牌缺失用缺省值；颜色缺失即关闭）。</summary>
    internal static SurfaceAmbientParams FromTokens(IReadOnlyDictionary<string, string>? tokens)
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
        var sparkle = ParseColor("ambient_sparkle");
        var shadow = ParseColor("ambient_shadow");

        return new SurfaceAmbientParams
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
