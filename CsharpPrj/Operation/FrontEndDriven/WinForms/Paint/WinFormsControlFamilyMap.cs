#nullable enable

using System.Windows.Forms;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// WinForms 控件类型 → 表面族键（唯一映射点）。
/// <para>族声明/注册表在 Share.Conversation.Rendering.Surface（载体无关）；此处只做运行时类型识别。</para>
/// </summary>
public static class WinFormsControlFamilyMap
{
    /// <summary>按控件与描述符类型解析族（描述符键优先，运行时类型兜底）。</summary>
    /// <param name="control">目标控件。</param>
    /// <param name="typeKey">描述符类型键（可空）。</param>
    public static SurfaceControlFamilySpec? Resolve(Control? control, string? typeKey)
    {
        var byDescriptor = SurfaceControlFamilyRegistry.For(typeKey);
        if (byDescriptor is not null) return byDescriptor;
        return control is null ? null : SurfaceControlFamilyRegistry.For(RuntimeKey(control));
    }

    /// <summary>运行时类型 → 族键。</summary>
    /// <param name="control">目标控件。</param>
    public static string RuntimeKey(Control control)
        => control switch
        {
            SplitterPanel => "splitterpanel",
            SplitContainer => "splitcontainer",
            CheckBox => "checkbox",
            ButtonBase => "button",
            TextBoxBase => "textbox",
            ListView => "listview",
            ListBox => "listbox",
            ComboBox => "combobox",
            TreeView => "treeview",
            GroupBox => "groupbox",
            Label => "label",
            TableLayoutPanel => "tablelayoutpanel",
            FlowLayoutPanel => "flowlayoutpanel",
            UserControl => "usercontrol",
            Panel => "panel",
            StatusStrip => "statusstrip",
            MenuStrip => "menustrip",
            ToolStrip => "toolstrip",
            ProgressBar => "progressbar",
            TrackBar => "trackbar",
            NumericUpDown => "numericupdown",
            DateTimePicker => "datetimepicker",
            _ => control.GetType().Name.ToLowerInvariant(),
        };
}
