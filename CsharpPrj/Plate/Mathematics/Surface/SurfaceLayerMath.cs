#nullable enable

using System;

namespace MathematicalAlgorithms.Surface;

/// <summary>浮点盒（设备像素或 DIP，由调用方口径决定）。</summary>
public readonly record struct SurfaceBoxF(float X, float Y, float Width, float Height);

/// <summary>阴影落笔盒 + 峰值 alpha。</summary>
public readonly record struct SurfaceShadowGeometry(SurfaceBoxF Box, int Alpha);

/// <summary>内环落笔几何（外沿半径/内沿半径/环宽，均为设备像素）。</summary>
public readonly record struct SurfaceInnerRingGeometry(
    SurfaceBoxF Inner, float RingWidthPx, float InnerRadiusPx, float OuterRadiusPx);

/// <summary>条带高光的渐变位置与锚点平移（设备像素/比例）。</summary>
public readonly record struct SurfaceSpecularBand(
    float HalfWidth, float Low, float High, int ShiftX, int ShiftY, int BandPx);

/// <summary>径向光斑的目标矩形（中心 + 半径 + 取整盒）。</summary>
public readonly record struct SurfaceSpotGeometry(
    float CenterX, float CenterY, float Radius, int TargetX, int TargetY, int TargetSize);

/// <summary>
/// 落笔位置/几何数学（**纯数值、零载体类型**）：阴影偏移与盒、内环、条带高光、光斑、边缘尺寸。
/// <para>输入全是标量/盒；Graphics/Brush/Path 的创建仍留在绘制层。</para>
/// </summary>
public static class SurfaceLayerMath
{
    /// <summary>阴影方向：声明给「离面距离」，方向由光场 ShadowX/Y 决定（无光时保持声明偏移）。</summary>
    public static void ShadowOffset(
        float declaredX, float declaredY, bool hasLight, float shadowDirX, float shadowDirY,
        out float offsetX, out float offsetY)
    {
        offsetX = declaredX;
        offsetY = declaredY;
        if (!hasLight) return;
        var magnitude = Math.Max(2f, MathF.Sqrt(declaredX * declaredX + declaredY * declaredY));
        offsetX = shadowDirX * magnitude;
        offsetY = shadowDirY * magnitude;
    }

    /// <summary>偏移/扩散 → 阴影落笔盒（宽高至少 1）+ alpha。</summary>
    public static SurfaceShadowGeometry ShadowBox(
        float boundsX, float boundsY, float width, float height,
        float offsetX, float offsetY, float spread, float blur)
        => new(
            new SurfaceBoxF(
                boundsX + offsetX - spread,
                boundsY + offsetY - spread,
                Math.Max(1f, width + spread * 2),
                Math.Max(1f, height + spread * 2)),
            ShadowAlpha(blur));

    /// <summary>阴影 alpha（声明 blur 越大越深，封顶 120）。</summary>
    public static int ShadowAlpha(float blur)
        => Math.Min(120, 40 + RoundedNonNegative(blur) * 4);

    /// <summary>四舍五入后的非负值（模糊半径/采样半径口径）。</summary>
    public static int RoundedNonNegative(float value)
        => (int)Math.Round(Math.Max(0, value));

    /// <summary>backdrop 近似渐变的底端 alpha（顶端 × 0.24）。</summary>
    public static int BackdropBottomAlpha(int topAlpha)
        => (int)Math.Round(topAlpha * 0.24);

    /// <summary>内环填充的几何：环宽至少 1px，内盒宽高至少 0.5px，内半径扣掉环宽。</summary>
    public static SurfaceInnerRingGeometry InnerRing(
        float boundsX, float boundsY, float width, float height,
        float ringWidthPx, float radiusPx)
    {
        var ring = Math.Max(1f, ringWidthPx);
        var inner = new SurfaceBoxF(
            boundsX + ring,
            boundsY + ring,
            Math.Max(0.5f, width - ring * 2),
            Math.Max(0.5f, height - ring * 2));
        return new SurfaceInnerRingGeometry(inner, ring, Math.Max(0f, radiusPx - ring), radiusPx);
    }

