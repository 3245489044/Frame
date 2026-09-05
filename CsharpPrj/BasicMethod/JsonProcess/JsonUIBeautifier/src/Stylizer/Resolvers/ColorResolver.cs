using JsonUIBeautifier.Stylizer.Models;

namespace JsonUIBeautifier.Stylizer.Resolvers;

public static class ColorResolver
{
    public static (string? BackColor, string? ForeColor, string? BorderColor) ResolveColors(
        ControlRole role, ColorPalette palette)
    {
        return role switch
        {
            ControlRole.PrimaryButton => (
                palette.Primary,
                palette.TextOnPrimary,
                null
            ),
            ControlRole.SecondaryButton => (
                palette.Surface,
                palette.TextPrimary,
                palette.Border
            ),
            ControlRole.Title => (
                null,
                palette.TextPrimary,
                null
            ),
            ControlRole.SectionHeader => (
                palette.SurfaceVariant,
                palette.TextSecondary,
                null
            ),
            ControlRole.BodyText => (
                null,
                palette.TextPrimary,
                null
            ),
            ControlRole.Caption => (
                null,
                palette.TextMuted,
                null
            ),
            ControlRole.Input => (
                palette.Surface,
                palette.TextPrimary,
                palette.Border
            ),
            ControlRole.Container => (
                palette.SurfaceVariant,
                palette.TextPrimary,
                palette.Border
            ),
            ControlRole.DataTable => (
                palette.Surface,
                palette.TextPrimary,
                palette.BorderLight
            ),
            ControlRole.StatusBar => (
                palette.SurfaceVariant,
                palette.TextMuted,
                null
            ),
            _ => (
                null,
                palette.TextPrimary,
                null
            ),
        };
    }

    public static (double FontSize, bool Bold) ResolveFontSize(
        TextRole role, TypographySystem typography)
    {
        return role switch
        {
            TextRole.Title => (typography.FontSizeXxl, typography.FontBoldPrimary),
            TextRole.SectionHeader => (typography.FontSizeXl, true),
            TextRole.BodyText => (typography.FontSizeBase, false),
            TextRole.Caption => (typography.FontSizeSm, false),
            TextRole.Muted => (typography.FontSizeXs, false),
            TextRole.Code => (typography.FontSizeBase, false),
            _ => (typography.FontSizeBase, false),
        };
    }

    public static int ResolveBorderRadius(string type, BorderSystem borders)
    {
        return type.ToLowerInvariant() switch
        {
            "button" => borders.RadiusMd,
            "textbox" or "richtextbox" => borders.RadiusSm,
            "combobox" => borders.RadiusSm,
            "panel" => borders.RadiusMd,
            "groupbox" => borders.RadiusMd,
            "datagridview" or "listview" or "datagrid" => borders.RadiusSm,
            "checkbox" => borders.RadiusSm,
            _ => borders.RadiusNone,
        };
    }

    public static Dictionary<string, object?> ConvertPaletteToTheme(ColorPalette palette)
    {
        return new Dictionary<string, object?>
        {
            ["primaryColor"] = palette.Primary,
            ["primaryLight"] = palette.PrimaryLight,
            ["primaryDark"] = palette.PrimaryDark,
            ["secondaryColor"] = palette.Secondary,
            ["surfaceColor"] = palette.Surface,
            ["surfaceVariant"] = palette.SurfaceVariant,
            ["backgroundColor"] = palette.Background,
            ["textPrimary"] = palette.TextPrimary,
            ["textSecondary"] = palette.TextSecondary,
            ["textMuted"] = palette.TextMuted,
            ["borderColor"] = palette.Border,
            ["borderFocus"] = palette.BorderFocus,
            ["successColor"] = palette.Success,
            ["warningColor"] = palette.Warning,
            ["errorColor"] = palette.Error,
            ["infoColor"] = palette.Info,
        };
    }
}
