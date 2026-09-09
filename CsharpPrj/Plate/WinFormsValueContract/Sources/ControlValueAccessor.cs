using System.Reflection;
using System.Windows.Forms;

namespace WinFormsValueContract.Sources;

/// <summary>
/// 控件取值内核（单点实现）——收敛散落在 WorkFlowMethod.ParameterValueResolver / Modules.MetaCommand.WinFormsControlService
/// 等处的「按控件类型读取当前值（含 ListView.SelectedItems 特判）」重复逻辑。
/// 统一语义：ListView 取选中项 Tag ?? Text；ComboBox 取 Text ?? SelectedItem；其余取 Text ?? Value ?? Checked；无则 null。
/// </summary>
public static class ControlValueAccessor
{
    /// <summary>按控件类型读取当前值。非控件对象返回 null。</summary>
    public static object? GetControlValue(object control)
    {
        if (control == null)
            return null;

        if (control is ListView listView)
            return ReadListViewSelectedValue(listView);

        if (control is ComboBox comboBox)
            return comboBox.Text ?? comboBox.SelectedItem?.ToString();

        if (control is Control c)
        {
            var textProp = c.GetType().GetProperty("Text");
            if (textProp != null && textProp.CanRead)
                return textProp.GetValue(c);

            var valueProp = c.GetType().GetProperty("Value")
                          ?? c.GetType().GetProperty("Checked");
            return valueProp?.GetValue(c);
        }

        // 非 Control 对象：退化为反射读取 Text/Value/Checked（兼容 ListView 等经 object 传入的场景）
        var genericText = control.GetType().GetProperty("Text",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (genericText != null && genericText.CanRead)
            return genericText.GetValue(control);

        var genericValue = control.GetType().GetProperty("Value",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? control.GetType().GetProperty("Checked",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return genericValue?.GetValue(control);
    }

    /// <summary>ListView 选中项取值：首个选中项的 Tag（优先）或 Text；无选中项返回 null。</summary>
    public static object? ReadListViewSelectedValue(ListView listView)
    {
        if (listView == null)
            return null;

        if (listView.SelectedItems.Count == 0)
            return null;

        var item = listView.SelectedItems[0];
        return item.Tag ?? item.Text;
    }
}
