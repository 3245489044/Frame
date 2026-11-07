#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
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

    /// <summary>安装期写入口：参数来自当前 Profile 的令牌表；不可用 ⇒ 关闭本层。</summary>
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

    /// <summary>是否应由本层接管卡片表面（玻璃 = 有 backdrop 层；其余材质不消费）。</summary>
    internal static bool ShouldChrome(ResolvedMaterial? material)
        => Enabled && material is { } m && m.Layers.Any(layer => layer.Primitive == "backdrop");

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
        using (var gradient = new LinearGradientBrush(
                   new Rectangle(0, 0, width, height), _params.GradientFrom!.Value, _params.GradientTo!.Value, 45f))
        {
            graphics.FillRectangle(gradient, 0, 0, width, height);
        }

        // 径向光：以光斑为中心、向外衰减的局部亮度分量（参考 R2：径向梯度 1.5–10 L*/100px）。
        PaintRadialGlow(graphics, width, height, scale, light);

        using (var ribbons = new RibbonBatch(graphics, width, height, Point.Empty, width, height, scale, light, _params, 1f))
        {
            ribbons.Paint();
        }

        PaintSparkles(graphics, width, height, scale, light);
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

    private static void PaintSparkles(Graphics graphics, int width, int height, float scale, SurfaceLightState? light)
    {
        var count = Math.Clamp((int)Math.Round(width * height * _params.SparkleDensity), 24, 220);
        if (count <= 0 || _params.SparkleTint is null) return;
        var halo = _params.SparkleTint.Value;
        var hueBase = HueBase(light);
        var hueSpan = HueBudget();
        var random = new Random(unchecked((int)((light?.LightSignature ?? 17) ^ 0x5A17)));
        for (var i = 0; i < count; i++)
        {
            var x = random.Next(0, Math.Max(1, width));
            var y = random.Next(0, Math.Max(1, height));
            var big = i % 12 == 0;
            var radius = (big ? 3.2f : 1.4f) * scale;
            var alpha = big ? 90 : 150;
            // 光点色相在整层色相预算内取样：带进第三族色调，又不扩大全屏色相跨度。
            var core = Color.FromArgb(IridescenceRamp.ArgbAt(hueBase, (i % 9) / 8f, hueSpan));
            using (var brush = new SolidBrush(Color.FromArgb(alpha, core)))
            {
                graphics.FillEllipse(brush, x - radius, y - radius, radius * 2f, radius * 2f);
            }

            if (!big) continue;
            using (var haloBrush = new SolidBrush(Color.FromArgb(26, halo)))
            {
                graphics.FillEllipse(haloBrush, x - radius * 3f, y - radius * 3f, radius * 6f, radius * 6f);
            }
        }
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
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{bounds.Width}x{bounds.Height}@{offset.X},{offset.Y}|{dpi:F0}|{scene.Token}|{_params.Token}");

        if (Chromes.TryGetValue(control, out var cached)
            && string.Equals(cached.Key, key, StringComparison.Ordinal))
        {
            return cached;
        }

        var built = BuildChrome(bounds, offset, dpi, key, scene, light);
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
        Rectangle bounds, Point offset, float dpi, string key, SceneSnapshot scene, SurfaceLightState? light)
    {
        var scale = dpi / 96f;
        var gutter = Math.Clamp((int)Math.Round(_params.Gutter * scale), 0, Math.Min(bounds.Width, bounds.Height) / 3);
        if (gutter <= 0 && _params.TextureAlpha <= 0f) return null;

        var radius = Math.Min(_params.CornerRadius * scale, Math.Min(bounds.Width, bounds.Height) / 2f);
        var inner = Rectangle.Inflate(bounds, -gutter, -gutter);
        inner.Width = Math.Max(1, inner.Width);
        inner.Height = Math.Max(1, inner.Height);

        // PArgb：SourceOver 贴图的 GDI+ 快路径（同像素数下比 Argb 快约 2×）。
        var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var phase = light?.IridescencePhase ?? 0.5f;
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            // 1) 留白环：同一条页面场的切片（位置/渐变/丝带/光点都与宿主背景一致）。
            if (gutter > 0)
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
            PaintGlow(graphics, inner, radius, scale, light);

            // 3) 双形态边缘光：细窄彩带（FWHM 3.6px / 色相 11°）+ 宽柔高光（28px / 色相 96° / 通道错位）。
            using (var outerPath = RoundedRect(bounds, radius + gutter))
            {
                graphics.SetClip(outerPath, CombineMode.Replace);
                PaintEdgeBand(graphics, inner, radius, phase, scale);
            }

            using (var innerPath = RoundedRect(inner, radius))
            {
                graphics.SetClip(innerPath, CombineMode.Replace);
                PaintWideEdgeBand(graphics, inner, radius, phase, scale, light);
                PaintTexture(graphics, innerPath, scale);
                graphics.ResetClip();
            }

            // 4) 丝带在卡面上的连续段：同一世界坐标公式，低强度叠加，使跨卡片的高光带不断裂。
            using var localRibbons = new RibbonBatch(
                graphics, scene.Width, scene.Height, offset, bounds.Width, bounds.Height, scale, light, _params, 0.35f);
            graphics.SetClip(RoundedRect(inner, radius), CombineMode.Replace);
            localRibbons.Paint();
            graphics.ResetClip();
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
    /// <para>四条直边各铺一段沿边方向的色相渐变（色相随弧长连续推进），四个圆角用相邻段的交界色补弧；
    /// 再叠一条更窄的内芯（相位 +半段）⇒ 跨带宽也有色相变化（参考 3.6px 带跨宽 11°/最大 19.5°）。</para>
    /// <para>为什么不用 <see cref="PathGradientBrush"/> 描边：GDI+ 把它当 Pen.Brush 时，
    /// 落在 brush 路径**内部**的笔触像素一律取 <c>CenterColor</c>（实测整条边恒色 165.2°），
    /// 无法做“沿边”渐变；按弧长给顶点取色也同样无效。改用每段 <see cref="LinearGradientBrush"/>
    /// 沿边铺色，几何、带宽、色相推进都完全可控。</para>
    /// </summary>
    private static void PaintEdgeBand(Graphics graphics, Rectangle inner, float radius, float phase, float scale)
    {
        var width = Math.Max(1f, _params.EdgeWidth * scale);
        // 整圈色相窗口：四条直边平分。用 2×令牌（≈22°）而不是 4×：实测 4× 在丝带穿过卡边时，
        // 细带 9% 的透明度会把丝带色透进来，单边实测可到 42°（越 25° 上限）。窗口收到 2× +
        // 提高不透明度后，单边理论 ≈8°、含内芯跨带 ≈12°，实测落在 5–25°。
        var window = Math.Min(HueBudget() * 0.5f, Math.Max(8f, _params.EdgeHueSpan * 2f));
        var segment = window / 4f;
        var coreShift = segment * 0.5f;
        var r = Math.Max(0f, radius);
        var coreWidth = Math.Max(1f, width * 0.55f);

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
            PaintEdgeGradient(graphics, a, b, phase, from * segment, (from + 1f) * segment, width, 245);
            PaintEdgeGradient(graphics, a, b, phase, from * segment + coreShift, (from + 1f) * segment + coreShift, coreWidth, 232);
        }

        if (r <= 0f) return;
        var corners = new (RectangleF Rect, float Start, float Hue)[]
        {
            (new RectangleF(inner.X, inner.Y, r * 2f, r * 2f), 180f, 0f),
            (new RectangleF(inner.Right - r * 2f, inner.Y, r * 2f, r * 2f), 270f, 1f),
            (new RectangleF(inner.Right - r * 2f, inner.Bottom - r * 2f, r * 2f, r * 2f), 0f, 2f),
            (new RectangleF(inner.X, inner.Bottom - r * 2f, r * 2f, r * 2f), 90f, 3f),
        };

        foreach (var (rect, start, hue) in corners)
        {
            using (var rim = new Pen(EdgeColor(phase, hue * segment, 245), width))
            {
                graphics.DrawArc(rim, rect, start, 90f);
            }

            using (var core = new Pen(EdgeColor(phase, hue * segment + coreShift, 232), coreWidth))
            {
                graphics.DrawArc(core, rect, start, 90f);
            }
        }
    }

    /// <summary>一段沿边方向的色相渐变（<paramref name="degrees0"/> → <paramref name="degrees1"/> 是相对色相）。</summary>
    private static void PaintEdgeGradient(
        Graphics graphics, PointF a, PointF b, float phase, float degrees0, float degrees1, float width, int alpha)
    {
        var horizontal = Math.Abs(b.Y - a.Y) < 0.01f;
        var forward = horizontal ? b.X > a.X : b.Y > a.Y;
        var rect = horizontal
            ? new RectangleF(Math.Min(a.X, b.X), a.Y, Math.Max(1f, Math.Abs(b.X - a.X)), 1f)
            : new RectangleF(a.X, Math.Min(a.Y, b.Y), 1f, Math.Max(1f, Math.Abs(b.Y - a.Y)));
        var from = EdgeColor(phase, degrees0, alpha);
        var to = EdgeColor(phase, degrees1, alpha);
        using var brush = new LinearGradientBrush(rect, forward ? from : to, forward ? to : from, horizontal ? 0f : 90f);
        using var pen = new Pen(brush, width) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        graphics.DrawLine(pen, a, b);
    }

    /// <summary>相对色相 → 颜色（<paramref name="degrees"/> 直接叠加在光场色相基准上，单位度）。</summary>
    private static Color EdgeColor(float phase, float degrees, int alpha)
        => Color.FromArgb(alpha, Color.FromArgb(IridescenceRamp.ArgbAt(phase, degrees / 360f, 360f)));

    private static void PaintWideEdgeBand(
        Graphics graphics, Rectangle inner, float radius, float phase, float scale, SurfaceLightState? light)
    {
        var width = Math.Max(4f, _params.WideEdgeWidth * scale);
        var top = light?.EdgeTop ?? 0.7f;
        var left = light?.EdgeLeft ?? 0.7f;
        var right = light?.EdgeRight ?? 0.3f;
        var bottom = light?.EdgeBottom ?? 0.3f;
        var peak = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
        if (peak <= 1e-3f) return;
        top /= peak;
        left /= peak;
        right /= peak;
        bottom /= peak;

        var strongest = Math.Max(Math.Max(top, bottom), Math.Max(left, right));
        RectangleF band;
        float angle;
        if (strongest == top)
        {
            band = new RectangleF(inner.X + radius, inner.Y, Math.Max(1f, inner.Width - radius * 2f), width);
            angle = 90f;
        }
        else if (strongest == bottom)
        {
            band = new RectangleF(inner.X + radius, inner.Y + inner.Height - width, Math.Max(1f, inner.Width - radius * 2f), width);
            angle = 270f;
        }
        else if (strongest == left)
        {
            band = new RectangleF(inner.X, inner.Y + radius, width, Math.Max(1f, inner.Height - radius * 2f));
            angle = 0f;
        }
        else
        {
            band = new RectangleF(inner.X + inner.Width - width, inner.Y + radius, width, Math.Max(1f, inner.Height - radius * 2f));
            angle = 180f;
        }

        var span = Math.Min(_params.RibbonSpan, HueBudget());
        using (var brush = SpectralBrush(band, phase, span, 120, angle))
        {
            graphics.FillRectangle(brush, band);
        }

        // 通道错位：沿边缘方向偏移 ±N px 的红/蓝薄带（色相取光谱两端，禁写死颜色）。
        var alpha = Math.Clamp((int)Math.Round(_params.RibbonAlpha * 255f * 0.55f), 0, 255);
        var first = IridescenceRamp.ArgbAt(phase, 0f, span);
        var last = IridescenceRamp.ArgbAt(phase, 1f, span);
        var shift = _params.ChannelOffset * scale;
        var thin = width * 0.28f;
        if (band.Width >= band.Height)
        {
            using var firstBrush = new SolidBrush(Color.FromArgb(alpha, Color.FromArgb(first)));
            using var lastBrush = new SolidBrush(Color.FromArgb(alpha, Color.FromArgb(last)));
            graphics.FillRectangle(firstBrush, band.X - shift, band.Y, band.Width, thin);
            graphics.FillRectangle(lastBrush, band.X + shift, band.Y + band.Height - thin, band.Width, thin);
        }
        else
        {
            using var firstBrush = new SolidBrush(Color.FromArgb(alpha, Color.FromArgb(first)));
            using var lastBrush = new SolidBrush(Color.FromArgb(alpha, Color.FromArgb(last)));
            graphics.FillRectangle(firstBrush, band.X, band.Y - shift, thin, band.Height);
            graphics.FillRectangle(lastBrush, band.X + band.Width - thin, band.Y + shift, thin, band.Height);
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

    private static LinearGradientBrush SpectralBrush(RectangleF rect, float phase, float span, int alpha, float angle)
    {
        var safe = new RectangleF(rect.X, rect.Y, Math.Max(1f, rect.Width), Math.Max(1f, rect.Height));
        var brush = new LinearGradientBrush(safe, Color.Black, Color.White, angle);
        var stops = IridescenceRamp.Stops(phase, span, 5);
        var colors = stops.Select(argb => Color.FromArgb(alpha, Color.FromArgb(argb))).ToArray();
        brush.InterpolationColors = new ColorBlend
        {
            Colors = colors,
            Positions = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f },
        };
        return brush;
    }

    /// <summary>整层色相基准（与光源同一份相位，所有丝带/边缘/光点共用）。</summary>
    private static float HueBase(SurfaceLightState? light) => light?.IridescencePhase ?? 0.5f;

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

    /// <summary>丝带批次：同一世界坐标公式，场景/卡面/通道错位共用一份实现。</summary>
    private sealed class RibbonBatch : IDisposable
    {
        private readonly Graphics _graphics;
        private readonly int _worldWidth;
        private readonly int _worldHeight;
        private readonly Point _origin;
        private readonly int _localWidth;
        private readonly int _localHeight;
        private readonly float _scale;
        private readonly SurfaceLightState? _light;
        private readonly SurfaceAmbientParams _params;
        private readonly float _alphaScale;

        internal RibbonBatch(
            Graphics graphics, int worldWidth, int worldHeight, Point origin,
            int localWidth, int localHeight, float scale, SurfaceLightState? light,
            SurfaceAmbientParams parameters, float alphaScale)
        {
            _graphics = graphics;
            _worldWidth = worldWidth;
            _worldHeight = worldHeight;
            _origin = origin;
            _localWidth = localWidth;
            _localHeight = localHeight;
            _scale = scale;
            _light = light;
            _params = parameters;
            _alphaScale = alphaScale;
        }

        internal void Paint()
        {
            var hueBase = HueBase(_light);
            var baseAlpha = Math.Clamp((int)Math.Round(_params.RibbonAlpha * 255f * _alphaScale), 0, 255);
            if (baseAlpha <= 2) return;

            // 全层色相预算：所有带共用同一个基准窗口；带间只做预算内的小幅偏移，
            // 不允许每带各加一个独立相位（否则叠加后总跨度会超出预算）。
            var budget = HueBudget();
            var span = Math.Min(_params.RibbonSpan, budget);
            var bandOffsetPhase = Math.Max(0f, budget - span) / 360f;

            for (var band = 0; band < 2; band++)
            {
                var shapePhase = hueBase + band * 0.28f;
                var huePhase = hueBase + band * bandOffsetPhase;
                var centerX = _worldWidth * (band == 0 ? 0.18f : 0.62f);
                var centerY = _worldHeight * (band == 0 ? 0.46f : 0.66f);
                var amplitude = _worldHeight * _params.RibbonAmplitude;
                var wavelength = _worldWidth * 1.05f;
                var width = _params.RibbonWidth * _scale * (band == 0 ? 1f : 0.82f);

                using var path = BuildBand(centerX, centerY, amplitude, wavelength, width, shapePhase);
                using var brush = SpectralBrush(path.GetBounds(), huePhase, span, baseAlpha, angle: 90f);
                _graphics.FillPath(brush, path);

                // 通道错位：红/蓝薄带各偏移 ±N px（色相取该带光谱两端，仍在预算窗口内）。
                var shifted = _params.ChannelOffset * _scale;
                var tintAlpha = Math.Clamp((int)Math.Round(baseAlpha * 0.55f), 0, 255);
                using var firstPath = BuildBand(centerX, centerY - shifted, amplitude, wavelength, width * 0.3f, shapePhase + 0.02f);
                using var lastPath = BuildBand(centerX, centerY + shifted, amplitude, wavelength, width * 0.3f, shapePhase - 0.02f);
                using var firstBrush = new SolidBrush(
                    Color.FromArgb(tintAlpha, Color.FromArgb(IridescenceRamp.ArgbAt(huePhase, 0f, span))));
                using var lastBrush = new SolidBrush(
                    Color.FromArgb(tintAlpha, Color.FromArgb(IridescenceRamp.ArgbAt(huePhase, 1f, span))));
                _graphics.FillPath(firstBrush, firstPath);
                _graphics.FillPath(lastBrush, lastPath);
            }
        }

        private GraphicsPath BuildBand(
            float centerX, float centerY, float amplitude, float wavelength, float width, float phase)
        {
            var top = new List<PointF>();
            var bottom = new List<PointF>();
            var step = Math.Max(8f, _localWidth / 48f);
            for (var x = -step; x <= _localWidth + step; x += step)
            {
                var worldX = x + _origin.X;
                var y = centerY + (worldX - centerX) * 0.34f
                        + amplitude * MathF.Sin((float)(2 * Math.PI * (worldX - centerX) / wavelength) + phase * 2f * (float)Math.PI);
                var localY = y - _origin.Y;
                var slope = 0.34f + amplitude * (2f * (float)Math.PI / wavelength)
                                        * MathF.Cos((float)(2 * Math.PI * (worldX - centerX) / wavelength) + phase * 2f * (float)Math.PI);
                var length = MathF.Sqrt(1f + slope * slope);
                var nx = -slope / length;
                var ny = 1f / length;
                var half = width * 0.5f;
                top.Add(new PointF(x + nx * half, localY + ny * half));
                bottom.Add(new PointF(x - nx * half, localY - ny * half));
            }

            var path = new GraphicsPath();
            if (top.Count >= 2)
            {
                path.AddPolygon(top.Concat(Enumerable.Reverse(bottom)).ToArray());
            }

            return path;
        }

        public void Dispose() { }
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
