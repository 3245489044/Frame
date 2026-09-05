using JsonUIBeautifier.Stylizer.Models;

namespace JsonUIBeautifier.Stylizer.Presets;

public static class StylePresets
{
    private static readonly Dictionary<string, StyleDefinition> _presets = new()
    {
        ["modern-light"] = CreateModernLight(),
        ["modern-dark"] = CreateModernDark(),
        ["material-design"] = CreateMaterialDesign(),
        ["fluent-ui"] = CreateFluentUI(),
        ["minimalist"] = CreateMinimalist(),
    };

    public static IEnumerable<string> GetPresetNames() => _presets.Keys;

    public static StyleDefinition? GetPreset(string name)
    {
        var key = name.ToLowerInvariant();
        return _presets.TryGetValue(key, out var preset) ? preset : null;
    }

    public static void RegisterPreset(string name, StyleDefinition style)
    {
        _presets[name.ToLowerInvariant()] = style;
    }


    private static StyleDefinition CreateModernLight() => new()
    {
        Name = "modern-light",
        Description = "现代浅色风格 - 简洁明亮，适合通用业务系统",
        Colors = new ColorPalette
        {
            Primary = "#2563EB",
            PrimaryLight = "#60A5FA",
            PrimaryDark = "#1D4ED8",
            OnPrimary = "#FFFFFF",
            Secondary = "#7C3AED",
            SecondaryLight = "#A78BFA",
            OnSecondary = "#FFFFFF",
            Success = "#10B981",
            Warning = "#F59E0B",
            Error = "#EF4444",
            Info = "#3B82F6",
            Surface = "#FFFFFF",
            SurfaceVariant = "#F8FAFC",
            Background = "#F1F5F9",
            TextPrimary = "#1F2937",
            TextSecondary = "#6B7280",
            TextMuted = "#9CA3AF",
            TextOnPrimary = "#FFFFFF",
            TextOnDark = "#F9FAFB",
            Border = "#E5E7EB",
            BorderFocus = "#2563EB",
            BorderLight = "#F3F4F6",
        },
        Typography = new TypographySystem
        {
            FontFamily = "Segoe UI",
            FontFamilyMono = "Consolas",
            FontSizeBase = 10,
            FontSizeXl = 14,
            FontSizeXxl = 18,
            FontBoldPrimary = true,
        },
        Spacing = new SpacingSystem { Unit = 4 },
        Borders = new BorderSystem
        {
            RadiusSm = 2,
            RadiusMd = 4,
            RadiusLg = 8,
            RadiusFull = 999,
            WidthDefault = 1,
            WidthFocus = 2,
        },
        Shadows = new ShadowSystem
        {
            Level1 = "0 1px 2px rgba(0,0,0,0.05)",
            Level2 = "0 4px 6px rgba(0,0,0,0.1)",
            Level3 = "0 10px 15px rgba(0,0,0,0.1)",
            Level4 = "0 20px 25px rgba(0,0,0,0.15)",
        },
    };

    private static StyleDefinition CreateModernDark() => new()
    {
        Name = "modern-dark",
        Description = "现代深色风格 - 护眼深邃，适合长时间使用",
        Colors = new ColorPalette
        {
            Primary = "#3B82F6",
            PrimaryLight = "#60A5FA",
            PrimaryDark = "#2563EB",
            OnPrimary = "#FFFFFF",
            Secondary = "#8B5CF6",
            SecondaryLight = "#A78BFA",
            OnSecondary = "#FFFFFF",
            Success = "#10B981",
            Warning = "#F59E0B",
            Error = "#EF4444",
            Info = "#3B82F6",
            Surface = "#1E293B",
            SurfaceVariant = "#334155",
            Background = "#0F172A",
            TextPrimary = "#F1F5F9",
            TextSecondary = "#94A3B8",
            TextMuted = "#64748B",
            TextOnPrimary = "#FFFFFF",
            TextOnDark = "#F9FAFB",
            Border = "#334155",
            BorderFocus = "#3B82F6",
            BorderLight = "#1E293B",
        },
        Typography = new TypographySystem
        {
            FontFamily = "Segoe UI",
            FontFamilyMono = "Cascadia Code, Consolas",
            FontSizeBase = 10,
            FontSizeXl = 14,
            FontSizeXxl = 18,
            FontBoldPrimary = true,
        },
        Spacing = new SpacingSystem { Unit = 4 },
        Borders = new BorderSystem
        {
            RadiusSm = 2,
            RadiusMd = 4,
            RadiusLg = 8,
            RadiusFull = 999,
            WidthDefault = 1,
            WidthFocus = 2,
        },
        Shadows = new ShadowSystem
        {
            Level1 = "0 1px 2px rgba(0,0,0,0.3)",
            Level2 = "0 4px 6px rgba(0,0,0,0.4)",
            Level3 = "0 10px 15px rgba(0,0,0,0.5)",
            Level4 = "0 20px 25px rgba(0,0,0,0.6)",
        },
    };

