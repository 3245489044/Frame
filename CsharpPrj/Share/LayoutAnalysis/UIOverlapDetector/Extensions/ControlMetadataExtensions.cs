using Modules.LayoutAnalysis.UIInspector;
using Modules.LayoutAnalysis.UIOverlapDetector.Calculator;
using Modules.LayoutAnalysis.UIOverlapDetector.Models;

namespace Modules.LayoutAnalysis.UIOverlapDetector.Extensions;

public static class ControlMetadataExtensions
{
    private static readonly OverlapCalculator DefaultCalculator = new();

    public static OverlapAnalysisResult DetectOverlaps(this IReadOnlyList<ControlMetadata> controls)
    {
        return DefaultCalculator.Calculate(controls);
    }

    public static OverlapAnalysisResult DetectOverlaps(
        this IReadOnlyList<ControlMetadata> controls,
        bool includeParentChild,
        double minOverlapThreshold)
    {
        return DefaultCalculator.Calculate(controls, includeParentChild, minOverlapThreshold);
    }

    public static string ToOverlapReport(this OverlapAnalysisResult result)
    {
        return result.FormatSummary();
    }
}
