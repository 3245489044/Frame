namespace JsonUIBeautifier.Models;

public class BeautifierConfig
{
    public double GridAlignment { get; set; } = 0.05;

    public string? PrimaryColor { get; set; }

    public int SpacingUnit { get; set; } = 8;

    public bool EnableThemeExtraction { get; set; } = true;

    public bool EnableStyleDeduplication { get; set; } = true;

    public bool EnableLayoutOptimization { get; set; } = true;

    public bool EnableColorHarmonization { get; set; } = true;

    public bool EnableSpacingNormalization { get; set; } = true;

    public bool EnableLayoutStrategyInference { get; set; } = true;

    public bool EnableStylization { get; set; } = false;

    public string? ActiveStyle { get; set; }

    public string? CustomStyleJson { get; set; }

    public string OutputFormat { get; set; } = "beautified";
}

public class BeautifyResult
{
    public string BeautifiedJson { get; set; } = "";

    public BeautifyReport Report { get; set; } = new();

    public string OriginalJson { get; set; } = "";
}

public class BeautifyReport
{
    public string InputFile { get; set; } = "";

    public string OutputFile { get; set; } = "";

    public BeautifyStatistics Statistics { get; set; } = new();

    public List<BeautifyChange> Changes { get; set; } = new();

    public List<string> Suggestions { get; set; } = new();

    public AnalysisScore Score { get; set; } = new();
}

public class AnalysisScore
{
    public double LayoutQuality { get; set; }

    public double ColorConsistency { get; set; }

    public double SpacingConsistency { get; set; }

    public double StructureClarity { get; set; }

    public double Total => Math.Round((LayoutQuality + ColorConsistency + SpacingConsistency + StructureClarity) / 4.0, 1);
}

public class BeautifyStatistics
{
    public int ControlsCount { get; set; }
    public int ColorsFound { get; set; }
    public int ColorsUnified { get; set; }
    public int LayoutsOptimized { get; set; }
    public int SpacingsNormalized { get; set; }
    public int ThemesExtracted { get; set; }
    public int StylesDeduplicated { get; set; }
    public int StrategiesInferred { get; set; }
    public int StylizationChanges { get; set; }
    public string? AppliedStyle { get; set; }
}

public class BeautifyChange
{
    public string Type { get; set; } = "";

    public string Target { get; set; } = "";

    public string? Before { get; set; }

    public string? After { get; set; }

    public string Description { get; set; } = "";
}

public class ColorUsage
{
    public string Color { get; set; } = "";
    public int Count { get; set; }
    public List<string> UsedBy { get; set; } = new();
}

public enum LayoutPatternType
{
    Unknown,
    FormRow,
    Toolbar,
    Sidebar,
    Grid,
    VerticalStack,
    HorizontalRow,
    Card,
    Panel
}

public class LayoutPattern
{
    public LayoutPatternType Type { get; set; }
    public string ContainerPath { get; set; } = "";
    public double Confidence { get; set; }
    public List<string> MemberControls { get; set; } = new();
}

public class ExtractedTheme
{
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? SurfaceColor { get; set; }
    public string? TextPrimary { get; set; }
    public string? TextSecondary { get; set; }
    public string? TextMuted { get; set; }
    public string? BorderColor { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    public int? BorderRadius { get; set; }
    public int? Spacing { get; set; }
    public int? Padding { get; set; }
}

public class StructureAnalysis
{
    public int MaxDepth { get; set; }
    public int TotalControls { get; set; }
    public int ContainerCount { get; set; }
    public int LeafControlCount { get; set; }
    public List<string> ControlTypes { get; set; } = new();
    public Dictionary<string, int> TypeDistribution { get; set; } = new();
}
