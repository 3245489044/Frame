#nullable enable

using System;

namespace MathematicalAlgorithms.Surface;

/// <summary>点（浮点）。</summary>
public readonly record struct SurfacePointF(float X, float Y);

/// <summary>页面径向光几何（中心/半径 + 峰值 alpha）。</summary>
public readonly record struct SurfaceRadialGlowGeometry(
    float CenterX, float CenterY, float Radius, int Alpha);

/// <summary>丝带几何（色相跨度 + 中心线/振幅/波长/带宽，均为像素）。</summary>
public readonly record struct SurfaceRibbonGeometry(
    float Span, float CenterX, float CenterY, float Amplitude, float Wavelength, float BandWidth);

/// <summary>卡片 chrome 布局（留白环 gutter/圆角/inner 盒，DIP 或像素由调用方口径决定）。</summary>
public readonly record struct SurfaceChromeGeometry(
    int Gutter, float Radius, float Scale,
    float InnerX, float InnerY, float InnerWidth, float InnerHeight);

/// <summary>外发光的一步（盒 + alpha + 半径 + 笔宽）。</summary>
public readonly record struct SurfaceGlowStep(
    SurfaceBoxF Rect, int Alpha, float Radius, float PenWidth);

/// <summary>细边带的一段（端点 + 起始段序号）。</summary>
public readonly record struct SurfaceEdgeSegment(SurfacePointF A, SurfacePointF B, float From);

/// <summary>细边带圆角段（弧盒 + GDI 起始角 + 段序号）。</summary>
public readonly record struct SurfaceEdgeCorner(SurfaceBoxF Rect, float Start, float SegmentIndex);

/// <summary>沿边渐变线（目标盒 + 方向）。</summary>
public readonly record struct SurfaceGradientLine(SurfaceBoxF Rect, bool Horizontal, bool Forward);

/// <summary>
/// 页面场/卡片装饰布局数学（**纯数值、零载体类型**）：径向光、丝带、chrome 尺度、外发光、细边带。
/// <para>颜色科学仍在 <see cref="SurfaceAmbientMath"/>；本类只算位置/尺寸/透明度。</para>
/// </summary>
public static class SurfaceAmbientLayoutMath
{
    /// <summary>无光阴影方向的缺省分量（两载体同一口径）。</summary>
    public const float DefaultShadowDirection = 0.35f;

    /// <summary>径向光：中心 = 页面尺寸 × 归一化光斑；半径 = max(8, 声明半径 × scale)；alpha = 声明 × 照度。</summary>
    public static SurfaceRadialGlowGeometry RadialGlow(
        int width, int height, float radialAlpha, float radialRadius,
        float scale, float shade, float spotX, float spotY)
    {
        var lit = Math.Clamp(shade, 0f, 1f);
        // 照度低于阈值 ⇒ 关闭径向光（与旧实现 shade<=0.02 的早退同一判据）。
        var alpha = lit <= 0.02f
            ? 0
            : Math.Clamp((int)Math.Round(radialAlpha * lit * 255f), 0, 255);
        return new SurfaceRadialGlowGeometry(
            width * spotX,
            height * spotY,
            Math.Max(8f, radialRadius * scale),
            alpha);
    }

    /// <summary>丝带几何：跨度取「声明跨度 ∩ 色相预算 ∩ 族宽」，中心 18% × 46%，波长 1.05×页宽。</summary>
    public static SurfaceRibbonGeometry Ribbon(
        int width, int height, float ribbonSpan, float hueBudget, float familySpan,
        float amplitudeRatio, float ribbonWidth, float scale)
    {
        var span = Math.Min(ribbonSpan, Math.Min(hueBudget, familySpan));
        return new SurfaceRibbonGeometry(
            span,
            width * 0.18f,
            height * 0.46f,
            height * amplitudeRatio,
            width * 1.05f,
            ribbonWidth * scale);
    }

    /// <summary>页面场径向光在引擎绘制项中的不透明度（声明 × 照度，0..1）。</summary>
    public static float SpotOpacity(float radialAlpha, float shade)
        => Math.Clamp(radialAlpha * Math.Clamp(shade, 0f, 1f), 0f, 1f);

    /// <summary>
    /// chrome 尺度门：gutter 取整并夹到短边 1/3；纹理关闭且无留白 ⇒ 不建位图；
    /// 最小边 &lt; 2×(gutter+圆角) 的控件不消费 chrome（卡片尺度语法）。
    /// </summary>
    public static bool TryChrome(
        float boundsX, float boundsY, int width, int height,
        float gutterDips, float cornerRadiusDips, float textureAlpha, float scale,
        out SurfaceChromeGeometry geometry)
    {
        var gutter = Math.Clamp(
            (int)Math.Round(gutterDips * scale), 0, Math.Min(width, height) / 3);
        geometry = default;
        if (gutter <= 0 && textureAlpha <= 0f) return false;

        var minSide = Math.Min(width, height);
        if (minSide < 2f * (gutter + cornerRadiusDips * scale)) return false;

        var radius = Math.Min(cornerRadiusDips * scale, minSide / 2f);
        geometry = new SurfaceChromeGeometry(
            gutter, radius, scale,
            boundsX + gutter, boundsY + gutter,
            Math.Max(1, width - gutter * 2), Math.Max(1, height - gutter * 2));
        return true;
    }

