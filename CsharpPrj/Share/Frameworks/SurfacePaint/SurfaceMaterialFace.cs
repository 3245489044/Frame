#nullable enable

using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Materials;
using UiBackend;

namespace Share.SurfacePaint;

/// <summary>
/// 材质面/描边取值（**唯一实现**）：属性通道、页底色、边框宿主共用同一「首个不透明填充 / 末条可见描边」口径。
/// <para>此前三处各自遍历层栈（分别只认 fill、认 fill|gradient、取首条/末条描边），同材质会得出不同结果。</para>
/// </summary>
public static class SurfaceMaterialFace
{
    /// <summary>首个可承载的填充色（<c>fill</c>/<c>gradient</c>，A&gt;0）；半透明按 <paramref name="behind"/> 合成成不透明。</summary>
    /// <param name="material">已解析材质（null ⇒ false）。</param>
    /// <param name="behind">背后不透明底色（ARGB）。</param>
    /// <param name="argb">不透明面底色（A=255）。</param>
    public static bool TryGetOpaqueFill(ResolvedMaterial? material, int behind, out int argb)
    {
        argb = 0;
        if (material is null) return false;

        foreach (var spec in ResolvedLayerSpecProjection.ParseCached(material.Layers).Specs)
        {
            if (spec.Kind is not (LayerPrimitive.Fill or LayerPrimitive.Gradient) || !spec.HasColor) continue;
            if (((spec.ColorArgb >> 24) & 0xFF) == 0) continue;
            argb = SurfaceColorComposition.Over(spec.ColorArgb, behind);
            return true;
        }

        return false;
    }

    /// <summary>材质声明的描边（末条可见宽度优先，其次末条带色描边；都没有 ⇒ null）。</summary>
    /// <param name="material">已解析材质（null ⇒ null）。</param>
    public static BorderStroke? DeclaredStroke(ResolvedMaterial? material)
    {
        if (material is null) return null;

        var strokes = BorderSpecProjection.From(material).Strokes;
        for (var index = strokes.Count - 1; index >= 0; index--)
        {
            if (strokes[index].HasVisibleWidth) return strokes[index];
        }

        return strokes.Count > 0 ? strokes[^1] : null;
    }
}
