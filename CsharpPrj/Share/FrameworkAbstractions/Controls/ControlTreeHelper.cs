#nullable enable
using System.Text;
using System.Windows.Forms;
using TConfig.Controls;

namespace FrameworkAbstractions.Controls
{
    public static class ControlTreeHelper
    {
        public static void ListControls(Control parent, StringBuilder sb, int indent = 0)
        {
            if (parent == null) return;

            foreach (Control control in parent.Controls)
            {
                var indentStr = new string(' ', indent * 2);
                var value = GetControlPreviewValue(control);

                sb.AppendLine($"{indentStr}{control.GetType().Name}: {control.Name}" +
                             (string.IsNullOrEmpty(value) ? "" : $" = \"{Truncate(value, 50)}\""));

                if (control.HasChildren)
                {
                    ListControls(control, sb, indent + 1);
                }
            }
        }

        public static void ListControls(object parent, StringBuilder sb, int indent = 0)
        {
            if (parent == null) return;

            if (parent is Control control)
            {
                ListControls(control, sb, indent);
                return;
            }

            var controlsProperty = parent.GetType().GetProperty("Controls");
            if (controlsProperty == null) return;

            var controls = controlsProperty.GetValue(parent) as System.Collections.IEnumerable;
            if (controls == null) return;

            foreach (var child in controls)
            {
                var indentStr = new string(' ', indent * 2);
                var typeName = child.GetType().Name;
                var name = GetPropertyValue<string>(child, "Name") ?? "(unnamed)";
                var value = GetControlPreviewValue(child);

                sb.AppendLine($"{indentStr}{typeName}: {name}" +
                             (string.IsNullOrEmpty(value) ? "" : $" = \"{Truncate(value, 50)}\""));

                var hasChildren = GetPropertyValue<bool>(child, "HasChildren");
                if (hasChildren)
                {
                    ListControls(child, sb, indent + 1);
                }
            }
        }

        public static string GetControlPreviewValue(object control, IControlValueAccessor? accessor = null)
        {
            if (control == null) return string.Empty;

            if (accessor != null)
            {
                return accessor.GetValue(control)?.ToString() ?? string.Empty;
            }

            var typeName = control.GetType().Name;
            return typeName switch
            {
                "TextBox" => GetPropertyValue<string>(control, "Text") ?? string.Empty,
                "Label" => GetPropertyValue<string>(control, "Text") ?? string.Empty,
                "Button" => GetPropertyValue<string>(control, "Text") ?? string.Empty,
                "CheckBox" => GetPropertyValue<bool>(control, "Checked").ToString(),
                "ComboBox" => GetPropertyValue<string>(control, "Text") ?? string.Empty,
                "NumericUpDown" => GetPropertyValue<decimal>(control, "Value").ToString(),
                "DateTimePicker" => GetPropertyValue<DateTime>(control, "Value").ToString("yyyy-MM-dd"),
                _ => string.Empty
            };
        }

        private static T? GetPropertyValue<T>(object obj, string propertyName)
        {
            try
            {
                var property = obj.GetType().GetProperty(propertyName);
                if (property != null)
                {
                    var value = property.GetValue(obj);
                    if (value is T tValue)
                        return tValue;
                }
            }
            catch
            {
            }
            return default;
        }

        private static string Truncate(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value[..maxLength] + "...";
        }
    }
}