    /// <summary>外发光第 index 步（内缩 + 光照方向偏移 + 递减 alpha，笔宽随 scale）。</summary>
    public static SurfaceGlowStep GlowStep(
        float innerX, float innerY, float innerWidth, float innerHeight, float radius,
        float spread, float shadowDirX, float shadowDirY, float glowAlpha,
        int index, int steps, float scale)
    {
        var shadowX = shadowDirX * spread * 0.35f;
        var shadowY = shadowDirY * spread * 0.35f;
        var inset = spread * (index / (float)steps);
        var rect = new SurfaceBoxF(
            innerX + inset + shadowX,
            innerY + inset + shadowY,
            Math.Max(1f, innerWidth - inset * 2f),
            Math.Max(1f, innerHeight - inset * 2f));
        var alpha = Math.Clamp(
            (int)Math.Round(glowAlpha * 255f * (1f - index / (float)steps)), 0, 255);
        return new SurfaceGlowStep(rect, alpha, Math.Max(0f, radius - inset), Math.Max(1f, 5f * scale));
    }

    /// <summary>细边带色相窗口：族宽 ∩ 4×单边跨度；返回窗口与单段宽度。</summary>
    public static (float Window, float Segment) EdgeBandWindow(float edgeHueSpan, float familySpan)
    {
        var window = Math.Min(familySpan, 4f * Math.Max(1f, edgeHueSpan));
        return (window, window / 4f);
    }

    /// <summary>四条直边（顺时针 上→右→下→左）端点与段序号。</summary>
    public static SurfaceEdgeSegment[] EdgeSegments(
        float innerX, float innerY, float innerWidth, float innerHeight, float radius)
    {
        var r = Math.Max(0f, radius);
        var right = innerX + innerWidth;
        var bottom = innerY + innerHeight;
        return new[]
        {
            new SurfaceEdgeSegment(new SurfacePointF(innerX + r, innerY), new SurfacePointF(right - r, innerY), 0f),
            new SurfaceEdgeSegment(new SurfacePointF(right, innerY + r), new SurfacePointF(right, bottom - r), 1f),
            new SurfaceEdgeSegment(new SurfacePointF(right - r, bottom), new SurfacePointF(innerX + r, bottom), 2f),
            new SurfaceEdgeSegment(new SurfacePointF(innerX, bottom - r), new SurfacePointF(innerX, innerY + r), 3f),
        };
    }

    /// <summary>四个圆角弧（左上/右上/右下/左下）弧盒与 GDI 起始角。</summary>
    public static SurfaceEdgeCorner[] EdgeCorners(
        float innerX, float innerY, float innerWidth, float innerHeight, float radius)
    {
        var r = Math.Max(0f, radius);
        var right = innerX + innerWidth;
        var bottom = innerY + innerHeight;
        return new[]
        {
            new SurfaceEdgeCorner(new SurfaceBoxF(innerX, innerY, r * 2f, r * 2f), 180f, 0f),
            new SurfaceEdgeCorner(new SurfaceBoxF(right - r * 2f, innerY, r * 2f, r * 2f), 270f, 1f),
            new SurfaceEdgeCorner(new SurfaceBoxF(right - r * 2f, bottom - r * 2f, r * 2f, r * 2f), 0f, 2f),
            new SurfaceEdgeCorner(new SurfaceBoxF(innerX, bottom - r * 2f, r * 2f, r * 2f), 90f, 3f),
        };
    }

    /// <summary>沿边渐变的落笔盒与方向（水平/垂直；端点顺序决定 from/to）。</summary>
    public static SurfaceGradientLine EdgeGradientLine(SurfacePointF a, SurfacePointF b)
    {
        var horizontal = Math.Abs(b.Y - a.Y) < 0.01f;
        var forward = horizontal ? b.X > a.X : b.Y > a.Y;
        var rect = horizontal
            ? new SurfaceBoxF(Math.Min(a.X, b.X), a.Y, Math.Max(1f, Math.Abs(b.X - a.X)), 1f)
            : new SurfaceBoxF(a.X, Math.Min(a.Y, b.Y), 1f, Math.Max(1f, Math.Abs(b.Y - a.Y)));
        return new SurfaceGradientLine(rect, horizontal, forward);
    }

    /// <summary>折射纹理 alpha（声明 × 255，0..255）。</summary>
    public static int TextureAlpha(float textureAlpha)
        => Math.Clamp((int)Math.Round(textureAlpha * 255f), 0, 255);
}