    private static StyleDefinition CreateMaterialDesign() => new()
    {
        Name = "material-design",
        Description = "Material Design 风格 - Google 设计语言，强调层次感和动效",
        Colors = new ColorPalette
        {
            Primary = "#1976D2",
            PrimaryLight = "#42A5F5",
            PrimaryDark = "#1565C0",
            OnPrimary = "#FFFFFF",
            Secondary = "#FF5722",
            SecondaryLight = "#FF8A65",
            OnSecondary = "#FFFFFF",
            Success = "#4CAF50",
            Warning = "#FFC107",
            Error = "#F44336",
            Info = "#2196F3",
            Surface = "#FFFFFF",
            SurfaceVariant = "#F5F5F5",
            Background = "#FAFAFA",
            TextPrimary = "#212121",
            TextSecondary = "#757575",
            TextMuted = "#BDBDBD",
            TextOnPrimary = "#FFFFFF",
            TextOnDark = "#FFFFFF",
            Border = "#E0E0E0",
            BorderFocus = "#1976D2",
            BorderLight = "#F5F5F5",
        },
        Typography = new TypographySystem
        {
            FontFamily = "Roboto, Segoe UI",
            FontFamilyMono = "Roboto Mono, Consolas",
            FontSizeBase = 10,
            FontSizeXl = 14,
            FontSizeXxl = 20,
            FontBoldPrimary = true,
        },
        Spacing = new SpacingSystem
        {
            Unit = 8,
            PaddingXs = 4,
            PaddingSm = 8,
            PaddingMd = 16,
            PaddingLg = 24,
            GapXs = 4,
            GapSm = 8,
            GapMd = 16,
            GapLg = 24,
        },
        Borders = new BorderSystem
        {
            RadiusSm = 2,
            RadiusMd = 4,
            RadiusLg = 8,
            RadiusFull = 999,
            WidthDefault = 1,
            WidthFocus = 2,
        },
        Shadows = new ShadowSystem
        {
            Level1 = "0 2px 4px rgba(0,0,0,0.12)",
            Level2 = "0 4px 8px rgba(0,0,0,0.14)",
            Level3 = "0 8px 16px rgba(0,0,0,0.16)",
            Level4 = "0 16px 24px rgba(0,0,0,0.18)",
        },
    };

    private static StyleDefinition CreateFluentUI() => new()
    {
        Name = "fluent-ui",
        Description = "Fluent UI 风格 - Microsoft 设计语言，简洁高效",
        Colors = new ColorPalette
        {
            Primary = "#0078D4",
            PrimaryLight = "#4BA0E0",
            PrimaryDark = "#005A9E",
            OnPrimary = "#FFFFFF",
            Secondary = "#107C10",
            SecondaryLight = "#4CAF50",
            OnSecondary = "#FFFFFF",
            Success = "#107C10",
            Warning = "#FFB900",
            Error = "#D13438",
            Info = "#0078D4",
            Surface = "#FFFFFF",
            SurfaceVariant = "#F3F3F3",
            Background = "#F3F3F3",
            TextPrimary = "#323130",
            TextSecondary = "#605E5C",
            TextMuted = "#A19F9D",
            TextOnPrimary = "#FFFFFF",
            TextOnDark = "#FFFFFF",
            Border = "#D1D0CE",
            BorderFocus = "#0078D4",
            BorderLight = "#EDEBE9",
        },
        Typography = new TypographySystem
        {
            FontFamily = "Segoe UI Variable, Segoe UI",
            FontFamilyMono = "Cascadia Code, Consolas",
            FontSizeBase = 10,
            FontSizeXl = 14,
            FontSizeXxl = 20,
            FontBoldPrimary = true,
        },
        Spacing = new SpacingSystem { Unit = 4 },
        Borders = new BorderSystem
        {
            RadiusNone = 0,
            RadiusSm = 0,
            RadiusMd = 2,
            RadiusLg = 4,
            RadiusFull = 999,
            WidthDefault = 1,
            WidthFocus = 2,
        },
        Shadows = new ShadowSystem
        {
            Level1 = "0 0.3px 0.9px rgba(0,0,0,0.1)",
            Level2 = "0 1.6px 3.6px rgba(0,0,0,0.1)",
            Level3 = "0 3.2px 7.2px rgba(0,0,0,0.12)",
            Level4 = "0 6.4px 14.4px rgba(0,0,0,0.14)",
        },
    };

    private static StyleDefinition CreateMinimalist() => new()
    {
        Name = "minimalist",
        Description = "极简风格 - 去除冗余装饰，黑白为主，专注内容",
        Colors = new ColorPalette
        {
            Primary = "#000000",
            PrimaryLight = "#333333",
            PrimaryDark = "#000000",
            OnPrimary = "#FFFFFF",
            Secondary = "#555555",
            SecondaryLight = "#777777",
            OnSecondary = "#FFFFFF",
            Success = "#2E7D32",
            Warning = "#F57F17",
            Error = "#C62828",
            Info = "#1565C0",
            Surface = "#FFFFFF",
            SurfaceVariant = "#F8F8F8",
            Background = "#FFFFFF",
            TextPrimary = "#000000",
            TextSecondary = "#666666",
            TextMuted = "#999999",
            TextOnPrimary = "#FFFFFF",
            TextOnDark = "#FFFFFF",
            Border = "#E8E8E8",
            BorderFocus = "#000000",
            BorderLight = "#F0F0F0",
        },
        Typography = new TypographySystem
        {
            FontFamily = "Inter, Segoe UI",
            FontFamilyMono = "JetBrains Mono, Consolas",
            FontSizeBase = 10,
            FontSizeXl = 14,
            FontSizeXxl = 18,
            FontBoldPrimary = true,
        },
        Spacing = new SpacingSystem { Unit = 8 },
        Borders = new BorderSystem
        {
            RadiusNone = 0,
            RadiusSm = 0,
            RadiusMd = 0,
            RadiusLg = 0,
            RadiusFull = 0,
            WidthDefault = 1,
            WidthFocus = 1,
        },
        Shadows = new ShadowSystem(),
    };
}