    /// <summary>
    /// 条带高光几何：带宽按短边 12%（28..64px），沿光向投影求半宽（0.012..0.12），
    /// 条带中心=世界锚点的载体局部位置（半径有效时整体平移）。
    /// </summary>
    public static SurfaceSpecularBand SpecularBand(
        float width, float height, float angleDegrees,
        float spotLocalX, float spotLocalY, float spotRadiusDips, float dpiScale)
    {
        var bandPx = (int)Math.Round(Math.Clamp(Math.Min(width, height) * 0.12, 28, 64));
        var radians = angleDegrees * Math.PI / 180.0;
        var extent = Math.Max(1.0,
            Math.Abs(width * Math.Cos(radians)) + Math.Abs(height * Math.Sin(radians)));
        var half = Math.Clamp(bandPx / 2.0 / extent, 0.012, 0.12);
        var low = (float)(0.5 - half);
        var high = (float)(0.5 + half);
        var shiftX = spotRadiusDips > 0f
            ? (int)Math.Round(spotLocalX * dpiScale - width * 0.5f)
            : 0;
        var shiftY = spotRadiusDips > 0f
            ? (int)Math.Round(spotLocalY * dpiScale - height * 0.5f)
            : 0;
        return new SurfaceSpecularBand((float)half, low, high, shiftX, shiftY, bandPx);
    }

    /// <summary>无运行时光的静态 sheen alpha（声明 strength × 70，封顶 160）。</summary>
    public static int StaticSheenAlpha(float strength)
        => Math.Clamp((int)Math.Round(70 * strength), 0, 160);

    /// <summary>静态 sheen 条带高度（上三分之一，至少 1px）。</summary>
    public static int StaticSheenHeight(int height)
        => Math.Max(1, height / 3);

    /// <summary>
    /// 径向光斑几何：半径来自世界光斑半径（回退短边比例），中心来自锚点载体局部位置
    /// （半径无效时回退归一化 SpotX/SpotY，并夹到 [0.05,0.95]）。
    /// </summary>
    public static SurfaceSpotGeometry SpotGeometry(
        float boundsX, float boundsY, float width, float height,
        float spotX, float spotY, float spotLocalX, float spotLocalY,
        float spotRadiusDips, float radiusFactor, float dpiScale)
    {
        var factor = Math.Clamp(radiusFactor, 0.25f, 1.2f);
        var radius = spotRadiusDips > 0f
            ? Math.Max(8f, spotRadiusDips * dpiScale * factor)
            : Math.Max(8f, Math.Min(width, height) * factor);
        var centerX = spotRadiusDips > 0f
            ? boundsX + spotLocalX * dpiScale
            : boundsX + width * Math.Clamp(spotX, 0.05f, 0.95f);
        var centerY = spotRadiusDips > 0f
            ? boundsY + spotLocalY * dpiScale
            : boundsY + height * Math.Clamp(spotY, 0.05f, 0.95f);
        var size = (int)Math.Round(radius * 2);
        return new SurfaceSpotGeometry(
            centerX, centerY, radius,
            (int)Math.Round(centerX - radius),
            (int)Math.Round(centerY - radius),
            size);
    }

    /// <summary>光斑目标盒是否与面板相交（完全在外 ⇒ 不落笔）。</summary>
    public static bool SpotVisible(
        int targetX, int targetY, int size,
        float boundsLeft, float boundsTop, float boundsRight, float boundsBottom)
        => targetX + size >= boundsLeft && targetX <= boundsRight
           && targetY + size >= boundsTop && targetY <= boundsBottom;

    /// <summary>边缘高光厚度（声明宽取整，1..6px）。</summary>
    public static int EdgeThickness(float widthDips)
        => Math.Clamp((int)Math.Round(widthDips), 1, 6);

    /// <summary>边缘高光长度（高度的 1/3，至少 1px）。</summary>
    public static int EdgeRunLength(int height)
        => Math.Max(1, height / 3);

    /// <summary>小元素圆角上限：短边 &lt; 120 DIP 时半径夹到 12 DIP（防大令牌把按钮画成胶囊）。</summary>
    public static float EffectiveRadiusDips(float shortSideDips, float radiusDips)
    {
        const float SmallElementShortSideDips = 120f;
        const float SmallElementMaxRadiusDips = 12f;
        return shortSideDips < SmallElementShortSideDips
            ? Math.Min(radiusDips, SmallElementMaxRadiusDips)
            : radiusDips;
    }

    /// <summary>九宫格切片（slice 夹到半边长内）。</summary>
    public static int NineSliceCut(int slice, int width, int height)
        => Math.Min(slice, Math.Min(width, height) / 2);

    /// <summary>九宫格中段尺寸（至少 1px）。</summary>
    public static int NineSliceCenter(int extent, int cut)
        => Math.Max(1, extent - cut * 2);

    /// <summary>单通道提亮（amount 0..1）。</summary>
    public static int LightenChannel(int channel, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return (int)Math.Round(channel + (255 - channel) * t);
    }
}
