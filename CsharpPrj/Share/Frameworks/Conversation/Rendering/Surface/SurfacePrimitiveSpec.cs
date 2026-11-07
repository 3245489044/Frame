#nullable enable

using System;
using System.Collections.Generic;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>图元类型（解析期一次定型；未知图元保留原始名，由载体按「未实现」记账）。</summary>
public enum SurfacePrimitiveKind
{
    /// <summary>无法识别（保持原始 primitive 文本）。</summary>
    Unknown = 0,

    /// <summary>纯色填充。</summary>
    Fill,

    /// <summary>线性渐变。</summary>
    Gradient,

    /// <summary>描边（几何另行经 <c>BorderSpec</c> 归一化）。</summary>
    Stroke,

    /// <summary>外阴影。</summary>
    Shadow,

    /// <summary>内阴影。</summary>
    InnerShadow,

    /// <summary>背景模糊/采样。</summary>
    Backdrop,

    /// <summary>高光（唯一动态层）。</summary>
    Specular,

    /// <summary>纹理贴图。</summary>
    Texture,

    /// <summary>贴画。</summary>
    Decal,

    /// <summary>遮罩。</summary>
    Mask,

    /// <summary>噪点。</summary>
    Noise,
}

/// <summary>
/// 类型化图元参数的**兼容视图**（K1 过渡层）。
/// <para>
/// 解析唯一实现已下移到 <see cref="ResolvedLayerSpec"/>（Plate，两载体共用）；本记录仅为既有调用点保留原字段形态，
/// 新代码请直接消费 <see cref="ResolvedLayerSpec"/> / <see cref="ResolvedLayerSpecProjection"/>。
/// </para>
/// </summary>
/// <param name="Kind">图元类型。</param>
/// <param name="Primitive">原始图元名（未知类型时保真）。</param>
/// <param name="ColorArgb">主色（打包 ARGB；无法解析 ⇒ 0 且 Issues 有记录）。</param>
/// <param name="GradientStops">渐变色标（≥1；仅 Gradient）。</param>
/// <param name="Angle">角度（度；gradient / specular）。</param>
/// <param name="Width">宽度（DIP；stroke / innerShadow / specular-edge）。</param>
/// <param name="Radius">圆角半径（DIP）或 specular-spot 的半径比例。</param>
/// <param name="Blur">模糊半径（DIP）。</param>
/// <param name="OffsetX">水平偏移（DIP）。</param>
/// <param name="OffsetY">垂直偏移（DIP）。</param>
/// <param name="Spread">阴影扩散（DIP）。</param>
/// <param name="Strength">强度（0..1；specular）。</param>
/// <param name="Mode">高光模式（band / edge / spot）。</param>
/// <param name="Asset">资源相对路径（texture / decal）。</param>
/// <param name="Slice">九宫格切片（设备像素；0 = 不切）。</param>
/// <param name="NoRepeat">是否禁止平铺。</param>
public sealed record SurfacePrimitiveSpec(
    SurfacePrimitiveKind Kind,
    string Primitive,
    int ColorArgb,
    IReadOnlyList<int> GradientStops,
    float Angle,
    float Width,
    float Radius,
    float Blur,
    float OffsetX,
    float OffsetY,
    float Spread,
    float Strength,
    string Mode,
    string Asset,
    int Slice,
    bool NoRepeat)
{
    /// <summary>主色 alpha（0..255）。</summary>
    public int Alpha => (ColorArgb >> 24) & 0xFF;

    /// <summary>解析一层（委托 <see cref="ResolvedLayerSpecProjection"/> 的唯一实现；字段形态保持不变）。</summary>
    /// <param name="layer">已解析层。</param>
    /// <param name="issues">产出问题（无则空）。</param>
    public static SurfacePrimitiveSpec Parse(ResolvedLayer layer, out IReadOnlyList<SurfaceIssue> issues)
    {
        var spec = ResolvedLayerSpecProjection.Parse(layer, out issues);
        return new SurfacePrimitiveSpec(
            KindOf(spec.Primitive),
            spec.Primitive,
            spec.ColorArgb,
            spec.Gradient.Colors,
            spec.Angle,
            spec.Width,
            spec.Radius,
            spec.Blur,
            spec.OffsetX,
            spec.OffsetY,
            spec.Spread,
            spec.Strength,
            spec.Mode,
            spec.Asset,
            spec.Slice,
            spec.NoRepeat);
    }

    /// <summary>图元 → 类型（未知保留 Unknown）。</summary>
    /// <param name="primitive">图元名。</param>
    public static SurfacePrimitiveKind KindOf(string primitive) => ResolvedLayerSpec.KindOf(primitive) switch
    {
        LayerPrimitive.Fill => SurfacePrimitiveKind.Fill,
        LayerPrimitive.Gradient => SurfacePrimitiveKind.Gradient,
        LayerPrimitive.Stroke => SurfacePrimitiveKind.Stroke,
        LayerPrimitive.Shadow => SurfacePrimitiveKind.Shadow,
        LayerPrimitive.InnerShadow => SurfacePrimitiveKind.InnerShadow,
        LayerPrimitive.Backdrop => SurfacePrimitiveKind.Backdrop,
        LayerPrimitive.Specular => SurfacePrimitiveKind.Specular,
        LayerPrimitive.Texture => SurfacePrimitiveKind.Texture,
        LayerPrimitive.Decal => SurfacePrimitiveKind.Decal,
        LayerPrimitive.Mask => SurfacePrimitiveKind.Mask,
        LayerPrimitive.Noise => SurfacePrimitiveKind.Noise,
        _ => SurfacePrimitiveKind.Unknown,
    };
}
