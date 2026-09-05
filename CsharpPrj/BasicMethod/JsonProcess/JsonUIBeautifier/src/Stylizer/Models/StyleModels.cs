namespace JsonUIBeautifier.Stylizer.Models;

public class StyleDefinition
{
    public string Name { get; set; } = "custom";
    public string Description { get; set; } = "";
    public ColorPalette Colors { get; set; } = new();
    public TypographySystem Typography { get; set; } = new();
    public SpacingSystem Spacing { get; set; } = new();
    public BorderSystem Borders { get; set; } = new();
    public ShadowSystem Shadows { get; set; } = new();
    public AnimationSystem Animations { get; set; } = new();
}

public class ColorPalette
{
    public string Primary { get; set; } = "#2563EB";
    public string PrimaryLight { get; set; } = "#60A5FA";
    public string PrimaryDark { get; set; } = "#1D4ED8";
    public string OnPrimary { get; set; } = "#FFFFFF";

    public string Secondary { get; set; } = "#7C3AED";
    public string SecondaryLight { get; set; } = "#A78BFA";
    public string OnSecondary { get; set; } = "#FFFFFF";

    public string Success { get; set; } = "#10B981";
    public string Warning { get; set; } = "#F59E0B";
    public string Error { get; set; } = "#EF4444";
    public string Info { get; set; } = "#3B82F6";

    public string Surface { get; set; } = "#FFFFFF";
    public string SurfaceVariant { get; set; } = "#F8FAFC";
    public string Background { get; set; } = "#F1F5F9";

    public string TextPrimary { get; set; } = "#1F2937";
    public string TextSecondary { get; set; } = "#6B7280";
    public string TextMuted { get; set; } = "#9CA3AF";
    public string TextOnPrimary { get; set; } = "#FFFFFF";
    public string TextOnDark { get; set; } = "#F9FAFB";

    public string Border { get; set; } = "#E5E7EB";
    public string BorderFocus { get; set; } = "#2563EB";
    public string BorderLight { get; set; } = "#F3F4F6";
}

public class TypographySystem
{
    public string FontFamily { get; set; } = "Segoe UI";
    public string FontFamilyMono { get; set; } = "Consolas";
    public double FontSizeXs { get; set; } = 8;
    public double FontSizeSm { get; set; } = 9;
    public double FontSizeBase { get; set; } = 10;
    public double FontSizeLg { get; set; } = 12;
    public double FontSizeXl { get; set; } = 14;
    public double FontSizeXxl { get; set; } = 18;
    public bool FontBoldPrimary { get; set; } = true;
}

public class SpacingSystem
{
    public int Unit { get; set; } = 4;
    public int PaddingXs { get; set; } = 2;
    public int PaddingSm { get; set; } = 4;
    public int PaddingMd { get; set; } = 8;
    public int PaddingLg { get; set; } = 16;
    public int GapXs { get; set; } = 2;
    public int GapSm { get; set; } = 4;
    public int GapMd { get; set; } = 8;
    public int GapLg { get; set; } = 16;
}

public class BorderSystem
{
    public int RadiusNone { get; set; } = 0;
    public int RadiusSm { get; set; } = 2;
    public int RadiusMd { get; set; } = 4;
    public int RadiusLg { get; set; } = 8;
    public int RadiusFull { get; set; } = 999;
    public int WidthDefault { get; set; } = 1;
    public int WidthFocus { get; set; } = 2;
}

public class ShadowSystem
{
    public string Level0 { get; set; } = "none";
    public string Level1 { get; set; } = "0 1px 2px rgba(0,0,0,0.05)";
    public string Level2 { get; set; } = "0 4px 6px rgba(0,0,0,0.1)";
    public string Level3 { get; set; } = "0 10px 15px rgba(0,0,0,0.1)";
    public string Level4 { get; set; } = "0 20px 25px rgba(0,0,0,0.15)";
}

public class AnimationSystem
{
    public int DurationFast { get; set; } = 150;
    public int DurationNormal { get; set; } = 300;
    public int DurationSlow { get; set; } = 500;
    public string Easing { get; set; } = "ease";
}

public enum ControlRole
{
    Default,
    PrimaryButton,
    SecondaryButton,
    Title,
    SectionHeader,
    BodyText,
    Caption,
    Input,
    Container,
    DataTable,
    StatusBar
}

public enum TextRole
{
    Title,
    SectionHeader,
    BodyText,
    Caption,
    Muted,
    Code
}
