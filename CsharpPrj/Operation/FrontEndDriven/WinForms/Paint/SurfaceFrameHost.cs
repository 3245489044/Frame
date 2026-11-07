#nullable enable

using System.Drawing;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 边框宿主（批次 1.5）：为无法在自身材质上表达描边的控件/结构宿主补一圈 1px 内衬玻璃边。
/// <para>适用条件 = 族声明的 <see cref="SurfaceEdgeMode.FrameHost"/>。面本身仍由材质决定（如 host_surface=flat），
/// 边框不改变面，只把「宿主改平后丢掉的那条边」补回来（borderToPanel 归零即此因）。</para>
/// <para>描边来源不硬编码颜色：优先取宿主自身材质的 stroke 层，缺失时取同档全局玻璃材质（panel_frost）的 stroke 层。</para>
/// </summary>
public static class SurfaceFrameHost
{
    /// <summary>
    /// 取可用于边框宿主的描边（宿主材质优先，其次全局玻璃材质；都没有 ⇒ null）。
    /// <para>null 表示「无描边来源」，调用方必须记账（禁静默变成一个没有边的宿主）。</para>
    /// </summary>
    /// <param name="material">宿主当前材质（可为 null）。</param>
    public static BorderStroke? EdgeStrokeFor(ResolvedMaterial? material)
    {
        var own = LastStrokeOf(material);
        return own ?? LastStrokeOf(SkinMaterialCatalog.Global);
    }

    /// <summary>
    /// 在控件客户区内绘制 1px 内衬边框。
    /// </summary>
    /// <param name="graphics">目标画布（控件 Paint 的 Graphics）。</param>
    /// <param name="bounds">客户区边界（描边外沿贴该矩形）。</param>
    /// <param name="edgeMode">族声明的边缘归属（只有 FrameHost 才接管）。</param>
    /// <param name="material">宿主当前材质。</param>
    /// <param name="dpi">设备 DPI（宽度按 DIP 换算）。</param>
    /// <param name="reason">未绘制原因（null = 已绘制）。</param>
    public static bool TryDraw(
        Graphics graphics,
        Rectangle bounds,
        SurfaceEdgeMode edgeMode,
        ResolvedMaterial? material,
        float dpi,
        out string? reason)
    {
        reason = null;
        if (edgeMode != SurfaceEdgeMode.FrameHost)
        {
            reason = "edge-mode=" + edgeMode;
            return false;
        }

        if (graphics is null || bounds.Width <= 0 || bounds.Height <= 0)
        {
            reason = "empty-bounds";
            return false;
        }

        var stroke = EdgeStrokeFor(material);
        if (stroke is null)
        {
            reason = "stroke-source-missing";
            return false;
        }

        BorderPainter.DrawStroke(graphics, bounds, stroke, dpi);
        return true;
    }

    private static BorderStroke? LastStrokeOf(ResolvedMaterial? material)
    {
        if (material is null) return null;
        var strokes = BorderSpecProjection.From(material).Strokes;
        for (var index = strokes.Count - 1; index >= 0; index--)
        {
            if (strokes[index].HasVisibleWidth) return strokes[index];
        }

        return null;
    }
}
