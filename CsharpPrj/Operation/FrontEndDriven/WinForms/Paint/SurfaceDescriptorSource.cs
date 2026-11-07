#nullable enable

using System.Windows.Forms;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.WinForms.Applier;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// A2：把 <see cref="SplitterPanel"/> 从“被实体查询过滤”补回描述符树。
/// <para>只绑定物理 HWND（Panel1/Panel2）到父 <see cref="SplitContainer"/> 的材质，不改布局/报告查询。</para>
/// </summary>
public static class SurfaceDescriptorSource
{
    /// <summary>为两个 SplitterPanel 绑定父级表面（幂等）。</summary>
    /// <param name="split">目标 SplitContainer。</param>
    /// <param name="parent">父级描述符（提供 SkinRefs / Issues）。</param>
    /// <returns>是否至少绑定了一个面板。</returns>
    public static bool BindInheritedPanels(SplitContainer split, ControlDescriptor parent)
    {
        var bound = false;
        bound |= Bind(split.Panel1, parent);
        bound |= Bind(split.Panel2, parent);
        if (!bound)
        {
            FrontEndDrivenLog.Log("SurfaceDescriptorSource_unbound", new
            {
                type = split.GetType().Name,
                name = split.Name,
                reason = "splitter-panel-missing",
            });
        }

        return bound;
    }

    private static bool Bind(SplitterPanel panel, ControlDescriptor parent)
    {
        if (panel is null) return false;
        var descriptor = new ControlDescriptor
        {
            Id = panel.Name,
            Name = panel.Name,
            Type = "splitterpanel",
            SkinRefs = parent.SkinRefs,
            Issues = parent.Issues,
        };
        SkinDecorator.Attach(panel, descriptor);
        return true;
    }
}
