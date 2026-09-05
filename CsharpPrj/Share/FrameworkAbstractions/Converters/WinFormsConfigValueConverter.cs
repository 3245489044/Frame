#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace FrameworkAbstractions.Converters
{
    public class WinFormsConfigValueConverter : IConfigValueConverter
    {
        public static WinFormsConfigValueConverter Default { get; } = new();

        public object ConvertDock(string? dock)
            => dock?.ToLower() switch
            {
                "fill" => DockStyle.Fill,
                "top" => DockStyle.Top,
                "bottom" => DockStyle.Bottom,
                "left" => DockStyle.Left,
                "right" => DockStyle.Right,
                _ => DockStyle.None
            };

        public object ConvertAnchor(string? anchor)
        {
            var result = AnchorStyles.None;
            if (anchor == null) return AnchorStyles.Top | AnchorStyles.Left;
            if (anchor.Contains("Top")) result |= AnchorStyles.Top;
            if (anchor.Contains("Bottom")) result |= AnchorStyles.Bottom;
            if (anchor.Contains("Left")) result |= AnchorStyles.Left;
            if (anchor.Contains("Right")) result |= AnchorStyles.Right;
            return result;
        }

        public object ConvertWindowState(string? windowState)
            => windowState?.ToLower() switch
            {
                "maximized" => FormWindowState.Maximized,
                "minimized" => FormWindowState.Minimized,
                _ => FormWindowState.Normal
            };

        public object ConvertFontStyle(string? fontStyle)
            => fontStyle?.ToLower() switch
            {
                "bold" => FontStyle.Bold,
                "italic" => FontStyle.Italic,
                "underline" => FontStyle.Underline,
                "strikeout" => FontStyle.Strikeout,
                _ => FontStyle.Regular
            };

        public object ConvertBorderStyle(string? borderStyle)
            => borderStyle?.ToLower() switch
            {
                "fixed3d" => BorderStyle.Fixed3D,
                "none" => BorderStyle.None,
                _ => BorderStyle.FixedSingle
            };

        public object ConvertUpdateMode(string? updateMode)
            => updateMode?.ToLower() switch
            {
                "never" => DataSourceUpdateMode.Never,
                "onvalidation" => DataSourceUpdateMode.OnValidation,
                "onpropertychanged" => DataSourceUpdateMode.OnPropertyChanged,
                _ => DataSourceUpdateMode.OnPropertyChanged
            };
    }
}
