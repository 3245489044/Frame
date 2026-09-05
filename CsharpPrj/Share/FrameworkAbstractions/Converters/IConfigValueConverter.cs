#nullable enable

namespace FrameworkAbstractions.Converters
{
    public interface IConfigValueConverter
    {
        object ConvertDock(string? dock);

        object ConvertAnchor(string? anchor);

        object ConvertWindowState(string? windowState);

        object ConvertFontStyle(string? fontStyle);

        object ConvertBorderStyle(string? borderStyle);

        object ConvertUpdateMode(string? updateMode);
    }
}
