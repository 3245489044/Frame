#nullable enable

namespace UiBackend;

/// <summary>
/// 图层相位（**唯一图层序口径**）：同相位内按 <see cref="ResolvedLayerSpec.Z"/> 升序，跨相位按枚举序绘制。
/// <para>相位取代「按 z 全局排序」与「字面量 specular 判动态」两套口径：shadow/backdrop 属 Underlay，
/// 不得盖在 fill/stroke 之上；specular 属 Sheen，是唯一动态相位。</para>
/// <para>相位在解析期随 <see cref="ResolvedLayerSpec.Phase"/> 定型，载体只按相位过滤/排序，不再解析图元名。</para>
/// </summary>
public enum LayerPhase
{
    /// <summary>底衬（backdrop / 外阴影）：必须先于填充与描边落笔。</summary>
    Underlay = 0,

    /// <summary>填充（fill / gradient / texture / decal）。</summary>
    Fill = 1,

    /// <summary>描边（stroke）。</summary>
    Edge = 2,

    /// <summary>内衬（innerShadow）：在填充之上、描边之下沿内沿落笔。</summary>
    Overlay = 3,

    /// <summary>高光（specular）：唯一随时间/光标变化的相位。</summary>
    Sheen = 4,
}

/// <summary>图元 → 相位（BCL-only 纯函数；载体禁各自判定图层序/动态层）。</summary>
public static class SurfaceLayerPhase
{
    /// <summary>图元所属相位（未知图元 ⇒ <see cref="LayerPhase.Overlay"/>，由调用方按「未实现」记账）。</summary>
    /// <param name="primitive">图元名。</param>
    public static LayerPhase Of(string primitive) => Of(ResolvedLayerSpec.KindOf(primitive));

    /// <summary>图元类型所属相位（解析期已定型；载体直接用本重载）。</summary>
    /// <param name="kind">图元类型。</param>
    public static LayerPhase Of(LayerPrimitive kind) => kind switch
    {
        LayerPrimitive.Backdrop => LayerPhase.Underlay,
        LayerPrimitive.Shadow => LayerPhase.Underlay,
        LayerPrimitive.Fill => LayerPhase.Fill,
        LayerPrimitive.Gradient => LayerPhase.Fill,
        LayerPrimitive.Texture => LayerPhase.Fill,
        LayerPrimitive.Decal => LayerPhase.Fill,
        LayerPrimitive.Stroke => LayerPhase.Edge,
        LayerPrimitive.InnerShadow => LayerPhase.Overlay,
        LayerPrimitive.Specular => LayerPhase.Sheen,
        _ => LayerPhase.Overlay,
    };

    /// <summary>是否动态层（唯一判据：specular；不再用字面量散落各处）。</summary>
    /// <param name="primitive">图元名。</param>
    public static bool IsDynamic(string primitive)
        => Of(primitive) == LayerPhase.Sheen;
}
